# SerialLedBtnLibEx

A C# **Windows Forms** example (.NET 10) for the course **PB1151 – Objektorientert programmering og databaser** at **USN**.

The app controls four LEDs and one servo, reads three pushbuttons, and reads two sensors (a TMP102 temperature sensor and an ADXL345 accelerometer) on a microcontroller connected over a serial port or Bluetooth Low Energy (BLE). It shows how to *use* an I/O library class (`JsonSerialController`) from a GUI — sending LED, servo, and sensor commands and reacting to switch and sensor events — without worrying about how the serial/JSON plumbing, or the transport underneath it, works internally.

![Application window](Images/AppScreen.png)

> The I/O library itself (`JsonSerialController`, `SerialPortEx`, `BleNusEx`) now lives in its own project, [`Usn.Pb1151.DeviceKit/`](Usn.Pb1151.DeviceKit/), so it can be published as a standalone NuGet package. This app references it as a project reference; see [`Usn.Pb1151.DeviceKit/README.md`](Usn.Pb1151.DeviceKit/README.md) for the library's own docs.

Each row pairs an **LED** checkbox (output, PC → device) with a **SW** checkbox (input, device → PC):

- Tick an `LED` box → that LED turns on.
- Press a physical button → its `SW` box updates automatically.

A **trackbar** drives the servo (output, PC → device):

- Drag the trackbar → the servo moves to that angle (0–180°).

## Connecting to the device

In `Form1_Load` the serial port is opened and handed to the controller. After that, you only ever talk to the controller — never the raw port:

```csharp
// Initialize the JsonSerialController using a serial port
_serialPort = new SerialPortEx { PortName = "COM3", BaudRate = 9600 };
_serialPort.Open();
jsonSerialController = new JsonSerialController(_serialPort);

// Lister for switch events from the device
jsonSerialController.ButtonControl["SW1"].ValueChanged += Form1_SwitchStateChanged;

// Turn LED1 on
jsonSerialController.LedControl["LED1"].Value = 1;

// Move the servo to 90 degrees
jsonSerialController.ServoControl["SV1"].Angle = 90;
```

What each line does:

