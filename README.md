# PB1151 – Code Snippets

Code snippets for the course **PB1151 – Objektorientert programmering og databaser** (Object-Oriented Programming and Databases) at the **University of South-Eastern Norway (USN)**.

The snippets demonstrate object-oriented programming concepts in C# by talking to real hardware: a Windows desktop app controls LEDs and a servo, and reads pushbuttons and I2C sensors, on a microcontroller over a serial port or BLE, using a small JSON-based protocol.

## What's in here

| Path | Description |
|------|-------------|
| `SerialLedBtnLibEx/` | C# WinForms host application (.NET 10). Controls LEDs and a servo, and mirrors switch state, over RS232 or BLE using JSON. |
| `Firmware/Micropython/jsonSerialAndBLE/` | Full-featured MicroPython firmware: LEDs, servo, switches, and I2C sensors (TMP102, ADXL345), served over **both** USB serial and BLE (Nordic UART Service) at once. |
| `Firmware/Micropython/snipplets/` | Smaller standalone examples (LED-only test, basic serial-only JSON, plain BLE echo) that the full firmware builds on. |

### Firmware

| File | Description |
|------|-------------|
| `jsonSerialAndBLE/serialBleJSONLedAndSwitches.py` | Full JSON protocol over serial **and** BLE simultaneously. LEDs, servo control, switch events, and one-shot/streaming I2C sensor reads (TMP102, ADXL345). Pair this one with the C# app. |
| `jsonSerialAndBLE/ble_nus.py` | BLE Nordic UART Service (NUS) transport used by the firmware above. |
| `jsonSerialAndBLE/tmp102.py` / `jsonSerialAndBLE/adxl345.py` | I2C drivers for the temperature sensor and 3-axis accelerometer. |
| `jsonSerialAndBLE/sensor_service.py` | Owns per-sensor stream timers and formats sensor request/response JSON. |
| `snipplets/serialJSONLedAndSwitches.py` | Earlier, serial-only version of the JSON protocol (LEDs + switches, no servo/BLE/sensors). |
| `snipplets/LED_Test.py` | Minimal standalone test. Mirrors each switch directly to its matching LED on the board — no PC needed. |

## Hardware

The firmware targets a board with:

- **4 LEDs** on GPIO 6, 7, 8, 9 → `LED1`–`LED4`
- **4 switches** on GPIO 10, 11, 12, 16 → `SW1`–`SW3` (pushbuttons, pull-down) and `SW4` (e.g. a proximity/IR sensor; value is inverted so "object near" reads as `1`)
- **1 servo** (SG90-style, PWM) on GPIO 21 → `SV1`
- **I2C sensors** on I2C0 (SDA = GP4, SCL = GP5): a TMP102 temperature sensor (`TEMP1`) and an ADXL345 3-axis accelerometer (`ACC1`)

Switches use interrupts (IRQ on both rising and falling edges), so both press and release are reported. Sensor drivers initialize lazily, so booting with sensors unplugged is fine — errors surface per request instead.

## The JSON serial protocol

Communication is line-based: one JSON object per line, terminated by a newline. The full firmware (`jsonSerialAndBLE/serialBleJSONLedAndSwitches.py`) parses identical JSON on **both** the USB serial console and a BLE NUS connection, and sends every response out on both transports. Default serial port settings are **COM3 @ 9600 baud** (change `COM3` in `Form1.cs` to match your machine); the board advertises over BLE as `Pico-NUS`.

PC → device (set LEDs).** A single LED or an array; LEDs not listed are left unchanged:

```json
{"leds": [{"id": "LED1", "value": 1}, {"id": "LED4", "value": 0}]}
```

PC → device (move/release a servo).** Angle is clamped to 0–180°; `release` drops holding torque:

```json
{"servos": [{"id": "SV1", "angle": 90}]}
{"servos": [{"id": "SV1", "action": "release"}]}
```

