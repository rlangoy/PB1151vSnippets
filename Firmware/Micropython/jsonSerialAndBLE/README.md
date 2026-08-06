# jsonSerialAndBLE — MicroPython Firmware

Full-featured MicroPython firmware for a Raspberry Pi Pico–class (RP2040) board: LEDs, a servo, pushbutton switches, and I2C sensors (TMP102 temperature, ADXL345 accelerometer), all controlled through one line-based JSON protocol served **simultaneously** over USB serial and BLE (Nordic UART Service). It is the counterpart firmware for the `SerialLedBtnLibEx` C# host application in this repository — see the [top-level README](../../../README.md) for the overall project.

## Files

| File | Role |
|------|------|
| `serialBleJSONLedAndSwitches.py` | Entry point. Owns the hardware objects, the JSON command dispatch, and the main loop. Flash and run this file. |
| `ble_nus.py` | BLE Nordic UART Service (NUS) transport. Advertises as `Pico-NUS`, buffers and reassembles multi-packet writes, and exposes `send()`/`on_rx()`. |
| `tmp102.py` | I2C driver for the TMP102 temperature sensor. |
| `adxl345.py` | I2C driver for the ADXL345 3-axis accelerometer. |
| `sensor_service.py` | Protocol layer for the `sensors` collection: request dispatch, per-sensor stream timers, response/error formatting. |
| `sensor-protocol-design-spec.md` | Design specification for the `sensors` collection — field reference, error taxonomy, and open design decisions. |
| `ServoTest.py` | Standalone servo sweep test, independent of the JSON protocol. Useful for verifying servo wiring/PWM in isolation. |

## Hardware

| Peripheral | Pin(s) | Notes |
|---|---|---|
| `LED1`–`LED4` | GPIO 6, 7, 8, 9 | Digital outputs |
| `SW1`–`SW3` | GPIO 10, 11, 12 | Pushbuttons, pull-down, IRQ on both edges |
| `SW4` | GPIO 16 | E.g. a proximity/IR sensor; reported value is inverted so "object near" reads as `1` |
| `SV1` | GPIO 21 | SG90-style hobby servo, 50 Hz PWM, 500–2500 µs pulse range |
| `TEMP1` | I2C0 (SDA=GP4, SCL=GP5) | TMP102, address `0x48` |
| `ACC1` | I2C0 (SDA=GP4, SCL=GP5) | ADXL345, address `0x53`, ±2 g full-resolution mode |

Sensor drivers initialize lazily and never touch the bus at construction time, so booting with a sensor unplugged is safe — failures surface per request instead of at boot.

## Installing

