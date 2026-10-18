// Installer di BlackBox: installa per l'utente corrente, SENZA permessi di amministratore.
//   - copia BlackBox.exe (contenuto in questo file) in %LocalAppData%\Programs\BlackBox
//   - collegamento nel menu Start (e, se richiesto, sul desktop)
//   - avvio automatico con Windows (opzionale) e voce in Impostazioni -> App per disinstallare
//   - se BlackBox e' gia' installato lo aggiorna; i dati in Documenti\BlackBox non vengono toccati
// Disinstallazione: "Disinstalla.exe /uninstall" nella cartella del programma.
// Senza finestre: "BlackBox-Setup.exe /silent [/noauto]" e "Disinstalla.exe /uninstall /quiet".
// Nessuna connessione di rete.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

// (titolo e versione vengono aggiunti da build.ps1, presi da src\App.cs)

namespace BlackBoxSetup
{
    public static class Program
    {
        const string AppName = "BlackBox";
        const string UninstKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\BlackBox";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Repo = "https://github.com/Pronerious/blackbox-crash-recorder";

        static string InstallDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\BlackBox"); } }
        static string ExeTarget { get { return Path.Combine(InstallDir, "BlackBox.exe"); } }
        static string StartMenuLnk { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "BlackBox.lnk"); } }
        static string DesktopLnk { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "BlackBox.lnk"); } }
        static string Version { get { var v = Assembly.GetExecutingAssembly().GetName().Version; return v.Major + "." + v.Minor + "." + v.Build; } }

        static readonly Brush Back = B("#0E1015"), Card = B("#161920"), Txt = B("#E6E8EE"), Muted = B("#8A91A0"), Green = B("#5EE08A"), Red = B("#FF5C6C"), Btn = B("#1D212B"), Accent = B("#2E6B47");
        static SolidColorBrush B(string h) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(h)); b.Freeze(); return b; }

        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                quiet = args.Contains("/quiet") || args.Contains("/silent");
                if (args.Length >= 1 && args[0] == "/uninstall") return StartUninstall();
                if (args.Length >= 2 && args[0] == "/uninstall-now") return Uninstall(args[1]);
                if (args.Contains("/silent"))
                {
                    // senza finestre (aggiornamenti automatici e prove): menu Start si', desktop solo se c'era, avvio automatico se non /noauto
                    Install(!args.Contains("/noauto"), true, File.Exists(DesktopLnk), s => { });
                    return 0;
                }
                return InstallUi();
            }
            catch (Exception ex)
            {
                if (!quiet) MessageBox.Show("Errore: " + ex.Message, "BlackBox", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }
        static bool quiet;

        // ================================================ installazione
        static string InstalledVersion()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(UninstKey)) return k == null ? null : k.GetValue("DisplayVersion") as string; }
            catch { return null; }
        }

        static int InstallUi()
        {
            string old = InstalledVersion();
            bool update = old != null && File.Exists(ExeTarget);
            var app = new Application();
            var win = new Window { Title = "BlackBox " + Version + " — installazione", Width = 560, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                                   Background = Back, Foreground = Txt, FontFamily = new FontFamily("Segoe UI"), WindowStartupLocation = WindowStartupLocation.CenterScreen };
            var sp = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
            sp.Children.Add(new TextBlock { Text = "BlackBox " + Version, FontSize = 24, FontWeight = FontWeights.SemiBold });
            sp.Children.Add(new TextBlock { Text = "la scatola nera del tuo PC, fatta per giocare", Foreground = Muted, Margin = new Thickness(0, 0, 0, 14) });
            sp.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
                Text = update ? "BlackBox " + old + " è già installato: prima verrà disinstallato del tutto, poi verrà installata la versione " + Version + " da zero. I tuoi dati (report, sessioni, impostazioni) restano dove sono."
                              : "BlackBox verrà installato solo per il tuo utente. Non servono permessi di amministratore e non si collega a internet." });
            sp.Children.Add(new TextBlock { Text = "Cartella:  " + InstallDir, Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
            Func<string, bool, CheckBox> cb = (t, on) => { var c = new CheckBox { Content = t, IsChecked = on, Foreground = Txt, Margin = new Thickness(0, 3, 0, 3) }; sp.Children.Add(c); return c; };
            bool autoNow = AutoStartPath() != null;
            var cAuto = cb("Avvia BlackBox con Windows (consigliato: così non perde nessuno spegnimento)", !update || autoNow);
            var cMenu = cb("Collegamento nel menu Start", true);
            var cDesk = cb("Collegamento sul desktop", update && File.Exists(DesktopLnk));
            var cRun = cb("Apri BlackBox alla fine", true);
            var status = new TextBlock { Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
            sp.Children.Add(status);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var bGo = new Button { Content = update ? "Aggiorna" : "Installa", Padding = new Thickness(20, 7, 20, 7), Background = Accent, Foreground = Txt, BorderBrush = Accent, MinWidth = 110 };
            var bNo = new Button { Content = "Annulla", Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(8, 0, 0, 0), Background = Btn, Foreground = Txt, BorderBrush = B("#2C3240") };
            buttons.Children.Add(bGo); buttons.Children.Add(bNo);
            sp.Children.Add(buttons);
            win.Content = sp;
            bool done = false;
            bNo.Click += (s, e) => win.Close();
            bGo.Click += (s, e) =>
            {
                if (done) { win.Close(); return; }
                bGo.IsEnabled = bNo.IsEnabled = false;
                bool auto = cAuto.IsChecked == true, menu = cMenu.IsChecked == true, desk = cDesk.IsChecked == true, run = cRun.IsChecked == true;
                status.Foreground = Muted; status.Text = "Un momento...";
                new Thread(() =>
                {
                    string err = null;
                    try { Install(auto, menu, desk, s2 => win.Dispatcher.BeginInvoke(new Action(() => status.Text = s2))); }
                    catch (Exception ex) { err = ex.Message; }
                    win.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (err != null) { status.Foreground = Red; status.Text = "Installazione non riuscita: " + err; bGo.IsEnabled = bNo.IsEnabled = true; return; }
                        status.Foreground = Green;
                        status.Text = (update ? "Aggiornato" : "Installato") + " BlackBox " + Version + ". Lo trovi nel menu Start" + (auto ? " e parte da solo con Windows" : "") + ".\nPer disinstallarlo: Impostazioni → App → BlackBox.";
                        done = true; bGo.Content = "Fine"; bGo.IsEnabled = true;
                        if (run) try { Process.Start(ExeTarget); } catch { }
                    }));
                }) { IsBackground = true }.Start();
            };
            app.Run(win);
            return 0;
        }

        static void Install(bool auto, bool menu, bool desk, Action<string> say)
        {
            if (InstalledVersion() != null || File.Exists(ExeTarget) || AutoStartPath() != null)
            {
                // versione precedente (o copia portatile con avvio automatico): prima la disinstallo del tutto, poi installo da zero
                say("Disinstallo la versione precedente...");
                RemoveInstalled(InstallDir, true);
            }
            else { say("Chiudo BlackBox se è aperto..."); CloseRunning(); }
            say("Copio i file...");
            Directory.CreateDirectory(InstallDir);
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("BlackBox.exe"))
            using (var f = File.Create(ExeTarget + ".new")) s.CopyTo(f);
            if (File.Exists(ExeTarget)) File.Delete(ExeTarget);
            File.Move(ExeTarget + ".new", ExeTarget);
            string self = Assembly.GetExecutingAssembly().Location, uninst = Path.Combine(InstallDir, "Disinstalla.exe");
            if (!string.Equals(self, uninst, StringComparison.OrdinalIgnoreCase)) File.Copy(self, uninst, true);

            say("Collegamenti e registrazione in Windows...");
            if (menu) Shortcut(StartMenuLnk, ExeTarget, "BlackBox - la scatola nera del tuo PC"); else TryDelete(StartMenuLnk);
            if (desk) Shortcut(DesktopLnk, ExeTarget, "BlackBox - la scatola nera del tuo PC"); else TryDelete(DesktopLnk);
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (auto) k.SetValue(AppName, "\"" + ExeTarget + "\" --tray");
                else if (k.GetValue(AppName) != null) k.DeleteValue(AppName);
            }
            using (var k = Registry.CurrentUser.CreateSubKey(UninstKey))
            {
                k.SetValue("DisplayName", "BlackBox");
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", "Pronerious");
                k.SetValue("DisplayIcon", ExeTarget);
                k.SetValue("InstallLocation", InstallDir);
                k.SetValue("UninstallString", "\"" + uninst + "\" /uninstall");
                k.SetValue("URLInfoAbout", Repo);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)((new FileInfo(ExeTarget).Length + new FileInfo(uninst).Length) / 1024), RegistryValueKind.DWord);
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            }
        }

        // chiude con garbo ogni BlackBox aperto (anche una copia portatile), poi, se serve, lo ferma
        static void CloseRunning()
        {
            EventWaitHandle ev;
            if (EventWaitHandle.TryOpenExisting(@"Local\BlackBoxApp_Exit", out ev)) { ev.Set(); ev.Dispose(); }
            for (int i = 0; i < 40 && Process.GetProcessesByName("BlackBox").Length > 0; i++) Thread.Sleep(250);
            foreach (var p in Process.GetProcessesByName("BlackBox")) try { p.Kill(); p.WaitForExit(3000); } catch { }
        }

        static string AutoStartPath()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k == null ? null : k.GetValue(AppName) as string; } catch { return null; }
        }

        static void Shortcut(string lnk, string target, string desc)
        {
            var t = Type.GetTypeFromProgID("WScript.Shell");
            object sh = Activator.CreateInstance(t);
            object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
            var st = sc.GetType();
            st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
            st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(target) });
            st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { desc });
            st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
            st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
        }

        static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }

        // ================================================ disinstallazione
        // il programma non puo' cancellare la cartella da cui gira: si copia in %TEMP% e riparte da li'
        static int StartUninstall()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "BlackBox-Disinstalla.exe");
            File.Copy(Assembly.GetExecutingAssembly().Location, tmp, true);
            Process.Start(tmp, "/uninstall-now \"" + InstallDir + "\"" + (quiet ? " /quiet" : ""));
            return 0;
        }

        static int Uninstall(string dir)
        {
            var q = quiet ? MessageBoxResult.Yes : MessageBox.Show("Disinstallare BlackBox?\n\nI tuoi dati in Documenti\\BlackBox (report, sessioni, impostazioni) restano: se vuoi, puoi cancellarli tu dopo.\n\n" +
                                    "Se hai installato OptiScaler in qualche gioco con BlackBox, rimuovilo prima da «Giochi & OptiScaler»: dopo resterà nei giochi.",
                                    "Disinstalla BlackBox", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (q != MessageBoxResult.Yes) return 0;
            RemoveInstalled(dir, false);
            if (!quiet) MessageBox.Show("BlackBox è stato disinstallato.", "BlackBox", MessageBoxButton.OK, MessageBoxImage.Information);
            return 0;
        }

        // toglie un'installazione: programma chiuso, avvio automatico, collegamenti, voce in App, file del programma.
        // I dati in Documenti\BlackBox non vengono mai toccati. anyAutoStart: toglie anche l'avvio automatico di una copia portatile.
        static void RemoveInstalled(string dir, bool anyAutoStart)
        {
            CloseRunning();
            Thread.Sleep(500);
            string run = AutoStartPath();
            if (run != null && (anyAutoStart || run.IndexOf(dir, StringComparison.OrdinalIgnoreCase) >= 0))
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true)) k.DeleteValue(AppName, false);
            TryDelete(StartMenuLnk); TryDelete(DesktopLnk);
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstKey, false); } catch { }
            // cancella solo i file del programma, nella sua cartella
            foreach (var f in new[] { "BlackBox.exe", "Disinstalla.exe", "BlackBox.exe.new" }) TryDelete(Path.Combine(dir, f));
            try { if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch { }
        }
    }
}
