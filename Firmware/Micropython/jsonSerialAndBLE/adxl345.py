"""ADXL345 3-axis accelerometer driver (I2C).

The device is configured for full-resolution +/-2 g mode, where the
scale factor is a fixed 1/256 g per LSB. Axis data is read as three
little-endian signed 16-bit values starting at DATAX0.

Initialization is lazy: the first read verifies the chip identity,
enables measurement mode, and then waits for the DATA_READY flag before
returning. The wait matters -- the data registers hold zeros until the
first conversion completes (1/ODR + 1.1 ms after entering measurement
mode), so reading immediately would report a bogus (0, 0, 0) sample.
Constructing the driver never touches the bus, so the application boots
cleanly even with the sensor unplugged.

This module is a pure hardware driver: it knows nothing about the JSON
protocol. I2C failures propagate as OSError; an unexpected chip at the
given address raises WrongDeviceError.
"""

import struct
import time

_ETIMEDOUT_ERRNO = 110              # raised if the first sample never arrives


class WrongDeviceError(Exception):
    """The chip at the given address did not identify as an ADXL345."""


class Adxl345:
    """Reads acceleration from an ADXL345 over I2C."""

    DEFAULT_ADDRESS = 0x53          # ALT ADDRESS pin low; 0x1D when high

    _EXPECTED_DEVICE_ID = 0xE5
    _G_PER_LSB = 1 / 256            # full-resolution mode scale factor

    _DEVICE_ID_REGISTER = 0x00
    _POWER_CONTROL_REGISTER = 0x2D
    _INT_SOURCE_REGISTER = 0x30
    _DATA_FORMAT_REGISTER = 0x31
    _AXIS_DATA_REGISTER = 0x32      # DATAX0..DATAZ1, six bytes
    _AXIS_DATA_LENGTH = 6

    _MEASURE_MODE = 0x08            # POWER_CTL: leave standby, start measuring
    _FULL_RESOLUTION_2G = 0x08      # DATA_FORMAT: FULL_RES bit, +/-2 g range
    _DATA_READY_BIT = 0x80          # INT_SOURCE: a new sample is available

    # The first valid sample arrives 1/ODR + 1.1 ms after entering
    # measurement mode (datasheet turn-on time): ~11 ms at the default
    # 100 Hz output data rate. The timeout leaves generous headroom.
    _FIRST_SAMPLE_TIMEOUT_MS = 100
    _READY_POLL_PAUSE_MS = 1

    def __init__(self, i2c, address=DEFAULT_ADDRESS):
        self._i2c = i2c
        self._address = address
        self._initialized = False

    def read_g(self):
        """Return acceleration as an (x, y, z) tuple in units of g.

        Initializes the device on first use, which blocks once for the
        turn-on time (~11 ms). Raises WrongDeviceError if the chip does
        not identify as an ADXL345, or OSError on I2C failure (device
        absent, bus timeout, first sample never ready).
        """
        self._ensure_initialized()
        raw_x, raw_y, raw_z = self._read_raw_axes()
        return (
            raw_x * self._G_PER_LSB,
            raw_y * self._G_PER_LSB,
            raw_z * self._G_PER_LSB,
        )

    def _ensure_initialized(self):
        if not self._initialized:
            self._initialize()

    def _initialize(self):
        self._verify_device_id()
        self._write_register(self._DATA_FORMAT_REGISTER, self._FULL_RESOLUTION_2G)
        self._write_register(self._POWER_CONTROL_REGISTER, self._MEASURE_MODE)
        self._wait_for_first_sample()
        self._initialized = True    # only reached on success; failures retry

    def _verify_device_id(self):
        device_id = self._read_register(self._DEVICE_ID_REGISTER)
        if device_id != self._EXPECTED_DEVICE_ID:
            raise WrongDeviceError(
                "expected device id 0x%02X, got 0x%02X"
                % (self._EXPECTED_DEVICE_ID, device_id)
            )

    def _wait_for_first_sample(self):
        """Block until DATA_READY sets after entering measurement mode.

        The data registers keep their reset value (zero) until the first
        conversion finishes, so returning earlier would let the caller
        read a fake (0, 0, 0) acceleration.
        """
        deadline = time.ticks_add(time.ticks_ms(), self._FIRST_SAMPLE_TIMEOUT_MS)
        while not self._is_data_ready():
            if time.ticks_diff(time.ticks_ms(), deadline) >= 0:
                raise OSError(_ETIMEDOUT_ERRNO)
            time.sleep_ms(self._READY_POLL_PAUSE_MS)

    def _is_data_ready(self):
        # Safe to poll: DATA_READY is cleared by reading the data
        # registers, not by reading INT_SOURCE, and no other interrupt
        # sources are enabled in this configuration.
        return bool(
            self._read_register(self._INT_SOURCE_REGISTER) & self._DATA_READY_BIT
        )

    def _read_raw_axes(self):
        data = self._i2c.readfrom_mem(
            self._address, self._AXIS_DATA_REGISTER, self._AXIS_DATA_LENGTH
        )
        return struct.unpack("<hhh", data)

    def _read_register(self, register):
        return self._i2c.readfrom_mem(self._address, register, 1)[0]

    def _write_register(self, register, value):
        self._i2c.writeto_mem(self._address, register, bytes([value]))
