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
    Debug.WriteLine($"{e.Id} = {e.Value}");
```

## Usage

```csharp
using Usn.Pb1151.DeviceKit;

// SerialPortEx implements ISerialDataReadWrite; a plain SerialPort does not.
var port = new SerialPortEx { PortName = "COM3", BaudRate = 9600 };
port.Open();
var controller = new JsonSerialController(port);

// Outgoing: turn LED1 on
controller.LedControl["LED1"].Value = 1;

// Outgoing: move SV1 to 90 degrees, then let it go slack
controller.ServoControl["SV1"].Angle = 90;
controller.ServoControl.Release("SV1");

// Incoming: react to SW1
controller.ButtonControl["SW1"].ValueChanged += (s, e) =>
{
    Debug.WriteLine($"{e.Id} = {e.Value}");
};

// Sensors: react to readings, then request them
controller.SensorControl["TEMP1"].DataChanged += (s, e) =>
{
    var temp = (TempSensor)e.Sensor;
    Debug.WriteLine($"{temp.Id}: {temp.Value} {temp.Unit}");
};
controller.SensorControl["ACC1"].DataChanged += (s, e) =>
{
    var acc = (AccSensor)e.Sensor;
    Debug.WriteLine($"{acc.Id}: x={acc.X} y={acc.Y} z={acc.Z} {acc.Unit}");
};
controller.SensorControl.SensorError += (s, e) =>
{
    Debug.WriteLine($"{e.Id} error: {e.Error}");
};

controller.SensorControl.Read("TEMP1");          // one-shot read
controller.SensorControl.StartStream("ACC1", 50); // stream every 50 ms
controller.SensorControl.StopStream("ACC1");      // stop the stream
```

`BleNusEx` implements `ISerialDataReadWrite` too, so it drops in unchanged:

```csharp
var ble = new BleNusEx("Pico-NUS");
await ble.ConnectAsync(TimeSpan.FromSeconds(15));
var controller = new JsonSerialController(ble);
```

## Library contents

| Type | Purpose |
|---|---|
| `JsonSerialController` | Entry point. Owns `LedControl`, `ServoControl`, `ButtonControl`, `SensorControl` over one shared port. |
| `SerialPortEx` | `ISerialDataReadWrite` over a USB `SerialPort`. |
| `BleNusEx` | `ISerialDataReadWrite` over BLE (Nordic UART Service). Drop-in replacement for `SerialPortEx`. |
| `ISerialDataReadWrite` | The transport abstraction — implement it yourself to plug in a different transport or a test double. |

## Documentation

Full API reference, JSON wire protocol, and sequence diagrams: [JsonSerialController.md](https://github.com/rlangoy/PB1151vSnippets/blob/main/SerialLedBtnLibEx/Usn.Pb1151.DeviceKit/JsonSerialController.md)

## Example project

A complete Windows Forms demo that consumes this library lives in the parent repository (`SerialLedBtnLibEx`) — see its `README.md` for a walkthrough of wiring the controller up to a GUI.
