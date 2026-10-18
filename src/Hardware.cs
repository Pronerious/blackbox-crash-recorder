using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Windows;

namespace BlackBox
{
    // ---------------------------------------------------------------- Riconoscimento del PC (hardware e software)
    // A ogni avvio BlackBox legge che CPU, scheda video, driver e Windows ci sono e sceglie le soglie giuste per
    // QUEL modello (es. RX 7800 XT: hotspot fino a 110 °C; Ryzen 7 7800X3D: CPU fino a 89 °C).
    // Al primo avvio mostra cosa ha riconosciuto; se poi cambia la scheda video o il driver lo segnala e si adatta.
    public static class Hardware
    {
        public static string Cpu = "", Gpu = "", GpuDriver = "", AmdSoftware = "", Windows = "";
        public static double VramGb = double.NaN, RamGb = double.NaN;
        public static int Cores;
        public static bool FirstRun;
        // il benvenuto resta in sospeso finche' non viene mostrato davvero (non sopra un gioco)
        public static bool WelcomePending { get { return Settings.Get("benvenuto_mostrato", "") != "1"; } }
        public static void WelcomeShown() { Settings.Set("benvenuto_mostrato", "1"); }
        public static readonly List<string> Changes = new List<string>();

        // ------------------------------------------------ limiti di progetto per modello
        public static double CpuMax = 95;      // Tjmax
        public static string CpuMaxWhy = "";
        public static double GpuHotMax = 110, GpuEdgeWarn = 90, GpuEdgeBad = 97;
        public static string GpuWhy = "";

