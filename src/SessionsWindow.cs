using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.IO.Path;

namespace BlackBox
{
    // ---------------------------------------------------------------- Campioni di una partita (dai CSV gia' salvati)
    public class SessionPoint { public DateTime T; public double Fps, Real, Edge, Hot, Power, Cpu, Clk; }

    public static class SessionData
    {
        // legge dai file monitor_*.csv i secondi della partita con il gioco in primo piano (solo quando si apre la finestra)
        public static List<SessionPoint> Load(string dir, SessionRow s)
        {
            var res = new List<SessionPoint>();
            for (var day = s.Start.Date; day <= s.End.Date; day = day.AddDays(1))
            {
                string f = Path.Combine(dir, "monitor_" + day.ToString("yyyy-MM-dd") + ".csv");
                if (!File.Exists(f)) continue;
                try
                {
                    using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var sr = new StreamReader(fs))
                    {
                        string[] h = null; string line;
                        int iFg = -1, iFps = -1, iReal = -1, iEdge = -1, iHot = -1, iPow = -1, iCpu = -1, iClk = -1;
                        while ((line = sr.ReadLine()) != null)
                        {
                            if (line.StartsWith("timestamp"))
                            {
                                h = line.Split(';');
                                iFg = Array.IndexOf(h, "app_primo_piano"); iFps = Array.IndexOf(h, "fps"); iReal = Array.IndexOf(h, "fps_reali");
                                iEdge = Array.IndexOf(h, "gpu_temp_edge"); iHot = Array.IndexOf(h, "gpu_temp_hotspot"); iPow = Array.IndexOf(h, "gpu_power_w");
                                iCpu = Array.IndexOf(h, "cpu_tctl"); iClk = Array.IndexOf(h, "gpu_clk_mhz");
                                continue;
                            }
                            if (h == null || line.Length < 19) continue;
                            DateTime t;
                            if (!DateTime.TryParseExact(line.Substring(0, 19), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) continue;
                            if (t < s.Start) continue;
                            if (t > s.End) break;
                            var a = line.Split(';');
                            if (iFg < 0 || iFg >= a.Length || !string.Equals(a[iFg], s.Game, StringComparison.OrdinalIgnoreCase)) continue;
                            Func<int, double> n = i => { double v; return i >= 0 && i < a.Length && double.TryParse(a[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : double.NaN; };
                            res.Add(new SessionPoint { T = t, Fps = n(iFps), Real = n(iReal), Edge = n(iEdge), Hot = n(iHot), Power = n(iPow), Cpu = n(iCpu), Clk = n(iClk) });
                        }
                    }
                }
                catch { }
            }
            return res;
        }
    }

    // ---------------------------------------------------------------- Riassunti da copiare e incollare (forum, Claude, amici)
    public static class Share
    {
        static string N(double v, string f = "0") { return double.IsNaN(v) ? "n/d" : v.ToString(f, CultureInfo.CurrentCulture); }

        static string Pc()
        {
            return "PC: " + Hardware.Cpu + " · " + Hardware.Gpu + (double.IsNaN(Hardware.VramGb) ? "" : " " + Hardware.VramGb.ToString("0") + " GB") +
                   " · driver " + Hardware.GpuDriver + (Hardware.AmdSoftware != "" ? " (Adrenalin " + Hardware.AmdSoftware + ")" : "") +
                   " · " + (double.IsNaN(Hardware.RamGb) ? "" : Hardware.RamGb + " GB RAM · ") + Hardware.Windows;
        }

        public static string Session(SessionRow s, List<SessionPoint> pts)
        {
            var sb = new StringBuilder();
            sb.AppendLine("BlackBox — partita a " + s.Game + " del " + s.Start.ToString("dd/MM/yyyy HH:mm") + " (" + N(s.Num("durata_min")) + " min)");
            sb.AppendLine(Pc());
            sb.AppendLine("Configurazione: " + (s.Config == "" ? "(non indicata)" : s.Config) + (s.Opti != "" ? " · OptiScaler: " + s.Opti : ""));
            sb.AppendLine("FPS medio " + N(s.Num("fps_medio")) + " · 1% low " + N(s.Num("fps_1low")) +
                          (s.Txt("frame_gen") == "si" ? " · generazione di fotogrammi attiva, FPS reali " + N(s.Num("fps_reali")) : ""));
            sb.AppendLine("GPU: " + N(s.Num("potenza_media_w")) + " W medi (picco " + N(s.Num("potenza_picco_w")) + " W) · clock medio " + N(s.Num("clock_medio_mhz")) +
                          " MHz · " + N(s.Num("mv_medio")) + " mV · edge max " + N(s.Num("edge_max")) + " °C · hotspot max " + N(s.Num("hotspot_max")) +
                          " °C (differenza max " + N(s.Num("delta_hot_edge_max")) + " °C) · VRAM max " + N(s.Num("vram_max_gb"), "0.0") + " GB");
            double cpu = pts == null ? double.NaN : pts.Select(p => p.Cpu).Where(x => !double.IsNaN(x)).DefaultIfEmpty(double.NaN).Max();
            if (!double.IsNaN(cpu)) sb.AppendLine("CPU: temperatura max " + N(cpu) + " °C");
            sb.AppendLine("Efficienza: " + N(s.Num("fps_per_watt"), "0.00") + " FPS reali per watt");
            sb.AppendLine("Stabilità: throttling " + N(s.Num("throttling_s")) + " s · crash del driver " + s.Tdr + " · crash del gioco " + s.Crash);
            return Anon(sb.ToString());
        }

        public static string Crash(string reportPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Pc());
            foreach (var l in File.ReadAllLines(reportPath))
            {
                if (l.StartsWith("--- Ultimi") && l.Contains("campioni")) break;   // la tabella dei campioni grezzi non serve per condividere
                sb.AppendLine(l);
            }
            return Anon(sb.ToString().TrimEnd());
        }

        // toglie cartella utente, nome utente e nome del PC dai testi da condividere
        public static string Anon(string t)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (home.Length > 3) t = System.Text.RegularExpressions.Regex.Replace(t, System.Text.RegularExpressions.Regex.Escape(home), "%USERPROFILE%", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (var w in new[] { Environment.UserName, Environment.MachineName })
                if (w.Length >= 3) t = System.Text.RegularExpressions.Regex.Replace(t, @"\b" + System.Text.RegularExpressions.Regex.Escape(w) + @"\b", "(nascosto)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return t;
        }
    }

    // ---------------------------------------------------------------- Finestra "Partite": grafici, confronto, riassunti
    // Lavora solo sui file gia' salvati e solo quando e' aperta: nessun effetto sul gioco.
    public class SessionsWindow
    {
        static readonly Brush Muted = F("#8A91A0"), Text = F("#E6E8EE"), Red = F("#FF5C6C"), Orange = F("#FFB547"), Green = F("#5EE08A"),
                              Blue = F("#4FC3F7"), Purple = F("#B388FF"), Back = F("#0E1015"), Sel = F("#22293A"), GridLine = F("#232733"), Pink = F("#F48FB1");
        static Brush F(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }

        readonly Window win;
        readonly Collector col;
        readonly string dir;
        List<SessionRow> sessions = new List<SessionRow>();
        SessionRow selected;
        List<SessionPoint> points;
        readonly StackPanel list, charts;
        readonly TextBlock summary, status;
        readonly Grid chartsPanel, comparePanel;
        readonly ComboBox boxA, boxB;
        readonly StackPanel compareBody;
        readonly Button tabCharts, tabCompare;

        public event Action Closed;

        public SessionsWindow(Window owner, Collector col)
        {
            this.col = col; dir = col.LogDir;
            win = new Window { Title = "BlackBox — Partite", Width = 1180, Height = 780, MinWidth = 900, MinHeight = 560, Background = Back, Foreground = Text,
                               FontFamily = new FontFamily("Segoe UI"), Owner = owner, Icon = owner.Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            win.Resources.MergedDictionaries.Add(owner.Resources);
            var root = new DockPanel { Margin = new Thickness(14) };

            var head = new StackPanel { Margin = new Thickness(6, 0, 6, 6) };
            head.Children.Add(new TextBlock { Text = "Partite", FontSize = 22, FontWeight = FontWeights.SemiBold });
            head.Children.Add(new TextBlock { Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8),
                Text = "Le tue partite registrate: grafici di FPS, temperature, watt e clock; confronto di due partite (per capire quale undervolt va meglio); riassunti da copiare. Tutto calcolato dai dati già salvati, solo quando questa finestra è aperta: nessun effetto sul gioco." });
            var tabs = new WrapPanel { Margin = new Thickness(-8, 0, 0, 0) };
            tabCharts = new Button { Content = "Grafici della partita" }; tabCompare = new Button { Content = "Confronta due partite (undervolt)" };
            tabCharts.Click += (s, e) => ShowTab(true); tabCompare.Click += (s, e) => ShowTab(false);
            tabs.Children.Add(tabCharts); tabs.Children.Add(tabCompare);
            head.Children.Add(tabs);
            status = new TextBlock { Foreground = Blue, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
            head.Children.Add(status);
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);

            var body = new Grid();
            // ---- scheda grafici: elenco a sinistra, grafici a destra
            chartsPanel = new Grid();
            chartsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
            chartsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            list = new StackPanel();
            chartsPanel.Children.Add(new Border { Style = (Style)owner.Resources["Card"], Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list } });
            var right = new StackPanel();
            summary = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5 };
            var btns = new WrapPanel { Margin = new Thickness(-8, 8, 0, 4) };
            var bCopy = new Button { Content = "Copia riassunto (da incollare)" }; bCopy.Click += (s, e) => CopySession();
            var bReport = new Button { Content = "Apri report completo" }; bReport.Click += (s, e) => OpenReport();
            btns.Children.Add(bCopy); btns.Children.Add(bReport);
            charts = new StackPanel();
            right.Children.Add(summary); right.Children.Add(btns); right.Children.Add(charts);
            var rcard = new Border { Style = (Style)owner.Resources["Card"], Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = right } };
            Grid.SetColumn(rcard, 1);
            chartsPanel.Children.Add(rcard);
            body.Children.Add(chartsPanel);

