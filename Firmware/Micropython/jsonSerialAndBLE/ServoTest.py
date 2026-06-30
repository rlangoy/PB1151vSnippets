"""
RP2040 MicroPython SG90 Servo Control
Controls an SG90 servo on GPIO 21 via 50 Hz PWM.
"""

from machine import Pin, PWM
import time


class SG90Servo:
    """SG90 servo controller using PWM on RP2040."""

    def __init__(self, gpio_pin: int, freq: int = 50,
                 min_us: int = 500, max_us: int = 2500):
        """
        Args:
            gpio_pin: GPIO pin number.
            freq:     PWM frequency in Hz (50 Hz for standard hobby servos).
            min_us:   Pulse width at 0° in microseconds.
            max_us:   Pulse width at 180° in microseconds.
                      SG90 typically lands around 500–2400 µs; widening this
                      risks driving the servo into its mechanical end-stops.
        """
        self.pwm = PWM(Pin(gpio_pin, Pin.OUT))
        self.pwm.freq(freq)

        self.min_us = min_us
        self.max_us = max_us
        self.period_us = 1_000_000 / freq   # derive period from freq, don't hardcode
        self.current_angle = 0.0

    def set_angle(self, angle: float) -> None:
        """Set servo to the given angle (0–180°)."""
        angle = max(0.0, min(180.0, angle))
        pulse_us = self.min_us + (angle / 180.0) * (self.max_us - self.min_us)
        duty = int((pulse_us / self.period_us) * 65535)
        self.pwm.duty_u16(duty)
        self.current_angle = angle

    def deinit(self) -> None:
        """Release the PWM (stops holding torque / jitter when idle)."""
        self.pwm.deinit()


# Demo: step 0° -> 180° -> 0°
servo = SG90Servo(gpio_pin=21)

servo.set_angle(0)
time.sleep(1)

servo.set_angle(180)
time.sleep(1)

servo.set_angle(0)
time.sleep(1)

servo.deinit()