Device → PC (switch changed).** Sent on every edge, so a press then release produces two lines:

```json
{"switches": [{"id": "SW1", "value": 1}]}
{"switches": [{"id": "SW1", "value": 0}]}
```

PC → device (read or stream a sensor).** One-shot reads and stream ticks share the same response shape:

```json
{"sensors": [{"id": "TEMP1", "read": 1}]}
```
Device → PC (response)

```json
{"sensors": [{"id": "TEMP1", "value": 23.56, "unit": "C"}]}
```

PC → device (start/stop stream a sensor).

```json
{"sensors": [{"id": "ACC1", "action": "stream", "interval_ms": 50}]}
{"sensors": [{"id": "ACC1", "value": {"x": 0.012, "y": -0.004, "z": 0.998}, "unit": "g"}]}
{"sensors": [{"id": "ACC1", "action": "stop"}]}
```

Device → PC (sensor stream relpies).

```json
{"sensors": [{"id": "ACC1", "value": {"x": 0.012, "y": -0.004, "z": 0.998}, "unit": "g"}]}
```
Collections (`leds`, `servos`, `sensors`) can be combined in a single line, and sensor failures are reported per `id` (e.g. `{"sensors": [{"id": "TEMP1", "error": "i2c_nack"}]}`) instead of a top-level failure. See `Firmware/Micropython/jsonSerialAndBLE/sensor-protocol-design-spec.md` for the full sensor protocol reference.

## C# project structure

The host app is organized to highlight object-oriented design:

- **`LED` / `Switch`** — model a single hardware element. Each wraps a value behind a property and raises a change event *only when the value actually changes*, so no redundant serial traffic is generated.
- **`LedControl`** — owns the set of LEDs, serializes them to JSON, and writes to the serial port whenever an LED changes. Supports `AllOn()` / `AllOff()` with update suppression so a batch change sends a single message.
- **`ButtonControl`** — owns the set of switches, parses incoming JSON, and raises per-switch events.
- **`JsonSerialController`** — top-level controller that composes `LedControl` (outgoing) and `ButtonControl` (incoming).
- **`Form1`** — the WinForms GUI. Checkboxes drive the LEDs; incoming switch events update checkboxes via `BeginInvoke` (the serial `DataReceived` handler runs on a background thread, so UI updates must be marshalled to the GUI thread).

Concepts illustrated: **classes and objects, encapsulation, properties, indexers, events and delegates, composition, and thread marshalling.**

## Getting started

### 1. Flash the firmware

1. Open the board in [Thonny](https://thonny.org/) (or your preferred MicroPython IDE).
2. Copy the whole `Firmware/Micropython/jsonSerialAndBLE/` folder to the board (the main script depends on `ble_nus.py`, `tmp102.py`, `adxl345.py`, and `sensor_service.py`).
3. Run `serialBleJSONLedAndSwitches.py`. Pressing a switch should print a JSON line to the console; the board also starts advertising over BLE as `Pico-NUS`.

> Only need LEDs and switches over serial, without BLE/servo/sensors? `Firmware/Micropython/snipplets/serialJSONLedAndSwitches.py` is the minimal predecessor version.

### 2. Run the C# app

Requirements: **.NET 10 SDK** and **Windows** (the project uses Windows Forms).

```bash
cd SerialLedBtnLibEx
dotnet run
```

Or open `SerialLedBtnLibEx.csproj` in Visual Studio and press **F5**.

> **Note:** Edit the COM port in `Form1.cs` (`new SerialPortEx { PortName = "COM3", BaudRate = 9600 }`) to match the port your board is connected to. Make sure the serial console (e.g. Thonny) is **closed** first — only one program can hold the port at a time.

Toggle the LED checkboxes to drive the LEDs; press the physical switches to see the SW checkboxes update.

## Course

University of South-Eastern Norway (USN) — PB1151 Objektorientert programmering og databaser.
