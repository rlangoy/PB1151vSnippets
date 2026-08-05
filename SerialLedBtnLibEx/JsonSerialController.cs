using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SerialLedBtnLibEx
{
    /// <summary>
    /// A single LED with an on/off (or brightness) value.
    /// Raises <see cref="ValueChanged"/> only when the value actually changes.
    /// </summary>
    public class LED
    {
        private int _value;

        public LED(string id, int value = 0)
        {
            Id = id;
            _value = value; // set the field directly so the event does not fire during construction
        }

        [JsonPropertyName("id")]
        public string Id { get; }

        [JsonPropertyName("value")]
        public int Value
        {
            get => _value;
            set
            {
                if (_value == value)
                    return; // no change -> no event, no serial traffic

                _value = value;
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler? ValueChanged;
    }

    /// <summary>
    /// Owns a fixed set of LEDs and pushes their state to the device as JSON
    /// whenever an LED changes.
    /// </summary>
    public class LedControl
    {
        private readonly ISerialDataReadWrite _serialPort;
        private bool _suppressUpdates;

        public LedControl(ISerialDataReadWrite serialPort)
        {
            _serialPort = serialPort;

            foreach (LED led in Leds)
                led.ValueChanged += OnLedValueChanged;
        }

        [JsonPropertyName("leds")]
        public List<LED> Leds { get; } = new()
        {
            new LED("LED1"),
            new LED("LED2"),
            new LED("LED3"),
            new LED("LED4"),
        };

        /// <summary>Indexer access, e.g. <c>control["LED1"].Value = 1;</c></summary>
        public LED this[string id] =>
            Leds.FirstOrDefault(led => led.Id == id)
            ?? throw new KeyNotFoundException($"No LED with id '{id}'.");

        public void AllOn() => SetAll(1);

        public void AllOff() => SetAll(0);

        public string ToJson() => JsonSerializer.Serialize(this);

        // Set every LED, then send a single update
        //    (Suppress updates while changing values )
        private void SetAll(int value)
        {
            _suppressUpdates = true;
            try
            {
                foreach (LED led in Leds)
                    led.Value = value;
            }
            finally
            {
                _suppressUpdates = false;
            }

            UpdateOverSerial();
        }

        private void OnLedValueChanged(object? sender, EventArgs e)
        {
            if (!_suppressUpdates)
                UpdateOverSerial();
        }

        private void UpdateOverSerial()
        {
            string json = ToJson();
            _serialPort.WriteLine(json);
            System.Diagnostics.Debug.WriteLine($"LedControl::UpdateOverSerial() -> {json}");
        }
    }

    /// <summary>
    /// A single servo with a target angle in degrees (0-180).
    /// Raises <see cref="ValueChanged"/> only when the (clamped) angle changes.
    /// </summary>
    public class Servo
    {
        private int _angle;

        public Servo(string id, int angle = 0)
        {
            Id = id;
            _angle = Clamp(angle); // set the field directly so the event does not fire during construction
        }

        [JsonPropertyName("id")]
        public string Id { get; }

        [JsonPropertyName("angle")]
        public int Angle
        {
            get => _angle;
            set
            {
                int clamped = Clamp(value);
                if (_angle == clamped)
                    return; // no change (incl. out-of-range that clamps to current) -> no traffic

                _angle = clamped;
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler? ValueChanged;

        private static int Clamp(int angle) => Math.Clamp(angle, 0, 180);
    }

    /// <summary>
    /// Owns a fixed set of servos and pushes angle / release commands to the
    /// device as JSON. Mirrors the device-side "servos" protocol:
    /// <code>
    /// {"servos": [{"id": "SV1", "angle": 90}]}
    /// {"servos": [{"id": "SV1", "action": "release"}]}
    /// </code>
    /// </summary>
    public class ServoControl
    {
        private readonly ISerialDataReadWrite _serialPort;
        private bool _suppressUpdates;

        public ServoControl(ISerialDataReadWrite serialPort)
        {
            _serialPort = serialPort;

            foreach (Servo servo in Servos)
                servo.ValueChanged += OnServoValueChanged;
        }

        [JsonPropertyName("servos")]
        public List<Servo> Servos { get; } = new()
        {
            new Servo("SV1"),
        };

        /// <summary>Indexer access, e.g. <c>control["SV1"].Angle = 90;</c></summary>
        public Servo this[string id] =>
            Servos.FirstOrDefault(servo => servo.Id == id)
            ?? throw new KeyNotFoundException($"No servo with id '{id}'.");

        /// <summary>Full snapshot of every servo as an angle message.</summary>
        public string ToJson() => JsonSerializer.Serialize(this);

        /// <summary>Centre all servos (90 deg).</summary>
        public void Center() => SetAll(90);

        /// <summary>Move every servo to the same angle, then send one combined update.</summary>
        public void SetAll(int angle)
        {
            _suppressUpdates = true;
            try
            {
                foreach (Servo servo in Servos)
                    servo.Angle = angle;
            }
            finally
            {
                _suppressUpdates = false;
            }

            SendServos(Servos);
        }

        /// <summary>
        /// Release a servo so it stops holding torque (drops jitter/heat when idle).
        /// The device re-attaches automatically on the servo's next angle command.
        /// </summary>
        public void Release(string id) => Release(this[id]);

        public void Release(Servo servo)
        {
            // Release is an action, not a position, so it is sent on its own
            // rather than as part of the angle snapshot.
            string json = JsonSerializer.Serialize(new
            {
                servos = new[] { new { id = servo.Id, action = "release" } }
            });
            Write(json);
        }

        private void OnServoValueChanged(object? sender, EventArgs e)
        {
            if (_suppressUpdates)
                return;

            // Send only the servo that changed, so releasing one servo is not
            // undone by re-commanding the others back to their last angle.
            if (sender is Servo servo)
                SendServos(new List<Servo> { servo });
        }

        private void SendServos(IEnumerable<Servo> servos)
        {
            string json = JsonSerializer.Serialize(new ServoMessage { Servos = servos.ToList() });
            Write(json);
        }

        private void Write(string json)
        {
            _serialPort.WriteLine(json);
            System.Diagnostics.Debug.WriteLine($"ServoControl::Write() -> {json}");
        }

        // DTO used only for serializing outgoing angle messages.
        private class ServoMessage
        {
            [JsonPropertyName("servos")]
            public List<Servo> Servos { get; set; } = new();
        }
    }

    /// <summary>
    /// Event data for a switch value change.
    /// </summary>
    public class SwitchEventArgs : EventArgs
    {
        public SwitchEventArgs(string id, int value)
        {
            Id = id;
            Value = value;
        }

        public string Id { get; }
        public int Value { get; }
    }

    /// <summary>
    /// A single hardware switch (button). Raises <see cref="ValueChanged"/>
    /// only when its value actually changes.
    /// </summary>
    public class Switch
    {
        private int _value;

        public Switch(string id, int value = 0)
        {
            Id = id;
            _value = value;
        }

        [JsonPropertyName("id")]
        public string Id { get; }

        [JsonPropertyName("value")]
        public int Value
        {
            get => _value;
            set
            {
                if (_value == value)
                    return; // no change -> no event

                _value = value;
                ValueChanged?.Invoke(this, new SwitchEventArgs(Id, _value));
            }
        }

        /// <summary>Raised when this switch's value changes.</summary>
        public event EventHandler<SwitchEventArgs>? ValueChanged;
    }

    /// <summary>
    /// Owns a fixed set of switches and parses incoming JSON from the device.
    /// Subscribe to per-switch changes via the indexer, e.g.:
    /// <code>buttonControl["SW1"].ValueChanged += OnSw1Changed;</code>
    /// </summary>
    public class ButtonControl
    {
        private readonly ISerialDataReadWrite _serialPort;
        private readonly LineBuffer _lineBuffer = new();
        private readonly object _receiveLock = new();

        /// <param name="subscribeToPort">
        /// Pass false when another object (e.g. <see cref="JsonSerialController"/>)
        /// reads the port and dispatches lines to <see cref="ProcessLine"/>.
        /// Two subscribers draining the same port would steal each other's data.
        /// </param>
        public ButtonControl(ISerialDataReadWrite serialPort, bool subscribeToPort = true)
        {
            _serialPort = serialPort;
            if (subscribeToPort)
                _serialPort.DataReceived += OnDataReceived;
        }

        [JsonPropertyName("switches")]
        public List<Switch> Switches { get; } = new()
        {
            new Switch("SW1"),
            new Switch("SW2"),
            new Switch("SW3"),
            new Switch("SW4"),
        };

        /// <summary>
        /// Indexer access, e.g. <c>buttonControl["SW1"].ValueChanged += handler;</c>
        /// </summary>
        public Switch this[string id] =>
            Switches.FirstOrDefault(sw => sw.Id == id)
            ?? throw new KeyNotFoundException($"No switch with id '{id}'.");

        /// <summary>
        /// Raised for any switch change. Inspect <see cref="SwitchEventArgs.Id"/>
        /// to know which switch changed.
        /// </summary>
        public event EventHandler<SwitchEventArgs>? SwitchChanged;

        private void OnDataReceived(object? sender, SerialDataReceivedEventArgs e)
        {
            // DataReceived can fire on several thread-pool threads at once;
            // the lock keeps reads and line handling in order.
            lock (_receiveLock)
            {
                // ReadExisting can return several lines at once, or stop
                // mid-line; the buffer hands back only complete lines.
                foreach (string line in _lineBuffer.AddData(_serialPort.ReadExisting()))
                    ProcessLine(line);
            }
        }

        /// <summary>
        /// Parses a single JSON line. Only lines describing switches
        /// (i.e. starting a "switches" array) are handled.
        /// </summary>
        public void ProcessLine(string json)
        {
            // Only parse switch messages.
            if (!json.Contains("\"switches\""))
                return;

            SwitchMessage? message;
            try
            {
                message = JsonSerializer.Deserialize<SwitchMessage>(json);
            }
            catch (JsonException)
            {
                return; // ignore malformed input
            }

            if (message?.Switches == null)
                return;

            foreach (SwitchState state in message.Switches)
            {
                if (string.IsNullOrEmpty(state.Id))
                    continue;

                Switch? sw = Switches.FirstOrDefault(s => s.Id == state.Id);
                if (sw == null)
                    continue;

                bool changed = sw.Value != state.Value;
                sw.Value = state.Value; // raises Switch.ValueChanged if it actually changed

                if (changed)
                    SwitchChanged?.Invoke(this, new SwitchEventArgs(state.Id, state.Value));
            }
        }

        // DTOs used only for deserializing the incoming JSON.
        private class SwitchMessage
        {
            [JsonPropertyName("switches")]
            public List<SwitchState>? Switches { get; set; }
        }

        private class SwitchState
        {
            [JsonPropertyName("id")]
            public string? Id { get; set; }

            [JsonPropertyName("value")]
            public int Value { get; set; }
        }
    }

    /// <summary>
    /// Event data for a sensor reading.
    /// </summary>
    public class SensorEventArgs : EventArgs
    {
        public SensorEventArgs(Sensor sensor)
        {
            Sensor = sensor;
        }

        public Sensor Sensor { get; }
        public string Id => Sensor.Id;
    }

    /// <summary>
    /// Event data for a sensor error reported by the device, e.g. "i2c_nack".
    /// </summary>
    public class SensorErrorEventArgs : EventArgs
    {
        public SensorErrorEventArgs(string id, string error)
        {
            Id = id;
            Error = error;
        }

        public string Id { get; }
        public string Error { get; }
    }

    /// <summary>
    /// Base class for a single device sensor. Subclasses parse the
    /// sensor-specific "value" payload. Raises <see cref="DataChanged"/>
    /// for every reading (one-shot reads and stream ticks share one shape).
    /// </summary>
    public abstract class Sensor
    {
        protected Sensor(string id, string unit)
        {
            Id = id;
            Unit = unit;
        }

        [JsonPropertyName("id")]
        public string Id { get; }

        /// <summary>Measurement unit, e.g. "C" or "g" (updated from responses).</summary>
        [JsonPropertyName("unit")]
        public string Unit { get; private set; }

        /// <summary>
        /// Raised for every reading received from the device. Carries a
        /// <see cref="SensorEventArgs"/> so handlers can reach the sensor
        /// via <see cref="SensorEventArgs.Sensor"/> without casting sender.
        /// </summary>
        public event EventHandler<SensorEventArgs>? DataChanged;

        /// <summary>Raised when the device reports an error for this sensor.</summary>
        public event EventHandler<SensorErrorEventArgs>? ErrorReceived;

        /// <summary>
        /// Parses and stores a "value" payload from the device.
        /// Raises <see cref="DataChanged"/> when the payload is valid.
        /// </summary>
        public bool TryApplyReading(JsonElement value, string? unit)
        {
            if (!TryParseValue(value))
                return false; // wrong shape for this sensor -> ignore

            if (!string.IsNullOrEmpty(unit))
                Unit = unit;

            DataChanged?.Invoke(this, new SensorEventArgs(this));
            return true;
        }

        public void ApplyError(string error) =>
            ErrorReceived?.Invoke(this, new SensorErrorEventArgs(Id, error));

        protected abstract bool TryParseValue(JsonElement value);
    }

    /// <summary>
    /// TMP102 temperature sensor. Reading:
    /// <code>{"sensors": [{"id": "TEMP1", "value": 23.56, "unit": "C"}]}</code>
    /// </summary>
    public class TempSensor : Sensor
    {
        public TempSensor(string id) : base(id, "C")
        {
        }

        /// <summary>Last received temperature in <see cref="Sensor.Unit"/>.</summary>
        public double Value { get; private set; }

        protected override bool TryParseValue(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Number)
                return false;

            Value = value.GetDouble();
            return true;
        }
    }

    /// <summary>
    /// ADXL345 acceleration sensor. Reading:
    /// <code>{"sensors": [{"id": "ACC1", "value": {"x": 0.012, "y": -0.004, "z": 0.998}, "unit": "g"}]}</code>
    /// </summary>
    public class AccSensor : Sensor
    {
        public AccSensor(string id) : base(id, "g")
        {
        }

        /// <summary>Last received acceleration in <see cref="Sensor.Unit"/>.</summary>
        public double X { get; private set; }
        public double Y { get; private set; }
        public double Z { get; private set; }

        protected override bool TryParseValue(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Object
                || !value.TryGetProperty("x", out JsonElement x)
                || !value.TryGetProperty("y", out JsonElement y)
                || !value.TryGetProperty("z", out JsonElement z))
                return false;

            X = x.GetDouble();
            Y = y.GetDouble();
            Z = z.GetDouble();
            return true;
        }
    }

    /// <summary>
    /// Owns a fixed set of sensors. Sends read/stream/stop requests and parses
    /// incoming sensor JSON from the device. Mirrors the device-side protocol:
    /// <code>
    /// {"sensors": [{"id": "TEMP1", "read": 1}]}
    /// {"sensors": [{"id": "ACC1", "action": "stream", "interval_ms": 50}]}
    /// {"sensors": [{"id": "ACC1", "action": "stop"}]}
    /// </code>
    /// Subscribe to per-sensor readings via the indexer, e.g.:
    /// <code>sensorControl["TEMP1"].DataChanged += OnTempReading;</code>
    /// </summary>
    public class SensorControl
    {
        private readonly ISerialDataReadWrite _serialPort;
        private readonly LineBuffer _lineBuffer = new();
        private readonly object _receiveLock = new();

        /// <param name="subscribeToPort">
        /// Pass false when another object (e.g. <see cref="JsonSerialController"/>)
        /// reads the port and dispatches lines to <see cref="ProcessLine"/>.
        /// Two subscribers draining the same port would steal each other's data.
        /// </param>
        public SensorControl(ISerialDataReadWrite serialPort, bool subscribeToPort = true)
        {
            _serialPort = serialPort;
            if (subscribeToPort)
                _serialPort.DataReceived += OnDataReceived;
        }

        [JsonPropertyName("sensors")]
        public List<Sensor> Sensors { get; } = new()
        {
            new TempSensor("TEMP1"),
            new AccSensor("ACC1"),
        };

        /// <summary>Indexer access, e.g. <c>sensorControl["TEMP1"].DataChanged += handler;</c></summary>
        public Sensor this[string id] =>
            FindSensor(id)
            ?? throw new KeyNotFoundException($"No sensor with id '{id}'.");

        /// <summary>
        /// Raised for any sensor reading. Inspect <see cref="SensorEventArgs.Sensor"/>
        /// to know which sensor was updated.
        /// </summary>
        public event EventHandler<SensorEventArgs>? SensorChanged;

        /// <summary>Raised when the device reports a sensor error, e.g. "i2c_nack".</summary>
        public event EventHandler<SensorErrorEventArgs>? SensorError;

        /// <summary>Request a single reading of one sensor.</summary>
        public void Read(string id) =>
            Send(new { id = this[id].Id, read = 1 });

        /// <summary>Start streaming a sensor at the given interval.</summary>
        public void StartStream(string id, int intervalMs) =>
            Send(new { id = this[id].Id, action = "stream", interval_ms = intervalMs });

        /// <summary>Stop streaming a sensor.</summary>
        public void StopStream(string id) =>
            Send(new { id = this[id].Id, action = "stop" });

        /// <summary>
        /// Parses a single JSON line. Only lines describing sensors
        /// (i.e. containing a "sensors" array) are handled.
        /// </summary>
        public void ProcessLine(string json)
        {
            // Only parse sensor messages.
            if (!json.Contains("\"sensors\""))
                return;

            SensorMessage? message;
            try
            {
                message = JsonSerializer.Deserialize<SensorMessage>(json);
            }
            catch (JsonException)
            {
                return; // ignore malformed input
            }

            if (message?.Sensors == null)
                return;

            foreach (SensorState state in message.Sensors)
            {
                if (string.IsNullOrEmpty(state.Id))
                    continue;

                Sensor? sensor = FindSensor(state.Id);
                if (sensor == null)
                    continue;

                if (!string.IsNullOrEmpty(state.Error))
                {
                    sensor.ApplyError(state.Error); // raises Sensor.ErrorReceived
                    SensorError?.Invoke(this, new SensorErrorEventArgs(sensor.Id, state.Error));
                    continue;
                }

                if (state.Value.ValueKind != JsonValueKind.Undefined
                    && sensor.TryApplyReading(state.Value, state.Unit))
                {
                    SensorChanged?.Invoke(this, new SensorEventArgs(sensor));
                }
            }
        }

        private void OnDataReceived(object? sender, SerialDataReceivedEventArgs e)
        {
            // DataReceived can fire on several thread-pool threads at once;
            // the lock keeps reads and line handling in order.
            lock (_receiveLock)
            {
                // ReadExisting can return several lines at once, or stop
                // mid-line; the buffer hands back only complete lines.
                foreach (string line in _lineBuffer.AddData(_serialPort.ReadExisting()))
                    ProcessLine(line);
            }
        }

        // Ids are matched case-insensitively because the device lowercases
        // incoming lines before parsing.
        private Sensor? FindSensor(string id) =>
            Sensors.FirstOrDefault(sensor =>
                string.Equals(sensor.Id, id, StringComparison.OrdinalIgnoreCase));

        private void Send(object command)
        {
            string json = JsonSerializer.Serialize(new { sensors = new[] { command } });
            _serialPort.WriteLine(json);
            System.Diagnostics.Debug.WriteLine($"SensorControl::Send() -> {json}");
        }

        // DTOs used only for deserializing the incoming JSON.
        private class SensorMessage
        {
            [JsonPropertyName("sensors")]
            public List<SensorState>? Sensors { get; set; }
        }

        private class SensorState
        {
            [JsonPropertyName("id")]
            public string? Id { get; set; }

            [JsonPropertyName("value")]
            public JsonElement Value { get; set; } // number (temp) or {x,y,z} object (acc)

            [JsonPropertyName("unit")]
            public string? Unit { get; set; }

            [JsonPropertyName("error")]
            public string? Error { get; set; }
        }
    }

    /// <summary>
    /// Accumulates raw serial data and yields only complete lines (terminated
    /// by '\n'), keeping any trailing partial line until the rest arrives.
    /// DataReceived fires as soon as bytes arrive, so ReadExisting can end
    /// mid-line; parsing such a fragment as JSON would silently fail.
    /// </summary>
    internal sealed class LineBuffer
    {
        private string _pending = "";

        /// <summary>Appends a chunk and returns the complete, non-empty, trimmed lines.</summary>
        public IEnumerable<string> AddData(string data)
        {
            _pending += data;

            int lastNewline = _pending.LastIndexOf('\n');
            if (lastNewline < 0)
                return Array.Empty<string>(); // no complete line yet -> wait for more data

            string complete = _pending.Substring(0, lastNewline);
            _pending = _pending.Substring(lastNewline + 1);

            return complete.Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0);
        }
    }

    public interface ISerialDataReadWrite
    {
        void WriteLine(string text);
        string ReadExisting();
        event SerialDataReceivedEventHandler DataReceived;
    }

    // This class inherits from SerialPort and implements ISerialDataReadWrite, so it can be used in place as a SerialPort object
    public sealed class SerialPortEx : SerialPort, ISerialDataReadWrite
    {
        public SerialPortEx()
        {
            // Forward the native SerialPort.DataReceived event to ISerialDataReadWrite subscribers.
            base.DataReceived += (s, e) => _dataReceived?.Invoke(s, e);
        }

        private SerialDataReceivedEventHandler? _dataReceived;

        event SerialDataReceivedEventHandler ISerialDataReadWrite.DataReceived
        {
            add => _dataReceived += value;
            remove => _dataReceived -= value;
        }
    }

    /// <summary>
    /// Top-level controller. Owns the LED and servo (outgoing) and the
    /// button/switch and sensor (incoming) sides of the JSON serial protocol.
    /// </summary>
    public class JsonSerialController
    {
        private readonly ISerialDataReadWrite _serialPort;
        private readonly LineBuffer _lineBuffer = new();
        private readonly object _receiveLock = new();

        //For serial port. Use the class SerialPortEx to implement ISerialDataReadWrite interface.
        public JsonSerialController(ISerialDataReadWrite serialReadWriter)
        {
            _serialPort = serialReadWriter;

            // This controller is the ONLY reader of the port: it drains the
            // data and hands every complete line to all incoming-message
            // parsers. The controls must not subscribe themselves — a second
            // subscriber calling ReadExisting would steal lines meant for the
            // other parser (DataReceived events can even run concurrently).
            _serialPort.DataReceived += OnDataReceived;

            LedControl = new LedControl(_serialPort);
            ServoControl = new ServoControl(_serialPort);
            ButtonControl = new ButtonControl(_serialPort, subscribeToPort: false);
            SensorControl = new SensorControl(_serialPort, subscribeToPort: false);
        }

        public LedControl LedControl { get; }

        public ServoControl ServoControl { get; }

        public ButtonControl ButtonControl { get; }

        public SensorControl SensorControl { get; }

        private void OnDataReceived(object? sender, SerialDataReceivedEventArgs e)
        {
            // DataReceived can fire on several thread-pool threads at once;
            // the lock keeps reads and line handling in order.
            lock (_receiveLock)
            {
                // ReadExisting can return several lines at once, or stop
                // mid-line; the buffer hands back only complete lines.
                foreach (string line in _lineBuffer.AddData(_serialPort.ReadExisting()))
                {
                    ButtonControl.ProcessLine(line);
                    SensorControl.ProcessLine(line);
                }
            }
        }
    }
}