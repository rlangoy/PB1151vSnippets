# ble_nus.py — Nordic UART Service (NUS) peripheral library for MicroPython.
#
# Reusable BLE serial transport based on BLEUARTEcho.py. Bytes written by the
# central are accumulated by the BLE stack and split on newlines; each complete
# line is decoded and handed to an `on_rx` callback. Outgoing data is sent with
# `send()`, which notifies all connected centrals over the NUS TX characteristic.
#
# Two settings make long messages work reliably:
#   * gatts_set_buffer(rx, N, append=True) — without append=True the RX
#     characteristic only keeps the LAST write, so a multi-packet message loses
#     everything but its final fragment. append=True accumulates the fragments.
#   * a larger ATT MTU (requested on init) so more bytes fit per packet.
# Senders must terminate each message with a newline ("\n").
#
#   * Onboard LED ON  while a BLE central is connected
#   * Onboard LED OFF when it disconnects (advertising restarts automatically)
#
# Uses only the built-in `bluetooth` module — no aioble required.

import bluetooth
import struct
import time
from machine import Pin
from micropython import const

# ---- BLE IRQ events ------------------------------------------------------
_IRQ_CENTRAL_CONNECT    = const(1)
_IRQ_CENTRAL_DISCONNECT = const(2)
_IRQ_GATTS_WRITE        = const(3)
_IRQ_MTU_EXCHANGED      = const(21)

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

# Size of the RX characteristic buffer. Big enough to hold a full multi-packet
# message before we split it on newlines.
_RX_BUFFER_SIZE = const(256)

# Larger ATT MTU to request so more bytes fit in each BLE packet (default is 23).
_PREFERRED_MTU = const(256)

# Conservative TX chunk size used until an MTU is negotiated. Updated from the
# _IRQ_MTU_EXCHANGED event so we can send bigger notifications when allowed.
_DEFAULT_TX_CHUNK = const(20)

# Pause between chunked notifications. Without a gap, back-to-back
# gatts_notify() calls queue faster than the radio transmits them and all but
# the last chunk get dropped (the central then only sees the final chunk).
_TX_CHUNK_GAP_MS = const(20)


