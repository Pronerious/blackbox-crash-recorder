using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

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
        public static List<HealthItem> Software()
        {
            var r = new List<HealthItem>();
            Action<int, string, string> add = (l, s, t) => r.Add(new HealthItem { Level = l, Short = s, Text = t });
            bool hwinfo = Process.GetProcessesByName("HWiNFO64").Length > 0 || Process.GetProcessesByName("HWiNFO32").Length > 0;
            bool lhm = Process.GetProcessesByName("LibreHardwareMonitor").Length > 0;
            if (CpuTemp.Source != "") add(0, "temperatura CPU", "Temperatura della CPU letta da " + CpuTemp.Source + ".");
            else if (hwinfo) add(-1, "HWiNFO senza memoria condivisa", "HWiNFO è aperto ma senza memoria condivisa: Settings → spunta «Shared Memory Support» (guida, punto 11).");
            else if (lhm) add(-1, "LibreHardwareMonitor", "LibreHardwareMonitor è aperto ma non pubblica i dati: avvialo come amministratore.");
            else add(-1, "temperatura CPU n/d", "Per la temperatura della CPU apri HWiNFO con la memoria condivisa (guida, punto 11).");
            add(Fps.ToolPresent ? 0 : -1, "PresentMon", Fps.ToolPresent ? "PresentMon " + Fps.ToolVersion + " presente: FPS attivi nelle sessioni di gioco." : "PresentMon non scaricato: senza, niente FPS (riga FPS → Scarica PresentMon).");
            if (!Fps.HasPermission) add(-1, "permesso FPS", "Manca il permesso per leggere gli FPS (riga FPS → Abilita FPS).");
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers"))
                {
                    object v = k == null ? null : k.GetValue("HwSchMode");
                    if (v is int && (int)v == 2) add(0, "HAGS attivo", "Pianificazione GPU con accelerazione hardware (HAGS) attiva: utile per la generazione di fotogrammi.");
                    else add(-1, "HAGS spento", "Pianificazione GPU con accelerazione hardware (HAGS) spenta: per la generazione di fotogrammi conviene accenderla (Impostazioni → Schermo → Grafica → Impostazioni grafiche predefinite).");
                }
            }
            catch { }
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar"))
                {
                    object v = k == null ? null : k.GetValue("AutoGameModeEnabled");
                    if (v is int && (int)v == 0) add(-1, "Modalità gioco spenta", "La Modalità gioco di Windows è spenta: di solito conviene accenderla (Impostazioni → Giochi → Modalità gioco).");
                    else add(0, "Modalità gioco", "Modalità gioco di Windows attiva.");
                }
            }
            catch { }
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity"))
                {
                    object v = k == null ? null : k.GetValue("Enabled");
                    if (v is int && (int)v == 1) add(-1, "Integrità della memoria", "Integrità della memoria (sicurezza di Windows) accesa: protegge il PC ma può togliere qualche FPS. Va bene lasciarla, è una scelta.");
                }
            }
            catch { }
            return r;
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
