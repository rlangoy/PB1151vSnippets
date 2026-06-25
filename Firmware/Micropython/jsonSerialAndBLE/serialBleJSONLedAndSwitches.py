# Import required modules
import time           # For delays and timing
from machine import Pin
import sys
import select
import json

from ble_nus import BLENUS   # BLE Nordic UART Service transport (see ble_nus.py)

# ---- BLE NUS transport ---------------------------------------------------
# on_rx is wired up after the JSON handler is defined (see below).
ble = BLENUS(name="Pico-NUS")

''' Serial + BLE (NUS) JSON LED control + switch status reporting.

    The same JSON protocol works over the USB serial console AND over a BLE
    Nordic UART Service connection. Lines received on either transport are
    parsed identically; all responses (errors, switch events) are sent back
    out over BOTH transports.

    Set an LED (single line, then newline):
        {"leds": {"id": "LED1", "value": 1}}     # LED1 on
        {"leds": {"id": "LED1", "value": 0}}     # LED1 off

    Multiple LEDs in one message (LEDs not listed are left unchanged):
        {"leds": [{"id": "LED1", "value": 1}, {"id": "LED4", "value": 0}]}

    Pressing a switch reports its status as JSON.
    SW1 fires on both edges, so a press then release reports:
        {"switches": [{"id": "SW1", "value": 1}]}   # pressed
        {"switches": [{"id": "SW1", "value": 0}]}   # released
'''

# Set up LEDs on GPIO 6, 7, 8, 9
leds = {
    "LED1" : Pin(6, Pin.OUT),
    "LED2" : Pin(7, Pin.OUT),
    "LED3" : Pin(8, Pin.OUT),
    "LED4" : Pin(9, Pin.OUT),
}

# Set up Buttons on GPIO 10, 11 ,12
sitches = {
   "SW1" : Pin(10, Pin.IN, Pin.PULL_DOWN),
   "SW2" : Pin(11, Pin.IN, Pin.PULL_DOWN),
   "SW3" : Pin(12, Pin.IN, Pin.PULL_DOWN),
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


def _queue_switch(sw_id, SW):
    _switch_events.append((sw_id, SW.value()))


def SW1Changed(SW):
    _queue_switch("SW1", SW)

def SW2Changed(SW):
    _queue_switch("SW2", SW)

def SW3Changed(SW):
    _queue_switch("SW3", SW)


def flushSwitchEvents():
    """Emit any queued switch events from normal (non-IRQ) context."""
    while _switch_events:
        sw_id, value = _switch_events.pop(0)
        emit('{"switches": [{"id": "%s", "value": %d}]}' % (sw_id, value))


#Setup button to activate IRQ handeling on input change
sitches["SW1"].irq(  trigger=Pin.IRQ_FALLING | Pin.IRQ_RISING ,
                handler=SW1Changed )

sitches["SW2"].irq(  trigger=Pin.IRQ_FALLING | Pin.IRQ_RISING ,
                handler=SW2Changed )

sitches["SW3"].irq(  trigger=Pin.IRQ_FALLING | Pin.IRQ_RISING ,
                handler=SW3Changed )


def applyJsonLedStates(payload):
    if len(payload) == 0:
        return

    try:
        payload = payload.lower()
        data = json.loads(payload)
    except (ValueError, TypeError):
        emit('{"ERROR" : "JSON not properly formated" }')
        #emit(payload)
        return

    #Return if key : "LEDS is missing" (This is not an error)
    led_list = data.get("leds") if isinstance(data, dict) else None


    # Accept both an object and an array:
    #   {"leds": {"id": "LED1", "value": 1}}         -> single object, wrap it
    #   {"leds": [{"id": "LED1", "value": 1}, ...]}  -> already a list
    if isinstance(led_list, dict):
        led_list = [led_list]


    if not isinstance(led_list, list):
        return

    for item in led_list:
        if not isinstance(item, dict):
            emit('{"ERROR": "JSON LEDS array is not an object"}')
            continue

        led_id = item.get("id")
        value = item.get("value")
        pin = leds.get(led_id.upper()) if isinstance(led_id, str) else None

        if pin is None:
            emit('{"ERROR": "JSON LEDS: unknown or missing LED id"}')   # this is the "bad ID" case
            continue
        if value not in (0, 1):
            emit('{"ERROR": "JSON LEDS: value must be 0 or 1"}')        # this is the "bad value" case
            continue

        #No errors update the LED value
        pin.value(value)


# Lines arriving over BLE are handled the same way as serial lines.
# NOTE: on_rx runs in BLE stack context (not a hardware IRQ), so calling
# emit() -> ble.sendline() from here is safe.
ble.on_rx(applyJsonLedStates)


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
    # Drain everything the OS has buffered right now.
    while _poll.poll(0):
        ch = _stdin.read(1)
        if not ch:
            break
        _serial_buf.extend(ch)
        if ch == b"\n":
            line = _serial_buf[:-1].rstrip(b"\r")
            del _serial_buf[:]
            try:
                return line.decode().strip()
            except Exception:
                return ""
    return None


while True:
    # 1) Emit any switch events captured by the IRQ handlers.
    flushSwitchEvents()

    # 2) Handle a serial command line if one is ready.
    serialInput = readSerialLine()
    if serialInput is not None:
        applyJsonLedStates(serialInput)

    time.sleep_ms(10)
