using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BlackBox
{
    // ---------------------------------------------------------------- Finestra "Giochi & OptiScaler"
    public class OptiWindow
    {
        static readonly Brush Muted = F("#8A91A0"), Text = F("#E6E8EE"), Red = F("#FF5C6C"), Orange = F("#FFB547"), Green = F("#5EE08A"),
                              Blue = F("#4FC3F7"), Card = F("#161920"), Back = F("#0E1015"), Line = F("#232733"), Sel = F("#22293A");
        static Brush F(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }

        readonly Window win;
        readonly Collector col;
        readonly TextBlock pkgText, status, dTitle, dInfo, dAdvice, dState, dCompare;
        readonly StackPanel list, actions;
        readonly CheckBox preCheck, fakeCheck;
        readonly ComboBox proxyBox;
        readonly Button bInstall, bUpdate, bRemove, bFolder;
        List<GameInfo> games = new List<GameInfo>();
        GameInfo selected;
        bool busy;

        public OptiWindow(Window owner, Collector col)
        {
            this.col = col;
            win = new Window { Title = "BlackBox — Giochi & OptiScaler", Width = 1100, Height = 720, MinWidth = 860, MinHeight = 520, Background = Back, Foreground = Text,
                               FontFamily = new FontFamily("Segoe UI"), WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = owner, Icon = owner.Icon };
            win.Resources.MergedDictionaries.Add(owner.Resources);

            var root = new DockPanel { Margin = new Thickness(14) };
            // ---- intestazione: pacchetto OptiScaler
            var head = new StackPanel { Margin = new Thickness(6, 0, 6, 8) };
            head.Children.Add(new TextBlock { Text = "Giochi & OptiScaler", FontSize = 22, FontWeight = FontWeights.SemiBold });
            head.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 4), FontSize = 12.5,
                Text = "Cos'è: OptiScaler è un programma gratuito che si mette nella cartella di un gioco e gli fa usare un upscaler migliore per la tua scheda (per esempio FSR 3.1/4 o XeSS al posto di DLSS, che sulle Radeon non funziona). Più FPS con un'immagine pulita." });
            head.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4), FontSize = 12.5, Foreground = Muted,
                Text = "Come si usa:  1) Scarica il pacchetto (una volta).   2) Scegli il gioco nell'elenco.   3) Chiudi il gioco e premi Installa.   4) Avvia il gioco: nelle opzioni grafiche scegli DLSS (o XeSS/FSR) e premi il tasto Inizio (Home) per il menu di OptiScaler.   5) Se qualcosa non va: Rimuovi, e il gioco torna com'era." });
            head.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), FontSize = 12.5, Foreground = Red, FontWeight = FontWeights.SemiBold,
                Text = "MAI nei giochi online (multigiocatore, classificate): gli anti-cheat vedono OptiScaler e puoi essere bannato. BlackBox blocca da solo i giochi con anti-cheat riconosciuto, ma se un gioco lo usi online non installarlo comunque." });
            var bar = new WrapPanel();
            pkgText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), FontWeight = FontWeights.SemiBold };
            var bDown = new Button { Content = "Scarica l'ultima versione" }; bDown.Click += (s, e) => DownloadPackage();
            preCheck = new CheckBox { Content = "includi pre-release / nightly", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            var bAdd = new Button { Content = "Aggiungi cartella di un gioco..." }; bAdd.Click += (s, e) => AddFolder();
            var bScan = new Button { Content = "Cerca di nuovo" }; bScan.Click += (s, e) => Scan();
            bar.Children.Add(pkgText); bar.Children.Add(bDown); bar.Children.Add(preCheck); bar.Children.Add(bAdd); bar.Children.Add(bScan);
            head.Children.Add(bar);
            status = new TextBlock { Foreground = Blue, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
            head.Children.Add(status);
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);

            // ---- elenco giochi | dettagli
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
            list = new StackPanel();
            var lcard = new Border { Style = (Style)owner.Resources["Card"], Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list } };
            grid.Children.Add(lcard);

            var det = new StackPanel();
            dTitle = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Text = "Scegli un gioco dall'elenco" };
            dInfo = new TextBlock { Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            dAdvice = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), FontWeight = FontWeights.SemiBold };
            dState = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
            dCompare = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Foreground = Muted };
            det.Children.Add(dTitle); det.Children.Add(dInfo); det.Children.Add(dAdvice); det.Children.Add(dState); det.Children.Add(dCompare);

            actions = new StackPanel { Margin = new Thickness(0, 14, 0, 0), Visibility = Visibility.Collapsed };
            var opt = new WrapPanel();
            opt.Children.Add(new TextBlock { Text = "Nome della DLL:", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            proxyBox = new ComboBox { Width = 130, ItemsSource = Opti.ProxyNames };
            opt.Children.Add(proxyBox);
            fakeCheck = new CheckBox { Content = "fakenvapi (mostra DLSS sulle schede AMD)", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            opt.Children.Add(fakeCheck);
            actions.Children.Add(opt);
            var btns = new WrapPanel { Margin = new Thickness(-8, 10, 0, 0) };
            bInstall = new Button { Content = "Installa" }; bInstall.Click += (s, e) => { if (ConfirmInstall()) Run("Installo", g => Opti.Install(g, LatestCached(), (string)proxyBox.SelectedItem, fakeCheck.IsChecked == true, null), "installato"); };
            bUpdate = new Button { Content = "Aggiorna" }; bUpdate.Click += (s, e) => Run("Aggiorno", g => Opti.Update(g, LatestCached(), fakeCheck.IsChecked == true), "aggiornato (impostazioni tenute)");
            bRemove = new Button { Content = "Rimuovi" }; bRemove.Click += (s, e) => Run("Rimuovo", g => Opti.Remove(g), "rimosso: la cartella del gioco è tornata com'era");
            bFolder = new Button { Content = "Apri cartella" }; bFolder.Click += (s, e) => { if (selected != null) try { Process.Start("explorer.exe", "\"" + selected.ExeDir + "\""); } catch { } };
            btns.Children.Add(bInstall); btns.Children.Add(bUpdate); btns.Children.Add(bRemove); btns.Children.Add(bFolder);
            actions.Children.Add(btns);
            actions.Children.Add(new TextBlock { Foreground = Muted, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 10, 0, 0),
                Text = "Il gioco deve essere chiuso. I file del gioco con lo stesso nome vengono messi da parte (.blackbox-bak) e rimessi a posto con Rimuovi. " +
                       "Se il gioco non parte o va in crash: Rimuovi, poi riprova con winmm.dll o version.dll. In gioco il menu di OptiScaler si apre con il tasto Inizio (Home)." });
            det.Children.Add(actions);
            var dcard = new Border { Style = (Style)owner.Resources["Card"], Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = det } };
            Grid.SetColumn(dcard, 1);
            grid.Children.Add(dcard);
            root.Children.Add(grid);
            win.Content = root;
            win.Closed += (s, e) => { if (Closed != null) Closed(); };
            UpdatePackage();
        }

        public event Action Closed;
        public void Show() { win.Show(); win.Activate(); Scan(); }
        public void Activate() { win.Activate(); }

        static string LatestCached() { var l = Opti.CachedVersions(); return l.Count > 0 ? l[0] : ""; }

        void UpdatePackage()
        {
            string v = LatestCached();
            pkgText.Text = v == "" ? "Pacchetto OptiScaler: non ancora scaricato" : "Pacchetto OptiScaler: " + v + " (scaricato)";
            pkgText.Foreground = v == "" ? Orange : Green;
            ShowDetails();
        }

        void Busy(bool b, string text) { busy = b; status.Text = text; win.Cursor = b ? System.Windows.Input.Cursors.Wait : null; }

        // ------------------------------------------------ elenco dei giochi (in background: la ricerca nelle cartelle puo' durare qualche secondo)
        void Scan()
        {
            if (busy) return;
            Busy(true, "Cerco i giochi installati (Steam, Epic, GOG, cartelle aggiunte, giochi visti in sessione)...");
            new Thread(() =>
            {
                List<GameInfo> found;
                try { found = Opti.FindGames(); } catch { found = new List<GameInfo>(); }
                win.Dispatcher.BeginInvoke(new Action(() =>
                {
                    games = found;
                    string keep = selected != null ? selected.ExeDir : null;
                    selected = games.FirstOrDefault(g => g.ExeDir == keep);
                    FillList();
                    ShowDetails();
                    Busy(false, games.Count + " giochi trovati." + (games.Count == 0 ? " Usa «Aggiungi cartella di un gioco»." : ""));
                }));
            }) { IsBackground = true }.Start();
        }

        void FillList()
        {
            list.Children.Clear();
            foreach (var g in games)
            {
                var gg = g;
                var sp = new StackPanel();
                sp.Children.Add(new TextBlock { Text = g.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                string tag = g.AntiCheat != "" ? "anti-cheat: bloccato" : !g.Is64 ? "32 bit: non compatibile" : g.OptiByBlackBox ? "OptiScaler " + g.OptiVersion : g.OptiByHand ? "OptiScaler (a mano)" : g.Upscalers;
                sp.Children.Add(new TextBlock { Text = g.Store + " · " + tag, FontSize = 12, Foreground = g.Blocked ? Red : (g.OptiByBlackBox || g.OptiByHand) ? Green : Muted, TextTrimming = TextTrimming.CharacterEllipsis });
                var b = new Border { Child = sp, Padding = new Thickness(8, 5, 8, 5), CornerRadius = new CornerRadius(6), Cursor = System.Windows.Input.Cursors.Hand,
                                     Background = g == selected ? Sel : Brushes.Transparent, Margin = new Thickness(0, 1, 0, 1) };
                b.MouseLeftButtonUp += (s, e) => { selected = gg; FillList(); ShowDetails(); };
                list.Children.Add(b);
            }
        }

        // ------------------------------------------------ dettagli del gioco scelto
        void ShowDetails()
        {
            var g = selected;
            if (g == null) { dTitle.Text = "Scegli un gioco dall'elenco"; dInfo.Text = dAdvice.Text = dState.Text = dCompare.Text = ""; actions.Visibility = Visibility.Collapsed; return; }
            dTitle.Text = g.Name;
            dInfo.Text = "Cartella dell'eseguibile: " + g.ExeDir + "\nEseguibile: " + Path.GetFileName(g.Exe) + (g.Vulkan ? "  (Vulkan)" : "") + "\nUpscaler nel gioco: " + g.Upscalers;
            dAdvice.Text = g.Advice;
            dAdvice.Foreground = g.Blocked ? Red : (g.Dlss || g.Xess || g.Fsr2) && !(g.Fsr31 || g.Fsr31Vk) ? Green : Text;

            string esito = Settings.Get(Opti.EsitoKey(g.ExeDir), "");
            string st = "OptiScaler: " + g.OptiState;
            if (esito != "")
            {
                var p = esito.Split('|');
                st += p[0] == "attivo" ? "\nUltima partita (" + p[1] + "): ATTIVO — " + (p.Length > 2 ? p[2] : "") : "\nUltima partita (" + p[1] + "): PROBLEMI — " + (p.Length > 2 ? p[2] : "");
                dState.Foreground = p[0] == "attivo" ? Green : Orange;
            }
            else dState.Foreground = Text;
            dState.Text = st;
            dCompare.Text = Compare(g);

            bool locked = g.Blocked;
            string ver = LatestCached();
            // gioco con anti-cheat: nessun pulsante, solo la spiegazione (si puo' solo rimuovere se gia' installato da BlackBox)
            actions.Visibility = locked && !g.OptiByBlackBox ? Visibility.Collapsed : Visibility.Visible;
            if (locked && g.AntiCheat == "")
                dAdvice.Text = "BLOCCATO: " + g.Name + " è un gioco a 32 bit e OptiScaler funziona solo con i giochi a 64 bit: installarlo lo farebbe solo andare in crash.";
            else if (locked)
            {
                dAdvice.Text = "BLOCCATO: " + g.Name + " ha un anti-cheat (" + g.AntiCheat + "). Gli anti-cheat controllano le DLL caricate dal gioco: con OptiScaler rischi il BAN dell'account, anche se lo usi una volta sola. BlackBox non permette l'installazione.";
                if (g.OptiByBlackBox) dAdvice.Text += "\nOptiScaler risulta installato: premi Rimuovi PRIMA di giocare online.";
            }
            bInstall.IsEnabled = !locked && ver != "" && !g.OptiByBlackBox && !g.OptiByHand;
            bUpdate.IsEnabled = !locked && ver != "" && g.OptiByBlackBox && g.OptiVersion != ver;
            bUpdate.Content = g.OptiByBlackBox && ver != "" && g.OptiVersion != ver ? "Aggiorna a " + ver : "Aggiorna";
            bRemove.IsEnabled = g.OptiByBlackBox;
            proxyBox.IsEnabled = fakeCheck.IsEnabled = !locked && !g.OptiByBlackBox;
            proxyBox.SelectedItem = g.OptiByBlackBox && g.OptiProxy != "" ? g.OptiProxy : Opti.SuggestedProxy(g);
            fakeCheck.IsChecked = g.Dlss;
            if (ver == "" && !locked) dState.Text += "\nPer installarlo scarica prima il pacchetto (pulsante in alto).";
            if (g.OptiByHand) dState.Text += "\nInstallato senza BlackBox: per sicurezza BlackBox non lo modifica (usa il file Remove_OptiScaler.bat nella cartella del gioco).";
        }

        // prima/dopo: medie delle partite a questo gioco con e senza OptiScaler (dai report di sessione)
        static string Compare(GameInfo g)
        {
            string proc = Path.GetFileNameWithoutExtension(g.Exe);
            var rows = SessionLog.Read(BlackBoxDir()).Where(r => string.Equals(r.Game, proc, StringComparison.OrdinalIgnoreCase) && !double.IsNaN(r.Fps)).ToList();
            if (rows.Count == 0) return "Confronto prima/dopo: ancora nessuna partita con FPS registrati.";
            Func<List<SessionRow>, string> d = l => l.Count == 0 ? "nessuna partita" :
                l.Average(x => x.Fps).ToString("0") + " FPS, 1% low " + l.Where(x => !double.IsNaN(x.Low)).Select(x => x.Low).DefaultIfEmpty(double.NaN).Average().ToString("0") +
                ", " + l.Where(x => !double.IsNaN(x.Power)).Select(x => x.Power).DefaultIfEmpty(double.NaN).Average().ToString("0") + " W (" + l.Count + (l.Count == 1 ? " partita)" : " partite)");
            var with = rows.Where(r => r.Opti.StartsWith("attivo")).ToList();
            var without = rows.Where(r => r.Opti == "").ToList();
            return "Confronto prima/dopo (medie delle partite; confronta la stessa scena):\n  senza OptiScaler: " + d(without) + "\n  con OptiScaler:   " + d(with);
        }

        // conferma prima di installare: cosa succede e il rischio dei giochi online
        bool ConfirmInstall()
        {
            var g = selected;
            if (g == null || g.Blocked) return false;
            int n = 0; try { n = Directory.GetFiles(Opti.PackageDir(LatestCached()), "*", SearchOption.AllDirectories).Length - 3; } catch { }
            string proxy = (string)proxyBox.SelectedItem ?? "dxgi.dll";
            string existing = Opti.ExistingProxyInfo(g.ExeDir, proxy);
            if (existing != null)
            {
                var w = MessageBox.Show(win, "Nella cartella del gioco c'è già un file " + proxy + " (" + existing + ").\n\n" +
                    (existing.IndexOf("ReShade", StringComparison.OrdinalIgnoreCase) >= 0 ? "È ReShade: se lo usi, scegli un altro nome per OptiScaler (es. winmm.dll), così funzionano tutti e due.\n\n" : "Potrebbe essere un'altra mod.\n\n") +
                    "Se continui, " + proxy + " viene messo da parte (.blackbox-bak) e torna al suo posto con «Rimuovi».\n\nContinuare con " + proxy + "?",
                    "File già presente", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (w != MessageBoxResult.Yes) { status.Foreground = Orange; status.Text = "Scegli un altro nome della DLL e premi di nuovo Installa."; return false; }
            }
            var q = MessageBox.Show(win,
                "Stai per installare OptiScaler in " + g.Name + ".\n\n" +
                "PRIMA DI CONTINUARE:\n" +
                "•  Il gioco deve essere CHIUSO.\n" +
                "•  Usalo SOLO se giochi a " + g.Name + " offline / in single player. Nelle partite online con altri giocatori puoi essere BANNATO, anche se BlackBox non ha trovato un anti-cheat.\n\n" +
                "COSA FA BLACKBOX:\n" +
                "•  Copia circa " + Math.Max(n, 10) + " file nella cartella del gioco:\n   " + g.ExeDir + "\n" +
                "•  Se il gioco ha già file con lo stesso nome, li mette da parte (.blackbox-bak).\n" +
                "•  Con «Rimuovi» la cartella torna esattamente com'era.\n\n" +
                "Confermi che userai " + g.Name + " SOLO offline?",
                "Installa OptiScaler — leggi prima", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (q != MessageBoxResult.Yes) { status.Foreground = Orange; status.Text = "Non installato. Per i giochi online OptiScaler può costarti il ban: meglio così."; return false; }
            return true;
        }

        // dopo l'installazione: come attivarlo nel gioco, passo passo
        void HowToUse(GameInfo g)
        {
            MessageBox.Show(win,
                "OptiScaler è installato in " + g.Name + ". Ora, nel gioco:\n\n" +
                "1.  Avvia " + g.Name + ".\n" +
                "2.  Apri le opzioni grafiche e scegli come upscaler " + (g.Dlss ? "DLSS (ora compare anche sulla tua Radeon)" : g.Xess ? "XeSS" : "FSR 2") + ", con la qualità che preferisci.\n" +
                "3.  Premi il tasto INIZIO (Home) sulla tastiera: si apre il menu di OptiScaler.\n" +
                "4.  Nel menu, alla voce «Upscalers», scegli FSR 3.1 / FSR 4 oppure XeSS. Prova e guarda gli FPS nell'overlay di BlackBox.\n" +
                (g.DlssFg || g.Dlss ? "5.  Generazione di fotogrammi: nel gioco attiva «DLSS Frame Generation»; OptiScaler la trasforma in quella di AMD (sezione «Frame Generation» del menu).\n" : "") +
                "\nPremi di nuovo Inizio (Home) per chiudere il menu. Le scelte restano salvate.\n\n" +
                "Se il gioco non parte o va in crash: chiudilo, qui premi Rimuovi e poi reinstalla scegliendo come nome della DLL winmm.dll o version.dll.\n" +
                "BlackBox controlla da solo, alla prossima partita, che OptiScaler sia attivo.",
                "Come attivarlo nel gioco", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        static string BlackBoxDir() { return Path.GetDirectoryName(Settings.Path); }

        // ------------------------------------------------ azioni
        void Run(string what, Action<GameInfo> act, string done)
        {
            var g = selected;
            if (g == null || busy) return;
            Busy(true, what + " OptiScaler in " + g.Name + "...");
            new Thread(() =>
            {
                string msg; bool ok = false;
                try { act(g); msg = "OptiScaler " + done + " in " + g.Name + "."; ok = true; }
                catch (Exception ex) { msg = "Non riuscito: " + ex.Message; }
                col.AddEvent(ok ? "INFO" : "WARN", msg);
                try { Opti.Analyze(g, g.Exe); } catch { }
                win.Dispatcher.BeginInvoke(new Action(() =>
                {
                    Busy(false, msg); status.Foreground = ok ? Green : Orange; FillList(); ShowDetails();
                    if (ok && done == "installato") HowToUse(g);
                }));
            }) { IsBackground = true }.Start();
        }

        void DownloadPackage()
        {
            if (busy) return;
            bool pre = preCheck.IsChecked == true;
            Busy(true, "Chiedo a GitHub l'ultima versione di OptiScaler...");
            new Thread(() =>
            {
                Opti.Release r = null; string err = null;
                try { r = Opti.ListReleases(pre).FirstOrDefault(); if (r == null) err = "nessuna release con un archivio .7z"; }
                catch (Exception ex) { err = ex.Message; }
                win.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (err != null) { Busy(false, "Non riesco a leggere le release: " + err); return; }
                    if (Opti.CachedVersions().Contains(r.Tag)) { Busy(false, "Hai già l'ultima versione (" + r.Tag + ")."); return; }
                    var q = MessageBox.Show(win, "Scarico OptiScaler " + r.Tag + (r.Pre ? " (PRE-RELEASE)" : "") + " del " + r.Date + ":\n" + r.Name + "  (" + (r.Size / 1048576.0).ToString("0") + " MB)\n\n" +
                        "Dalla pagina ufficiale github.com/optiscaler/OptiScaler. È l'unica connessione, e solo perché l'hai chiesta tu.\n" +
                        (r.Digest != "" ? "L'archivio viene controllato con l'impronta SHA-256 pubblicata da GitHub.\n" : "") +
                        "Viene salvato in Documenti\\BlackBox\\optiscaler. Procedo?", "Scarica OptiScaler", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (q != MessageBoxResult.Yes) { Busy(false, ""); return; }
                    Busy(true, "Scarico ed estraggo OptiScaler " + r.Tag + " (" + (r.Size / 1048576.0).ToString("0") + " MB)...");
                    new Thread(() =>
                    {
                        string msg; bool ok = false;
                        try { msg = "OptiScaler " + Opti.Download(r) + " scaricato e pronto."; ok = true; }
                        catch (Exception ex) { msg = "Download di OptiScaler non riuscito: " + ex.Message; }
                        col.AddEvent(ok ? "INFO" : "WARN", msg);
                        win.Dispatcher.BeginInvoke(new Action(() => { Busy(false, msg); status.Foreground = ok ? Green : Orange; UpdatePackage(); }));
                    }) { IsBackground = true }.Start();
                }));
            }) { IsBackground = true }.Start();
        }

        void AddFolder()
        {
            using (var d = new System.Windows.Forms.FolderBrowserDialog { Description = "Scegli la cartella del gioco (quella con l'eseguibile, o la cartella principale del gioco)" })
                if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    Opti.AddManualFolder(d.SelectedPath);
                    Scan();
                }
        }
    }
}
