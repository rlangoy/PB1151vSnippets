# main.py — Nordic UART Service (NUS) echo peripheral for the Raspberry Pi Pico W
#
#   * Onboard LED ON  while a BLE central is connected
#   * Onboard LED OFF when it disconnects (advertising restarts automatically)
#   * Every byte written to the NUS RX characteristic is echoed back on TX
#
# Uses only the built-in `bluetooth` module — no aioble required.

import bluetooth
import struct
import time
from machine import Pin
from micropython import const

_NAME = "Pico-NUS"


# ---- BLE IRQ events ------------------------------------------------------
_IRQ_CENTRAL_CONNECT    = const(1)
_IRQ_CENTRAL_DISCONNECT = const(2)
_IRQ_GATTS_WRITE        = const(3)

# ---- GATT characteristic flags -------------------------------------------
_FLAG_WRITE        = const(0x0008)
_FLAG_WRITE_NO_RSP = const(0x0004)
_FLAG_NOTIFY       = const(0x0010)

# ---- Advertising data types ----------------------------------------------
_ADV_TYPE_FLAGS   = const(0x01)
_ADV_TYPE_NAME    = const(0x09)
_ADV_TYPE_UUID128 = const(0x07)

# ---- Nordic UART Service -------------------------------------------------
_NUS_UUID = bluetooth.UUID("6E400001-B5A3-F393-E0A9-E50E24DCCA9E")
_RX_UUID  = bluetooth.UUID("6E400002-B5A3-F393-E0A9-E50E24DCCA9E")  # central -> Pico (write)
_TX_UUID  = bluetooth.UUID("6E400003-B5A3-F393-E0A9-E50E24DCCA9E")  # Pico -> central (notify)

_NUS_SERVICE = (
    _NUS_UUID,
    (
        (_TX_UUID, _FLAG_NOTIFY),
        (_RX_UUID, _FLAG_WRITE | _FLAG_WRITE_NO_RSP),
    ),
)


def _advertising_payload(name=None, services=None):
    """Build a BLE advertising / scan-response payload."""
    payload = bytearray()

    def _append(adv_type, value):
        payload.extend(struct.pack("BB", len(value) + 1, adv_type) + value)

    if name is not None:
        # Flags: general discoverable, BR/EDR not supported.
        _append(_ADV_TYPE_FLAGS, struct.pack("B", 0x06))
        _append(_ADV_TYPE_NAME, name.encode())

    if services:
        for uuid in services:
            _append(_ADV_TYPE_UUID128, bytes(uuid))

    return payload


class BLEUARTEcho:
    def __init__(self, ble, name=_NAME):
        self._led = Pin("LED", Pin.OUT)
        self._led.off()

        self._ble = ble
        self._ble.active(True)
        self._ble.irq(self._irq)

        # Register NUS and capture the value handles (TX first, RX second).
        ((self._tx_handle, self._rx_handle),) = self._ble.gatts_register_services((_NUS_SERVICE,))

        self._connections = set()

        # Name fits in the adv packet; the 128-bit UUID goes in the scan response.
        self._adv_payload = _advertising_payload(name=name)
        self._scan_resp = _advertising_payload(services=[_NUS_UUID])

        self._advertise()

    def _irq(self, event, data):
        if event == _IRQ_CENTRAL_CONNECT:
            conn_handle, _, _ = data
            self._connections.add(conn_handle)
            self._led.on()                       # connected   -> LED on            

        elif event == _IRQ_CENTRAL_DISCONNECT:
            conn_handle, _, _ = data
            self._connections.discard(conn_handle)
            self._led.off()                      # disconnected -> LED off
            self._advertise()                    # accept the next connection

        elif event == _IRQ_GATTS_WRITE:
            conn_handle, value_handle = data
            if value_handle == self._rx_handle:
                self._echo(self._ble.gatts_read(self._rx_handle))

    def _echo(self, data):
        """Notify the received bytes straight back on the TX characteristic."""
        for conn_handle in self._connections:
            self._ble.gatts_notify(conn_handle, self._tx_handle, data)

    def _advertise(self, interval_us=50_000):
        self._ble.gap_advertise(
            interval_us,
            adv_data=self._adv_payload,
            resp_data=self._scan_resp,
        )



def main():
    BLEUARTEcho(bluetooth.BLE())
    while True:
        time.sleep_ms(100)


if __name__ == "__main__":
    main()