1. Open the board in [Thonny](https://thonny.org/) (or your preferred MicroPython IDE/tool).
2. Copy this entire folder to the board's filesystem. `serialBleJSONLedAndSwitches.py` imports `ble_nus.py`, `tmp102.py`, `adxl345.py`, and `sensor_service.py`, so all four must be present alongside it.
3. Run `serialBleJSONLedAndSwitches.py` (or set it as `main.py` to run on boot). On start the board:
   - opens a non-blocking USB serial console, and
   - begins BLE advertising as **`Pico-NUS`**.

Only need LEDs and switches over plain serial, with no BLE/servo/sensors? See `../snipplets/serialJSONLedAndSwitches.py` — the minimal predecessor this firmware builds on.

## Interfacing with the firmware

Communication is **line-based JSON**: one JSON object per line, terminated with `\n`. Both transports (USB serial and BLE NUS) accept identical input and are treated as equivalent — every response, event, and error is written out to **both** transports at once, regardless of which one the request arrived on.

- **Serial:** USB-CDC, so baud rate is not enforced by the firmware. The host app in this repo connects at **COM3 @ 9600 baud**; match the port/baud in your serial client to the board.
- **BLE:** the board advertises as `Pico-NUS` using the standard Nordic UART Service UUIDs (`6E400001…`/`…0002`/`…0003`). Any NUS-compatible central can connect; write requests to the RX characteristic, subscribe to notifications on the TX characteristic. Each written chunk must end in `\n` — the transport reassembles multi-packet writes before parsing.
- Incoming lines are lowercased before parsing, so command keys, `id`s, and other tokens are case-insensitive on input. Don't rely on case to carry meaning in outgoing commands.
- A single line may combine any mix of `leds`, `servos`, and `sensors` keys; each collection is handled independently, and a malformed entry in one does not block the others.

### LEDs

Set one or more LEDs. LEDs not mentioned are left unchanged:

```json
{"leds": [{"id": "LED1", "value": 1}, {"id": "LED4", "value": 0}]}
```

A single object (not wrapped in an array) is also accepted:

```json
{"leds": {"id": "LED1", "value": 1}}
```

### Servo

Move `SV1` to an angle in degrees (clamped to 0–180):

```json
{"servos": [{"id": "SV1", "angle": 90}]}
```

Release the servo (drops PWM / holding torque and jitter until the next angle command):

```json
{"servos": [{"id": "SV1", "action": "release"}]}
```

### Switches (device → host, unsolicited)

Every state change on `SW1`–`SW4` is reported as it happens (both press and release, since IRQs trigger on both edges):

```json
{"switches": [{"id": "SW1", "value": 1}]}
{"switches": [{"id": "SW1", "value": 0}]}
```

### Sensors (`TEMP1`, `ACC1`)

One-shot read:

```json
{"sensors": [{"id": "TEMP1", "read": 1}]}
```

Response — TMP102 returns a scalar, ADXL345 returns an `{x, y, z}` triple; one-shot and streamed responses share the same shape:

```json
{"sensors": [{"id": "TEMP1", "value": 23.56, "unit": "C"}]}
{"sensors": [{"id": "ACC1", "value": {"x": 0.012, "y": -0.004, "z": 0.998}, "unit": "g"}]}
```

Start/stop a continuous stream at a per-sensor interval (minimum 10 ms, bounded by the ~10 ms main loop period):

```json
{"sensors": [{"id": "ACC1", "action": "stream", "interval_ms": 50}]}
{"sensors": [{"id": "ACC1", "action": "stop"}]}
```

Sensor failures are reported per `id` instead of failing the whole message, using a fixed error taxonomy (`i2c_timeout`, `i2c_nack`, `not_initialized`, `unknown_id`):

```json
{"sensors": [{"id": "TEMP1", "error": "i2c_nack"}]}
```

Multiple sensors, independent rates, combined in one request:

```json
{"sensors": [
  {"id": "TEMP1", "action": "stream", "interval_ms": 1000},
  {"id": "ACC1", "action": "stream", "interval_ms": 50}
]}
```

See `sensor-protocol-design-spec.md` for the full field reference and error taxonomy.

### Malformed input

Unparseable JSON, or a recognized collection with a malformed entry, produces a top-level error line rather than crashing the loop:

```json
{"ERROR": "JSON not properly formatted"}
```

## Design notes relevant to integrators

- **Both transports, one protocol.** `emit()` in `serialBleJSONLedAndSwitches.py` writes every outgoing line to `print()` (serial) and `ble.sendline()` (BLE) together, so a host only needs to implement one parser regardless of which transport it uses.
- **Non-blocking main loop.** The firmware never blocks: switch IRQs only queue events, sensor streams only mark timers due, and the serial reader polls without blocking — everything is drained and emitted from the main loop, at roughly a 10 ms period.
- **IRQ safety.** BLE notifications and I2C transactions are unsafe to call from a hardware interrupt context. Switch handlers therefore just enqueue `(id, value)` pairs in IRQ context; the main loop flushes and emits them afterward.
- **Reconnect behavior.** BLE advertising restarts automatically on disconnect. A host application should be tolerant of the board being connected over serial only, BLE only, or both at once.
