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
        private readonly SerialPort _serialPort;
        private bool _suppressUpdates;

        public LedControl(SerialPort serialPort)
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
            System.Diagnostics.Debug.WriteLine($"LedControl::UpdateOverSerial() -> RS232: {json}");
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
    /// A single hardware switch (button). Raises <see cref="SwitchStateChanged"/>
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
                SwitchStateChanged?.Invoke(this, new SwitchEventArgs(Id, _value));
            }
        }

        /// <summary>Raised when this switch's value changes.</summary>
        public event EventHandler<SwitchEventArgs>? SwitchStateChanged;
    }

    /// <summary>
    /// Owns a fixed set of switches and parses incoming JSON from the device.
    /// Subscribe to per-switch changes via the indexer, e.g.:
    /// <code>buttonControl["SW1"].SwitchStateChanged += OnSw1Changed;</code>
    /// </summary>
    public class ButtonControl
    {
        private readonly SerialPort _serialPort;

        public ButtonControl(SerialPort serialPort)
        {
            _serialPort = serialPort;
            _serialPort.DataReceived += OnDataReceived;
        }

        [JsonPropertyName("switches")]
        public List<Switch> Switches { get; } = new()
        {
            new Switch("SW1"),
            new Switch("SW2"),
            new Switch("SW3"),
        };

        /// <summary>
        /// Indexer access, e.g. <c>buttonControl["SW1"].SwitchStateChanged += handler;</c>
        /// </summary>
        public Switch this[string id] =>
            Switches.FirstOrDefault(sw => sw.Id == id)
            ?? throw new KeyNotFoundException($"No switch with id '{id}'.");

        /// <summary>
        /// Raised for any switch change. Inspect <see cref="SwitchEventArgs.Id"/>
        /// to know which switch changed.
        /// </summary>
        public event EventHandler<SwitchEventArgs>? SwitchChanged;

        private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            // ReadExisting can return several lines at once; handle each separately.
            string data = _serialPort.ReadExisting();
            foreach (string line in data.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0)
                    ProcessLine(trimmed);
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
                sw.Value = state.Value; // raises Switch.SwitchStateChanged if it actually changed

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
    /// Top-level controller. Owns both the LED (outgoing) and the
    /// button/switch (incoming) sides of the JSON serial protocol.
    /// </summary>
    public class JsonSerialController
    {
        public JsonSerialController(SerialPort serialPort)
        {
            LedControl = new LedControl(serialPort);
            ButtonControl = new ButtonControl(serialPort);
        }

        public LedControl LedControl { get; }

        public ButtonControl ButtonControl { get; }
    }
}
