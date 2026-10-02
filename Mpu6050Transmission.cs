using System;
using System.IO;

namespace MPU6050Monitor
{
    // To jest właściwy moduł transmisji używany przez aplikację.
    // Jego zadaniem jest zebranie w jednym miejscu wszystkich operacji związanych z komunikacją:
    // wyszukiwania portów, otwierania połączenia, zamykania połączenia, odbioru linii tekstowych
    // oraz wysyłania komend protokołu MPU6050.
    //
    // Dzięki temu Form1 nie zarządza już bezpośrednio mechaniką transmisji. Formularz tylko:
    // 1) prosi moduł o wykonanie operacji,
    // 2) reaguje na zdarzenia odebranych ramek,
    // 3) aktualizuje interfejs użytkownika.
    internal sealed class Mpu6050Transmission : IDisposable
    {
        // Niskopoziomowa warstwa UART została zostawiona jako osobna klasa pomocnicza.
        // Ten moduł nakłada na nią prostsze API zgodne z potrzebami aplikacji.
        private readonly SerialTransport _transport = new();

        // Formularz subskrybuje to zdarzenie, aby otrzymywać gotowe linie odebrane z UART.
        // Moduł nie analizuje jeszcze treści tych ramek, tylko dostarcza je wyżej.
        public event EventHandler<string>? LineReceived
        {
            add => _transport.LineReceived += value;
            remove => _transport.LineReceived -= value;
        }

        // Właściwości udostępniają aktualny stan połączenia bez konieczności wystawiania
        // obiektu SerialPort ani klasy SerialTransport na zewnątrz.
        public bool IsOpen => _transport.IsOpen;
        public string? PortName => _transport.PortName;
        public int BaudRate => _transport.BaudRate;

        // Zwraca listę dostępnych portów COM. Dla formularza to jest kompletna operacja transmisyjna:
        // UI nie musi wiedzieć, skąd pochodzą dane ani jak są sortowane.
        public string[] GetAvailablePorts()
        {
            return SerialTransport.GetAvailablePorts();
        }

        // Otwiera połączenie z urządzeniem. Sama konfiguracja 8N1, kodowanie i odbiór zdarzeń
        // są już zaszyte w niższej warstwie, więc tutaj API pozostaje proste.
        public void Open(string portName, int baudRate)
        {
            _transport.Open(portName, baudRate);
        }

        // Zamyka połączenie. Moduł celowo nie aktualizuje UI ani nie czyści kontrolek,
        // bo to należy do warstwy formularza.
        public void Close()
        {
            _transport.Close();
        }

        // Wysyła zapytanie o aktualnie ustawione zakresy akcelerometru i żyroskopu.
        // To już jest komenda poziomu protokołu aplikacji, dlatego trafia do tego modułu,
        // a nie do ogólnego SerialTransport.
        public void QueryCurrentRanges()
        {
            WriteProtocolLine("RANGE?");
        }

        // Wysyła komendę zmiany zakresów czujników.
        // Format wiadomości jest spójny z firmware po stronie STM32.
        public void ApplyRanges(int accelerometerFullScale, int gyroscopeFullScale)
        {
            WriteProtocolLine($"RANGE,{accelerometerFullScale},{gyroscopeFullScale}");
        }

        // Pozostawiamy też ogólną metodę do wysłania dowolnej linii tekstowej,
        // gdyby później pojawiły się nowe komendy protokołu.
        public void SendLine(string line)
        {
            WriteProtocolLine(line);
        }

        // Dispose zamyka połączenie i zwalnia zasoby warstwy transportowej.
        public void Dispose()
        {
            _transport.Dispose();
        }

        // Prywatna bramka dla wszystkich operacji nadawczych.
        // Z punktu widzenia logiki aplikacji ważne jest, że każda komenda kończy się tutaj
        // przejściem przez jeden wspólny punkt, co ułatwia późniejsze logowanie, diagnostykę
        // lub walidację wysyłanych wiadomości.
        private void WriteProtocolLine(string line)
        {
            try
            {
                _transport.WriteLine(line);
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (IOException)
            {
                throw;
            }
        }
    }
}