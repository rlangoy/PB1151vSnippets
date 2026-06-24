# PB1151 – Code Snippets

Code snippets for the course **PB1151 – Objektorientert programmering og databaser** (Object-Oriented Programming and Databases) at the **University of South-Eastern Norway (USN)**.

The snippets demonstrate object-oriented programming concepts in C# by talking to real hardware: a Windows desktop app controls LEDs and reads pushbuttons on a microcontroller over a serial port, using a small JSON-based protocol.

## What's in here

| Path | Description |
|------|-------------|
| `SerialLedBtnLibEx/` | C# WinForms host application (.NET 10). Controls LEDs and mirrors switch state over RS232 using JSON. |
| `Firmware/Micropython/` | MicroPython firmware for the microcontroller (Raspberry Pi Pico style board). |

### Firmware

| File | Description |
|------|-------------|
| `serialJSONLedAndSwitches.py` | Full JSON protocol. Reads LED commands from serial and prints switch changes as JSON. Pair this one with the C# app. |
| `LED_Test.py` | Minimal standalone test. Mirrors each switch directly to its matching LED on the board — no PC needed. |

## Hardware

The firmware targets a board with:

- **4 LEDs** on GPIO 6, 7, 8, 9 → `LED1`–`LED4`
- **3 switches** (pushbuttons, pull-down) on GPIO 10, 11, 12 → `SW1`–`SW3`

Switches use interrupts (IRQ on both rising and falling edges), so both press and release are reported.

## The JSON serial protocol

Communication is line-based: one JSON object per line, terminated by a newline. Default port settings are **COM3 @ 9600 baud** (change `COM3` in `Form1.cs` to match your machine).

**PC → device (set LEDs).** A single LED or an array; LEDs not listed are left unchanged:

```json
{"leds": [{"id": "LED1", "value": 1}, {"id": "LED4", "value": 0}]}
```

**Device → PC (switch changed).** Sent on every edge, so a press then release produces two lines:

```json
{"switches": [{"id": "SW1", "value": 1}]}
{"switches": [{"id": "SW1", "value": 0}]}
```

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
2. Copy `Firmware/Micropython/serialJSONLedAndSwitches.py` to the board.
3. Run it. Pressing a switch should print a JSON line to the console.

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
