using System.IO.Ports;
using System.Text;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;


namespace Usn.Pb1151.DeviceKit
{

    /// <summary>Thrown when a BLE scan or connection attempt fails.</summary>
    public sealed class BleConnectionException : Exception
    {
        public BleConnectionException(string message) : base(message) { }
        public BleConnectionException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Event arguments carrying a line of text received over the Nordic UART Service.
    /// </summary>
    public sealed class NusDataReceivedEventArgs : EventArgs
    {
        public NusDataReceivedEventArgs(string text) => Text = text;

        /// <summary>The decoded UTF-8 text received from the peripheral.</summary>
        public string Text { get; }
    }

    /// <summary>
    /// Connects to a BLE peripheral exposing the Nordic UART Service (NUS) and
    /// allows sending and receiving data. Received data is delivered via the
    /// <see cref="DataReceived"/> event.
    /// </summary>
    public sealed class BleNusEx : IAsyncDisposable, ISerialDataReadWrite
    {
        // Nordic UART Service UUIDs.
        private static readonly Guid NusServiceUuid = new("6E400001-B5A3-F393-E0A9-E50E24DCCA9E");
        private static readonly Guid NusRxCharUuid = new("6E400002-B5A3-F393-E0A9-E50E24DCCA9E"); // Write  (client -> device)
        private static readonly Guid NusTxCharUuid = new("6E400003-B5A3-F393-E0A9-E50E24DCCA9E"); // Notify (device -> client)

        private readonly string _deviceName;

        // Buffers text received from notifications so it can be drained by ReadExisting().
        private readonly StringBuilder _rxBuffer = new();
        private readonly object _rxLock = new();

        // Backing field for the ISerialDataReadWrite.DataReceived event.
        private SerialDataReceivedEventHandler? _serialDataReceived;

        private BluetoothLEAdvertisementWatcher? _watcher;
        private BluetoothLEDevice? _device;
        private GattCharacteristic? _rxCharacteristic;
        private GattCharacteristic? _txCharacteristic;

        /// <summary>Raised when a notification is received from the peripheral's TX characteristic.</summary>
        public event EventHandler<NusDataReceivedEventArgs>? DataReceived;

        /// <summary>Raised when the connection status changes (true = connected).</summary>
        public event EventHandler<bool>? ConnectionChanged;

        /// <param name="deviceName">The advertised name of the BLE device to connect to.</param>
        public BleNusEx(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName))
                throw new ArgumentException("Device name must be provided.", nameof(deviceName));

            _deviceName = deviceName;
        }

        public bool IsConnected => _device?.ConnectionStatus == BluetoothConnectionStatus.Connected;

        /// <summary>
        /// Scans for the named device, connects, and subscribes to NUS notifications.
        /// </summary>
        /// <param name="timeout">How long to scan before giving up.</param>
        public async Task ConnectAsync(TimeSpan timeout)
        {
            ulong address = await ScanForDeviceAsync(timeout);

            _device = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
            if (_device is null)
                throw new InvalidOperationException("Failed to create a BLE device from the discovered address.");

            _device.ConnectionStatusChanged += OnConnectionStatusChanged;

            await DiscoverNusCharacteristicsAsync();
            await SubscribeToNotificationsAsync();
        }

        /// <summary>
        /// Sends a UTF-8 encoded string to the peripheral's RX characteristic.
        /// </summary>
        public async Task SendAsync(string text)
        {
            if (_rxCharacteristic is null)
                throw new InvalidOperationException("Not connected. Call ConnectAsync first.");

            using var writer = new DataWriter();
            writer.WriteBytes(Encoding.UTF8.GetBytes(text));

            GattCommunicationStatus status =
                await _rxCharacteristic.WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithoutResponse);