class BLENUS:
    """Nordic UART Service serial transport.

    Receive: the BLE stack accumulates writes into the RX characteristic
    (append=True); each complete newline-terminated line is decoded to a `str`
    and passed to the `on_rx` callback. The callback runs in BLE IRQ context,
    so keep it short.

    Transmit: `send(data)` notifies all connected centrals on the TX
    characteristic, chunked to fit the negotiated MTU.

    NOTE: do NOT call send()/sendline() from a hardware Pin IRQ handler.
    BLE notifications issued from interrupt context are unreliable, and the
    inter-chunk delay used here cannot run inside an IRQ. Queue the event in
    the IRQ and send it from your main loop instead.
    """

    def __init__(self, name="Pico-NUS", on_rx=None, ble=None, led="LED"):
        self._name = name
        self._on_rx = on_rx

        self._led = Pin(led, Pin.OUT) if led is not None else None
        if self._led is not None:
            self._led.off()

        self._ble = ble or bluetooth.BLE()
        self._ble.active(True)
        self._ble.irq(self._irq)

        # Ask for a bigger MTU; the central may grant less. Safe to call early.
        try:
            self._ble.config(mtu=_PREFERRED_MTU)
        except Exception:
            pass  # not all ports support config(mtu=...)

        # Register NUS and capture the value handles (TX first, RX second).
        ((self._tx_handle, self._rx_handle),) = self._ble.gatts_register_services((_NUS_SERVICE,))

        # Grow the RX buffer and, critically, append successive writes instead
        # of overwriting — otherwise only the last fragment of a long message
        # survives.
        self._ble.gatts_set_buffer(self._rx_handle, _RX_BUFFER_SIZE, True)

        self._connections = set()
        self._tx_chunk = _DEFAULT_TX_CHUNK

        # Name fits in the adv packet; the 128-bit UUID goes in the scan response.
        self._adv_payload = self._advertising_payload(name=self._name)
        self._scan_resp = self._advertising_payload(services=[_NUS_UUID])

        self._advertise()

    # ---- public API ------------------------------------------------------

    def on_rx(self, callback):
        """Register/replace the message-received callback: callback(text)."""
        self._on_rx = callback

    def is_connected(self):
        return bool(self._connections)

    def send(self, data):
        """Send bytes/str to all connected centrals over NUS TX.

        Data longer than one notification is split into MTU-sized pieces with a
        short pause between them so the radio can flush each chunk before the
        next is queued (otherwise only the last chunk arrives).
        """
        if not self._connections:
            return
        if isinstance(data, str):
            data = data.encode()
        chunk_size = self._tx_chunk
        for conn_handle in self._connections:
            n = len(data)
            i = 0
            while i < n:
                chunk = data[i:i + chunk_size]
                try:
                    self._ble.gatts_notify(conn_handle, self._tx_handle, chunk)
                except OSError:
                    # Central went away mid-send; ignore and let the
                    # disconnect IRQ clean up the connection set.
                    break
                i += chunk_size
                # Pace only between chunks, not after the final one.
                if i < n:
                    time.sleep_ms(_TX_CHUNK_GAP_MS)

    def sendline(self, data):
        """Like send() but guarantees a trailing newline for line framing."""
        if isinstance(data, bytes):
            data = data.decode()
        if not data.endswith("\n"):
            data += "\n"
        self.send(data)

    # ---- internals -------------------------------------------------------

    @staticmethod
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

    def _irq(self, event, data):
        if event == _IRQ_CENTRAL_CONNECT:
            conn_handle, _, _ = data
            self._connections.add(conn_handle)
            if self._led is not None:
                self._led.on()                   # connected   -> LED on

        elif event == _IRQ_CENTRAL_DISCONNECT:
            conn_handle, _, _ = data
            self._connections.discard(conn_handle)
            self._tx_chunk = _DEFAULT_TX_CHUNK   # reset for the next connection
            if self._led is not None:
                self._led.off()                  # disconnected -> LED off
            self._advertise()                    # accept the next connection

        elif event == _IRQ_MTU_EXCHANGED:
            conn_handle, mtu = data
            # Usable notification payload is MTU - 3 bytes of ATT header.
            self._tx_chunk = max(_DEFAULT_TX_CHUNK, mtu - 3)

        elif event == _IRQ_GATTS_WRITE:
            conn_handle, value_handle = data
            if value_handle == self._rx_handle:
                self._receive()                  # central wrote to NUS RX

    def _receive(self):
        """Read the bytes the central wrote and dispatch each complete line.

        With append=True the BLE stack accumulates every write into the RX
        characteristic, so gatts_read() returns the whole message-so-far. We
        wait until it contains a newline, then process the complete lines and
        reset the stack buffer to just the trailing partial (if any).
        """
        text = self._ble.gatts_read(self._rx_handle).decode("utf-8")

        # No complete line yet: leave it accumulating in the stack buffer.
        if "\n" not in text:
            return

        # Split into complete lines plus a trailing remainder, then reset the
        # stack buffer to just that remainder.
        *lines, remainder = text.split("\n")
        self._ble.gatts_write(self._rx_handle, remainder.encode("utf-8"))

        for line in lines:
            #print("BLE rx:", line)        # show each complete message
            self._deliver(line.strip())

    def _deliver(self, message):
        """Pass one complete message to the on_rx callback (IRQ-safe)."""
        if not message or self._on_rx is None:
            return
        try:
            self._on_rx(message)
        except Exception as e:
            # Never let a callback error kill the BLE IRQ.
            print('{"ERROR": "on_rx callback raised: %s"}' % e)

    def _advertise(self, interval_us=50_000):
        self._ble.gap_advertise(
            interval_us,
            adv_data=self._adv_payload,
            resp_data=self._scan_resp,
        )
