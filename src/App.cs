// BlackBox, la scatola nera del PC - applicazione con icona nella barra di sistema.
// PRIVACY: nessuna telemetria. Le uniche connessioni di rete sono i download da GitHub che l'utente avvia con un pulsante
// (PresentMon, OptiScaler). I dati restano in Documenti\BlackBox.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Path = System.IO.Path;
using WF = System.Windows.Forms;
using SD = System.Drawing;

[assembly: AssemblyTitle("BlackBox")]
[assembly: AssemblyProduct("BlackBox")]
[assembly: AssemblyVersion("1.2.8.0")]
[assembly: AssemblyFileVersion("1.2.8.0")]
[assembly: AssemblyInformationalVersion("1.2.8")]
[assembly: AssemblyDescription("BlackBox - la scatola nera del PC, fatta per giocare")]

namespace BlackBox
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            bool tray = args.Contains("--tray");
            if (args.Contains("--exit"))
            {
                // chiude con garbo un BlackBox gia' aperto (lo usa l'installer prima di aggiornare)
                EventWaitHandle ev;
                if (EventWaitHandle.TryOpenExisting(@"Local\BlackBoxApp_Exit", out ev)) { ev.Set(); ev.Dispose(); }
                return 0;
            }
            string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BlackBox");
            string snapshot = null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--logdir") logDir = args[i + 1];
                if (args[i] == "--snapshot") snapshot = args[i + 1];
            }

            bool created;
            var mutex = new Mutex(true, snapshot == null ? @"Local\BlackBoxApp" : @"Local\BlackBoxSnap", out created);
            var showEvt = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\BlackBoxApp_Show");
            if (!created) { showEvt.Set(); return 0; }   // gia' in esecuzione: mostra la finestra esistente

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try
            {
                var main = new MainApp(app, logDir, tray, snapshot, showEvt);
                app.Run();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore all'avvio:\n\n" + ex.Message, "BlackBox", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
            GC.KeepAlive(mutex);
            return 0;
        }
    }

    class Tile
    {
        public TextBlock Value, Sub;
        public Canvas Cv;
        public Polyline Line;
        public Polygon Fill;
        public double Max;
    }

    public class MainApp
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "BlackBox";

        readonly Application app;
        readonly string logDir, snapshot;
        readonly Window win;
        readonly Collector col;
        readonly WF.NotifyIcon tray;
        readonly DispatcherTimer timer;
        readonly Dictionary<string, Tile> tiles = new Dictionary<string, Tile>();
        bool exiting, crashShown, trayHintShown;
        string crashReport;
        int tick;

        static readonly Brush Muted = B("#8A91A0"), Text = B("#E6E8EE"), Red = B("#FF5C6C"), Orange = B("#FFB547"), Green = B("#5EE08A"), Blue = B("#4FC3F7");

        static SolidColorBrush B(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        T F<T>(string name) where T : class { return win.FindName(name) as T; }

        public MainApp(Application app, string logDir, bool startInTray, string snapshot, EventWaitHandle showEvt)
        {
            this.app = app; this.logDir = logDir; this.snapshot = snapshot;

            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml"))
                win = (Window)XamlReader.Load(s);
            // schermi piccoli: la misura minima non puo' superare lo schermo (in quel caso la finestra si apre a tutto schermo)
            var wa = SystemParameters.WorkArea;
            if (win.MinWidth > wa.Width || win.MinHeight > wa.Height)
            {
                win.MinWidth = Math.Min(win.MinWidth, wa.Width); win.MinHeight = Math.Min(win.MinHeight, wa.Height);
                win.WindowState = WindowState.Maximized;
            }
            win.Width = Math.Min(win.Width, wa.Width); win.Height = Math.Min(win.Height, wa.Height);
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
                    win.Icon = BitmapFrame.Create(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            }
            catch { }

            NewTile("cpu", "CPU carico", "%", "#4FC3F7", 100);
            NewTile("mhz", "CPU frequenza reale", "MHz", "#7AA2FF", 5500);
            NewTile("gpu", "GPU carico", "%", "#B388FF", 100);
            NewTile("pow", "GPU potenza", "W", "#FFB547", 0);   // 0 = scala automatica
            NewTile("temp", "GPU temperatura", "°C", "#FF7A7A", 110);
            NewTile("clk", "GPU frequenza", "MHz", "#5EE08A", 3000);
            NewTile("ram", "RAM impegnata", "%", "#4DD0C8", 100);
            NewTile("vram", "VRAM usata", "GB", "#F48FB1", 0);

            col = new Collector(logDir);
            col.Start();
            F<TextBlock>("SubTitle").Text = "v" + Version + "  •  registra ogni secondo  •  tutto in locale";
            F<TextBlock>("SubTitle").ToolTip = "Registrazione su disco ogni secondo. 100% locale: internet solo per i download che avvii tu.\nDati in: " + logDir;

            if (snapshot == null) overlay = new Overlay(col);
            LoadPastEvents();
            WireButtons();
            UpdateAutoBtn();

            // ---- icona nella barra di sistema
            tray = new WF.NotifyIcon();
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
                    tray.Icon = new SD.Icon(s, WF.SystemInformation.SmallIconSize);
            }
            catch { tray.Icon = SD.SystemIcons.Application; }
            tray.Text = "BlackBox - in registrazione";
            var menu = new WF.ContextMenuStrip();
            menu.Items.Add("Apri monitor", null, (s, e) => ShowWindow());
            menu.Items.Add("Ultimo report", null, (s, e) => OpenLastReport());
            menu.Items.Add("Ultima sessione di gioco", null, (s, e) => OpenLastSession());
            menu.Items.Add("Partite e confronti", null, (s, e) => { ShowWindow(); OpenSessions(); });
            menu.Items.Add("Giochi && OptiScaler", null, (s, e) => { ShowWindow(); OpenGames(); });
            menu.Items.Add("Copia l'ultimo report di crash (da incollare)", null, (s, e) => CopyLastCrash());
            menu.Items.Add("Il tuo PC (hardware riconosciuto)", null, (s, e) => ShowMyPc(false));
            menu.Items.Add("Apri cartella log", null, (s, e) => OpenLogs());
            menu.Items.Add("Guida", null, (s, e) => OpenGuide());
            menu.Items.Add(new WF.ToolStripSeparator());
            if (overlay != null) menu.Items.Add(OverlayMenu());
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add("Esci (interrompe la registrazione)", null, (s, e) => Exit());
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == WF.MouseButtons.Left) ShowWindow(); };
            tray.Visible = snapshot == null;

            // ---- chiudere o ridurre a icona = nascondi nella barra, la registrazione continua
            win.StateChanged += (s, e) => { if (win.WindowState == WindowState.Minimized) HideToTray(); };
            // finestra chiusa = BlackBox restituisce a Windows la memoria della grafica che non serve
            win.IsVisibleChanged += (s, e) => { col.WindowHidden = !win.IsVisible; if (!win.IsVisible) Collector.TrimMemory(); };
            win.Closing += (s, e) => { if (!exiting) { e.Cancel = true; HideToTray(); } };
            app.SessionEnding += (s, e) => { exiting = true; col.Stop("Windows in arresto: BlackBox chiuso"); tray.Visible = false; };

            // ---- richiesta di chiusura da un altro processo (installer: BlackBox.exe --exit)
            var exitEvt = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\BlackBoxApp_Exit");
            var te = new Thread(() => { exitEvt.WaitOne(); app.Dispatcher.BeginInvoke(new Action(() => { if (!exiting) Exit(); })); });
            te.IsBackground = true; te.Start();

            // ---- una seconda apertura dell'exe mostra questa finestra
            var t = new Thread(() => { while (true) { showEvt.WaitOne(); app.Dispatcher.BeginInvoke(new Action(ShowWindow)); } });
            t.IsBackground = true; t.Start();

            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (s, e) => OnTick();
            timer.Start();

            if (!startInTray) win.Show();
        }

        // ------------------------------------------------------------ finestra / barra
        void ShowWindow()
        {
            win.Show();
            if (win.WindowState == WindowState.Minimized) win.WindowState = WindowState.Normal;
            win.Activate();
            win.Topmost = true; win.Topmost = false;
        }

        void HideToTray()
        {
            win.Hide();
            if (!trayHintShown && snapshot == null)
            {
                trayHintShown = true;
                tray.ShowBalloonTip(4000, "BlackBox", "Continua a registrare in background. Clicca l'icona per riaprirlo, tasto destro per uscire.", WF.ToolTipIcon.Info);
            }
        }

        // ------------------------------------------------------------ overlay
        Overlay overlay;

        WF.ToolStripMenuItem OverlayMenu()
        {
            var root = new WF.ToolStripMenuItem("Overlay in gioco");
            var on = new WF.ToolStripMenuItem("Attivo");
            on.Click += (s, e) => overlay.SetEnabled(!overlay.Enabled);
            var always = new WF.ToolStripMenuItem("Mostra anche fuori dai giochi");
            always.Click += (s, e) => overlay.SetAlways(!Overlay.Always);
            var preview = new WF.ToolStripMenuItem("Anteprima (8 secondi)");
            preview.Click += (s, e) => overlay.Preview();
            var checks = new List<Action>();
            Func<string, string[][], Func<string>, Action<string>, WF.ToolStripMenuItem> choice = (title, list, cur, set) =>
            {
                var m = new WF.ToolStripMenuItem(title);
                foreach (var x in list)
                {
                    string v = x[1]; var it = new WF.ToolStripMenuItem(x[0]);
                    it.Click += (s, e) => set(v);
                    checks.Add(() => it.Checked = cur() == v);
                    m.DropDownItems.Add(it);
                }
                return m;
            };
            var parts = new WF.ToolStripMenuItem("Cosa mostrare");
            foreach (var x in Overlay.Parts)
            {
                string v = x[1]; var it = new WF.ToolStripMenuItem(x[0]);
                it.Click += (s, e) => overlay.TogglePart(v);
                checks.Add(() => it.Checked = Overlay.Shows(v));
                parts.DropDownItems.Add(it);
            }
            root.DropDownItems.Add(on);
            root.DropDownItems.Add(always);
            root.DropDownItems.Add(preview);
            root.DropDownItems.Add(new WF.ToolStripSeparator());
            root.DropDownItems.Add(choice("Forma", Overlay.Shapes, () => Overlay.Shape, overlay.SetShape));
            root.DropDownItems.Add(choice("Posizione", Overlay.Positions, () => Overlay.Corner, overlay.SetCorner));
            root.DropDownItems.Add(choice("Dimensione del testo", Overlay.Sizes, () => Overlay.FontSize.ToString(CultureInfo.InvariantCulture), overlay.SetFontSize));
            root.DropDownItems.Add(choice("Colore del testo", Overlay.TextColors, () => Overlay.TextColor, overlay.SetTextColor));
            root.DropDownItems.Add(choice("Colore del grafico", Overlay.GraphColors, () => Overlay.GraphColor, overlay.SetGraphColor));
            root.DropDownItems.Add(choice("Sfondo", Overlay.Backgrounds, () => Overlay.Background, overlay.SetBackground));
            root.DropDownItems.Add(parts);
            root.DropDownOpening += (s, e) =>
            {
                on.Checked = overlay.Enabled; on.Text = "Attivo   (" + overlay.HotkeyText + ")";
                always.Checked = Overlay.Always;
                foreach (var c in checks) c();
            };
            foreach (WF.ToolStripItem it in root.DropDownItems)
            {
                var mi = it as WF.ToolStripMenuItem;
                if (mi != null && mi.DropDownItems.Count > 0) mi.DropDownOpening += (s, e) => { foreach (var c in checks) c(); };
            }
            overlay.Changed += UpdateOverlayBtn;
            return root;
        }
        void UpdateOverlayBtn()
        {
            var b = F<Button>("BtnOverlay");
            if (overlay == null) { b.Visibility = Visibility.Collapsed; return; }
            b.Content = "Overlay: " + (overlay.Enabled ? "ON" : "OFF");
            b.ToolTip = "Overlay in gioco. Tasto rapido " + overlay.HotkeyText + ". Posizione e dimensione: tasto destro sull'icona di BlackBox → Overlay in gioco.";
        }

        void Exit()
        {
            exiting = true;
            timer.Stop();
            col.Stop("BlackBox chiuso dall'utente");
            if (overlay != null) overlay.Dispose();
            tray.Visible = false;
            tray.Dispose();
            app.Shutdown();
        }

        // ------------------------------------------------------------ riquadri
        void NewTile(string key, string title, string unit, string color, double max)
        {
            var border = new Border { Style = (Style)win.Resources["Card"] };
            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var t = new TextBlock { Text = title.ToUpper(), FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Muted };
            var vp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            var v = new TextBlock { Text = "--", FontSize = 30, FontWeight = FontWeights.SemiBold };
            var u = new TextBlock { Text = " " + unit, FontSize = 14, Foreground = Muted, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(2, 0, 0, 6) };
            vp.Children.Add(v); vp.Children.Add(u);
            var sub = new TextBlock { FontSize = 12, Foreground = Muted, TextTrimming = TextTrimming.CharacterEllipsis };
            var cv = new Canvas { Height = 30, Margin = new Thickness(0, 6, 0, 0), ClipToBounds = true };   // basso: lascia spazio ai riquadri sotto
            var c = (Color)ColorConverter.ConvertFromString(color);
            var fill = new Polygon { Fill = new LinearGradientBrush(Color.FromArgb(90, c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B), 90) };
            var line = new Polyline { StrokeThickness = 1.8, StrokeLineJoin = PenLineJoin.Round, Stroke = B(color) };
            cv.Children.Add(fill); cv.Children.Add(line);
            Grid.SetRow(vp, 1); Grid.SetRow(sub, 2); Grid.SetRow(cv, 3);
            g.Children.Add(t); g.Children.Add(vp); g.Children.Add(sub); g.Children.Add(cv);
            border.Child = g;
            F<System.Windows.Controls.Primitives.UniformGrid>("Tiles").Children.Add(border);
            tiles[key] = new Tile { Value = v, Sub = sub, Cv = cv, Line = line, Fill = fill, Max = max };
        }

        static void Spark(Tile tile, double[] vals)
        {
            double w = tile.Cv.ActualWidth, h = tile.Cv.ActualHeight;
            if (w <= 0 || vals.Length < 2) return;
            double top = 0;
            foreach (var x in vals) if (!double.IsNaN(x) && x > top) top = x;
            double max = tile.Max > 0 ? Math.Max(tile.Max, top) : Math.Max(1, top * 1.25);
            const int n = 120;
            int offset = n - vals.Length;
            var pts = new PointCollection();
            for (int i = 0; i < vals.Length; i++)
            {
                double v = double.IsNaN(vals[i]) ? 0 : vals[i];
                pts.Add(new Point((i + offset) / (double)(n - 1) * w, h - 2 - v / max * (h - 4)));
            }
            var poly = new PointCollection(pts);
            poly.Add(new Point(pts[pts.Count - 1].X, h));
            poly.Add(new Point(pts[0].X, h));
            tile.Line.Points = pts;
            tile.Fill.Points = poly;
        }

        static string Fmt(double v, int d = 0)
        {
            return double.IsNaN(v) ? "--" : Math.Round(v, d).ToString("N" + d, CultureInfo.CurrentCulture);
        }

        // ------------------------------------------------------------ storico spegnimenti
        void UpdateHistory()
        {
            var list = F<StackPanel>("HistList");
            list.Children.Clear();
            var hist = Analyzer.History(120, logDir);
            F<TextBlock>("HistTitle").Text = "SPEGNIMENTI ANOMALI (KERNEL-POWER 41) — " + hist.Count + " negli ultimi 120 giorni";
            foreach (var h in hist)
            {
                var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(115) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(215) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                string bsod = h.Bugcheck == 0 ? "nessun BSOD" : "BSOD 0x" + h.Bugcheck.ToString("X");
                var t1 = new TextBlock { Text = h.BootTime.ToString("dd/MM/yy HH:mm"), FontWeight = FontWeights.SemiBold };
                var t2 = new TextBlock { Text = bsod + " · " + Analyzer.SleepText(h.SleepState), Foreground = Muted, TextTrimming = TextTrimming.CharacterEllipsis };
                string txt = h.Verdict != "" ? h.Verdict : "BlackBox non attivo";
                if (h.Config != "") txt += "   [cfg: " + h.Config + "]";
                var t3 = new TextBlock { Text = txt, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = txt };
                t3.Foreground = h.Verdict != "" && h.Verdict != "BlackBox non attivo" ? Orange : Muted;
                if (h.ReportPath != "")
                {
                    string p = h.ReportPath;
                    t3.Cursor = System.Windows.Input.Cursors.Hand;
                    t3.TextDecorations = TextDecorations.Underline;
                    t3.MouseLeftButtonUp += (s, e) => OpenFile(p);
                }
                Grid.SetColumn(t2, 1); Grid.SetColumn(t3, 2);
                row.Children.Add(t1); row.Children.Add(t2); row.Children.Add(t3);
                list.Children.Add(row);
            }
            UpdateDriverCrashes();
            UpdateConfig();
        }

        // crash del driver video e dei giochi, raggruppati per gioco e per configurazione A/B
        void UpdateDriverCrashes()
        {
            var items = SessionLog.Crashes(logDir, 120);
            var game = F<TextBlock>("DrvGame"); var cfg = F<TextBlock>("DrvCfg");
            if (items.Count == 0)
            {
                game.Text = "Nessun crash del driver video né dei giochi.";
                game.Foreground = Green; cfg.Text = "";
                return;
            }
            Func<IEnumerable<CrashItem>, string> count = g =>
            {
                int t = g.Count(x => x.Kind == "TDR"), c = g.Count(x => x.Kind == "crash");
                return string.Join(", ", new[] { t > 0 ? t + " TDR" : "", c > 0 ? c + " crash del gioco" : "" }.Where(x => x != ""));
            };
            game.Foreground = Orange;
            game.Text = "Per gioco:  " + string.Join("   ·   ", items.GroupBy(x => x.Game).OrderByDescending(g => g.Count()).Select(g => g.Key + " — " + count(g)));
            cfg.Text = "Per configurazione:  " + string.Join("   ·   ", items.GroupBy(x => x.Config).OrderByDescending(g => g.Count()).Select(g => g.Key + " — " + count(g)));
        }

        // ------------------------------------------------------------ configurazione A/B
        void UpdateConfig()
        {
            string cfg = Analyzer.ConfigAt(logDir, DateTime.Now);
            DateTime since = Analyzer.ConfigSince(logDir);
            var info = F<TextBlock>("CfgInfo"); var days = F<TextBlock>("CfgDays"); var lbl = F<TextBlock>("CfgDaysLbl");
            if (cfg == "") { info.Text = "Nessuna configurazione indicata."; days.Text = ""; lbl.Text = ""; return; }
            var box = F<TextBox>("CfgBox");
            if (!box.IsKeyboardFocused) box.Text = cfg;
            int crashes = Analyzer.History(365, logDir).Count(h => h.BootTime >= since);
            info.Text = "Configurazione attiva dal " + since.ToString("dd/MM/yyyy HH:mm");   // il testo e' gia' nella casella sopra
            if (crashes == 0)
            {
                // giorni puliti necessari: almeno 7, o il doppio dell'intervallo medio tra gli spegnimenti di questo PC
                double d = (DateTime.Now - since).TotalDays;
                double mean = Analyzer.MeanCrashInterval(logDir);
                double need = double.IsNaN(mean) ? 7 : Math.Max(7, Math.Ceiling(mean * 2));
                double hrs = (DateTime.Now - since).TotalHours;
                days.Text = (hrs < 1 ? "Meno di un'ora" : hrs < 24 ? Math.Floor(hrs).ToString("0") + (Math.Floor(hrs) == 1 ? " ora" : " ore")
                            : d < 2 ? "1 giorno" : Math.Floor(d).ToString("0") + " giorni") + " senza spegnimenti";
                days.Foreground = d >= need ? Green : Blue;
                lbl.Text = d >= need
                    ? "Stabile da " + d.ToString("N0") + " giorni: questa modifica sembra aver risolto."
                    : (double.IsNaN(mean) ? "" : "Su questo PC gli spegnimenti capitavano in media ogni " + mean.ToString("N1") + " giorni. ")
                      + "Servono almeno " + need.ToString("N0") + " giorni puliti per esserne sicuri.";
            }
            else
            {
                days.Text = crashes + " spegnimenti con questa configurazione";
                days.Foreground = Red;
                lbl.Text = "Questa configurazione NON risolve: cambia una sola cosa alla volta e salvala qui.";
            }
        }

        // ------------------------------------------------------------ eventi
        void AddEventRow(EventLine ev)
        {
            var list = F<StackPanel>("EvList");
            var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
            tb.Inlines.Add(new Run(ev.Time.ToString("dd/MM HH:mm:ss") + "   ") { Foreground = Muted });
            tb.Inlines.Add(new Run(ev.Text) { Foreground = ev.Level == "ERR" ? Red : ev.Level == "WARN" ? Orange : Text });
            list.Children.Insert(0, tb);
            while (list.Children.Count > 300) list.Children.RemoveAt(list.Children.Count - 1);
            if (ev.Text.Contains("Kernel-Power 41")) UpdateHistory();
            else if (ev.Text.Contains("Display 4101") || ev.Text.StartsWith("Sessione di gioco finita")) UpdateDriverCrashes();
            if ((ev.Level == "ERR" || ev.Text.StartsWith("Sbloccato") || ev.Text.StartsWith("Adrenalin ha ripristinato")) && snapshot == null)
            {
                // in partita niente notifiche (possono dare uno scatto o rubare lo schermo): le mostro a fine partita
                if (col.Games.InSession) heldNotices.Add(ev.Text);
                else tray.ShowBalloonTip(8000, "BlackBox", ev.Text, WF.ToolTipIcon.Warning);
            }
        }

        void LoadPastEvents()
        {
            try
            {
                if (!File.Exists(col.EventLogPath)) return;
                string[] lines;
                using (var fs = new FileStream(col.EventLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs)) lines = sr.ReadToEnd().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var rx = new System.Text.RegularExpressions.Regex(@"^(\S+ \S+) \[(\w+)\] (.*)$");
                var list = F<StackPanel>("EvList");
                foreach (var l in lines.Skip(Math.Max(0, lines.Length - 60)))
                {
                    var m = rx.Match(l.TrimEnd('\r'));
                    if (!m.Success) continue;
                    var ev = new EventLine { Time = DateTime.ParseExact(m.Groups[1].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), Level = m.Groups[2].Value, Text = m.Groups[3].Value };
                    var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
                    tb.Inlines.Add(new Run(ev.Time.ToString("dd/MM HH:mm:ss") + "   ") { Foreground = Muted });
                    tb.Inlines.Add(new Run(ev.Text) { Foreground = ev.Level == "ERR" ? Red : ev.Level == "WARN" ? Orange : Text });
                    list.Children.Insert(0, tb);
                }
            }
            catch { }
        }

        // ------------------------------------------------------------ pulsanti
        static void OpenFile(string path) { try { Process.Start("notepad.exe", "\"" + path + "\""); } catch { } }
        void OpenLogs() { try { Process.Start("explorer.exe", "\"" + logDir + "\""); } catch { } }

        void OpenLastReport()
        {
            var r = new DirectoryInfo(logDir).GetFiles("crash_*.txt").OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
            if (r != null) OpenFile(r.FullName);
            else MessageBox.Show("Nessun report ancora: verrà creato al primo spegnimento anomalo con BlackBox attivo.", "BlackBox");
        }

        void OpenLastSession()
        {
            var r = new DirectoryInfo(logDir).GetFiles("sessione_*.txt").OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
            if (r != null) OpenFile(r.FullName);
            else MessageBox.Show("Nessuna sessione di gioco ancora: il report viene creato quando chiudi un gioco.", "BlackBox");
        }

        // la guida e' dentro l'exe: la scrive in Documenti\BlackBox e la apre con il Blocco note
        void OpenGuide()
        {
            try
            {
                string p = Path.Combine(logDir, "GUIDA.txt");
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("GUIDA.md"))
                using (var sr = new StreamReader(s))
                    File.WriteAllText(p, sr.ReadToEnd(), new System.Text.UTF8Encoding(true));
                OpenFile(p);
            }
            catch (Exception ex) { MessageBox.Show("Impossibile aprire la guida:\n\n" + ex.Message, "BlackBox"); }
        }

        static string ExePath { get { return Assembly.GetExecutingAssembly().Location; } }
        public static string Version { get { var v = Assembly.GetExecutingAssembly().GetName().Version; return v.Major + "." + v.Minor + "." + v.Build; } }

        bool AutoStartOn()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue(RunName) != null;
        }

        void UpdateAutoBtn() { F<Button>("BtnAuto").Content = AutoStartOn() ? "Avvio automatico: ON" : "Avvio automatico: OFF"; }

        void WireButtons()
        {
            F<Button>("BtnReport").Click += (s, e) => OpenLastReport();
            F<Button>("BtnFps").Click += (s, e) => OnFpsButton();
            F<Button>("BtnGames").Click += (s, e) => OpenGames();
            F<Button>("BtnSessions").Click += (s, e) => OpenSessions();
            F<TextBlock>("ActMax").MouseLeftButtonUp += (s, e) => ShowHealth();
            F<Button>("BtnOverlay").Click += (s, e) => { if (overlay != null) overlay.SetEnabled(!overlay.Enabled); };
            UpdateOverlayBtn();
            F<Button>("BtnLogs").Click += (s, e) => OpenLogs();
            F<Button>("BtnGuide").Click += (s, e) => OpenGuide();
            F<Button>("BtnTray").Click += (s, e) => HideToTray();
            F<Button>("BtnHideBanner").Click += (s, e) => F<Border>("Banner").Visibility = Visibility.Collapsed;
            F<Button>("BtnOpenCrash").Click += (s, e) => { if (crashReport != null) OpenFile(crashReport); };
            F<Button>("BtnAuto").Click += (s, e) =>
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (k.GetValue(RunName) != null) k.DeleteValue(RunName);
                    else k.SetValue(RunName, "\"" + ExePath + "\" --tray");
                }
                UpdateAutoBtn();
            };
            F<Button>("BtnCfg").Click += (s, e) =>
            {
                string t = F<TextBox>("CfgBox").Text.Trim();
                if (t == "") return;
                Analyzer.SetConfig(logDir, t);
                col.AddEvent("INFO", "Configurazione cambiata: " + t);
                UpdateConfig();
            };
        }

        // ------------------------------------------------------------ aggiornamento ogni secondo
        void OnTick()
        {
            try
            {
                tick++;
                if (!crashShown && col.CrashAnalysisDone)
                {
                    crashShown = true;
                    var lc = col.LastCrash;
                    if (lc != null && lc.IsNew)
                    {
                        crashReport = lc.ReportPath;
                        F<TextBlock>("BannerTitle").Text = "Spegnimento anomalo rilevato — riavvio del " + lc.BootTime.ToString("dd/MM HH:mm:ss");
                        F<TextBlock>("BannerText").Text = lc.Summary;
                        F<Border>("Banner").Visibility = Visibility.Visible;
                        if (snapshot == null)
                            tray.ShowBalloonTip(15000, "Spegnimento anomalo rilevato", lc.Summary, WF.ToolTipIcon.Warning);
                    }
                    UpdateHistory();
                }

                // benvenuto del primo avvio: mai sopra un gioco (aspetta che la GPU sia libera da 10 s)
                if (!welcomeShown && col.HardwareReady && snapshot == null && !col.Games.InSession && tick > 12 &&
                    col.GetHistory(10).All(x => double.IsNaN(x.Fg3d) || x.Fg3d < 30))
                {
                    welcomeShown = true;
                    if (Hardware.WelcomePending) ShowWelcome();
                }
                if (heldNotices.Count > 0 && !col.Games.InSession)
                {
                    string first = heldNotices[0];
                    tray.ShowBalloonTip(10000, heldNotices.Count == 1 ? "BlackBox — durante la partita" : "BlackBox — " + heldNotices.Count + " avvisi durante la partita",
                                        first + (heldNotices.Count > 1 ? "  (gli altri sono negli Eventi)" : ""), WF.ToolTipIcon.Warning);
                    heldNotices.Clear();
                }

                EventLine ev;
                while (col.NewEvents.TryDequeue(out ev)) AddEventRow(ev);

                var h = col.GetHistory(120);
                if (h.Length == 0) return;
                var s = h[h.Length - 1];

                string tip = "BlackBox - GPU " + Fmt(s.GpuPower) + " W, " + Fmt(s.GpuTempHot) + "°C";
                if (snapshot == null) tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;

                if (!win.IsVisible) return;   // finestra nascosta: registra soltanto

                tiles["cpu"].Value.Text = Fmt(s.CpuUtil);
                tiles["cpu"].Sub.Text = "core più carico " + Fmt(s.CpuCoreMax) + "% · " +
                    (double.IsNaN(s.CpuTctl) ? "temperatura n/d — apri HWiNFO con memoria condivisa" : Fmt(s.CpuTctl) + " °C (" + CpuTemp.Source + ")");
                tiles["cpu"].Value.Foreground = s.CpuTctl >= Limits.CpuMax ? Red : s.CpuTctl >= Limits.CpuMax - 4 ? Orange : Text;
                tiles["mhz"].Value.Text = Fmt(s.CpuMhz);
                tiles["mhz"].Sub.Text = "prestazioni " + Fmt(s.CpuPerf) + "% del clock base";
                tiles["gpu"].Value.Text = Fmt(s.Gpu3d);
                tiles["gpu"].Sub.Text = "attività driver " + Fmt(s.GpuAct) + "% · memoria " + Fmt(s.GpuMemAct) + "%";
                tiles["pow"].Value.Text = Fmt(s.GpuPower);
                tiles["pow"].Sub.Text = "picco 1s " + Fmt(s.GpuPowerMax) + " W · max sessione " + Fmt(col.SessionMaxPower) + " W";
                double everMax = col.EverMaxPower;
                tiles["pow"].Value.Foreground = !double.IsNaN(everMax) && everMax >= 100 && s.GpuPowerMax >= 0.9 * everMax ? Orange : Text;
                if (col.GpuVendor == "") tiles["pow"].Sub.Text = "sensori GPU non disponibili (supportate AMD e NVIDIA)";
                tiles["temp"].Value.Text = Fmt(s.GpuTempEdge);
                tiles["temp"].Sub.Text = "hotspot " + Fmt(s.GpuTempHot) + "° · memoria " + Fmt(s.GpuTempMem) + "° · max " + Fmt(col.SessionMaxHot) + "°";
                tiles["temp"].Value.Foreground = s.GpuTempHot >= Limits.HotBad ? Red : s.GpuTempHot >= Limits.HotWarn ? Orange : Text;
                tiles["clk"].Value.Text = Fmt(s.GpuClk);
                tiles["clk"].Sub.Text = Fmt(s.GpuVddc) + " mV · ventola " + Fmt(s.GpuFanRpm) + " rpm (" + Fmt(s.GpuFanPct) + "%)";
                tiles["ram"].Value.Text = Fmt(s.RamCommit);
                tiles["ram"].Sub.Text = "libera " + Fmt(s.RamFreeMb / 1024, 1) + " GB";
                tiles["vram"].Value.Text = Fmt(s.VramMb / 1024, 1);
                tiles["vram"].Sub.Text = "memoria dedicata della scheda video";

                Spark(tiles["cpu"], h.Select(x => x.CpuUtil).ToArray());
                Spark(tiles["mhz"], h.Select(x => x.CpuMhz).ToArray());
                Spark(tiles["gpu"], h.Select(x => x.Gpu3d).ToArray());
                Spark(tiles["pow"], h.Select(x => x.GpuPowerMax).ToArray());
                Spark(tiles["temp"], h.Select(x => x.GpuTempHot).ToArray());
                Spark(tiles["clk"], h.Select(x => x.GpuClk).ToArray());
                Spark(tiles["ram"], h.Select(x => x.RamCommit).ToArray());
                Spark(tiles["vram"], h.Select(x => x.VramMb / 1024).ToArray());

                F<TextBlock>("ActFg").Text = s.Foreground != "" ? s.Foreground : "--";
                F<TextBlock>("ActCpu").Text = s.TopCpuProc != "" ? s.TopCpuProc + "  (" + Fmt(s.TopCpuPct, 1) + "%)" : "--";
                F<TextBlock>("ActGpu").Text = s.TopGpuProc != "" ? s.TopGpuProc + "  (" + Fmt(s.TopGpuPct, 1) + "%)" : "--";
                string game = col.Games.StatusText;
                F<TextBlock>("ActGame").Text = game != "" ? game : "nessuna (parte da sola quando giochi)";
                F<TextBlock>("ActGame").Foreground = game != "" ? Green : Muted;
                UpdateFps(s);
                if (tick % 2 == 0)
                {
                    int lv; string sum = Health.Summary(Health.Check(col), out lv);
                    var am = F<TextBlock>("ActMax");
                    am.Text = (lv >= 2 ? "✖ " : lv == 1 ? "⚠ " : "✔ ") + sum;
                    am.Foreground = lv >= 2 ? Red : lv == 1 ? Orange : Green;
                }
                F<Ellipse>("RecDot").Opacity = tick % 2 == 1 ? 1 : 0.35;
                if (tick % 60 == 0) UpdateConfig();

                if (snapshot != null && tick == 8) SaveSnapshot();
            }
            catch (Exception ex)
            {
                F<TextBlock>("SubTitle").Text = "Errore interfaccia: " + ex.Message;
            }
        }

        // ------------------------------------------------------------ FPS (PresentMon)
        bool fpsBusy, fpsRelogin;

        void UpdateFps(Sample s)
        {
            var t = F<TextBlock>("ActFps"); var b = F<Button>("BtnFps");
            b.Visibility = Visibility.Collapsed; t.Foreground = Text;
            if (fpsBusy) { t.Text = "un momento..."; t.Foreground = Muted; return; }
            if (!Fps.ToolPresent)
            {
                t.Text = "serve PresentMon (gratuito, di Intel)"; t.Foreground = Muted;
                b.Content = "Scarica PresentMon"; b.Visibility = Visibility.Visible; return;
            }
            if (!Fps.HasPermission || Fps.Denied)
            {
                t.Foreground = Orange;
                if (fpsRelogin) { t.Text = "permesso dato: esci da Windows e rientra (o riavvia)"; return; }
                t.Text = "manca il permesso per leggere gli FPS";
                b.Content = "Abilita FPS (chiede admin una volta)"; b.Visibility = Visibility.Visible; return;
            }
            if (!double.IsNaN(s.Fps))
            {
                t.Text = Fmt(s.Fps) + " FPS · 1% low " + Fmt(s.Fps1Low) + " · " + Fmt(s.FrameTimeMs, 1) + " ms";
                t.Foreground = s.Fps < 30 ? Red : s.Fps < 50 ? Orange : Green;
                return;
            }
            t.Foreground = Muted;
            t.Text = Fps.Running ? (Fps.Status != "" ? Fps.Status : "in attesa dei fotogrammi") : "si misurano durante le sessioni di gioco" + (Fps.ToolVersion != "" ? " (PresentMon " + Fps.ToolVersion + ")" : "");
        }

        void OnFpsButton()
        {
            if (fpsBusy) return;
            if (!Fps.ToolPresent)
            {
                var r = MessageBox.Show("BlackBox scaricherà PresentMon (circa 1 MB) dalla pagina ufficiale di Intel su GitHub:\n" +
                    "github.com/GameTechDev/PresentMon\n\nÈ l'unica connessione a internet, e solo perché l'hai chiesta tu. " +
                    "Il file viene controllato (firma digitale di Intel) e salvato in Documenti\\BlackBox\\tools.\n\nProcedo?",
                    "Scarica PresentMon", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return;
                fpsBusy = true;
                new Thread(() =>
                {
                    string msg; bool ok = false;
                    try { msg = "PresentMon " + Fps.Download() + " scaricato e verificato"; ok = true; }
                    catch (Exception ex) { msg = "Download di PresentMon non riuscito: " + ex.Message; }
                    col.AddEvent(ok ? "INFO" : "WARN", msg);
                    app.Dispatcher.BeginInvoke(new Action(() => { fpsBusy = false; MessageBox.Show(msg, "BlackBox"); }));
                }) { IsBackground = true }.Start();
                return;
            }
            var q = MessageBox.Show("Per leggere gli FPS Windows deve permettere a BlackBox di vedere gli eventi della grafica.\n\n" +
                "Ora Windows chiederà UNA VOLTA SOLA di confermare come amministratore: il tuo utente verrà aggiunto al gruppo " +
                "«Performance Log Users». BlackBox continuerà a funzionare senza permessi di amministratore.\n\n" +
                "Dopo bisogna uscire da Windows e rientrare (o riavviare) perché valga.\n\nProcedo?",
                "Abilita FPS", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (q != MessageBoxResult.Yes) return;
            fpsBusy = true;
            new Thread(() =>
            {
                string err; bool ok = Fps.RequestPermission(out err);
                if (ok) col.AddEvent("INFO", "Permesso per gli FPS richiesto: vale dopo essere usciti da Windows e rientrati");
                app.Dispatcher.BeginInvoke(new Action(() =>
                {
                    fpsBusy = false;
                    if (ok) { fpsRelogin = true; MessageBox.Show("Fatto. Esci da Windows e rientra (o riavvia il PC): da lì gli FPS funzioneranno.", "BlackBox"); }
                    else MessageBox.Show("Permesso non dato: " + err, "BlackBox");
                }));
            }) { IsBackground = true }.Start();
        }

        bool welcomeShown;
        readonly List<string> heldNotices = new List<string>();

        // primo avvio (e menu "Il tuo PC"): cosa ha riconosciuto BlackBox e cosa conviene fare
        void ShowWelcome() { ShowMyPc(true); }

        void ShowMyPc(bool welcome)
        {
            ShowWindow();
            new StatusWindow(win, col, welcome, fix => OnFpsButton()).ShowDialog();
            if (welcome) Hardware.WelcomeShown();
        }
        SessionsWindow sessWin;
        void OpenSessions()
        {
            if (sessWin != null) { sessWin.Activate(); return; }
            sessWin = new SessionsWindow(win, col);
            sessWin.Closed += () => sessWin = null;
            sessWin.Show();
        }

        // riassunto dell'ultimo spegnimento anomalo negli appunti, senza la tabella dei campioni grezzi
        void CopyLastCrash()
        {
            var r = new DirectoryInfo(logDir).GetFiles("crash_*.txt").OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
            if (r == null) { MessageBox.Show("Nessun report di crash da copiare.", "BlackBox"); return; }
            try { Clipboard.SetText(Share.Crash(r.FullName)); tray.ShowBalloonTip(4000, "BlackBox", "Report del crash copiato: incollalo (Ctrl + V) dove vuoi.", WF.ToolTipIcon.Info); }
            catch (Exception ex) { MessageBox.Show("Non riesco a copiare: " + ex.Message, "BlackBox"); }
        }

        OptiWindow optiWin;
        void OpenGames()
        {
            if (optiWin != null) { optiWin.Activate(); return; }
            optiWin = new OptiWindow(win, col);
            optiWin.Closed += () => optiWin = null;
            optiWin.Show();
        }

        void ShowHealth() { ShowMyPc(false); }

        void SaveSnapshot()
        {
            win.UpdateLayout();
            var rtb = new RenderTargetBitmap((int)win.ActualWidth, (int)win.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(win);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(snapshot)) enc.Save(fs);
            Exit();
        }
    }
}
