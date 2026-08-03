"""TMP102 digital temperature sensor driver (I2C).

The TMP102 powers up in continuous-conversion mode, so no initialization
sequence is needed: the temperature register can be read at any time.
The reading is a 12-bit two's-complement value, left-justified in the
16-bit temperature register, at 0.0625 degC per LSB.

This module is a pure hardware driver: it knows nothing about the JSON
protocol. I2C failures propagate as OSError.
"""


class Tmp102:
    """Reads temperature from a TMP102 over I2C."""

    DEFAULT_ADDRESS = 0x48          # ADD0 pin tied to GND

    _TEMPERATURE_REGISTER = 0x00
    _CELSIUS_PER_LSB = 0.0625
    _SIGN_BIT = 0x800               # bit 11 of the 12-bit reading
    _TWOS_COMPLEMENT_SPAN = 0x1000

    def __init__(self, i2c, address=DEFAULT_ADDRESS):
        self._i2c = i2c
        self._address = address

    def read_celsius(self):
        """Return the current temperature in degrees Celsius.

        Raises OSError on I2C failure (device absent, bus timeout).
        """
        return self._read_raw_temperature() * self._CELSIUS_PER_LSB

    def _read_raw_temperature(self):
        high_byte, low_byte = self._i2c.readfrom_mem(
            self._address, self._TEMPERATURE_REGISTER, 2
        )
        raw = (high_byte << 4) | (low_byte >> 4)
        if raw & self._SIGN_BIT:
            raw -= self._TWOS_COMPLEMENT_SPAN
        return raw
