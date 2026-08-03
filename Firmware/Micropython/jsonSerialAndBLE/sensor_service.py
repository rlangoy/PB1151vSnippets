"""JSON protocol layer for the "sensors" collection.

Implements the sensor extension of the LED/servo/switch JSON protocol
(see sensor-protocol-design-spec.md):

    one-shot read:   {"sensors": [{"id": "TEMP1", "read": 1}]}
    start stream:    {"sensors": [{"id": "ACC1", "action": "stream", "interval_ms": 50}]}
    stop stream:     {"sensors": [{"id": "ACC1", "action": "stop"}]}

One-shot responses and stream ticks share the same per-sensor shape and
are emitted one JSON object per line through a caller-supplied emit
function (which fans out to USB serial and BLE NUS). Failures are
reported per sensor id using the spec's error taxonomy: i2c_timeout,
i2c_nack, not_initialized, unknown_id.

Response lines are hand-formatted rather than built with json.dumps()
so that field order is stable (MicroPython dicts do not guarantee key
order) and float precision is controlled.
"""

import time

from adxl345 import WrongDeviceError

_ETIMEDOUT_ERRNO = 110              # errno of an I2C bus timeout on MicroPython

# A stream cannot fire more often than the main loop calls
# service_streams(), which runs once per ~10 ms loop pass.
_MINIMUM_INTERVAL_MS = 10


# ---- Sensor endpoints ----------------------------------------------------
class _SensorEndpoint:
    """Binds a canonical sensor id to a driver and formats its readings."""

    def __init__(self, sensor_id, driver):
        self.sensor_id = sensor_id
        self._driver = driver

    def read_as_json(self):
        raise NotImplementedError


class Tmp102Endpoint(_SensorEndpoint):
    """TMP102 temperature: scalar value in degrees Celsius."""

    def read_as_json(self):
        celsius = self._driver.read_celsius()
        return '{"id": "%s", "value": %.2f, "unit": "C"}' % (self.sensor_id, celsius)


class Adxl345Endpoint(_SensorEndpoint):
    """ADXL345 accelerometer: {x, y, z} value triple in g."""

    def read_as_json(self):
        x, y, z = self._driver.read_g()
        return (
            '{"id": "%s", "value": {"x": %.3f, "y": %.3f, "z": %.3f}, "unit": "g"}'
            % (self.sensor_id, x, y, z)
        )


# ---- Service -------------------------------------------------------------
class _StreamState:
    """Timer state for one continuously streaming sensor."""

    def __init__(self, interval_ms, first_due_ms):
        self.interval_ms = interval_ms
        self.next_due_ms = first_due_ms


class SensorService:
    """Dispatches "sensors" requests and drives the continuous streams.

    handle_request() consumes the entries of one incoming message; it may
    run from BLE receive context. service_streams() must be called
    regularly from the main loop -- all stream samples are emitted there,
    never from interrupt context.
    """

    def __init__(self, emit_line):
        self._emit = emit_line
        self._endpoints = {}
        self._streams = {}          # sensor id -> _StreamState

    def register(self, endpoint):
        self._endpoints[endpoint.sensor_id] = endpoint

    # ---- request handling ------------------------------------------------
    def handle_request(self, entries):
        """Process the "sensors" entries of one message (None = key absent)."""
        if entries is None:
            return
        for entry in entries:
            self._handle_entry(entry)

    def _handle_entry(self, entry):
        if not isinstance(entry, dict):
            self._emit('{"ERROR": "JSON SENSORS array is not an object"}')
            return

        endpoint = self._find_endpoint(entry.get("id"))
        if endpoint is None:
            return

        # An explicit action takes precedence over a one-shot read,
        # mirroring how servos treat action vs. angle.
        action = entry.get("action")
        if action == "stream":
            self._start_stream(endpoint, entry.get("interval_ms"))
        elif action == "stop":
            self._stop_stream(endpoint)
        elif action is not None:
            self._emit('{"ERROR": "JSON SENSORS: unknown action"}')
        elif entry.get("read") == 1:
            self._emit_reading(endpoint)
        else:
            self._emit('{"ERROR": "JSON SENSORS: expected read or action"}')

    def _find_endpoint(self, sensor_id):
        """Look up a sensor id, reporting the failure if it is unknown."""
        if not isinstance(sensor_id, str):
            self._emit('{"ERROR": "JSON SENSORS: missing sensor id"}')
            return None
        endpoint = self._endpoints.get(sensor_id.upper())
        if endpoint is None:
            self._emit_sensor_line(_error_json(sensor_id.upper(), "unknown_id"))
        return endpoint

    # ---- streaming -------------------------------------------------------
    def _start_stream(self, endpoint, interval_ms):
        """Start (or re-arm) a stream; the first sample fires immediately."""
        if not _is_valid_interval(interval_ms):
            self._emit(
                '{"ERROR": "JSON SENSORS: interval_ms must be an integer >= %d"}'
                % _MINIMUM_INTERVAL_MS
            )
            return
        self._streams[endpoint.sensor_id] = _StreamState(interval_ms, time.ticks_ms())

    def _stop_stream(self, endpoint):
        # Stopping a sensor that is not streaming is a harmless no-op.
        self._streams.pop(endpoint.sensor_id, None)

    def service_streams(self):
        """Emit one sample for every stream whose deadline has passed.

        Call from normal (non-IRQ) context at least as often as the
        shortest interval. ticks_ms() arithmetic keeps the deadlines
        correct across timer wraparound.
        """
        now = time.ticks_ms()
        for sensor_id, stream in self._streams.items():
            if time.ticks_diff(now, stream.next_due_ms) < 0:
                continue
            self._emit_reading(self._endpoints[sensor_id])
            stream.next_due_ms = _next_deadline(stream, now)

    # ---- emitting ----------------------------------------------------------
    def _emit_reading(self, endpoint):
        """Read one sensor and emit its value line, or its error line."""
        try:
            sensor_json = endpoint.read_as_json()
        except (OSError, WrongDeviceError) as exc:
            sensor_json = _error_json(endpoint.sensor_id, _error_code_from(exc))
        self._emit_sensor_line(sensor_json)

    def _emit_sensor_line(self, sensor_json):
        self._emit('{"sensors": [%s]}' % sensor_json)


# ---- Helpers -------------------------------------------------------------
def _is_valid_interval(interval_ms):
    return isinstance(interval_ms, int) and interval_ms >= _MINIMUM_INTERVAL_MS


def _next_deadline(stream, now):
    """One interval after the previous deadline; re-based if we fell behind.

    Advancing from the previous deadline keeps the cadence steady, while
    re-basing to "now" prevents a burst of catch-up samples after a stall
    (e.g. a slow BLE write).
    """
    next_due = time.ticks_add(stream.next_due_ms, stream.interval_ms)
    if time.ticks_diff(now, next_due) >= 0:
        next_due = time.ticks_add(now, stream.interval_ms)
    return next_due


def _error_json(sensor_id, error_code):
    return '{"id": "%s", "error": "%s"}' % (sensor_id, error_code)


def _error_code_from(exc):
    """Map a driver exception onto the spec's error taxonomy."""
    if isinstance(exc, WrongDeviceError):
        return "not_initialized"
    if _errno_of(exc) == _ETIMEDOUT_ERRNO:
        return "i2c_timeout"
    return "i2c_nack"


def _errno_of(exc):
    return exc.args[0] if exc.args else None
