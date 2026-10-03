using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MPU6050Monitor
{
    public partial class Form1 : Form
    {
        private static readonly Color WindowColor = Color.FromArgb(243, 246, 245);
        private static readonly Color InkColor = Color.FromArgb(29, 48, 50);
        private static readonly Color MutedColor = Color.FromArgb(112, 129, 127);
        private static readonly Color AccentColor = Color.FromArgb(8, 126, 120);
        private static readonly Color BorderColor = Color.FromArgb(216, 225, 223);
        private double _accelerometerCountsPerG = 16384.0;
        private double _gyroscopeCountsPerDegreePerSecond = 131.0;
        private const double AccelerometerXBiasG = 0.0;
        private const double AccelerometerXScale = 1.0;
        private const double AccelerometerYBiasG = 0.0;
        private const double AccelerometerYScale = 1.0;
        private const double AccelerometerZBiasG = -0.178;
        private const double AccelerometerZScale = 1.0 / 1.018;
        private const int MaximumDisplayedFrames = 1500;
        private const string TemperaturePlaceholder = "TEMP: — °C";

        private readonly ComboBox _portComboBox = new();
        private readonly ComboBox _baudComboBox = new();
        private readonly ComboBox _accelRangeComboBox = new();
        private readonly ComboBox _gyroRangeComboBox = new();
        private readonly Button _connectButton = new();
        private readonly Button _refreshButton = new();
        private readonly Button _applyRangesButton = new();
        private readonly Button _clearButton = new();
        private readonly ListBox _receivedListBox = new();
        private readonly Label _statusIndicator = new();
        private readonly Label _statusText = new();
        private readonly Label _statusDetail = new();
        private readonly Label _frameCountText = new();
        private readonly Label _latestFrameText = new();
        private readonly Label _lastReceivedText = new();
        private readonly Label _temperatureText = new();
        private readonly Button _exitButton = new();
        private readonly AccelerationPlot _accelerationPlot = new();
        private readonly GyroscopePlot _gyroscopePlot = new();
        private readonly OrientationView _orientationView = new();
        private readonly Mpu6050Transmission _transmission = new();
        private readonly System.Windows.Forms.Timer _displayTimer = new() { Interval = 50 };
        private readonly object _receiveLock = new();
        private readonly Queue<ReceivedFrame> _pendingFrames = new();
        private long _receivedFrameCount;
        private bool _rangeCommandPending;

        private static readonly RangeOption[] AccelerometerRanges =
        {
            new("±2 g", 2, 16384.0),
            new("±4 g", 4, 8192.0),
            new("±8 g", 8, 4096.0),
            new("±16 g", 16, 2048.0)
        };

        private static readonly RangeOption[] GyroscopeRanges =
        {
            new("±250 °/s", 250, 131.0),
            new("±500 °/s", 500, 65.5),
            new("±1000 °/s", 1000, 32.8),
            new("±2000 °/s", 2000, 16.4)
        };

        private readonly record struct ReceivedFrame(DateTime Timestamp, string Line);
        private readonly record struct RangeOption(string Label, int FullScale, double Sensitivity)
        {
            public override string ToString() => Label;
        }

        private readonly record struct Mpu6050Frame(
            int AccelX, int AccelY, int AccelZ,
            int TemperatureRaw,
            int GyroX, int GyroY, int GyroZ)
        {
            public double TemperatureCelsius => TemperatureRaw / 340.0 + 36.53;
        }

        public Form1()
        {
            Text = "IMU Flight View | UART Monitor";
            ClientSize = new Size(1080, 808);
            MinimumSize = new Size(820, 620);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = WindowColor;
            ForeColor = InkColor;
            Font = new Font("Segoe UI", 9.5f);
            AutoScaleMode = AutoScaleMode.Dpi;

            _transmission.LineReceived += Transmission_LineReceived;
            Controls.Add(BuildLayout());
            Load += (_, _) => RefreshPorts();
            Load += (_, _) => _displayTimer.Start();
            _displayTimer.Tick += (_, _) => RefreshReceivedFrames();
            FormClosing += (_, _) =>
            {
                _displayTimer.Stop();
                _displayTimer.Dispose();
                DisconnectPort();
                _transmission.Dispose();
            };
        }

        private void SetRangeControlsEnabled(bool enabled)
        {
            _accelRangeComboBox.Enabled = enabled;
            _gyroRangeComboBox.Enabled = enabled;
            _applyRangesButton.Enabled = enabled && !_rangeCommandPending;
        }

        private void ApplySelectedRanges()
        {
            if (!_transmission.IsOpen ||
                _accelRangeComboBox.SelectedItem is not RangeOption accelRange ||
                _gyroRangeComboBox.SelectedItem is not RangeOption gyroRange)
            {
                return;
            }

            try
            {
                _transmission.ApplyRanges(accelRange.FullScale, gyroRange.FullScale);
                _rangeCommandPending = true;
                _accelRangeComboBox.Enabled = false;
                _gyroRangeComboBox.Enabled = false;
                _applyRangesButton.Enabled = false;
                _statusDetail.Text = "Oczekiwanie na potwierdzenie zakresu";
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException)
            {
                _statusDetail.Text = "Nie udało się wysłać ustawień zakresu";
                MessageBox.Show(this, exception.Message, "Błąd portu", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void QueryCurrentRanges()
        {
            if (_transmission.IsOpen)
            {
                _transmission.QueryCurrentRanges();
            }
        }

        private static bool TryParseRangeConfirmation(
            string line,
            out RangeOption accelRange,
            out RangeOption gyroRange)
        {
            accelRange = default;
            gyroRange = default;
            var fields = line.Split(',');
            if (fields.Length != 3 ||
                !string.Equals(fields[0], "RANGE_OK", StringComparison.Ordinal) ||
                !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var accelFullScale) ||
                !int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var gyroFullScale))
            {
                return false;
            }

            foreach (var range in AccelerometerRanges)
            {
                if (range.FullScale == accelFullScale)
                {
                    accelRange = range;
                    break;
                }
            }
            foreach (var range in GyroscopeRanges)
            {
                if (range.FullScale == gyroFullScale)
                {
                    gyroRange = range;
                    break;
                }
            }

            return accelRange.FullScale != 0 && gyroRange.FullScale != 0;
        }

        private void SetConfirmedRanges(RangeOption accelRange, RangeOption gyroRange)
        {
            _accelerometerCountsPerG = accelRange.Sensitivity;
            _gyroscopeCountsPerDegreePerSecond = gyroRange.Sensitivity;
            _accelerationPlot.SetRange(accelRange.FullScale);
            _gyroscopePlot.SetRange(gyroRange.FullScale);
            _accelRangeComboBox.SelectedItem = accelRange;
            _gyroRangeComboBox.SelectedItem = gyroRange;
            _rangeCommandPending = false;
            SetRangeControlsEnabled(_transmission.IsOpen);

            if (_transmission.IsOpen)
            {
                _statusDetail.Text = $"{_transmission.PortName}  ·  {_transmission.BaudRate} baud  ·  {accelRange.Label}  ·  {gyroRange.Label}";
            }
        }


        private void RefreshPorts()
        {
            var previousPort = _portComboBox.SelectedItem as string;

            try
            {
                var ports = _transmission.GetAvailablePorts();
                _portComboBox.Items.Clear();
                _portComboBox.Items.AddRange(ports);

                if (previousPort is not null && ports.Contains(previousPort, StringComparer.OrdinalIgnoreCase))
                {
                    _portComboBox.SelectedItem = ports.First(port =>
                        string.Equals(port, previousPort, StringComparison.OrdinalIgnoreCase));
                }
                else if (ports.Length > 0)
                {
                    _portComboBox.SelectedIndex = 0;
                }

                if (!_transmission.IsOpen)
                {
                    SetDisconnectedStatus(ports.Length == 0
                        ? "Nie znaleziono portów COM"
                        : $"Znaleziono porty: {ports.Length}");
                }
            }
            catch (Exception exception)
            {
                SetDisconnectedStatus("Nie można odczytać portów");
                MessageBox.Show(this, exception.Message, "Błąd portu", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ToggleConnection()
        {
            if (_transmission.IsOpen)
            {
                DisconnectPort();
                return;
            }

            if (_portComboBox.SelectedItem is not string portName ||
                _baudComboBox.SelectedItem is not string baudText ||
                !int.TryParse(baudText, out var baudRate))
            {
                SetDisconnectedStatus("Wybierz port i prędkość");
                return;
            }

            try
            {
                _transmission.Open(portName, baudRate);
                _portComboBox.Enabled = false;
                _baudComboBox.Enabled = false;
                _refreshButton.Enabled = false;
                _connectButton.Text = "Zamknij port";
                _connectButton.BackColor = Color.FromArgb(171, 75, 64);
                _statusIndicator.ForeColor = Color.FromArgb(81, 202, 157);
                _statusText.Text = "POŁĄCZONO";
                _statusDetail.Text = $"{portName}  ·  {baudRate} baud  ·  8N1";
                SetRangeControlsEnabled(true);
                QueryCurrentRanges();
            }
            catch (Exception exception)
            {
                _transmission.Close();
                _rangeCommandPending = false;
                SetRangeControlsEnabled(false);
                SetDisconnectedStatus($"Nie udało się otworzyć {portName}");
                MessageBox.Show(this, exception.Message, "Błąd otwierania portu",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Transmission_LineReceived(object? sender, string line)
        {
            lock (_receiveLock)
            {
                _pendingFrames.Enqueue(new ReceivedFrame(DateTime.Now, line));
            }
        }

        private void RefreshReceivedFrames()
        {
            var frames = new List<ReceivedFrame>();
            lock (_receiveLock)
            {
                while (_pendingFrames.Count > 0)
                {
                    frames.Add(_pendingFrames.Dequeue());
                }
            }

            if (frames.Count == 0)
            {
                return;
            }

            foreach (var frame in frames)
            {
                if (TryParseRangeConfirmation(frame.Line, out var accelRange, out var gyroRange))
                {
                    SetConfirmedRanges(accelRange, gyroRange);
                }
                else if (frame.Line.StartsWith("RANGE_ERR,", StringComparison.Ordinal))
                {
                    _rangeCommandPending = false;
                    SetRangeControlsEnabled(_transmission.IsOpen);
                    _statusDetail.Text = $"Zakres niezmieniony: {frame.Line[10..]}";
                }
            }

            double? latestTemperature = null;
            // var displayedFrames = new string[frames.Count];
            for (var index = 0; index < frames.Count; index++)
            {
                var frame = frames[index];
                if (TryParseMpu6050Frame(frame.Line, out var sensorFrame))
                {
                    latestTemperature = sensorFrame.TemperatureCelsius;
                    var (accelXG, accelYG, accelZG) = ConvertAcceleration(
                        sensorFrame.AccelX, sensorFrame.AccelY, sensorFrame.AccelZ);
                    var (gyroXDps, gyroYDps, gyroZDps) = ConvertGyroscope(
                        sensorFrame.GyroX, sensorFrame.GyroY, sensorFrame.GyroZ);
                    _accelerationPlot.AddSample(frame.Timestamp, accelXG, accelYG, accelZG);
                    _gyroscopePlot.AddSample(frame.Timestamp, gyroXDps, gyroYDps, gyroZDps);
                    _orientationView.AddSample(frame.Timestamp, accelXG, accelYG, accelZG, gyroXDps, gyroYDps, gyroZDps);
                }
                else if (TryParseAccelerometerFrame(frame.Line, out var accelX, out var accelY, out var accelZ))
                {
                    var (accelXG, accelYG, accelZG) = ConvertAcceleration(accelX, accelY, accelZ);
                    _accelerationPlot.AddSample(frame.Timestamp, accelXG, accelYG, accelZG);
                }

                // var displayedFrame = FormatFrame(frame.Line, multiline: false);
                // displayedFrames[index] = $"{frame.Timestamp:HH:mm:ss.fff}  {displayedFrame}";
            }

            // Lista odebranych ramek jest wyłączona.
            // _receivedListBox.BeginUpdate();
            // try
            // {
            //     _receivedListBox.Items.AddRange(displayedFrames);
            //     while (_receivedListBox.Items.Count > MaximumDisplayedFrames)
            //     {
            //         _receivedListBox.Items.RemoveAt(0);
            //     }
            //
            //     _receivedListBox.TopIndex = _receivedListBox.Items.Count - 1;
            // }
            // finally
            // {
            //     _receivedListBox.EndUpdate();
            // }

            if (latestTemperature is { } temperature)
            {
                _temperatureText.Text = $"TEMP: {temperature:F1} °C";
            }

            var latestFrame = frames[^1];
            var latestDisplay = FormatFrame(latestFrame.Line, multiline: true);
            _receivedFrameCount += frames.Count;
            _frameCountText.Text = _receivedFrameCount.ToString("N0");
            _latestFrameText.Text = latestDisplay;
            _lastReceivedText.Text = $"Czas: {latestFrame.Timestamp:HH:mm:ss.fff}";
        }

        private string FormatFrame(string line, bool multiline)
        {
            if (TryParseMpu6050Frame(line, out var frame))
            {
                var (accelXG, accelYG, accelZG) = ConvertAcceleration(frame.AccelX, frame.AccelY, frame.AccelZ);
                var acceleration = $"x={FormatSignedG(accelXG)}, y={FormatSignedG(accelYG)}, z={FormatSignedG(accelZG)}";
                var magnitude = Math.Sqrt(accelXG * accelXG + accelYG * accelYG + accelZG * accelZG)
                    .ToString("F3", CultureInfo.CurrentCulture);
                return multiline
                    ? $"ACC [g]: {acceleration}\n|a|: {magnitude} g | TEMP: {frame.TemperatureCelsius:F2} °C\nGYRO raw: x={frame.GyroX}, y={frame.GyroY}, z={frame.GyroZ}"
                    : $"ACC [g]: {acceleration} | |a|={magnitude} g | TEMP: {frame.TemperatureCelsius:F2} °C | GYRO raw: x={frame.GyroX}, y={frame.GyroY}, z={frame.GyroZ}";
            }

            return TryParseAccelerometerFrame(line, out var x, out var y, out var z)
                ? FormatAccelerometerG(x, y, z)
                : line;
        }

        private string FormatAccelerometerG(int accelX, int accelY, int accelZ)
        {
            var (accelXG, accelYG, accelZG) = ConvertAcceleration(accelX, accelY, accelZ);
            var magnitude = Math.Sqrt(accelXG * accelXG + accelYG * accelYG + accelZG * accelZG);
            return $"ACC [g]: x={FormatSignedG(accelXG)}, y={FormatSignedG(accelYG)}, z={FormatSignedG(accelZG)} | |a|={magnitude:F3} g";
        }

        private (double X, double Y, double Z) ConvertAcceleration(int accelX, int accelY, int accelZ)
        {
            var accelXG = CorrectAcceleration(accelX / _accelerometerCountsPerG, AccelerometerXBiasG, AccelerometerXScale);
            var accelYG = CorrectAcceleration(accelY / _accelerometerCountsPerG, AccelerometerYBiasG, AccelerometerYScale);
            var accelZG = CorrectAcceleration(accelZ / _accelerometerCountsPerG, AccelerometerZBiasG, AccelerometerZScale);
            return (accelXG, accelYG, accelZG);
        }

        private (double X, double Y, double Z) ConvertGyroscope(int gyroX, int gyroY, int gyroZ)
        {
            var gyroXDps = gyroX / _gyroscopeCountsPerDegreePerSecond;
            var gyroYDps = gyroY / _gyroscopeCountsPerDegreePerSecond;
            var gyroZDps = gyroZ / _gyroscopeCountsPerDegreePerSecond;
            return (gyroXDps, gyroYDps, gyroZDps);
        }

        private static double CorrectAcceleration(double valueG, double biasG, double scale) =>
            (valueG - biasG) * scale;

        private static string FormatSignedG(double value) =>
            value.ToString("+0.000;-0.000;0.000", CultureInfo.CurrentCulture);

        private static bool TryParseMpu6050Frame(string line, out Mpu6050Frame frame)
        {
            frame = default;
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 3 ||
                !fields[0].StartsWith("ACC=", StringComparison.Ordinal) ||
                !fields[1].StartsWith("TEMP=", StringComparison.Ordinal) ||
                !fields[2].StartsWith("GYRO=", StringComparison.Ordinal))
            {
                return false;
            }

            var accelerometer = fields[0][4..].Split(',');
            var gyroscope = fields[2][5..].Split(',');
            if (accelerometer.Length != 3 || gyroscope.Length != 3 ||
                !int.TryParse(accelerometer[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var accelX) ||
                !int.TryParse(accelerometer[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var accelY) ||
                !int.TryParse(accelerometer[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var accelZ) ||
                !int.TryParse(fields[1].AsSpan(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out var temperatureRaw) ||
                !int.TryParse(gyroscope[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var gyroX) ||
                !int.TryParse(gyroscope[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var gyroY) ||
                !int.TryParse(gyroscope[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var gyroZ))
            {
                return false;
            }

            frame = new Mpu6050Frame(accelX, accelY, accelZ, temperatureRaw, gyroX, gyroY, gyroZ);
            return true;
        }

        private static bool TryParseAccelerometerFrame(string line, out int x, out int y, out int z)
        {
            x = 0;
            y = 0;
            z = 0;

            var fields = line.Split(',');
            return fields.Length == 4 &&
                string.Equals(fields[0].Trim(), "ACC", StringComparison.Ordinal) &&
                int.TryParse(fields[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out x) &&
                int.TryParse(fields[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out y) &&
                int.TryParse(fields[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out z);
        }

        private void ClearReceivedFrames()
        {
            lock (_receiveLock)
            {
                _pendingFrames.Clear();
            }

            _receivedListBox.Items.Clear();
            _receivedFrameCount = 0;
            _frameCountText.Text = "0";
            _latestFrameText.Text = "—";
            _lastReceivedText.Text = "Brak odebranych danych";
            _temperatureText.Text = TemperaturePlaceholder;
            _accelerationPlot.ClearSamples();
            _gyroscopePlot.ClearSamples();
            _orientationView.Reset();
        }

        private void DisconnectPort()
        {
            _transmission.Close();

            lock (_receiveLock)
            {
                _pendingFrames.Clear();
            }

            if (IsDisposed)
            {
                return;
            }

            _temperatureText.Text = TemperaturePlaceholder;
            _rangeCommandPending = false;
            SetRangeControlsEnabled(false);
            _portComboBox.Enabled = true;
            _baudComboBox.Enabled = true;
            _refreshButton.Enabled = true;
            _connectButton.Text = "Otwórz port";
            _connectButton.BackColor = AccentColor;
            SetDisconnectedStatus("Port zamknięty");
        }

        private void SetDisconnectedStatus(string detail)
        {
            _statusIndicator.ForeColor = Color.FromArgb(217, 109, 97);
            _statusText.Text = "ROZŁĄCZONO";
            _statusDetail.Text = detail;
        }
    }
}