        public static void Detect()
        {
            try { using (var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0")) Cpu = ((k.GetValue("ProcessorNameString") as string) ?? "").Trim(); } catch { }
            Cores = Environment.ProcessorCount;
            try
            {
                var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
                long kb;   // memoria installata (quella "totale" di Windows esclude la parte riservata)
                if (GetPhysicallyInstalledSystemMemory(out kb) && kb > 0) RamGb = Math.Round(kb / 1048576.0);
                else if (GlobalMemoryStatusEx(ref m)) RamGb = Math.Round(m.ullTotalPhys / 1073741824.0);
            }
            catch { }
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    string name = (k.GetValue("ProductName") as string) ?? "Windows";
                    int build; int.TryParse((k.GetValue("CurrentBuild") as string) ?? "0", out build);
                    if (build >= 22000) name = name.Replace("Windows 10", "Windows 11");   // il Registro dice ancora "10"
                    Windows = name + " " + ((k.GetValue("DisplayVersion") as string) ?? "") + " (build " + build + ")";
                }
            }
            catch { }
            // scheda video: quella con piu' memoria (la dedicata, non la grafica integrata)
            try
            {
                using (var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                    foreach (var n in cls.GetSubKeyNames())
                        try
                        {
                            using (var k = cls.OpenSubKey(n))
                            {
                                if (k == null) continue;
                                var mem = k.GetValue("HardwareInformation.qwMemorySize");
                                double gb = mem is long ? (long)mem / 1073741824.0 : 0;
                                if (gb <= 0 || (!double.IsNaN(VramGb) && gb <= VramGb)) continue;
                                VramGb = Math.Round(gb, 1);
                                Gpu = ((k.GetValue("DriverDesc") as string) ?? "").Trim();
                                GpuDriver = ((k.GetValue("DriverVersion") as string) ?? "").Trim();
                                AmdSoftware = ((k.GetValue("RadeonSoftwareVersion") as string) ?? "").Trim();
                            }
                        }
                        catch { }
            }
            catch { }
            ChooseLimits();
            CompareWithLast();
        }

        static void ChooseLimits()
        {
            string c = Cpu.ToUpperInvariant();
            var mm = System.Text.RegularExpressions.Regex.Match(c, @"\b(\d)\d{3}");   // "7800X3D" -> serie 7
            char serie = mm.Success ? mm.Groups[1].Value[0] : ' ';
            if (c.Contains("RYZEN") && c.Contains("X3D") && serie != '9')
            { CpuMax = serie == '5' ? 90 : 89; CpuMaxWhy = "Ryzen X3D (cache 3D): limite più basso, " + CpuMax + " °C"; }
            else if (c.Contains("RYZEN") && (serie == '7' || serie == '9'))
            { CpuMax = 95; CpuMaxWhy = "Ryzen 7000/9000: arriva a 95 °C per progetto sotto carico pieno"; }
            else if (c.Contains("RYZEN") && serie == '3') { CpuMax = 95; CpuMaxWhy = "Ryzen 3000: limite 95 °C"; }
            else if (c.Contains("RYZEN")) { CpuMax = 90; CpuMaxWhy = "Ryzen: limite 90 °C"; }
            else if (c.Contains("ULTRA")) { CpuMax = 105; CpuMaxWhy = "Intel Core Ultra: limite 105 °C"; }
            else if (c.Contains("INTEL")) { CpuMax = 100; CpuMaxWhy = "Intel Core: limite 100 °C"; }
            else { CpuMax = 95; CpuMaxWhy = "modello non riconosciuto: limite prudente 95 °C"; }
            string g = Gpu.ToUpperInvariant();
            if (g.Contains("NVIDIA") || GpuSensors.Vendor == "NVIDIA")
            { GpuHotMax = 105; GpuEdgeWarn = 83; GpuEdgeBad = 88; GpuWhy = "NVIDIA: la temperatura del chip rallenta verso 88-90 °C"; }
            else if (g.Contains("INTEL") && g.Contains("ARC"))
            { GpuHotMax = 105; GpuEdgeWarn = 90; GpuEdgeBad = 100; GpuWhy = "Intel Arc: limite circa 100 °C"; }
            else if (g.Contains("RX 5") && !g.Contains("RX 5500") && !g.Contains("RX 5600") && !g.Contains("RX 5700") && g.Length > 0)
            { GpuHotMax = 105; GpuEdgeWarn = 85; GpuEdgeBad = 92; GpuWhy = "Radeon Polaris (RX 470-590): limite più basso"; }
            else
            { GpuHotMax = 110; GpuEdgeWarn = 90; GpuEdgeBad = 97; GpuWhy = "Radeon RDNA (RX 5000/6000/7000/9000): hotspot fino a 110 °C per progetto"; }
        }

        // primo avvio, scheda video cambiata o driver aggiornato
        static void CompareWithLast()
        {
            string lastGpu = Settings.Get("hw_gpu", ""), lastDrv = Settings.Get("hw_driver", ""), lastCpu = Settings.Get("hw_cpu", "");
            FirstRun = lastGpu == "" && lastCpu == "";
            if (!FirstRun)
            {
                if (lastGpu != Gpu)
                {
                    Changes.Add("Scheda video cambiata: " + lastGpu + " → " + Gpu + ". Le soglie e i valori imparati ripartono da capo.");
                    Settings.Set("gpu_delta_tipico", "");
                    ResetMaxPower = true;
                }
                else if (lastDrv != GpuDriver && GpuDriver != "")
                    Changes.Add("Driver video aggiornato: " + lastDrv + " → " + GpuDriver + (AmdSoftware != "" ? " (Adrenalin " + AmdSoftware + ")" : "") +
                                ". Dopo un aggiornamento Adrenalin a volte azzera lo UV/OC: controllerò alla prossima partita.");
                if (lastCpu != Cpu) Changes.Add("Processore cambiato: " + lastCpu + " → " + Cpu + ".");
            }
            Settings.Set("hw_gpu", Gpu); Settings.Set("hw_driver", GpuDriver); Settings.Set("hw_cpu", Cpu);
        }
        public static bool ResetMaxPower;
        public static bool DriverChanged { get { return Changes.Any(x => x.StartsWith("Driver video aggiornato")); } }

        // ------------------------------------------------ software e impostazioni di Windows utili per giocare
        // ogni voce che si puo' sistemare ha un pulsante "Sistema" (Fix) con una spiegazione semplice
        public static List<HealthItem> Software()
        {
            var r = new List<HealthItem>();
            Action<int, string, string, string, string> add = (l, s, t, fix, label) => r.Add(new HealthItem { Level = l, Short = s, Text = t, Fix = fix ?? "", FixLabel = label ?? "" });
            bool hwinfo = Process.GetProcessesByName("HWiNFO64").Length > 0 || Process.GetProcessesByName("HWiNFO32").Length > 0;
            bool lhm = Process.GetProcessesByName("LibreHardwareMonitor").Length > 0;
            if (CpuTemp.Source != "") add(0, "temperatura CPU", "Temperatura della CPU letta da " + CpuTemp.Source + ".", null, null);
            else if (hwinfo) add(1, "HWiNFO senza memoria condivisa", "HWiNFO è aperto ma non condivide i dati, quindi BlackBox non vede la temperatura della CPU. Basta una spunta in HWiNFO: il pulsante ti mostra dove.", "hwinfo-sm", "Mostrami come");
            else if (lhm) add(-1, "LibreHardwareMonitor", "LibreHardwareMonitor è aperto ma non pubblica i dati: chiudilo e riaprilo come amministratore.", null, null);
            else if (HwinfoExe() != null) add(-1, "HWiNFO chiuso", "Per la temperatura della CPU serve HWiNFO aperto (con la memoria condivisa).", "hwinfo-open", "Apri HWiNFO");
            else add(-1, "temperatura CPU n/d", "Per la temperatura della CPU serve HWiNFO (gratuito). Il pulsante apre la pagina ufficiale per scaricarlo.", "hwinfo-get", "Pagina di HWiNFO");
            if (Fps.ToolPresent) add(0, "PresentMon", "PresentMon " + Fps.ToolVersion + " presente: FPS attivi nelle sessioni di gioco.", null, null);
            else add(1, "PresentMon mancante", "Senza PresentMon (gratuito, di Intel) BlackBox non può misurare gli FPS.", "presentmon", "Scarica PresentMon");
            if (!Fps.HasPermission) add(1, "permesso FPS", "Manca il permesso di Windows per leggere gli FPS. Il pulsante lo chiede una volta (conferma da amministratore), poi bisogna uscire da Windows e rientrare.", "fpsperm", "Dai il permesso");
            if (HagsOn()) add(0, "HAGS attivo", "Pianificazione GPU con accelerazione hardware (HAGS) attiva: utile per la generazione di fotogrammi.", null, null);
            else add(1, "HAGS spento", "La «Pianificazione GPU con accelerazione hardware» (HAGS) è spenta. Serve per la generazione di fotogrammi (FSR 3 / DLSS FG) e di solito aiuta un po' la fluidità. Il pulsante la accende: Windows chiede una conferma da amministratore, poi serve un riavvio.", "hags", "Accendi HAGS");
            if (GameModeOn()) add(0, "Modalità gioco", "Modalità gioco di Windows attiva.", null, null);
            else add(1, "Modalità gioco spenta", "La Modalità gioco di Windows è spenta: quando giochi, Windows dà la precedenza al gioco solo se è accesa. Il pulsante la accende (nessun permesso speciale).", "gamemode", "Accendi");
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity"))
                {
                    object v = k == null ? null : k.GetValue("Enabled");
                    if (v is int && (int)v == 1) add(-1, "Integrità della memoria", "Integrità della memoria (sicurezza di Windows) accesa: protegge il PC ma può togliere qualche FPS. È una scelta di sicurezza: BlackBox non la tocca.", null, null);
                }
            }
            catch { }
            return r;
        }