            if (status != GattCommunicationStatus.Success)
                throw new InvalidOperationException($"Write failed with status: {status}.");
        }

        // --- Connection helpers -------------------------------------------------

        private Task<ulong> ScanForDeviceAsync(TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource<ulong>(TaskCreationOptions.RunContinuationsAsynchronously);

            _watcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Active
            };

            _watcher.Received += (_, args) =>
            {
                if (string.Equals(args.Advertisement.LocalName, _deviceName, StringComparison.Ordinal))
                {
                    _watcher.Stop();
                    tcs.TrySetResult(args.BluetoothAddress);
                }
            };

            _watcher.Stopped += (_, args) =>
            {
                if (args.Error != BluetoothError.Success)
                    tcs.TrySetException(new BleConnectionException($"BLE scan stopped with error: {args.Error}."));
            };

            try
            {
                _watcher.Start();
            }
            catch (Exception ex)
            {
                // 0x800710DF = ERROR_DEVICE_NOT_AVAILABLE: radio is off or adapter unavailable.
                string hint = (uint)ex.HResult == 0x800710DF
                    ? "The Bluetooth radio is off. Turn on Bluetooth in Windows Settings and try again."
                    : "Ensure Bluetooth is enabled and the adapter is present.";
                return Task.FromException<ulong>(new BleConnectionException(hint, ex));
            }

            // Fail the task if the device is not found within the timeout.
            _ = Task.Delay(timeout).ContinueWith(_ =>
            {
                try { _watcher.Stop(); } catch { /* ignore if already stopped */ }
                tcs.TrySetException(
                    new TimeoutException($"Device '{_deviceName}' not found within {timeout.TotalSeconds:0} s."));
            }, TaskScheduler.Default);

            return tcs.Task;
        }

        private async Task DiscoverNusCharacteristicsAsync()
        {
            GattDeviceServicesResult servicesResult =
                await _device!.GetGattServicesForUuidAsync(NusServiceUuid);

            if (servicesResult.Status != GattCommunicationStatus.Success || servicesResult.Services.Count == 0)
                throw new InvalidOperationException("Nordic UART Service not found on the device.");

            GattDeviceService service = servicesResult.Services[0];

            _rxCharacteristic = await GetCharacteristicAsync(service, NusRxCharUuid, "RX (write)");
            _txCharacteristic = await GetCharacteristicAsync(service, NusTxCharUuid, "TX (notify)");
        }

        private static async Task<GattCharacteristic> GetCharacteristicAsync(
            GattDeviceService service, Guid uuid, string description)
        {
            GattCharacteristicsResult result = await service.GetCharacteristicsForUuidAsync(uuid);

            if (result.Status != GattCommunicationStatus.Success || result.Characteristics.Count == 0)
                throw new InvalidOperationException($"NUS {description} characteristic not found.");

            return result.Characteristics[0];
        }

        private async Task SubscribeToNotificationsAsync()
        {
            _txCharacteristic!.ValueChanged += OnTxValueChanged;

            GattCommunicationStatus status =
                await _txCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.Notify);

            if (status != GattCommunicationStatus.Success)
                throw new InvalidOperationException($"Failed to enable notifications: {status}.");
        }

        // --- Event handlers -----------------------------------------------------

        private void OnTxValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            using var reader = DataReader.FromBuffer(args.CharacteristicValue);
            var bytes = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(bytes);

            string text = Encoding.UTF8.GetString(bytes);

            // Native (typed) event for consumers that want the decoded text directly.
            DataReceived?.Invoke(this, new NusDataReceivedEventArgs(text));

            // ISerialDataReadWrite path: buffer the text and raise the SerialPort-style event.
            lock (_rxLock)
                _rxBuffer.Append(text);

            // SerialDataReceivedEventArgs has no public constructor, so pass null;
            // subscribers are expected to call ReadExisting() to obtain the data.
            _serialDataReceived?.Invoke(this, null!);
        }

        private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
            => ConnectionChanged?.Invoke(this, sender.ConnectionStatus == BluetoothConnectionStatus.Connected);

        // --- ISerialDataReadWrite ----------------------------------------------

        /// <summary>
        /// Sends <paramref name="text"/> followed by a newline to the peripheral,
        /// mirroring <see cref="SerialPort.WriteLine(string)"/> semantics.
        /// BLE writes are asynchronous; this fires-and-forgets the send.
        /// </summary>
        void ISerialDataReadWrite.WriteLine(string text)
        {
            // Append a newline to match SerialPort.WriteLine line framing.
            _ = SendAsync(text + "\n");
        }

        /// <summary>
        /// Returns all text received since the last call and clears the buffer,
        /// mirroring <see cref="SerialPort.ReadExisting"/>.
        /// </summary>
        string ISerialDataReadWrite.ReadExisting()
        {
            lock (_rxLock)
            {
                string existing = _rxBuffer.ToString();
                _rxBuffer.Clear();
                return existing;
            }
        }

        /// <summary>
        /// Raised when data arrives from the peripheral. Subscribers should call
        /// <see cref="ISerialDataReadWrite.ReadExisting"/> to read the data.
        /// </summary>
        event SerialDataReceivedEventHandler ISerialDataReadWrite.DataReceived
        {
            add => _serialDataReceived += value;
            remove => _serialDataReceived -= value;
        }

        // --- Cleanup ------------------------------------------------------------

        public async ValueTask DisposeAsync()
        {
            if (_txCharacteristic is not null)
            {
                _txCharacteristic.ValueChanged -= OnTxValueChanged;
                try
                {
                    await _txCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.None);
                }
                catch { /* ignore cleanup errors */ }
            }

            if (_device is not null)
            {
                _device.ConnectionStatusChanged -= OnConnectionStatusChanged;
                _device.Dispose();
                _device = null;
            }

            _watcher?.Stop();
        }
    }
}
