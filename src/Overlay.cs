using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace BlackBox
{
    // ---------------------------------------------------------------- Overlay in gioco
    // Finestra trasparente sempre in primo piano che non riceve clic e non ruba il focus.
    // Leggera: testo aggiornato 2 volte al secondo, grafico del frame time (un solo Polyline) 10 volte al secondo,
    // niente effetti ne' ombre, pennelli congelati. Non compare nei giochi in schermo intero esclusivo.
    public class Overlay
    {
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int idx, int v);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
        const int GWL_EXSTYLE = -20;
        const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x8000000;
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
        const int WM_HOTKEY = 0x312, HotkeyId = 0xB10C;

        static readonly Brush Dim = F("#8A91A0"), Red = F("#FF5C6C"), Orange = F("#FFB547"), Green = F("#5EE08A");
        static Brush F(string hex) { return F((Color)ColorConverter.ConvertFromString(hex)); }
        static Brush F(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        // ------------------------------------------------ scelte di personalizzazione (nome mostrato, valore salvato)
        public static readonly string[][] Shapes = { new[] { "Blocco (più righe)", "blocco" }, new[] { "Barra in lungo (una riga)", "barra" } };
        public static readonly string[][] Positions = {
            new[] { "In alto a sinistra", "alto-sinistra" }, new[] { "In alto al centro", "alto-centro" }, new[] { "In alto a destra", "alto-destra" },
            new[] { "In basso a sinistra", "basso-sinistra" }, new[] { "In basso al centro", "basso-centro" }, new[] { "In basso a destra", "basso-destra" } };
        public static readonly string[][] Sizes = { new[] { "Piccolo", "11" }, new[] { "Medio", "13" }, new[] { "Grande", "16" }, new[] { "Molto grande", "20" } };
        public static readonly string[][] TextColors = {
            new[] { "Bianco", "#E6E8EE" }, new[] { "Giallo", "#FFE066" }, new[] { "Ciano", "#4FE3F7" }, new[] { "Verde", "#7CF29A" }, new[] { "Arancione", "#FFB547" }, new[] { "Rosa", "#FF8AD8" } };
        public static readonly string[][] GraphColors = {
            new[] { "Azzurro", "#4FC3F7" }, new[] { "Verde", "#5EE08A" }, new[] { "Giallo", "#FFE066" }, new[] { "Arancione", "#FFB547" }, new[] { "Rosa", "#FF8AD8" }, new[] { "Bianco", "#E6E8EE" } };
        public static readonly string[][] Backgrounds = { new[] { "Scuro", "E6" }, new[] { "Semitrasparente", "B4" }, new[] { "Molto trasparente", "66" }, new[] { "Nessuno (solo testo)", "00" } };
        public static readonly string[][] Parts = { new[] { "FPS e 1% low", "fps" }, new[] { "Grafico del frame time", "grafico" }, new[] { "Scheda video (GPU)", "gpu" }, new[] { "CPU, RAM e VRAM", "cpu" }, new[] { "Controllo stato e UV/OC", "stato" }, new[] { "Stato di OptiScaler", "opti" } };

        static string Pick(string key, string[][] list, string def) { string v = Settings.Get(key, def); return list.Any(x => x[1] == v) ? v : def; }
        public static string Shape { get { return Pick("overlay_forma", Shapes, "blocco"); } }
        public static string Corner { get { return Pick("overlay_posizione", Positions, "alto-sinistra"); } }
        public static string TextColor { get { return Pick("overlay_colore_testo", TextColors, "#E6E8EE"); } }
        public static string GraphColor { get { return Pick("overlay_colore_grafico", GraphColors, "#4FC3F7"); } }
        public static string Background { get { return Pick("overlay_sfondo", Backgrounds, "B4"); } }
        public static double FontSize { get { double v; return double.TryParse(Settings.Get("overlay_testo", "13"), out v) && v >= 8 && v <= 32 ? v : 13; } }
        public static bool Always { get { return Settings.Get("overlay_quando", "gioco") == "sempre"; } }
        public static bool Shows(string part) { return ("," + Settings.Get("overlay_mostra", "fps,grafico,gpu,cpu,stato,opti") + ",").Contains("," + part + ","); }

        readonly Collector col;
        readonly Window win;
        readonly Border box;
        readonly TextBlock t1, t2, t3, t4, t5, tFt;
        readonly Canvas cv;
        readonly StackPanel gRow;
        readonly Polyline graph;
        readonly DispatcherTimer textTimer, graphTimer;
        Brush Txt;
        HwndSource hotkeyWnd;
        DateTime previewUntil = DateTime.MinValue;
        int tick, healthTick;

        public bool Enabled { get; private set; }
        public string HotkeyText { get; private set; }
        public event Action Changed;

        public Overlay(Collector col)
        {
            this.col = col;
            Enabled = Settings.Get("overlay_attivo", "1") == "1";

            t1 = Tb(); t2 = Tb(); t3 = Tb(); t4 = Tb(); t5 = Tb(); tFt = Tb();
            graph = new Polyline { StrokeThickness = 1.2 };
            cv = new Canvas { ClipToBounds = true };
            cv.Children.Add(graph);
            gRow = new StackPanel { Orientation = Orientation.Horizontal };
            gRow.Children.Add(cv); tFt.VerticalAlignment = VerticalAlignment.Center; gRow.Children.Add(tFt);
            box = new Border { CornerRadius = new CornerRadius(6) };
            win = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true,
                ShowInTaskbar = false, ShowActivated = false, ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.WidthAndHeight,
                Focusable = false, IsHitTestVisible = false, Title = "BlackBox overlay", Content = box
            };
            TextOptions.SetTextFormattingMode(win, TextFormattingMode.Display);
            win.SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(win).Handle;
                SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
                // disegno con la CPU (pochi pixel): l'overlay non chiede lavoro alla scheda video del gioco
                try { var src = HwndSource.FromHwnd(h); if (src != null) src.CompositionTarget.RenderMode = RenderMode.SoftwareOnly; } catch { }
            };
            win.SizeChanged += (s, e) => Place();
            ApplyLook();

            textTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
            textTimer.Tick += (s, e) => UpdateText();
            graphTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
            graphTimer.Tick += (s, e) => UpdateGraph();
            // il controllo di quando mostrarlo resta sempre acceso (1 volta al secondo, costo trascurabile)
            var check = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            check.Tick += (s, e) => Refresh();
            check.Start();

            RegisterHotkey();
        }

        static TextBlock Tb() { return new TextBlock { FontFamily = new FontFamily("Segoe UI"), FontWeight = FontWeights.SemiBold }; }

        // applica forma, colori, dimensione e righe scelte (ricostruisce il contenuto)
        void ApplyLook()
        {
            double fs = FontSize;
            Txt = F(TextColor);
            graph.Stroke = F(GraphColor);
            foreach (var t in new[] { t1, t2, t3, t4, t5, tFt }) { t.FontSize = fs; t.Foreground = Txt; }
            bool bar = Shape == "barra";
            if (!bar) t1.FontSize = fs + 3;
            cv.Width = bar ? fs * 7 : fs * 12; cv.Height = bar ? fs * 1.4 : fs * 2.2;
            cv.Margin = new Thickness(0, 2, bar ? 4 : 8, 2);
            byte alpha = Convert.ToByte(Background, 16);
            box.Background = alpha == 0 ? Brushes.Transparent : F(Color.FromArgb(alpha, 0x0E, 0x10, 0x15));
            box.Padding = bar ? new Thickness(10, 3, 10, 3) : new Thickness(10, 6, 10, 6);

            var old = box.Child as Panel; if (old != null) old.Children.Clear();
            Panel p = bar ? (Panel)new StackPanel { Orientation = Orientation.Horizontal } : new StackPanel();
            var parts = new List<Tuple<string, FrameworkElement>> {
                Tuple.Create("fps", (FrameworkElement)t1), Tuple.Create("grafico", (FrameworkElement)gRow), Tuple.Create("gpu", (FrameworkElement)t2),
                Tuple.Create("cpu", (FrameworkElement)t3), Tuple.Create("stato", (FrameworkElement)t4), Tuple.Create("opti", (FrameworkElement)t5) };
            foreach (var x in parts)
            {
                x.Item2.Visibility = Shows(x.Item1) ? Visibility.Visible : Visibility.Collapsed;
                x.Item2.VerticalAlignment = VerticalAlignment.Center;
                x.Item2.Margin = bar ? new Thickness(0, 0, fs * 1.2, 0) : new Thickness(0);
                p.Children.Add(x.Item2);
            }
            box.Child = p;
            UpdateText();
            Place();
        }

        void Changed2(string key, string value)
        {
            Settings.Set(key, value);
            ApplyLook();
            Preview();
            if (Changed != null) Changed();
        }

        // mostra l'overlay per qualche secondo anche fuori dal gioco, per vedere le modifiche
        public void Preview() { previewUntil = DateTime.Now.AddSeconds(8); Refresh(); }

        public void SetEnabled(bool on)
        {
            Enabled = on;
            Settings.Set("overlay_attivo", on ? "1" : "0");
            Refresh();
            if (Changed != null) Changed();
        }

        public void SetAlways(bool always) { Settings.Set("overlay_quando", always ? "sempre" : "gioco"); Refresh(); if (Changed != null) Changed(); }
        public void SetShape(string v)
        {
            // la barra in lungo sta bene al centro: se era in un angolo la sposto in alto al centro
            if (v == "barra" && !Corner.EndsWith("centro")) Settings.Set("overlay_posizione", Corner.StartsWith("basso") ? "basso-centro" : "alto-centro");
            Changed2("overlay_forma", v);
        }
        public void SetCorner(string v) { Changed2("overlay_posizione", v); }
        public void SetFontSize(string v) { Changed2("overlay_testo", v); }
        public void SetTextColor(string v) { Changed2("overlay_colore_testo", v); }
        public void SetGraphColor(string v) { Changed2("overlay_colore_grafico", v); }
        public void SetBackground(string v) { Changed2("overlay_sfondo", v); }
        public void TogglePart(string part)
        {
            var on = Parts.Select(x => x[1]).Where(x => x == part ? !Shows(x) : Shows(x)).ToList();
            if (on.Count == 0) return;   // almeno una cosa da mostrare
            Changed2("overlay_mostra", string.Join(",", on));
        }
        // ------------------------------------------------ tasto rapido globale (predefinito Ctrl+Shift+O)
        void RegisterHotkey()
        {
            string txt = Settings.Get("overlay_tasto", "Ctrl+Shift+O");
            uint mods = 0x4000, vk = 0;   // MOD_NOREPEAT
            foreach (var part in txt.Split('+').Select(p => p.Trim().ToLowerInvariant()))
            {
                if (part == "ctrl") mods |= 2; else if (part == "shift") mods |= 4; else if (part == "alt") mods |= 1; else if (part == "win") mods |= 8;
                else if (part.Length == 1 && char.IsLetterOrDigit(part[0])) vk = char.ToUpperInvariant(part[0]);
                else if (part.Length >= 2 && part[0] == 'f') { int n; if (int.TryParse(part.Substring(1), out n) && n >= 1 && n <= 24) vk = (uint)(0x6F + n); }
            }
            if (vk == 0) { txt = "Ctrl+Shift+O"; mods = 0x4000 | 2 | 4; vk = 'O'; }
            HotkeyText = txt;
            var hp = new HwndSourceParameters("BlackBoxHotkey") { Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3) };   // HWND_MESSAGE
            hotkeyWnd = new HwndSource(hp);
            hotkeyWnd.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
            {
                if (msg == WM_HOTKEY && w.ToInt32() == HotkeyId) { SetEnabled(!Enabled); handled = true; }
                return IntPtr.Zero;
            });
            if (!RegisterHotKey(hotkeyWnd.Handle, HotkeyId, mods, vk))
            {
                col.AddEvent("WARN", "Tasto rapido dell'overlay " + txt + " già usato da un altro programma: cambialo in impostazioni.ini (overlay_tasto=)");
                HotkeyText = txt + " (occupato)";
            }
        }

        public void Dispose()
        {
            try { if (hotkeyWnd != null) { UnregisterHotKey(hotkeyWnd.Handle, HotkeyId); hotkeyWnd.Dispose(); } } catch { }
            try { win.Close(); } catch { }
        }

        // ------------------------------------------------ mostrare / nascondere
        void Refresh()
        {
            bool show = (Enabled && (Always || col.Games.InSession)) || DateTime.Now < previewUntil;
            if (show && !win.IsVisible)
            {
                UpdateText();
                win.Show(); Place();
                textTimer.Start(); graphTimer.Start();
            }
            else if (!show && win.IsVisible)
            {
                textTimer.Stop(); graphTimer.Stop();
                win.Hide();
            }
            if (show && ++tick % 3 == 0)
            {
                // alcuni giochi si mettono sopra: ogni 3 s l'overlay torna in cima, senza prendere il focus
                var h = new WindowInteropHelper(win).Handle;
                if (h != IntPtr.Zero) SetWindowPos(h, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }

        // sullo schermo principale (dove di solito c'e' il gioco), a 12 pixel dal bordo
        void Place()
        {
            if (!win.IsVisible) return;
            double W = SystemParameters.PrimaryScreenWidth, H = SystemParameters.PrimaryScreenHeight, m = 12;
            string c = Corner;
            win.Left = c.EndsWith("destra") ? W - win.ActualWidth - m : c.EndsWith("centro") ? (W - win.ActualWidth) / 2 : m;
            win.Top = c.StartsWith("basso") ? H - win.ActualHeight - m : m;
        }

        // ------------------------------------------------ contenuto
        static Run R(string text, Brush b) { return new Run(text) { Foreground = b }; }
        static string N(double v, string fmt = "0") { return double.IsNaN(v) ? "--" : v.ToString(fmt, System.Globalization.CultureInfo.CurrentCulture); }
        static void Set(TextBlock t, params Run[] runs) { t.Inlines.Clear(); foreach (var r in runs) t.Inlines.Add(r); }

        void UpdateText()
        {
            var s = col.GetLatest();
            if (s == null) return;
            Brush fpsB = double.IsNaN(s.Fps) ? Dim : s.Fps < 30 ? Red : s.Fps < 50 ? Orange : Green;
            var fpsRuns = new List<Run> { R("FPS ", Dim), R(N(s.Fps), fpsB) };
            if (s.FgFactor > 1 && !double.IsNaN(s.Fps)) { fpsRuns.Add(R("  FG " + s.FgFactor + "x → ", Dim)); fpsRuns.Add(R(N(s.FpsReal) + " reali", Txt)); }
            fpsRuns.Add(R("   1% low ", Dim));
            fpsRuns.Add(R(N(s.Fps1Low), double.IsNaN(s.Fps1Low) ? Dim : s.Fps1Low < 30 ? Red : s.Fps1Low < 50 ? Orange : Txt));
            Set(t1, fpsRuns.ToArray());
            Set(tFt, R(N(s.FrameTimeMs, "0.0") + " ms", Txt));
            Brush hotB = s.GpuTempHot >= Limits.HotBad ? Red : s.GpuTempHot >= Limits.HotWarn ? Orange : Txt;
            double everMax = col.EverMaxPower;
            Brush powB = !double.IsNaN(everMax) && everMax >= 100 && s.GpuPowerMax >= 0.9 * everMax ? Orange : Txt;
            Set(t2, R("GPU ", Dim), R(N(s.GpuTempEdge) + "°C", Txt), R("  hot ", Dim), R(N(s.GpuTempHot) + "°C", hotB),
                    R("  " + N(s.GpuPower) + " W", powB), R("  " + N(s.GpuClk) + " MHz", Txt), R("  " + N(s.GpuVddc) + " mV", Txt));
            Brush cpuB = s.CpuTctl >= Limits.CpuMax ? Red : s.CpuTctl >= Limits.CpuMax - 4 ? Orange : Txt;
            Set(t3, R("CPU ", Dim), R(N(s.CpuUtil) + "%", Txt), R("  " + (double.IsNaN(s.CpuTctl) ? "--" : N(s.CpuTctl) + "°C"), cpuB),
                    R("   RAM ", Dim), R(N(s.RamCommit) + "%", Txt), R("   VRAM ", Dim), R(N(s.VramMb / 1024, "0.0") + " GB", Txt));
            // controllo dello stato ogni 2 s: simbolo + riassunto, giudizio UV/OC ed efficienza
            if (Shows("stato") && healthTick++ % 4 == 0)
            {
                var items = Health.Check(col);
                int lv; string sum = Health.Summary(items, out lv);
                var runs = new List<Run> { R(lv >= 2 ? "✖ " : lv == 1 ? "⚠ " : "✔ ", lv >= 2 ? Red : lv == 1 ? Orange : Green), R(sum, lv >= 2 ? Red : lv == 1 ? Orange : Green) };
                var uv = items.FirstOrDefault(x => x.Short.StartsWith("UV/OC"));
                if (uv != null && lv == 0) runs.Add(R("   " + uv.Short, Txt));
                var eff = items.FirstOrDefault(x => x.Short.EndsWith("FPS/W"));
                if (eff != null) runs.Add(R("   " + eff.Short, Txt));
                Set(t4, runs.ToArray());
            }            if (string.IsNullOrEmpty(s.OptiScaler)) t5.Visibility = Visibility.Collapsed;
            else { t5.Visibility = Visibility.Visible; Set(t5, R("OptiScaler: ", Dim), R(s.OptiScaler, s.OptiScaler.StartsWith("attivo") ? Green : Txt)); }
        }

        // ultimi 120 fotogrammi; scala adattiva: il frame time medio sta a meta' altezza, i picchi (scatti) salgono
        void UpdateGraph()
        {
            var ft = Fps.RecentFrameTimes(120);
            double w = cv.Width, h = cv.Height;
            if (ft.Length < 2) { if (graph.Points.Count > 0) graph.Points = new PointCollection(); return; }
            double max = Math.Max(ft.Max() * 1.1, 2 * ft.Average());
            var pts = new PointCollection(ft.Length);
            int off = 120 - ft.Length;
            for (int i = 0; i < ft.Length; i++) pts.Add(new Point((i + off) * w / 119.0, h - 1 - ft[i] / max * (h - 2)));
            pts.Freeze();
            graph.Points = pts;
        }
    }
}