        static bool HagsOn()
        {
            try { using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers")) { object v = k == null ? null : k.GetValue("HwSchMode"); return v is int && (int)v == 2; } }
            catch { return true; }
        }
        static bool GameModeOn()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar")) { object v = k == null ? null : k.GetValue("AutoGameModeEnabled"); return !(v is int && (int)v == 0); } }
            catch { return true; }
        }

        public static string HwinfoExe()
        {
            foreach (var p in new[] { @"C:\Program Files\HWiNFO64\HWiNFO64.exe", @"C:\Program Files (x86)\HWiNFO64\HWiNFO64.exe" })
                if (File.Exists(p)) return p;
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\HWiNFO64_is1"))
                {
                    string dir = k == null ? null : k.GetValue("InstallLocation") as string;
                    if (dir != null && File.Exists(Path.Combine(dir, "HWiNFO64.exe"))) return Path.Combine(dir, "HWiNFO64.exe");
                }
            }
            catch { }
            return null;
        }

        // ------------------------------------------------ pulsante "Sistema": esegue la correzione, restituisce cosa e' successo
        public static string ApplyFix(string fix, Window owner)
        {
            switch (fix)
            {
                case "gamemode":
                    using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\GameBar"))
                    { k.SetValue("AutoGameModeEnabled", 1, RegistryValueKind.DWord); k.SetValue("AllowAutoGameMode", 1, RegistryValueKind.DWord); }
                    return "Modalità gioco accesa.";
                case "hags":
                    if (MessageBox.Show(owner, "Accendo la «Pianificazione GPU con accelerazione hardware» (HAGS).\n\nWindows ti chiederà una conferma da amministratore: premi Sì.\nDopo bisogna riavviare il PC perché valga.\n\nProcedo?",
                                        "Accendi HAGS", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return "";
                    try
                    {
                        var psi = new ProcessStartInfo("reg.exe", @"add ""HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers"" /v HwSchMode /t REG_DWORD /d 2 /f")
                        { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                        using (var p = Process.Start(psi)) p.WaitForExit(20000);
                    }
                    catch (Exception ex) { return "HAGS non acceso: " + ex.Message; }
                    if (!HagsOn()) return "HAGS non acceso (conferma annullata?).";
                    if (MessageBox.Show(owner, "HAGS acceso. Serve un riavvio perché valga.\n\nRiavviare adesso? (salva prima il tuo lavoro)", "HAGS acceso",
                                        MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                        try { Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 5") { UseShellExecute = false, CreateNoWindow = true }); } catch { }
                    return "HAGS acceso: vale dal prossimo riavvio.";
                case "hwinfo-open":
                    try { Process.Start(HwinfoExe()); return "HWiNFO aperto: se la temperatura non arriva, controlla la memoria condivisa (pulsante «Mostrami come»)."; }
                    catch (Exception ex) { return "HWiNFO non aperto: " + ex.Message; }
                case "hwinfo-get":
                    try { Process.Start("https://www.hwinfo.com/download/"); } catch { }
                    return "Pagina di HWiNFO aperta nel browser: scegli «Installer» (gratuito), installalo e aprilo.";
                case "hwinfo-sm":
                    MessageBox.Show(owner, "Accendere la memoria condivisa di HWiNFO (una volta sola):\n\n" +
                        "1.  Apri la finestra di HWiNFO: clic sulla sua icona vicino all'orologio (freccetta ^ se non la vedi).\n" +
                        "2.  Nella finestra dei sensori, in basso, clicca l'INGRANAGGIO (Settings).\n" +
                        "3.  Nella scheda «General / User Interface» metti la spunta su «Shared Memory Support».\n" +
                        "4.  Premi OK.\n\n" +
                        "Dopo pochi secondi la temperatura della CPU compare in BlackBox.\n" +
                        "Nota: nella versione gratuita di HWiNFO la memoria condivisa si spegne dopo 12 ore: se la temperatura torna «n/d», rimetti la spunta.",
                        "HWiNFO — memoria condivisa", MessageBoxButton.OK, MessageBoxImage.Information);
                    return "Quando hai messo la spunta, la temperatura della CPU compare da sola.";
                case "taskmgr":
                    try { Process.Start("taskmgr.exe"); } catch { }
                    return "";
            }
            return "";
        }
        // testo del riepilogo (primo avvio e controllo dello stato)
        public static string Describe()
        {
            var l = new List<string>();
            l.Add("Processore:   " + (Cpu == "" ? "n/d" : Cpu) + " (" + Cores + " thread) — " + CpuMaxWhy);
            l.Add("Scheda video: " + (Gpu == "" ? "n/d" : Gpu) + (double.IsNaN(VramGb) ? "" : ", " + VramGb.ToString("0", CultureInfo.InvariantCulture) + " GB") + " — " + GpuWhy);
            l.Add("Driver video: " + (GpuDriver == "" ? "n/d" : GpuDriver) + (AmdSoftware != "" ? "  ·  AMD Software (Adrenalin) " + AmdSoftware : ""));
            l.Add("Sensori GPU:  " + (GpuSensors.Ok ? GpuSensors.Vendor + " attivi" : "non disponibili"));
            l.Add("Memoria:      " + (double.IsNaN(RamGb) ? "n/d" : RamGb + " GB"));
            l.Add("Sistema:      " + Windows);
            return string.Join(Environment.NewLine, l);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MEMORYSTATUSEX { public uint dwLength, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
        [DllImport("kernel32.dll")] static extern bool GetPhysicallyInstalledSystemMemory(out long kb);
    }
}
