"""
Serial + BLE (NUS) JSON control: LEDs, servo, and switch status reporting.

The same JSON protocol works over the USB serial console AND over a BLE
Nordic UART Service connection. Lines received on either transport are
parsed identically; all responses (errors, switch events) are sent back
out over BOTH transports.

Set an LED (single line, then newline):
    {"leds": {"id": "LED1", "value": 1}}              # LED1 on
    {"leds": {"id": "LED1", "value": 0}}              # LED1 off

Multiple LEDs in one message (LEDs not listed are left unchanged):
    {"leds": [{"id": "LED1", "value": 1}, {"id": "LED4", "value": 0}]}

Move a servo to an angle (0-180):
    {"servos": [{"id": "SV1", "angle": 90}]}

Release a servo (drops holding torque / jitter until next angle command):
    {"servos": [{"id": "SV1", "action": "release"}]}

LEDs, servos, and switches are independent collections and may be combined:
    {"leds": [{"id": "LED1", "value": 1}], "servos": [{"id": "SV1", "angle": 0}]}

Pressing a switch reports its status as JSON. SW1 fires on both edges, so a
press then release reports:
    {"switches": [{"id": "SW1", "value": 1}]}         # pressed
    {"switches": [{"id": "SW1", "value": 0}]}         # released

Note: incoming lines are lowercased before parsing, so command/id casing is
not significant. Avoid carrying case-sensitive string values in commands.
"""

import time
import sys
import select
import json
from machine import Pin, PWM

from ble_nus import BLENUS   # BLE Nordic UART Service transport (see ble_nus.py)


# ---- Servo driver --------------------------------------------------------
class SG90Servo:
    """SG90 servo controller using PWM on RP2040."""

    def __init__(self, gpio_pin: int, freq: int = 50,
                 min_us: int = 500, max_us: int = 2500):
        """
        Args:
            gpio_pin: GPIO pin number.
            freq:     PWM frequency in Hz (50 Hz for standard hobby servos).
            min_us:   Pulse width at 0 deg in microseconds.
            max_us:   Pulse width at 180 deg in microseconds.
                      SG90 typically lands around 500-2400 us; widening this
                      risks driving the servo into its mechanical end-stops.
        """
        self.pin = Pin(gpio_pin, Pin.OUT)
        self.freq = freq
        self.min_us = min_us
        self.max_us = max_us
        self.period_us = 1_000_000 / freq   # derive period from freq, don't hardcode
        self.current_angle = 0.0
        self.pwm = None
        self.attach()

    def attach(self) -> None:
        """(Re)create the PWM if it was previously released."""
        if self.pwm is None:
            self.pwm = PWM(self.pin)
            self.pwm.freq(self.freq)

    def set_angle(self, angle: float) -> None:
        """Set servo to the given angle (0-180 deg)."""
        self.attach()                       # re-init if previously released
        angle = max(0.0, min(180.0, angle))
        pulse_us = self.min_us + (angle / 180.0) * (self.max_us - self.min_us)
        duty = int((pulse_us / self.period_us) * 65535)
        self.pwm.duty_u16(duty)
        self.current_angle = angle

    def release(self) -> None:
        """Release the PWM (stops holding torque / jitter when idle)."""
        if self.pwm is not None:
            self.pwm.deinit()
            self.pwm = None


# ---- BLE NUS transport ---------------------------------------------------
# on_rx is wired up after the JSON handler is defined (see below).
ble = BLENUS(name="Pico-NUS")


# ---- Hardware ------------------------------------------------------------
# Servos on GPIO 21, addressable by id.
servos = {
    "SV1": SG90Servo(gpio_pin=21),
}

# LEDs on GPIO 6, 7, 8, 9
leds = {
    "LED1": Pin(6, Pin.OUT),
    "LED2": Pin(7, Pin.OUT),
    "LED3": Pin(8, Pin.OUT),
    "LED4": Pin(9, Pin.OUT),
}

# Switches on GPIO 10, 11, 12
switches = {
    "SW1": Pin(10, Pin.IN, Pin.PULL_DOWN),
    "SW2": Pin(11, Pin.IN, Pin.PULL_DOWN),
    "SW3": Pin(12, Pin.IN, Pin.PULL_DOWN),
    "SW4": Pin(16, Pin.IN),
}


def emit(msg):
    """Send a line to BOTH serial and the connected BLE central.

    Call this only from the main loop / normal context -- NOT from a hardware
    IRQ handler. BLE notifications must not be issued from inside an IRQ.
    """
    print(msg)            # serial / USB console
    ble.sendline(msg)     # BLE NUS TX (no-op if nothing connected)


# ---- Switch events -------------------------------------------------------
# Pin IRQs fire in interrupt context, where heavy work (and BLE notifies in
# particular) is unsafe. The handlers do the minimum: capture the event into
# a queue. The main loop drains the queue and emits the JSON over serial+BLE.
_switch_events = []   # list of (id_str, value) pushed from IRQ context


def _queue_switch(sw_id, sw):
    _switch_events.append((sw_id, sw.value()))


