using System;
using System.Drawing;
using System.Windows.Forms;

namespace MPU6050Monitor
{
    public partial class Form1
    {
        // Buduje główny kontener formularza. Cała formatka jest podzielona na trzy poziome pasy:
        // 1) pasek statusu u góry,
        // 2) pasek połączenia i ustawień transmisji,
        // 3) obszar roboczy z listą ramek i wykresem.
        // TableLayoutPanel pilnuje proporcji i eliminuje konieczność ręcznego liczenia pozycji.
        private Control BuildLayout()
        {
            var layout = new TableLayoutPanel
            {
                // Kontener ma zająć całe dostępne wnętrze formularza.
                Dock = DockStyle.Fill,
                // Jeden słupek i trzy wiersze: status, połączenie, zawartość.
                ColumnCount = 1,
                RowCount = 3,
                BackColor = WindowColor,
                // Brak zewnętrznego marginesu, żeby layout przylegał do granic formularza.
                Margin = Padding.Empty
            };
            // Jedyna kolumna rozciąga się na pełną szerokość.
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            // Górny pasek ma stałą wysokość 58 px.
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            // Pasek połączenia ma stałą wysokość 148 px.
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 148));
            // Ostatni wiersz zabiera całą pozostałą przestrzeń.
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            layout.Controls.Add(BuildStatusBar(), 0, 0);
            layout.Controls.Add(BuildConnectionBar(), 0, 1);
            layout.Controls.Add(BuildContent(), 0, 2);
            return layout;
        }

        // Tworzy górny pasek informujący o stanie połączenia z urządzeniem.
        // Ten fragment używa pozycjonowania bezpośredniego przez Location, bo elementów jest mało
        // i mają stałe, przewidywalne położenie względem paska.
        private Control BuildStatusBar()
        {
            var bar = new Panel
            {
                // Pasek ma zawsze rozciągać się na całą szerokość swojego wiersza.
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(34, 54, 56),
                // Padding daje wewnętrzny oddech od lewej i prawej krawędzi.
                Padding = new Padding(24, 0, 24, 0)
            };

            // Graficzna kropka statusu. Kolor zmienia się później zależnie od stanu połączenia.
            _statusIndicator.Text = "●";
            _statusIndicator.Font = new Font("Segoe UI Symbol", 12, FontStyle.Bold);
            _statusIndicator.ForeColor = Color.FromArgb(217, 109, 97);
            _statusIndicator.AutoSize = true;
            // Pozycja ustawiona ręcznie w obrębie paska statusu.
            _statusIndicator.Location = new Point(24, 18);

            // Główny tekst statusu, np. ROZŁĄCZONO / POŁĄCZONO.
            _statusText.Text = "ROZŁĄCZONO";
            _statusText.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            _statusText.ForeColor = Color.FromArgb(242, 246, 245);
            _statusText.AutoSize = true;
            _statusText.Location = new Point(43, 11);

            // Dodatkowa linia opisu pod statusem, używana np. do numeru portu i prędkości.
            _statusDetail.Text = "Wybierz port szeregowy";
            _statusDetail.Font = new Font("Segoe UI", 8.5f);
            _statusDetail.ForeColor = Color.FromArgb(175, 192, 189);
            _statusDetail.AutoSize = true;
            _statusDetail.Location = new Point(43, 31);

            // Etykieta techniczna w prawym górnym rogu paska.
            var appTag = new Label
            {
                Text = "UART MONITOR  /  8N1",
                AutoSize = true,
                ForeColor = Color.FromArgb(175, 192, 189),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                // Pozycja początkowa; dokładne wyrównanie do prawej jest później korygowane przy Resize.
                Location = new Point(Width - 200, 20)
            };
            // Po zmianie szerokości paska etykieta jest dosuwana do prawej krawędzi z odstępem 24 px.
            bar.Resize += (_, _) => appTag.Location = new Point(bar.ClientSize.Width - appTag.Width - 24, 20);

            bar.Controls.Add(_statusIndicator);
            bar.Controls.Add(_statusText);
            bar.Controls.Add(_statusDetail);
            bar.Controls.Add(appTag);
            return bar;
        }

        // Buduje środkowy pasek z ustawieniami połączenia i doboru zakresów pomiarowych.
        // Układ jest zrobiony warstwowo: najpierw Panel, w nim TableLayoutPanel z dwoma wierszami,
        // a w każdym wierszu FlowLayoutPanel układający kontrolki od lewej do prawej.
        private Control BuildConnectionBar()
        {
            var bar = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White
            };

            // Ujednolicone formatowanie list rozwijanych: szerokość, wysokość, font i margines.
            ConfigureComboBox(_portComboBox, 160);
            ConfigureComboBox(_baudComboBox, 160);
            // Jawnie ustawiamy brak możliwości wpisywania własnej wartości.
            _baudComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            // Dostępne prędkości transmisji UART. To zawartość listy, nie jej wygląd.
            _baudComboBox.Items.AddRange(new object[] {
                            "300",
                            "600",
                            "1200",
                            "2400",
                            "4800",
                            "9600",
                            "14400",
                            "19200",
                            "28800",
                            "38400",
                            "57600",
                            "115200",
                            "230400",
                            "460800",
                            "921600" });
            // Domyślny baudrate po uruchomieniu aplikacji.
            _baudComboBox.SelectedItem = "921600";

            // Wypełnienie listy zakresów akcelerometru obiektami opisującymi etykietę i czułość.
            foreach (var range in AccelerometerRanges)
            {
                _accelRangeComboBox.Items.Add(range);
            }
            // Startujemy od najmniejszego zakresu, czyli najwyższej czułości.
            _accelRangeComboBox.SelectedIndex = 0;

            // Wypełnienie listy zakresów żyroskopu.
            foreach (var range in GyroscopeRanges)
            {
                _gyroRangeComboBox.Items.Add(range);
            }
            _gyroRangeComboBox.SelectedIndex = 0;

            // Konfiguracja przycisku "Odśwież".
            // Text: napis wyświetlany użytkownikowi.
            // ConfigureSecondaryButton: nadaje wygląd przycisku pomocniczego,
            // czyli biały środek, cienką ramkę, lżejszy font i standardowy rozmiar.
            // Margin: ustawia odstępy względem sąsiednich kontrolek w FlowLayoutPanel.
            // W tym przypadku 20 px od góry wyrównuje go optycznie do pól formularza,
            // a 42 px z prawej daje większy odstęp przed przyciskiem głównym.
            // Click: po kliknięciu wywoływana jest metoda odświeżająca listę dostępnych portów COM.
            _refreshButton.Text = "Odśwież";
            ConfigureSecondaryButton(_refreshButton, 88);
            _refreshButton.Margin = new Padding(20, 20, 42, 0);
            _refreshButton.Click += (_, _) => RefreshPorts();

            // Konfiguracja głównego przycisku akcji odpowiedzialnego za otwieranie i zamykanie portu.
            // Używamy stylu primary, aby wizualnie odróżnić go od pomocniczego przycisku odświeżania.
            // Szerokość 142 px zostawia miejsce na oba napisy: "Otwórz port" i "Zamknij port".
            // Górny margines 20 px wyrównuje go do wspólnej linii z listami rozwijanymi.
            _connectButton.Text = "Otwórz port";
            ConfigurePrimaryButton(_connectButton, 142);
            _connectButton.Margin = new Padding(0, 20, 0, 0);
            _connectButton.Click += (_, _) => ToggleConnection();

            // Przycisk zatwierdzający zmianę zakresów pomiarowych.
            // Na starcie pozostaje wyłączony, bo nie ma jeszcze aktywnego połączenia z urządzeniem.
            _applyRangesButton.Text = "Zastosuj";
            ConfigureSecondaryButton(_applyRangesButton, 100);
            _applyRangesButton.Margin = new Padding(20, 10, 0, 0);
            _applyRangesButton.Enabled = false;
            _applyRangesButton.Click += (_, _) => ApplySelectedRanges();

            // Pierwszy rząd kontrolek: port, prędkość, odświeżenie, połączenie.
            // FlowLayoutPanel sam wylicza pozycje dzieci na podstawie kolejności dodania i ich marginesów.
            var fields = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                // Kontener ma wysokość zgodną z wyższymi polami wejściowymi.
                Height = 74,
                // Nie zawijamy do drugiej linii, bo cały rząd ma pozostać w jednym pasie.
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.White,
                Margin = Padding.Empty
            };
            // Kolejność dodania definiuje kolejność wyświetlania od lewej do prawej.
            fields.Controls.Add(BuildField("Nr portu", _portComboBox, 176));
            fields.Controls.Add(BuildField("Prędkość", _baudComboBox, 176));
            fields.Controls.Add(_refreshButton);
            fields.Controls.Add(_connectButton);

            // Drugi rząd: ustawienia zakresów pracy czujników.
            var rangeFields = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                Height = 42,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.White,
                Margin = Padding.Empty
            };
            rangeFields.Controls.Add(BuildCompactField("Akcelerometr", _accelRangeComboBox, 142));
            rangeFields.Controls.Add(BuildCompactField("Żyroskop", _gyroRangeComboBox, 142));
            rangeFields.Controls.Add(_applyRangesButton);
            // Zakresy są aktywowane dopiero po otwarciu portu i pobraniu aktualnej konfiguracji.
            SetRangeControlsEnabled(false);

            // Zewnętrzny układ paska połączenia. Dwa wiersze zamykają oba FlowLayoutPanel-e
            // we wspólnej białej sekcji z jednakowym wewnętrznym paddingiem.
            var rows = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                // Padding buduje odstęp od obramowania białego paska.
                Padding = new Padding(24, 8, 24, 8),
                BackColor = Color.White
            };
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            // Górny rząd jest wyższy, bo zawiera standardowe pola i przyciski o wysokości 38 px.
            rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
            // Dolny rząd jest niższy, bo zawiera bardziej zwarte pola zakresów.
            rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            rows.Controls.Add(fields, 0, 0);
            rows.Controls.Add(rangeFields, 0, 1);
            bar.Controls.Add(rows);
            return bar;
        }

        // Buduje kompaktowe pole z etykietą nad listą rozwijaną.
        // Ta wersja jest używana dla zakresów czujników, gdzie pionowy układ jest niższy niż w głównych polach.
        private Control BuildCompactField(string labelText, Control editor, int width)
        {
            var field = new TableLayoutPanel
            {
                Width = width,
                Height = 40,
                RowCount = 2,
                ColumnCount = 1,
                Margin = new Padding(0, 0, 14, 0),
                BackColor = Color.White
            };
            field.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            field.RowStyles.Add(new RowStyle(SizeType.Absolute, 17));
            field.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var label = new Label
            {
                Text = labelText,
                Dock = DockStyle.Fill,
                ForeColor = MutedColor,
                TextAlign = ContentAlignment.MiddleLeft
            };
            // Pole edycyjne musi być ComboBox-em, bo ta metoda została zaprojektowana tylko dla list zakresów.
            ConfigureComboBox(editor as ComboBox ?? throw new ArgumentException("Range editor must be a ComboBox."), width - 2);
            // Dock Fill powoduje, że kontrolka wypełnia swoją komórkę układu.
            editor.Dock = DockStyle.Fill;
            field.Controls.Add(label, 0, 0);
            field.Controls.Add(editor, 0, 1);
            return field;
        }

        // Buduje standardowe pole formularza: etykieta u góry i kontrolka wejściowa pod spodem.
        // Używane dla wyboru portu i baudrate.
        private Control BuildField(string labelText, Control editor, int width)
        {
            var field = new TableLayoutPanel
            {
                Width = width,
                Height = 74,
                RowCount = 2,
                ColumnCount = 1,
                Margin = new Padding(0, 0, 14, 0),
                BackColor = Color.White
            };
            field.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            field.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            field.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

            var label = new Label
            {
                Text = labelText,
                Dock = DockStyle.Fill,
                ForeColor = MutedColor,
                TextAlign = ContentAlignment.MiddleLeft
            };
            // Wypełnienie całej dolnej komórki pozwala zachować jednolite szerokości kontrolek.
            editor.Dock = DockStyle.Fill;
            field.Controls.Add(label, 0, 0);
            field.Controls.Add(editor, 0, 1);
            return field;
        }

        // Buduje dolną część okna i dzieli ją na dwa obszary:
        // lewa kolumna pokazuje odebrane ramki tekstowe,
        // prawa kolumna pokazuje ostatnią ramkę i wykres przyspieszeń.
        private Control BuildContent()
        {
            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                // Padding oddziela sekcję roboczą od białego paska ustawień ponad nią.
                Padding = new Padding(24, 18, 24, 24),
                BackColor = WindowColor
            };
            // Lewa kolumna ma stałą szerokość, żeby lista ramek nie zmieniała proporcji całego ekranu.
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 390));
            // Prawa kolumna jest elastyczna i dostaje całą resztę miejsca.
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            content.Controls.Add(BuildReceivePanel(), 0, 0);
            content.Controls.Add(BuildLatestFramePanel(), 1, 0);
            return content;
        }

        // Tworzy lewy panel z historią odebranych danych UART.
        private Control BuildReceivePanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                // Cienkie obramowanie wydziela panel z tła formatki.
                BorderStyle = BorderStyle.FixedSingle,
                // 1 px paddingu zabezpiecza wizualnie zawartość przed kontaktem z obramowaniem.
                Padding = new Padding(1)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Color.White
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            // Nagłówek panelu.
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            // Cienki separator poziomy.
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));
            // Lista danych zabiera całą pozostałą przestrzeń.
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // Pasek nagłówka zawierający tytuł, licznik i przycisk czyszczenia.
            var heading = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(15, 0, 12, 0) };
            var title = new Label
            {
                Text = "ODEBRANE RAMKI",
                AutoSize = true,
                ForeColor = MutedColor,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Location = new Point(15, 17)
            };
            _frameCountText.Text = "0";
            _frameCountText.AutoSize = true;
            _frameCountText.ForeColor = AccentColor;
            _frameCountText.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            // Licznik stoi tuż za tytułem, więc pozycja jest nadana ręcznie.
            _frameCountText.Location = new Point(145, 17);

            // Przycisk pomocniczy czyszczący historię w panelu odbioru.
            _clearButton.Text = "Wyczyść";
            ConfigureSecondaryButton(_clearButton, 82);
            // W nagłówku chcemy trochę niższy przycisk niż standardowe 38 px.
            _clearButton.Height = 30;
            _clearButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            // Pozycja jest liczona od prawej krawędzi, żeby przycisk trzymał się końca panelu.
            _clearButton.Location = new Point(heading.ClientSize.Width - 94, 8);
            _clearButton.Click += (_, _) => ClearReceivedFrames();
            // Przy każdej zmianie szerokości nagłówka przycisk jest dorysowywany od nowa przy prawej krawędzi.
            heading.Resize += (_, _) => _clearButton.Location = new Point(heading.ClientSize.Width - 94, 8);
            heading.Controls.Add(title);
            heading.Controls.Add(_frameCountText);
            heading.Controls.Add(_clearButton);

            // Lista z użyciem fontu monospace ułatwia czytanie ramek i wyrównanie danych liczbowych.
            _receivedListBox.Dock = DockStyle.Fill;
            _receivedListBox.BorderStyle = BorderStyle.None;
            _receivedListBox.BackColor = Color.White;
            _receivedListBox.ForeColor = InkColor;
            _receivedListBox.Font = new Font("Consolas", 9.5f);
            // Ramki mogą być szerokie, więc dopuszczamy przewijanie poziome.
            _receivedListBox.HorizontalScrollbar = true;
            // Lista ma rozciągać się płynnie, zamiast dopasowywać wysokość do pełnych elementów.
            _receivedListBox.IntegralHeight = false;

            var divider = new Panel { Dock = DockStyle.Fill, BackColor = BorderColor, Height = 1 };
            layout.Controls.Add(heading, 0, 0);
            layout.Controls.Add(divider, 0, 1);
            layout.Controls.Add(_receivedListBox, 0, 2);
            panel.Controls.Add(layout);
            return panel;
        }

        // Buduje prawą stronę aplikacji: ostatnią zdekodowaną ramkę i obszar zakładek.
        // Pierwsza zakładka nadal zawiera wykres akcelerometru, a dwie kolejne zostawiają
        // gotowe miejsce pod następne widoki aplikacji.
        private Control BuildLatestFramePanel()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                BackColor = WindowColor,
                // Większy lewy padding odsuwa prawy panel od listy ramek i poprawia czytelność.
                Padding = new Padding(32, 12, 8, 8)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // Kontrolka kart zastępuje dawny pojedynczy obszar wykresu.
            // Dzięki temu w tym samym miejscu można przełączać kilka niezależnych widoków.
            var tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = new Point(18, 6)
            };

            // Pierwsza zakładka przejmuje istniejący wykres, więc logika odświeżania danych
            // z Form1 może pozostać bez zmian.
            var plotTab = new TabPage(" Akcelerometr")
            {
                BackColor = Color.White,
                Padding = new Padding(8)
            };

            // Druga zakładka pokazuje przebiegi żyroskopu w stopniach na sekundę.
            var rawDataTab = new TabPage("Żyroskop")
            {
                BackColor = Color.White,
                Padding = new Padding(8)
            };
            var diagnosticsTab = new TabPage("Wizualizacja")
            {
                BackColor = Color.White,
                Padding = new Padding(8)
            };

            var heading = new Label
            {
                Text = "OSTATNIA RAMKA",
                Dock = DockStyle.Fill,
                ForeColor = MutedColor,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = Padding.Empty
            };
            // Myślnik sygnalizuje brak danych tuż po uruchomieniu aplikacji.
            _latestFrameText.Text = "—";
            _latestFrameText.Dock = DockStyle.Fill;
            _latestFrameText.ForeColor = InkColor;
            _latestFrameText.Font = new Font("Consolas", 11, FontStyle.Regular);
            // Gdy tekst jest za długi, końcówka zostanie zastąpiona wielokropkiem zamiast wyjść poza panel.
            _latestFrameText.AutoEllipsis = true;
            _latestFrameText.Margin = Padding.Empty;

            _lastReceivedText.Text = "Brak odebranych danych";
            _lastReceivedText.Dock = DockStyle.Fill;
            _lastReceivedText.ForeColor = MutedColor;
            _lastReceivedText.Font = new Font("Segoe UI", 9);
            _lastReceivedText.TextAlign = ContentAlignment.MiddleLeft;
            _lastReceivedText.Margin = Padding.Empty;

            var plotHeading = new Label
            {
                Text = "PRZEBIEG AKCELEROMETRU · OSTATNIE 3 MIN",
                Dock = DockStyle.Fill,
                ForeColor = MutedColor,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = Padding.Empty
            };

            // Wykres nadal zajmuje całe wnętrze swojej zakładki.
            _accelerationPlot.Dock = DockStyle.Fill;
            _accelerationPlot.Margin = Padding.Empty;
            _gyroscopePlot.Dock = DockStyle.Fill;
            _gyroscopePlot.Margin = Padding.Empty;

            // Trzecia zakładka pokazuje orientację czujnika w 3D.
            _orientationView.Dock = DockStyle.Fill;
            _orientationView.Margin = Padding.Empty;

            plotTab.Controls.Add(_accelerationPlot);
            rawDataTab.Controls.Add(_gyroscopePlot);
            diagnosticsTab.Controls.Add(_orientationView);
            tabs.TabPages.Add(plotTab);
            tabs.TabPages.Add(rawDataTab);
            tabs.TabPages.Add(diagnosticsTab);

            layout.Controls.Add(heading, 0, 0);
            layout.Controls.Add(_latestFrameText, 0, 1);
            layout.Controls.Add(_lastReceivedText, 0, 2);
            layout.Controls.Add(plotHeading, 0, 3);
            layout.Controls.Add(tabs, 0, 4);
            return layout;
        }

        // Wspólna konfiguracja list rozwijanych. Dzięki tej metodzie wszystkie ComboBox-y
        // w formularzu wyglądają i zachowują się spójnie.
        private static void ConfigureComboBox(ComboBox comboBox, int width)
        {
            // Szerokość jest parametryzowana, bo różne sekcje używają różnych wariantów układu.
            comboBox.Width = width;
            comboBox.Height = 38;
            // Użytkownik może tylko wybrać jedną z dostarczonych pozycji.
            comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox.Font = new Font("Segoe UI", 9.5f);
            comboBox.BackColor = Color.White;
            comboBox.ForeColor = InkColor;
            // Prawy margines tworzy odstęp między kolejnymi kontrolkami w FlowLayoutPanel.
            comboBox.Margin = new Padding(0, 0, 14, 0);
        }

        // Formatowanie głównego przycisku akcji.
        // Tego stylu używamy dla przycisków, które mają prowadzić wzrok użytkownika do podstawowej operacji.
        private static void ConfigurePrimaryButton(Button button, int width)
        {
            button.Width = width;
            button.Height = 38;
            // Flat daje nowocześniejszy wygląd niż klasyczny Button WinForms.
            button.FlatStyle = FlatStyle.Flat;
            // Główny przycisk nie ma dodatkowej ramki, ma być pełnym, kolorowym blokiem.
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = AccentColor;
            button.ForeColor = Color.White;
            button.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            // Kursor ręki wzmacnia komunikat, że element jest klikalny.
            button.Cursor = Cursors.Hand;
        }

        // Formatowanie przycisku pomocniczego.
        // Ten wariant jest spokojniejszy wizualnie: białe tło, cienka ramka i lżejszy font.
        private static void ConfigureSecondaryButton(Button button, int width)
        {
            button.Width = width;
            button.Height = 38;
            button.FlatStyle = FlatStyle.Flat;
            // Jasna ramka odcina biały przycisk od białego tła panelu.
            button.FlatAppearance.BorderColor = BorderColor;
            button.FlatAppearance.BorderSize = 1;
            button.BackColor = Color.White;
            button.ForeColor = InkColor;
            button.Font = new Font("Segoe UI", 8.5f);
            button.Cursor = Cursors.Hand;
        }
    }
}