            // ---- scheda confronto
            comparePanel = new Grid { Visibility = Visibility.Collapsed };
            var cp = new StackPanel();
            cp.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Muted, FontSize = 12.5, Margin = new Thickness(0, 0, 0, 10),
                Text = "Come confrontare due undervolt (o due impostazioni qualsiasi):\n" +
                       "1. Nella finestra principale scrivi la configurazione nel riquadro del test A/B (es. «UV 1080 mV») e premi Salva.\n" +
                       "2. Gioca almeno 10 minuti, sempre nella stessa zona o scena del gioco.\n" +
                       "3. Cambia l'impostazione in Adrenalin, scrivi la nuova configurazione (es. «UV 1050 mV») e salva.\n" +
                       "4. Rigioca la stessa scena per lo stesso tempo.\n" +
                       "5. Qui scegli le due partite: BlackBox ti dice quale va meglio (FPS per watt, temperature, stabilità)." });
            var pick = new WrapPanel();
            pick.Children.Add(new TextBlock { Text = "Partita A:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            boxA = new ComboBox { Width = 420 }; pick.Children.Add(boxA);
            pick.Children.Add(new TextBlock { Text = "   Partita B:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            boxB = new ComboBox { Width = 420 }; pick.Children.Add(boxB);
            boxA.SelectionChanged += (s, e) => Compare(); boxB.SelectionChanged += (s, e) => Compare();
            cp.Children.Add(pick);
            compareBody = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            cp.Children.Add(compareBody);
            comparePanel.Children.Add(new Border { Style = (Style)owner.Resources["Card"], Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = cp } });
            body.Children.Add(comparePanel);
            root.Children.Add(body);
            win.Content = root;
            win.Closed += (s, e) => { if (Closed != null) Closed(); };
            Load();
        }

        public void Show() { win.Show(); win.Activate(); }
        public void Activate() { win.Activate(); }

        void ShowTab(bool chartsTab)
        {
            chartsPanel.Visibility = chartsTab ? Visibility.Visible : Visibility.Collapsed;
            comparePanel.Visibility = chartsTab ? Visibility.Collapsed : Visibility.Visible;
            tabCharts.FontWeight = chartsTab ? FontWeights.Bold : FontWeights.Normal;
            tabCompare.FontWeight = chartsTab ? FontWeights.Normal : FontWeights.Bold;
        }

        static string Label(SessionRow s)
        {
            string fps = double.IsNaN(s.Fps) ? "" : " · " + s.Fps.ToString("0") + " FPS";
            return s.Start.ToString("dd/MM HH:mm") + " · " + s.Game + " · " + s.Num("durata_min").ToString("0") + " min" + fps + (s.Config != "" ? " · [" + s.Config + "]" : "");
        }

        void Load()
        {
            sessions = SessionLog.Read(dir).Where(s => (s.End - s.Start).TotalMinutes >= 1).OrderByDescending(s => s.Start).ToList();
            list.Children.Clear();
            if (sessions.Count == 0) { list.Children.Add(new TextBlock { Text = "Nessuna partita ancora: giocando, le partite compaiono qui da sole.", Foreground = Muted, TextWrapping = TextWrapping.Wrap }); }
            foreach (var s in sessions)
            {
                var ss = s;
                var sp = new StackPanel();
                sp.Children.Add(new TextBlock { Text = s.Game, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                sp.Children.Add(new TextBlock { Text = s.Start.ToString("dd/MM/yyyy HH:mm") + " · " + s.Num("durata_min").ToString("0") + " min" + (double.IsNaN(s.Fps) ? "" : " · " + s.Fps.ToString("0") + " FPS") +
                                                       (s.Tdr + s.Crash > 0 ? " · crash" : ""), FontSize = 12, Foreground = s.Tdr + s.Crash > 0 ? Orange : Muted });
                var b = new Border { Child = sp, Padding = new Thickness(8, 5, 8, 5), CornerRadius = new CornerRadius(6), Cursor = System.Windows.Input.Cursors.Hand,
                                     Background = s == selected ? Sel : Brushes.Transparent, Margin = new Thickness(0, 1, 0, 1), Tag = s };
                b.MouseLeftButtonUp += (o, e) => Select(ss);
                list.Children.Add(b);
            }
            var labels = sessions.Select(Label).ToList();
            boxA.ItemsSource = labels; boxB.ItemsSource = labels;
            // proposta: le ultime due partite allo stesso gioco con configurazioni diverse, altrimenti le ultime due
            var pair = sessions.SelectMany((a, i) => sessions.Skip(i + 1).Where(b => b.Game == a.Game && b.Config != a.Config).Select(b => new[] { b, a })).FirstOrDefault()
                       ?? (sessions.Count >= 2 ? new[] { sessions[1], sessions[0] } : null);
            if (pair != null) { boxA.SelectedIndex = sessions.IndexOf(pair[0]); boxB.SelectedIndex = sessions.IndexOf(pair[1]); }
            if (sessions.Count > 0) Select(sessions[0]);
            else summary.Text = "";
        }

        void Select(SessionRow s)
        {
            selected = s;
            foreach (Border b in list.Children.OfType<Border>()) b.Background = b.Tag == s ? Sel : Brushes.Transparent;
            summary.Text = Share.Session(s, null);
            charts.Children.Clear();
            charts.Children.Add(new TextBlock { Text = "Carico i dati della partita...", Foreground = Muted });
            points = null;
            new Thread(() =>
            {
                var pts = SessionData.Load(dir, s);
                win.Dispatcher.BeginInvoke(new Action(() => { if (selected == s) { points = pts; summary.Text = Share.Session(s, pts); DrawCharts(pts); } }));
            }) { IsBackground = true }.Start();
        }

        // ------------------------------------------------ grafici (un Polyline per serie, al massimo 600 punti)
        void DrawCharts(List<SessionPoint> pts)
        {
            charts.Children.Clear();
            if (pts.Count < 5)
            {
                charts.Children.Add(new TextBlock { Foreground = Muted, TextWrapping = TextWrapping.Wrap,
                    Text = "I dati secondo per secondo di questa partita non ci sono più: BlackBox tiene i dati dettagliati per 14 giorni (o la cartella dei log è stata svuotata). Il riassunto qui sopra resta." });
                return;
            }
            // la linea degli FPS reali serve solo con la generazione di fotogrammi (altrimenti coincide con gli FPS)
            if (selected != null && selected.Txt("frame_gen") == "si")
                Chart("FPS", " FPS", pts, new[] { new KeyValuePair<string, Func<SessionPoint, double>>("FPS mostrati", p => p.Fps), new KeyValuePair<string, Func<SessionPoint, double>>("FPS reali", p => p.Real) }, new[] { Green, Blue }, true);
            else
                Chart("FPS", " FPS", pts, new[] { new KeyValuePair<string, Func<SessionPoint, double>>("FPS", p => p.Fps) }, new[] { Blue }, true);
            Chart("Temperature", " °C", pts, new[] { new KeyValuePair<string, Func<SessionPoint, double>>("hotspot GPU", p => p.Hot), new KeyValuePair<string, Func<SessionPoint, double>>("edge GPU", p => p.Edge),
                                                      new KeyValuePair<string, Func<SessionPoint, double>>("CPU", p => p.Cpu) }, new[] { Red, Orange, Purple }, false);
            Chart("Potenza della scheda video", " W", pts, new[] { new KeyValuePair<string, Func<SessionPoint, double>>("W", p => p.Power) }, new[] { Orange }, true);
            Chart("Clock della scheda video", " MHz", pts, new[] { new KeyValuePair<string, Func<SessionPoint, double>>("MHz", p => p.Clk) }, new[] { Pink }, false);
        }

        void Chart(string title, string unit, List<SessionPoint> pts, KeyValuePair<string, Func<SessionPoint, double>>[] series, Brush[] colors, bool fromZero)
        {
            var all = series.SelectMany(sv => pts.Select(sv.Value)).Where(v => !double.IsNaN(v) && v > 0).ToList();
            if (all.Count == 0) return;
            double max = all.Max(), min = fromZero ? 0 : all.Min();
            if (max - min < 1) max = min + 1;
            double pad = (max - min) * 0.08; max += pad; if (!fromZero) min = Math.Max(0, min - pad);
            const double W = 760, H = 120;
            var head = new DockPanel { Margin = new Thickness(0, 14, 0, 4) };
            var legend = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < series.Length; i++)
            {
                var v = pts.Select(series[i].Value).Where(x => !double.IsNaN(x) && x > 0).ToList();
                if (v.Count == 0) continue;
                legend.Children.Add(new Rectangle { Width = 10, Height = 3, Fill = colors[i], Margin = new Thickness(10, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
                legend.Children.Add(new TextBlock { Text = series[i].Key + " media " + v.Average().ToString("0") + ", max " + v.Max().ToString("0"), Foreground = Muted, FontSize = 12 });
            }
            DockPanel.SetDock(legend, Dock.Right);
            head.Children.Add(legend);
            head.Children.Add(new TextBlock { Text = title.ToUpper(), FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Muted });
            charts.Children.Add(head);
            var cv = new Canvas { Width = W, Height = H, Background = F("#12151B"), HorizontalAlignment = HorizontalAlignment.Left, ClipToBounds = true };
            for (int k = 1; k < 4; k++) cv.Children.Add(new Line { X1 = 0, X2 = W, Y1 = H * k / 4, Y2 = H * k / 4, Stroke = GridLine, StrokeThickness = 1 });
            int buckets = Math.Min(600, pts.Count);
            for (int i = 0; i < series.Length; i++)
            {
                var poly = new Polyline { Stroke = colors[i], StrokeThickness = 1.4 };
                var sel = series[i].Value;
                for (int b = 0; b < buckets; b++)
                {
                    int a0 = b * pts.Count / buckets, a1 = Math.Max(a0 + 1, (b + 1) * pts.Count / buckets);
                    var v = pts.Skip(a0).Take(a1 - a0).Select(sel).Where(x => !double.IsNaN(x) && x > 0).ToList();
                    if (v.Count == 0) continue;
                    double y = H - (v.Average() - min) / (max - min) * H;
                    poly.Points.Add(new Point(b * W / Math.Max(1, buckets - 1), Math.Max(0, Math.Min(H, y))));
                }
                if (poly.Points.Count > 1) cv.Children.Add(poly);
            }
            var axis = new DockPanel { Width = W, HorizontalAlignment = HorizontalAlignment.Left };
            var right = new TextBlock { Text = pts[pts.Count - 1].T.ToString("HH:mm"), Foreground = Muted, FontSize = 11 };
            DockPanel.SetDock(right, Dock.Right);
            axis.Children.Add(right);
            axis.Children.Add(new TextBlock { Text = pts[0].T.ToString("HH:mm") + "   (scala " + min.ToString("0") + "–" + max.ToString("0") + unit + ")", Foreground = Muted, FontSize = 11 });
            charts.Children.Add(cv);
            charts.Children.Add(axis);
        }

        // ------------------------------------------------ confronto di due partite
        void Compare()
        {
            compareBody.Children.Clear();
            if (boxA.SelectedIndex < 0 || boxB.SelectedIndex < 0) return;
            var a = sessions[boxA.SelectedIndex]; var b = sessions[boxB.SelectedIndex];
            if (a == b) { compareBody.Children.Add(new TextBlock { Text = "Scegli due partite diverse.", Foreground = Orange }); return; }
            var grid = new Grid();
            for (int c = 0; c < 4; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = c == 0 ? new GridLength(230) : new GridLength(170) });
            int row = 0;
            Action<string, string, string, string, Brush> addRow = (m, va, vb, d, db) =>
            {
                grid.RowDefinitions.Add(new RowDefinition());
                var cells = new[] { new TextBlock { Text = m, Foreground = Muted }, new TextBlock { Text = va }, new TextBlock { Text = vb }, new TextBlock { Text = d, Foreground = db ?? Text } };
                for (int c = 0; c < 4; c++) { Grid.SetRow(cells[c], row); Grid.SetColumn(cells[c], c); cells[c].Margin = new Thickness(0, 2, 8, 2); if (row == 0) cells[c].FontWeight = FontWeights.SemiBold; grid.Children.Add(cells[c]); }
                row++;
            };
            addRow("", "A: " + a.Start.ToString("dd/MM HH:mm"), "B: " + b.Start.ToString("dd/MM HH:mm"), "B rispetto ad A", null);
            addRow("Gioco", a.Game, b.Game, a.Game == b.Game ? "stesso gioco" : "giochi diversi!", a.Game == b.Game ? Green : Orange);
            addRow("Configurazione", a.Config == "" ? "(non indicata)" : a.Config, b.Config == "" ? "(non indicata)" : b.Config, "", null);
            // higherBetter: true = piu' alto e' meglio
            Action<string, string, string, bool, int> metric = (label, key, unit, higherBetter, dec) =>
            {
                double va = a.Num(key), vb = b.Num(key);
                string fa = double.IsNaN(va) ? "n/d" : va.ToString("N" + dec) + unit, fb = double.IsNaN(vb) ? "n/d" : vb.ToString("N" + dec) + unit;
                string d = ""; Brush db = null;
                if (!double.IsNaN(va) && !double.IsNaN(vb) && va != 0)
                {
                    double pct = (vb - va) / Math.Abs(va) * 100;
                    d = (pct >= 0 ? "+" : "") + pct.ToString("0.0") + "%";
                    if (Math.Abs(pct) >= 2) db = (pct > 0) == higherBetter ? Green : Red;
                }
                addRow(label, fa, fb, d, db);
            };
            addRow("Durata", a.Num("durata_min").ToString("0") + " min", b.Num("durata_min").ToString("0") + " min", "", null);
            metric("FPS medio", "fps_medio", "", true, 0);
            metric("1% low", "fps_1low", "", true, 0);
            metric("FPS reali", "fps_reali", "", true, 0);
            metric("Potenza media GPU", "potenza_media_w", " W", false, 0);
            metric("Efficienza (FPS reali/W)", "fps_per_watt", "", true, 3);
            metric("Clock medio GPU", "clock_medio_mhz", " MHz", true, 0);
            metric("Tensione media GPU", "mv_medio", " mV", false, 0);
            metric("Hotspot massimo", "hotspot_max", " °C", false, 0);
            metric("Edge massimo", "edge_max", " °C", false, 0);
            metric("Throttling", "throttling_s", " s", false, 0);
            addRow("Crash (driver + gioco)", (a.Tdr + a.Crash).ToString(), (b.Tdr + b.Crash).ToString(), "", a.Tdr + a.Crash != b.Tdr + b.Crash ? Orange : null);
            compareBody.Children.Add(grid);

            var verdict = Verdict(a, b);
            compareBody.Children.Add(new TextBlock { Text = verdict, TextWrapping = TextWrapping.Wrap, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 0) });
            var bc = new Button { Content = "Copia confronto (da incollare)", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
            bc.Click += (s, e) => { SetClip(CompareText(a, b, verdict)); };
            compareBody.Children.Add(bc);
        }

        // giudizio: prima la stabilita', poi l'efficienza, con gli avvertimenti sull'affidabilita' del confronto
        static string Verdict(SessionRow a, SessionRow b)
        {
            var warn = new List<string>();
            if (a.Game != b.Game) warn.Add("sono giochi diversi: il confronto vale poco");
            if (a.Num("durata_min") < 5 || b.Num("durata_min") < 5) warn.Add("una partita è più corta di 5 minuti");
            if (a.Config == b.Config && a.Config != "") warn.Add("la configurazione indicata è la stessa");
            if ((a.Txt("frame_gen") == "si") != (b.Txt("frame_gen") == "si")) warn.Add("la generazione di fotogrammi è attiva solo in una delle due");
            string tail = warn.Count > 0 ? "\nAttenzione: " + string.Join("; ", warn) + "." : "";
            bool unstA = a.Tdr + a.Crash > 0, unstB = b.Tdr + b.Crash > 0;
            if (unstA && !unstB) return "Meglio B: la partita A ha avuto crash (impostazione instabile)." + tail;
            if (unstB && !unstA) return "Meglio A: la partita B ha avuto crash (impostazione instabile): torna a quella di A o rendi B più prudente." + tail;
            if (unstA && unstB) return "Entrambe hanno avuto crash: tutte e due le impostazioni sono instabili." + tail;
            double ea = a.Num("fps_per_watt"), eb = b.Num("fps_per_watt");
            if (double.IsNaN(ea) || double.IsNaN(eb)) return "Mancano gli FPS in una delle due partite: confronto dell'efficienza non possibile." + tail;
            if (a.Game != b.Game) return "Nessun vincitore: per giudicare un undervolt servono due partite allo stesso gioco (meglio la stessa scena). Entrambe stabili." + tail;
            if (Math.Abs(eb - ea) / Math.Min(ea, eb) * 100 < 3) return "Praticamente uguali (differenza di efficienza sotto il 3%: è normale variazione). Entrambe stabili." + tail;
            bool bWins = eb > ea;
            SessionRow w = bWins ? b : a, l = bWins ? a : b;
            string wn = bWins ? "B" : "A", ln = bWins ? "A" : "B";
            double gain = (w.Num("fps_per_watt") - l.Num("fps_per_watt")) / l.Num("fps_per_watt") * 100;
            double hw = w.Num("hotspot_max"), hl = l.Num("hotspot_max");
            string temp = !double.IsNaN(hw) && !double.IsNaN(hl) && Math.Abs(hw - hl) >= 2 ? (hw < hl ? " e scalda " + (hl - hw).ToString("0") + " °C in meno" : ", però scalda " + (hw - hl).ToString("0") + " °C in più") : "";
            double fw = w.Num("fps_medio"), fl = l.Num("fps_medio");
            string fps = !double.IsNaN(fw) && !double.IsNaN(fl) && fl > 0 && (fl - fw) / fl > 0.05 ? " Nota: " + wn + " fa meno FPS (" + fw.ToString("0") + " contro " + fl.ToString("0") + "): se ti servono gli FPS, scegli " + ln + "." : "";
            return "Meglio " + wn + ": " + gain.ToString("0") + "% di FPS in più per ogni watt rispetto a " + ln + temp + ". Entrambe stabili." + fps + tail;
        }

        string CompareText(SessionRow a, SessionRow b, string verdict)
        {
            Func<SessionRow, string> one = s => s.Start.ToString("dd/MM HH:mm") + " · " + s.Game + " · " + (s.Config == "" ? "(config non indicata)" : s.Config) + " · " +
                s.Num("fps_medio").ToString("0") + " FPS (1% low " + s.Num("fps_1low").ToString("0") + ") · " + s.Num("potenza_media_w").ToString("0") + " W · " +
                s.Num("fps_per_watt").ToString("0.000") + " FPS/W · hotspot max " + s.Num("hotspot_max").ToString("0") + " °C · " + s.Num("mv_medio").ToString("0") + " mV · crash " + (s.Tdr + s.Crash);
            return Share.Anon("BlackBox — confronto di due partite\nA: " + one(a) + "\nB: " + one(b) + "\n" + verdict);
        }

        void CopySession()
        {
            if (selected == null) return;
            SetClip(Share.Session(selected, points));
        }

        void SetClip(string text)
        {
            try { Clipboard.SetText(text); status.Foreground = Green; status.Text = "Copiato: ora puoi incollarlo (Ctrl + V) dove vuoi, per esempio in un forum o a Claude."; }
            catch (Exception ex) { status.Foreground = Orange; status.Text = "Non riesco a copiare: " + ex.Message; }
        }

        void OpenReport()
        {
            if (selected == null || selected.Report == "") { status.Text = "Questa partita non ha un report."; return; }
            string p = Path.Combine(dir, selected.Report);
            if (File.Exists(p)) try { Process.Start("notepad.exe", "\"" + p + "\""); } catch { }
            else status.Text = "Il report di questa partita non c'è più.";
        }
    }
}