- **`new SerialPortEx { PortName = "COM3", BaudRate = 9600 }`** — opens COM3 at 9600 baud. Change `COM3` to match the port your board uses.
- **`new JsonSerialController(_serialPort)`** — wraps the port. The controller exposes four helpers: `LedControl` and `ServoControl` (outgoing), `ButtonControl` (incoming), and `SensorControl` (request/response).
- **`ButtonControl["SW1"].ValueChanged += ...`** — subscribes to a single switch by name. When `SW1` changes on the hardware, your handler runs. Do the same for `SW2` and `SW3`.
- **`LedControl["LED1"].Value = 1`** — turns `LED1` on (`0` = off, `1` = on). See [Controlling an LED](#controlling-an-led-pc--device) below.
- **`ServoControl["SV1"].Angle = 90`** — moves servo `SV1` to 90°. See [Controlling a servo](#controlling-a-servo-pc--device) below.

### Connecting over BLE instead

`BleNusEx` implements the same `ISerialDataReadWrite` interface as `SerialPortEx`, so it is a drop-in replacement — `JsonSerialController` cannot tell the difference:

```csharp
BleNusEx _serialDev = new BleNusEx("Pico-NUS");
await _serialDev.ConnectAsync(TimeSpan.FromSeconds(15));
jsonSerialController = new JsonSerialController(_serialDev);
```

- **`new BleNusEx("Pico-NUS")`** — the constructor takes the name your board advertises over BLE. Change `"Pico-NUS"` to match your board.
- **`ConnectAsync(TimeSpan.FromSeconds(15))`** — scans for the device, connects, and subscribes to notifications on the Nordic UART Service (NUS). Throws `BleConnectionException` if Bluetooth is off or the device isn't found within the timeout.
- Everything downstream — `LedControl`, `ServoControl`, `ButtonControl`, `SensorControl` — is unchanged, since both transports satisfy `ISerialDataReadWrite`.

`Form1.cs` has both connection methods; only one is active — comment out the one you are not using.

## Reacting to a switch (device → PC)

The handler receives a `SwitchEventArgs` with which switch changed (`Id`) and its new value:

```csharp
private void Form1_SwitchStateChanged(object? sender, SwitchEventArgs e)
{
    if (e.Id == "SW1")
        checkBox2.BeginInvoke(() => checkBox2.Checked = Convert.ToBoolean(e.Value));
}
```

> **Why `BeginInvoke`?** The serial data arrives on a background thread, but Windows Forms controls may only be touched from the GUI thread. `BeginInvoke` marshals the update back onto the UI thread. Updating the checkbox directly from the event would throw a cross-thread exception.

## Controlling an LED (PC → device)

Writing to an LED is a single assignment. The controller serializes it to JSON and sends it over the port for you:

```csharp
private void checkBox1_CheckedChanged(object sender, EventArgs e)
{
    // checkBox1.Checked returns a bool, so convert it to an int (0 or 1)
    jsonSerialController.LedControl["LED1"].Value = Convert.ToInt32(checkBox1.Checked);
}
```

- **`LedControl["LED1"]`** — look up an LED by name.
- **`.Value = ...`** — set it. `0` = off, `1` = on. Setting the value automatically sends the command; nothing happens if the value is unchanged.
- **`Convert.ToInt32(bool)`** — a checkbox gives `true`/`false`; the LED wants `0`/`1`.

## Controlling a servo (PC → device)

The trackbar drives the servo the same way a checkbox drives an LED:

```csharp
private void trackBar1_Scroll(object sender, EventArgs e)
{
    jsonSerialController.ServoControl["SV1"].Angle = trackBar1.Value;
    this.textBox1.Text = trackBar1.Value.ToString();
}
```

- **`ServoControl["SV1"]`** — look up the servo by name.
- **`.Angle = ...`** — set the target angle in degrees. The value is clamped to `0`–`180`; setting the angle it already has sends nothing.
- **`textBox1.Text = ...`** — just mirrors the trackbar value in the textbox; no serial traffic involved.

> To let a servo go slack when it's not needed (less jitter and heat), call `jsonSerialController.ServoControl.Release("SV1")` instead of setting an angle.

## Reading a sensor (PC → device → PC)

Sensors work in two steps: subscribe to the reading event, then ask the device to send data — either once or as a continuous stream:

```csharp
// Listen for temperature readings
jsonSerialController.SensorControl["TEMP1"].DataChanged += Form1_TempSensorDataChanged;

// Ask for one reading now
jsonSerialController.SensorControl.Read("TEMP1");

// Or stream the accelerometer every 50 ms — and stop it again
jsonSerialController.SensorControl.StartStream("ACC1", 50);
jsonSerialController.SensorControl.StopStream("ACC1");
```

The handler receives a `SensorEventArgs`; pattern-match `e.Sensor` to the concrete sensor type to get at its values:

```csharp
private void Form1_TempSensorDataChanged(object? sender, SensorEventArgs e)
{
    if (e.Sensor is TempSensor temp)
        textBox2.BeginInvoke(() => textBox2.Text = $"{temp.Value} {temp.Unit}");
}
```

- **`SensorControl["TEMP1"]`** — look up a sensor by name (`"TEMP1"` = TMP102 temperature, `"ACC1"` = ADXL345 acceleration).
- **`.DataChanged += ...`** — runs your handler for *every* reading, whether it came from a one-shot `Read` or a stream tick. `TempSensor` exposes `Value` (°C); `AccSensor` exposes `X`, `Y`, `Z` (g).
- **`Read("TEMP1")`** — request a single reading; the answer arrives via the event.
- **`StartStream("ACC1", 50)` / `StopStream("ACC1")`** — the device sends readings on its own every 50 ms until told to stop.
- **`BeginInvoke`** — same rule as switches: readings arrive on a background thread, so GUI updates must be marshalled to the UI thread.

> If the hardware fails (e.g. an I²C sensor doesn't answer), the device reports an error code such as `i2c_nack`. Subscribe to `jsonSerialController.SensorControl.SensorError` to catch these instead of silently getting no reading.

## Running it

Requirements: **.NET 10 SDK** on **Windows**.

```bash
dotnet run
```

Or open `SerialLedBtnLibEx.csproj` in Visual Studio and press **F5**.

> Set the COM port in `Form1.cs` to match your board, and close any serial console (e.g. Thonny) first — only one program can hold the port at a time. If you're connecting over BLE instead, no COM port is needed — just make sure Bluetooth is turned on and the board is advertising.
