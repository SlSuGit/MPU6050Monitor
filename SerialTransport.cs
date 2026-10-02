using System;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;

namespace MPU6050Monitor
{
    // Ta klasa izoluje całą niskopoziomową obsługę portu szeregowego od warstwy formularza.
    // Form1 nie musi już wiedzieć, jak skonfigurować SerialPort, jak składać przychodzące znaki
    // w pełne linie ani jak bezpiecznie zamykać połączenie. Formularz dostaje gotowe ramki tekstowe
    // i wywołuje proste operacje Open / Close / WriteLine.
    internal sealed class SerialTransport : IDisposable
    {
        // Zabezpieczenie przed nieskończonym rozrostem bufora, gdy urządzenie przestanie wysyłać znaki końca linii.
        private const int MaximumPartialLineLength = 4096;

        // Lock chroni jednocześnie referencję do portu i bufor linii przed równoległym dostępem
        // z wątku UI oraz z wątku zdarzenia DataReceived.
        private readonly object _syncLock = new();

        // Bufor przechowuje fragment bieżącej, jeszcze niekompletnej linii odebranej z UART.
        private readonly StringBuilder _partialLine = new();

        // Aktualnie otwarty port. Gdy połączenie jest zamknięte, pole ma wartość null.
        private SerialPort? _serialPort;

        // Zdarzenie emitowane po złożeniu pełnej linii zakończonej znakiem nowej linii.
        // Odbiorca dostaje już czysty tekst bez końcowego CRLF.
        public event EventHandler<string>? LineReceived;

        // Ułatwienie dla warstwy UI: prosty odczyt stanu bez bezpośredniego dostępu do SerialPort.
        public bool IsOpen => _serialPort?.IsOpen == true;

        // Dane diagnostyczne aktualnego połączenia, używane przez formularz do budowania statusu.
        public string? PortName => _serialPort?.PortName;
        public int BaudRate => _serialPort?.BaudRate ?? 0;

        // Zwraca listę portów COM posortowaną alfabetycznie, tak aby formularz nie musiał powtarzać
        // tej samej logiki przy każdym odświeżeniu listy.
        public static string[] GetAvailablePorts() =>
            SerialPort.GetPortNames()
                .OrderBy(port => port, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        // Otwiera nowe połączenie UART z typowymi ustawieniami używanymi przez aplikację.
        // Jeśli poprzedni port był otwarty, metoda kończy się wyjątkiem zamiast cicho nadpisywać stan.
        public void Open(string portName, int baudRate)
        {
            lock (_syncLock)
            {
                if (_serialPort?.IsOpen == true)
                {
                    throw new InvalidOperationException("Port szeregowy jest już otwarty.");
                }

                var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
                {
                    Encoding = Encoding.ASCII,
                    Handshake = Handshake.None,
                    ReadTimeout = 500
                };

                // Zdarzenie DataReceived pochodzi z klasy SerialPort i jest wywoływane na wątku roboczym,
                // nie na wątku UI. Tutaj tylko odbieramy tekst i składamy go w pełne linie.
                port.DataReceived += SerialPort_DataReceived;

                try
                {
                    port.Open();
                    _serialPort = port;
                    _partialLine.Clear();
                }
                catch
                {
                    port.DataReceived -= SerialPort_DataReceived;
                    port.Dispose();
                    throw;
                }
            }
        }

        // Wysyła jedną komendę tekstową do urządzenia. WriteLine dopisuje końcówkę linii zgodną z SerialPort.
        // Gdy port nie jest otwarty, rzucamy wyjątek, bo jest to błąd użycia warstwy transportu.
        public void WriteLine(string line)
        {
            lock (_syncLock)
            {
                if (_serialPort?.IsOpen != true)
                {
                    throw new InvalidOperationException("Port szeregowy nie jest otwarty.");
                }

                _serialPort.WriteLine(line);
            }
        }

        // Zamyka aktywne połączenie i czyści bufor niepełnej linii.
        // Metoda jest bezpieczna do wielokrotnego wywołania: jeśli nie ma otwartego portu, po prostu porządkuje stan.
        public void Close()
        {
            SerialPort? port;

            lock (_syncLock)
            {
                port = _serialPort;
                _serialPort = null;
                _partialLine.Clear();
            }

            if (port is null)
            {
                return;
            }

            port.DataReceived -= SerialPort_DataReceived;

            try
            {
                if (port.IsOpen)
                {
                    port.Close();
                }
            }
            catch (IOException)
            {
                // Przy zamykaniu nie propagujemy błędu I/O, bo aplikacja i tak przechodzi do stanu rozłączonego.
            }
            finally
            {
                port.Dispose();
            }
        }

        // Domknięcie zasobu sprowadzamy do standardowego zamknięcia połączenia.
        public void Dispose()
        {
            Close();
        }

        // Odbiera porcję danych z UART, dokleja ją do bufora i emituje zdarzenie dla każdej pełnej linii.
        // Urządzenie może wysłać kilka linii naraz albo tylko fragment jednej linii, więc przetwarzanie znak po znaku
        // jest tutaj celowe i pozwala zachować poprawność niezależnie od granic pakietów systemowych.
        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (sender is not SerialPort port)
            {
                return;
            }

            try
            {
                var chunk = port.ReadExisting();
                ProcessChunk(chunk);
            }
            catch (InvalidOperationException)
            {
                // Port mógł zostać zamknięty równolegle z przychodzącym zdarzeniem.
            }
            catch (IOException)
            {
                // Błędy odczytu ignorujemy tutaj, bo warstwa UI i tak operuje na ostatnim poprawnym stanie połączenia.
            }
        }

        // Składa odebrane znaki w pełne linie tekstowe.
        // Znak '\n' kończy ramkę, a '\r' jest usuwany z końca, bo zwykle jest tylko częścią CRLF.
        private void ProcessChunk(string chunk)
        {
            string[] completedLines;

            lock (_syncLock)
            {
                var lines = new List<string>();

                foreach (var character in chunk)
                {
                    if (character == '\n')
                    {
                        var line = _partialLine.ToString().TrimEnd('\r');
                        _partialLine.Clear();

                        if (line.Length > 0)
                        {
                            lines.Add(line);
                        }
                    }
                    else if (_partialLine.Length < MaximumPartialLineLength)
                    {
                        _partialLine.Append(character);
                    }
                    else
                    {
                        // Jeśli ramka jest nienaturalnie długa, porzucamy ją, żeby nie trzymać uszkodzonych danych w pamięci.
                        _partialLine.Clear();
                    }
                }

                completedLines = lines.ToArray();
            }

            foreach (var line in completedLines)
            {
                LineReceived?.Invoke(this, line);
            }
        }
    }
}