# Usn.Pb1151.DeviceKit

A small C# I/O library for the **PB1151 – Objektorientert programmering og databaser** course at **USN**.

It talks to a microcontroller board (4 LEDs, 1 servo, 3 pushbuttons, a TMP102 temperature sensor, and an ADXL345 accelerometer) over a line-based JSON protocol, connected either via a USB serial port or Bluetooth Low Energy (Nordic UART Service). You use it as an object-oriented API — setting properties and subscribing to events — without touching JSON or the raw port yourself.

## Install

```bash
dotnet add package Usn.Pb1151.DeviceKit
```

Requires **.NET 10** on **Windows** (the BLE transport uses the Windows Runtime Bluetooth APIs).

## Quick start

```csharp
using Usn.Pb1151.DeviceKit;

// Connect over USB serial...
var port = new SerialPortEx { PortName = "COM3", BaudRate = 9600 };
port.Open();
var controller = new JsonSerialController(port);

// ...or over BLE instead — same controller API either way.
// var ble = new BleNusEx("Pico-NUS");
// await ble.ConnectAsync(TimeSpan.FromSeconds(15));
// var controller = new JsonSerialController(ble);

// Turn LED1 on
controller.LedControl["LED1"].Value = 1;

// Move servo SV1 to 90 degrees
controller.ServoControl["SV1"].Angle = 90;

// React to a pushbutton
controller.ButtonControl["SW1"].ValueChanged += (s, e) =>
    Console.WriteLine($"{e.Id} = {e.Value}");

// Read the temperature sensor once
controller.SensorControl["TEMP1"].DataChanged += (s, e) =>
{
    var temp = (TempSensor)e.Sensor;
    Console.WriteLine($"{temp.Value} {temp.Unit}");
};
controller.SensorControl.Read("TEMP1");
```

## What's in the box

| Type | Purpose |
|---|---|
| `JsonSerialController` | Entry point. Owns `LedControl`, `ServoControl`, `ButtonControl`, `SensorControl` over one shared port. |
| `SerialPortEx` | `ISerialDataReadWrite` over a USB `SerialPort`. |
| `BleNusEx` | `ISerialDataReadWrite` over BLE (Nordic UART Service). Drop-in replacement for `SerialPortEx`. |
| `ISerialDataReadWrite` | The transport abstraction — implement it yourself to plug in a different transport or a test double. |

Full API reference, JSON wire protocol, and sequence diagrams: see [`JsonSerialController.md`](JsonSerialController.md).

## Example project

A complete Windows Forms demo that consumes this library lives in the parent repository (`SerialLedBtnLibEx`) — see its `README.md` for a walkthrough of wiring the controller up to a GUI.