def SW1Changed(sw):
    _queue_switch("SW1", sw)

def SW2Changed(sw):
    _queue_switch("SW2", sw)

def SW3Changed(sw):
    _queue_switch("SW3", sw)

def SW4Changed(sw):
    _queue_switch("SW4", sw)


def flushSwitchEvents():
    """Emit any queued switch events from normal (non-IRQ) context."""
    while _switch_events:        
        sw_id, value = _switch_events.pop(0)
        if(sw_id == "SW4") :
            value=value ^ 1 # toogle value , so that when a bobject is near the value is 1
        emit('{"switches": [{"id": "%s", "value": %d}]}' % (sw_id, value))


# Activate IRQ handling on input change (both edges).
switches["SW1"].irq(trigger=Pin.IRQ_FALLING | Pin.IRQ_RISING, handler=SW1Changed)
switches["SW2"].irq(trigger=Pin.IRQ_FALLING | Pin.IRQ_RISING, handler=SW2Changed)
switches["SW3"].irq(trigger=Pin.IRQ_FALLING | Pin.IRQ_RISING, handler=SW3Changed)
switches["SW4"].irq(trigger=Pin.IRQ_FALLING | Pin.IRQ_RISING, handler=SW4Changed)


# ---- Command handlers ----------------------------------------------------
def _as_list(value):
    """Accept either a single object or an array; return a list (or None).

        {"id": ...}            -> [{"id": ...}]
        [{"id": ...}, ...]     -> unchanged
    """
    if isinstance(value, dict):
        return [value]
    if isinstance(value, list):
        return value
    return None


def handleLeds(data):
    # Missing "leds" key is not an error -- just nothing to do.
    led_list = _as_list(data.get("leds"))
    if led_list is None:
        return

    for item in led_list:
        if not isinstance(item, dict):
            emit('{"ERROR": "JSON LEDS array is not an object"}')
            continue

        led_id = item.get("id")
        value = item.get("value")
        pin = leds.get(led_id.upper()) if isinstance(led_id, str) else None

        if pin is None:
            emit('{"ERROR": "JSON LEDS: unknown or missing LED id"}')
            continue
        if value not in (0, 1):
            emit('{"ERROR": "JSON LEDS: value must be 0 or 1"}')
            continue

        pin.value(value)


def handleServos(data):
    # Missing "servos" key is not an error -- just nothing to do.
    servo_list = _as_list(data.get("servos"))
    if servo_list is None:
        return

    for item in servo_list:
        if not isinstance(item, dict):
            emit('{"ERROR": "JSON SERVOS array is not an object"}')
            continue

        sv_id = item.get("id")
        servo = servos.get(sv_id.upper()) if isinstance(sv_id, str) else None
        if servo is None:
            emit('{"ERROR": "JSON SERVOS: unknown or missing servo id"}')
            continue

        # An explicit action takes precedence over a position.
        if item.get("action") == "release":
            servo.release()
            continue

        angle = item.get("angle")
        if not isinstance(angle, (int, float)):
            emit('{"ERROR": "JSON SERVOS: angle must be a number 0-180"}')
            continue

        servo.set_angle(angle)   # set_angle clamps to 0-180


def parseJson(payload):
    """Parse one JSON line and dispatch to the LED and servo handlers."""
    if len(payload) == 0:
        return

    try:
        payload = payload.lower()
        data = json.loads(payload)
    except (ValueError, TypeError):
        emit('{"ERROR": "JSON not properly formatted"}')
        return

    if not isinstance(data, dict):
        return

    handleLeds(data)
    handleServos(data)


# Lines arriving over BLE are handled the same way as serial lines.
# NOTE: on_rx runs in BLE stack context (not a hardware IRQ), so calling
# emit() -> ble.sendline() from here is safe.
ble.on_rx(parseJson)


# ---- Non-blocking serial line reader -------------------------------------
# We poll the *binary* stdin stream (sys.stdin.buffer) and read one byte at a
# time, assembling lines ourselves. Polling sys.stdin (the text wrapper) is
# unreliable on MicroPython's USB-CDC; the binary stream works consistently.
_stdin = sys.stdin.buffer
_poll = select.poll()
_poll.register(_stdin, select.POLLIN)

_serial_buf = bytearray()


def readSerialLine():
    """Return one stripped serial line if a full line is ready, else None.

    Reads all currently-available bytes without blocking, buffering until a
    newline arrives, then returns the completed line.
    """
    global _serial_buf
    while _poll.poll(0):
        ch = _stdin.read(1)
        if not ch:
            break
        _serial_buf.extend(ch)
        if ch == b"\n":
            line = _serial_buf[:-1].rstrip(b"\r")
            _serial_buf = bytearray()
            try:
                return line.decode().strip()
            except Exception:
                return ""
    return None


# ---- Main loop -----------------------------------------------------------
while True:
    # 1) Emit any switch events captured by the IRQ handlers.
    flushSwitchEvents()

    # 2) Handle a serial command line if one is ready.
    serialInput = readSerialLine()
    if serialInput is not None:
        parseJson(serialInput)

    time.sleep_ms(10)