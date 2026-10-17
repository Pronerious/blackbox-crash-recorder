using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace BlackBox
{
    public class Sample
    {
        public DateTime Time;
        public double CpuUtil = double.NaN, CpuCoreMax = double.NaN, CpuPerf = double.NaN, CpuMhz = double.NaN;
        public double RamCommit = double.NaN, RamFreeMb = double.NaN;
        public double Gpu3d = double.NaN, GpuAct = double.NaN, GpuMemAct = double.NaN;
        public double GpuPower = double.NaN, GpuPowerMax = double.NaN;
        public double GpuClk = double.NaN, GpuClkMax = double.NaN, GpuMemClk = double.NaN, GpuVddc = double.NaN;
        public double GpuTempEdge = double.NaN, GpuTempHot = double.NaN, GpuTempMem = double.NaN;
        public double GpuFanRpm = double.NaN, GpuFanPct = double.NaN, VramMb = double.NaN, ThermalC = double.NaN;
        public string Foreground = "", TopCpuProc = "", TopGpuProc = "";
        public double TopCpuPct = double.NaN, TopGpuPct = double.NaN;
        public string Note = "";
        // colonne aggiunte in coda al CSV dalla versione 1.2 (vuote finche' la fonte non e' disponibile)
        public double Fps = double.NaN, Fps1Low = double.NaN, FrameTimeMs = double.NaN, CpuTctl = double.NaN, FpsReal = double.NaN;
        public int FgFactor = 1;   // 2 = generazione di fotogrammi 2x, ecc.
        public string OptiScaler = "";
        // solo in memoria: processo in primo piano e suo uso della GPU 3D (per le sessioni di gioco)
        public int ForegroundPid;
        public double Fg3d = double.NaN;
    }

    public class EventLine
    {
        public DateTime Time;
        public string Level;   // ERR / WARN / INFO
        public string Text;
    }

    public class CrashInfo
    {
        public DateTime BootTime;
        public long RecordId;
        public int Bugcheck;
        public int SleepState;
        public long PowerButton;
        public bool Covered;
        public DateTime LastSample;
        public string LastForeground = "";
        public string ReportPath = "";
        public string Summary = "";
        public string Verdict = "";
        public string Config = "";
        public bool IsNew;
    }

    // ---------------------------------------------------------------- PDH
    static class Pdh
    {
        public const uint FMT_DOUBLE = 0x200, FMT_NOCAP100 = 0x8000;
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] public static extern uint PdhOpenQueryW(string src, IntPtr user, out IntPtr q);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] public static extern uint PdhAddEnglishCounterW(IntPtr q, string path, IntPtr user, out IntPtr c);
        [DllImport("pdh.dll")] public static extern uint PdhCollectQueryData(IntPtr q);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] public static extern uint PdhGetFormattedCounterArrayW(IntPtr c, uint fmt, ref uint size, out uint count, IntPtr buf);

        public static Dictionary<string, double> ReadArray(IntPtr c)
        {
            var res = new Dictionary<string, double>();
            if (c == IntPtr.Zero) return res;
            uint size = 0, count;
            uint fmt = FMT_DOUBLE | FMT_NOCAP100;
            uint st = PdhGetFormattedCounterArrayW(c, fmt, ref size, out count, IntPtr.Zero);
            if (st != 0x800007D2 || size == 0) return res;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                st = PdhGetFormattedCounterArrayW(c, fmt, ref size, out count, buf);
                if (st != 0) return res;
                for (int i = 0; i < count; i++)
                {
                    IntPtr item = IntPtr.Add(buf, i * 24);
                    string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item));
                    uint cs = (uint)Marshal.ReadInt32(item, 8);
                    if (cs > 1) continue;
                    double v = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, 16));
                    if (name != null) res[name] = res.ContainsKey(name) ? res[name] + v : v;
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
            return res;
        }
    }

    // ---------------------------------------------------------------- AMD ADL
    static class Adl
    {
        public delegate IntPtr AllocCb(int size);
        static AllocCb cb = new AllocCb(Alloc);
        static IntPtr Alloc(int size) { return Marshal.AllocCoTaskMem(size); }
        [DllImport("atiadlxx.dll")] static extern int ADL2_Main_Control_Create(AllocCb cb, int enumConnected, out IntPtr ctx);
        [DllImport("atiadlxx.dll")] static extern int ADL2_Adapter_NumberOfAdapters_Get(IntPtr ctx, out int n);
        [DllImport("atiadlxx.dll")] static extern int ADL2_New_QueryPMLogData_Get(IntPtr ctx, int idx, IntPtr outp);

        static IntPtr ctx = IntPtr.Zero;
        static int adapter = -1;
        const int N = 256, SZ = 4 + N * 8;
        static IntPtr buf = IntPtr.Zero;
        public static bool Ok { get { return adapter >= 0; } }

        public static void Init()
        {
            try
            {
                if (ADL2_Main_Control_Create(cb, 1, out ctx) != 0) return;
                int n; ADL2_Adapter_NumberOfAdapters_Get(ctx, out n);
                buf = Marshal.AllocHGlobal(SZ);
                for (int i = 0; i < n; i++)
                {
                    if (Query(i) && Get(8) >= 0) { adapter = i; break; }
                }
            }
            catch { adapter = -1; }
        }

        static bool Query(int idx)
        {
            for (int k = 0; k < SZ; k += 4) Marshal.WriteInt32(buf, k, 0);
            Marshal.WriteInt32(buf, 0, SZ);
            return ADL2_New_QueryPMLogData_Get(ctx, idx, buf) == 0;
        }

        public static bool Refresh() { return adapter >= 0 && Query(adapter); }

        // -1 = sensore non supportato
        public static int Get(int sensor)
        {
            if (Marshal.ReadInt32(buf, 4 + sensor * 8) == 0) return -1;
            return Marshal.ReadInt32(buf, 8 + sensor * 8);
        }
    }

    // ---------------------------------------------------------------- NVIDIA NVML (nvml.dll installata con il driver)
    static class Nvml
    {
        [StructLayout(LayoutKind.Sequential)] struct Util { public uint Gpu; public uint Memory; }
        [DllImport("nvml.dll")] static extern int nvmlInit_v2();
        [DllImport("nvml.dll")] static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr dev);
        [DllImport("nvml.dll")] static extern int nvmlDeviceGetTemperature(IntPtr dev, int sensor, out uint t);
        [DllImport("nvml.dll")] static extern int nvmlDeviceGetPowerUsage(IntPtr dev, out uint milliwatt);
        [DllImport("nvml.dll")] static extern int nvmlDeviceGetClockInfo(IntPtr dev, int type, out uint mhz);
        [DllImport("nvml.dll")] static extern int nvmlDeviceGetFanSpeed(IntPtr dev, out uint pct);
        [DllImport("nvml.dll")] static extern int nvmlDeviceGetUtilizationRates(IntPtr dev, out Util u);

        static IntPtr dev;
        public static bool Ok;

        public static void Init()
        {
            try { Ok = nvmlInit_v2() == 0 && nvmlDeviceGetHandleByIndex_v2(0, out dev) == 0; }
            catch { Ok = false; }
        }

        public static bool Refresh()
        {
            if (!Ok) return false;
            try
            {
                uint v; Util u;
                if (nvmlDeviceGetPowerUsage(dev, out v) == 0) GpuSensors.Power = v / 1000.0;
                if (nvmlDeviceGetTemperature(dev, 0, out v) == 0) GpuSensors.TempEdge = v;
                if (nvmlDeviceGetClockInfo(dev, 0, out v) == 0) GpuSensors.Clk = v;
                if (nvmlDeviceGetClockInfo(dev, 2, out v) == 0) GpuSensors.MemClk = v;
                if (nvmlDeviceGetFanSpeed(dev, out v) == 0) GpuSensors.FanPct = v;
                if (nvmlDeviceGetUtilizationRates(dev, out u) == 0) { GpuSensors.Act = u.Gpu; GpuSensors.MemAct = u.Memory; }
                return true;
            }
            catch { return false; }
        }
    }

    // ---------------------------------------------------------------- Sensori GPU (AMD o NVIDIA, NaN se non disponibili)
    static class GpuSensors
    {
        public static string Vendor = "";
        public static bool Ok { get { return Vendor != ""; } }
        public static double Power, Clk, MemClk, Vddc, TempEdge, TempHot, TempMem, FanRpm, FanPct, Act, MemAct;

        public static void Init()
        {
            Adl.Init();
            if (Adl.Ok) { Vendor = "AMD"; return; }
            Nvml.Init();
            if (Nvml.Ok) Vendor = "NVIDIA";
        }

        static double Nz(int v) { return v < 0 ? double.NaN : v; }

        public static bool Refresh()
        {
            Power = Clk = MemClk = Vddc = TempEdge = TempHot = TempMem = FanRpm = FanPct = Act = MemAct = double.NaN;
            if (Vendor == "AMD")
            {
                if (!Adl.Refresh()) return false;
                int pw = Adl.Get(73); if (pw < 0) pw = Adl.Get(23);
                Power = Nz(pw); Clk = Nz(Adl.Get(1)); MemClk = Nz(Adl.Get(2)); Vddc = Nz(Adl.Get(21));
                TempEdge = Nz(Adl.Get(8)); TempMem = Nz(Adl.Get(9)); TempHot = Nz(Adl.Get(27));
                FanRpm = Nz(Adl.Get(14)); FanPct = Nz(Adl.Get(15)); Act = Nz(Adl.Get(19)); MemAct = Nz(Adl.Get(20));
                return true;
            }
            if (Vendor == "NVIDIA") return Nvml.Refresh();
            return false;
        }
    }

    static class Native
    {
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("psapi.dll")] public static extern bool EmptyWorkingSet(IntPtr h);
    }

    // ---------------------------------------------------------------- Collector
    public class Collector
    {
        public string LogDir;
        public string EventLogPath { get { return Path.Combine(LogDir, "eventi.log"); } }
        public string CurrentCsv = "";
        public string GpuVendor { get { return GpuSensors.Vendor; } }
        public double EverMaxPower = double.NaN;   // massima potenza GPU mai vista su questo PC (per soglie relative)
        public CrashInfo LastCrash;
        public volatile bool CrashAnalysisDone, HardwareReady;
        public double SessionMaxPower = double.NaN, SessionMaxHot = double.NaN, SessionMaxEdge = double.NaN;
        public DateTime StartedAt = DateTime.Now;

        public ConcurrentQueue<EventLine> NewEvents = new ConcurrentQueue<EventLine>();
        readonly List<Sample> history = new List<Sample>();
        readonly object hlock = new object();
        volatile bool running;
        volatile string stopReason = "BlackBox chiuso";
        public volatile bool WindowHidden = true;   // la finestra principale e' chiusa: si puo' liberare memoria
        DateTime nextTrim = DateTime.Now.AddMinutes(2);

        // restituisce a Windows la memoria non usata (utile prima di una partita e con la finestra chiusa)
        public static void TrimMemory()
        {
            try
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                using (var me = Process.GetCurrentProcess()) Native.EmptyWorkingSet(me.Handle);
            }
            catch { }
        }
        Thread thread;

        IntPtr q, cCpuUtil, cCpuCores, cCpuPerf, cCpuFreq, cRamCommit, cRamFree, cGpuEng, cVram, cThermal;
        StreamWriter csv; DateTime csvDay;
        readonly object evLock = new object();
        string pendingNote = "";

        public const string Header = "timestamp;cpu_util;cpu_core_max;cpu_perf_pct;cpu_mhz;ram_commit_pct;ram_free_mb;gpu_3d_pct;gpu_act_pct;gpu_mem_act_pct;gpu_power_w;gpu_power_peak_w;gpu_clk_mhz;gpu_clk_peak_mhz;gpu_mem_clk_mhz;gpu_vddc_mv;gpu_temp_edge;gpu_temp_hotspot;gpu_temp_mem;gpu_fan_rpm;gpu_fan_pct;vram_mb;acpi_temp;app_primo_piano;top_cpu_proc;top_cpu_pct;top_gpu_proc;top_gpu_pct;note;fps;fps_1low;frametime_ms;cpu_tctl;optiscaler;fps_reali;frame_gen";

        public GameTracker Games;

        public Collector(string dir)
        {
            LogDir = dir;
            Directory.CreateDirectory(dir);
            Settings.Load(dir);
            Fps.Init(dir);
            Opti.Init(dir);
            Games = new GameTracker(this);
        }

        public void Start()
        {
            running = true;
            try
            {
                Microsoft.Win32.SystemEvents.PowerModeChanged += (s, e) =>
                {
                    if (e.Mode == Microsoft.Win32.PowerModes.Suspend) AddEvent("INFO", "Windows entra in sospensione");
                    else if (e.Mode == Microsoft.Win32.PowerModes.Resume) AddEvent("INFO", "Windows riprende dalla sospensione");
                };
                Microsoft.Win32.SystemEvents.SessionEnding += (s, e) =>
                    AddEvent("INFO", "Arresto/disconnessione regolare di Windows (" + e.Reason + ")");
            }
            catch { }
            thread = new Thread(Run) { IsBackground = true, Name = "BlackBoxRecorder" };
            thread.Start();
        }

        public void Stop(string reason)
        {
            AddEvent("INFO", reason);
            stopReason = reason;
            running = false;
            try { if (thread != null) thread.Join(2000); } catch { }
            lock (evLock) { try { if (csv != null) csv.Dispose(); } catch { } csv = null; }
        }

        public Sample[] GetHistory(int n)
        {
            lock (hlock)
            {
                int k = Math.Min(n, history.Count);
                return history.GetRange(history.Count - k, k).ToArray();
            }
        }

        public Sample GetLatest()
        {
            lock (hlock) { return history.Count > 0 ? history[history.Count - 1] : null; }
        }

        public void AddEvent(string level, string text)
        {
            var ev = new EventLine { Time = DateTime.Now, Level = level, Text = text };
            AddEventLine(ev);
        }

        void AddEventLine(EventLine ev)
        {
            NewEvents.Enqueue(ev);
            lock (evLock)
            {
                try
                {
                    using (var fs = new FileStream(EventLogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, FileOptions.WriteThrough))
                    using (var w = new StreamWriter(fs, new UTF8Encoding(false)))
                        w.WriteLine(ev.Time.ToString("yyyy-MM-dd HH:mm:ss") + " [" + ev.Level + "] " + ev.Text);
                }
                catch { }
                if (ev.Level != "INFO" || ev.Text.StartsWith("Windows")) pendingNote = (pendingNote + " | " + ev.Text).Trim(' ', '|');
            }
        }

        // ------------------------------------------------ main loop
        void Run()
        {
            try { LastCrash = Analyzer.AnalyzeLatest(LogDir); } catch (Exception ex) { AddEvent("WARN", "Analisi crash fallita: " + ex.Message); }
            CrashAnalysisDone = true;
            if (LastCrash != null && LastCrash.IsNew) Games.MarkTuningCheck(LastCrash.BootTime, "lo spegnimento anomalo");
            try { Analyzer.Cleanup(LogDir, 14); } catch { }

            InitPdh();
            EverMaxPower = Analyzer.ReadMaxPower(LogDir);
            GpuSensors.Init();
            CpuTemp.Start();
            try
            {
                Hardware.Detect();
                if (Hardware.ResetMaxPower) { EverMaxPower = double.NaN; try { File.Delete(Analyzer.MaxPowerFile(LogDir)); } catch { } }
                foreach (var ch in Hardware.Changes) AddEvent("WARN", ch);
                if (Hardware.DriverChanged) Games.MarkTuningCheck(DateTime.Now, "l'aggiornamento del driver video");
                AddEvent("INFO", (Hardware.FirstRun ? "Primo avvio, PC riconosciuto: " : "PC: ") + Hardware.Cpu + " · " + Hardware.Gpu + " · driver " + Hardware.GpuDriver);
            }
            catch (Exception ex) { AddEvent("WARN", "Riconoscimento del PC non riuscito: " + ex.Message); }
            HardwareReady = true;
            AddEvent("INFO", "BlackBox avviato" + (GpuSensors.Ok ? " (sensori GPU " + GpuSensors.Vendor + " attivi)" : " (sensori GPU non disponibili: supportate schede AMD e NVIDIA)"));
            pendingNote = "avvio monitor";

            DateTime nextSec = DateTime.Now.AddSeconds(1), lastRow = DateTime.Now, nextEvPoll = DateTime.Now, nextProc = DateTime.Now;
            DateTime evSince = DateTime.Now.AddMinutes(-2);
            double pSum = 0, pMax = double.NaN, clkMax = double.NaN; int pN = 0;
            var topCpu = new KeyValuePair<string, double>("", double.NaN);

            while (running)
            {
                DateTime now = DateTime.Now;
                // sensori GPU veloci (5 Hz) per catturare i picchi
                if (GpuSensors.Refresh())
                {
                    double pw = GpuSensors.Power, ck = GpuSensors.Clk;
                    if (!double.IsNaN(pw)) { pSum += pw; pN++; if (double.IsNaN(pMax) || pw > pMax) pMax = pw; }
                    if (!double.IsNaN(ck) && (double.IsNaN(clkMax) || ck > clkMax)) clkMax = ck;
                }

                // in partita BlackBox si alleggerisce: elenco dei processi ogni 5 s e Registro eventi ogni 30 s
                bool gaming = Games.InSession;
                if (now >= nextProc) { topCpu = TopCpuProcess(); nextProc = now.AddSeconds(gaming ? 5 : 2); }
                if (now >= nextEvPoll) { PollEvents(ref evSince); nextEvPoll = now.AddSeconds(gaming ? 30 : 10); }
                if (now >= nextTrim) { if (WindowHidden) TrimMemory(); nextTrim = now.AddMinutes(10); }

                if (now >= nextSec)
                {
                    double gap = (now - lastRow).TotalSeconds;
                    if (gap > 6) AddEvent("WARN", "Buco di " + Math.Round(gap) + " s nei dati (sospensione/ibernazione o PC bloccato)");
                    var s = BuildSample(now, pN > 0 ? pSum / pN : double.NaN, pMax, clkMax, topCpu);
                    pSum = 0; pN = 0; pMax = double.NaN; clkMax = double.NaN;
                    lock (hlock) { history.Add(s); if (history.Count > 900) history.RemoveRange(0, history.Count - 900); }
                    WriteRow(s);
                    try { Games.OnSample(s); } catch (Exception ex) { AddEvent("WARN", "Errore nelle sessioni di gioco: " + ex.Message); }
                    lastRow = now;
                    nextSec = nextSec.AddSeconds(1);
                    if (nextSec < now) nextSec = now.AddSeconds(1);
                }
                Thread.Sleep(200);
            }
            try { Games.Finish(stopReason); } catch { }
        }

        void InitPdh()
        {
            if (Pdh.PdhOpenQueryW(null, IntPtr.Zero, out q) != 0) { q = IntPtr.Zero; return; }
            cCpuUtil = Add(@"\Processor Information(_Total)\% Processor Utility");
            cCpuCores = Add(@"\Processor Information(*)\% Processor Utility");
            cCpuPerf = Add(@"\Processor Information(_Total)\% Processor Performance");
            cCpuFreq = Add(@"\Processor Information(_Total)\Processor Frequency");
            cRamCommit = Add(@"\Memory\% Committed Bytes In Use");
            cRamFree = Add(@"\Memory\Available MBytes");
            cGpuEng = Add(@"\GPU Engine(*)\Utilization Percentage");
            cVram = Add(@"\GPU Adapter Memory(*)\Dedicated Usage");
            cThermal = Add(@"\Thermal Zone Information(*)\Temperature");
            Pdh.PdhCollectQueryData(q);
        }

        IntPtr Add(string path)
        {
            IntPtr c;
            return Pdh.PdhAddEnglishCounterW(q, path, IntPtr.Zero, out c) == 0 ? c : IntPtr.Zero;
        }

        static double First(Dictionary<string, double> d)
        {
            foreach (var v in d.Values) return v;
            return double.NaN;
        }

        readonly Dictionary<int, string> pidNames = new Dictionary<int, string>();
        string NameOf(int pid)
        {
            string n;
            if (pidNames.TryGetValue(pid, out n)) return n;
            try { using (var p = Process.GetProcessById(pid)) n = p.ProcessName; } catch { n = "pid " + pid; }
            if (pidNames.Count > 2000) pidNames.Clear();
            pidNames[pid] = n;
            return n;
        }

        Sample BuildSample(DateTime now, double pAvg, double pMax, double clkMax, KeyValuePair<string, double> topCpu)
        {
            var s = new Sample { Time = now };
            uint fgPid = 0;
            try { Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out fgPid); } catch { }
            s.ForegroundPid = (int)fgPid;
            if (q != IntPtr.Zero)
            {
                Pdh.PdhCollectQueryData(q);
                s.CpuUtil = Math.Min(100, First(Pdh.ReadArray(cCpuUtil)));
                double coreMax = double.NaN;
                foreach (var kv in Pdh.ReadArray(cCpuCores))
                    if (!kv.Key.Contains("_Total") && (double.IsNaN(coreMax) || kv.Value > coreMax)) coreMax = kv.Value;
                s.CpuCoreMax = Math.Min(100, coreMax);
                s.CpuPerf = First(Pdh.ReadArray(cCpuPerf));
                double baseF = First(Pdh.ReadArray(cCpuFreq));
                s.CpuMhz = baseF * s.CpuPerf / 100.0;
                s.RamCommit = First(Pdh.ReadArray(cRamCommit));
                s.RamFreeMb = First(Pdh.ReadArray(cRamFree));

                // GPU: 3D per adattatore (max) + processo che usa di piu' la GPU
                var eng = Pdh.ReadArray(cGpuEng);
                var perLuid = new Dictionary<string, double>();
                var perPidType = new Dictionary<string, double>();
                var rx = new Regex(@"pid_(\d+)_luid_(0x[0-9A-Fa-f]+_0x[0-9A-Fa-f]+).*engtype_(.+)$");
                foreach (var kv in eng)
                {
                    var m = rx.Match(kv.Key);
                    if (!m.Success) continue;
                    string pid = m.Groups[1].Value, luid = m.Groups[2].Value, type = m.Groups[3].Value;
                    if (type == "3D") { double o; perLuid.TryGetValue(luid, out o); perLuid[luid] = o + kv.Value; }
                    if (pid == "0") continue;
                    string key = pid + "|" + type;
                    double p0; perPidType.TryGetValue(key, out p0); perPidType[key] = p0 + kv.Value;
                }
                s.Gpu3d = perLuid.Count > 0 ? Math.Min(100, perLuid.Values.Max()) : double.NaN;
                if (eng.Count > 0) { double fg3; s.Fg3d = perPidType.TryGetValue(fgPid + "|3D", out fg3) ? Math.Min(100, fg3) : 0; }
                if (perPidType.Count > 0)
                {
                    var best = perPidType.OrderByDescending(x => x.Value).First();
                    if (best.Value >= 0.5)
                    {
                        s.TopGpuProc = NameOf(int.Parse(best.Key.Split('|')[0])) + " (" + best.Key.Split('|')[1] + ")";
                        s.TopGpuPct = Math.Min(100, best.Value);
                    }
                }
                var vr = Pdh.ReadArray(cVram);
                s.VramMb = vr.Count > 0 ? vr.Values.Max() / 1048576.0 : double.NaN;
                var th = Pdh.ReadArray(cThermal);
                if (th.Count > 0) { double t = th.Values.Max(); if (t > 200) s.ThermalC = t - 273.15; }
            }

            if (GpuSensors.Ok)
            {
                s.GpuPower = pAvg; s.GpuPowerMax = pMax; s.GpuClkMax = clkMax;
                s.GpuClk = GpuSensors.Clk; s.GpuMemClk = GpuSensors.MemClk;
                s.GpuTempEdge = GpuSensors.TempEdge; s.GpuTempMem = GpuSensors.TempMem; s.GpuTempHot = GpuSensors.TempHot;
                s.GpuFanRpm = GpuSensors.FanRpm; s.GpuFanPct = GpuSensors.FanPct;
                s.GpuAct = GpuSensors.Act; s.GpuMemAct = GpuSensors.MemAct; s.GpuVddc = GpuSensors.Vddc;
                SessionMaxPower = MaxN(SessionMaxPower, pMax);
                if (!double.IsNaN(pMax) && (double.IsNaN(EverMaxPower) || pMax > EverMaxPower + 0.5))
                {
                    EverMaxPower = pMax;
                    try { File.WriteAllText(Analyzer.MaxPowerFile(LogDir), EverMaxPower.ToString("0", CultureInfo.InvariantCulture)); } catch { }
                }
                SessionMaxHot = MaxN(SessionMaxHot, s.GpuTempHot);
                SessionMaxEdge = MaxN(SessionMaxEdge, s.GpuTempEdge);
            }

            s.TopCpuProc = topCpu.Key; s.TopCpuPct = topCpu.Value;
            s.CpuTctl = CpuTemp.Value;
            var gs = Games.Current; if (gs != null) s.OptiScaler = gs.OptiStatus;
            Fps.Current(out s.Fps, out s.Fps1Low, out s.FrameTimeMs, out s.FpsReal);
            s.FgFactor = double.IsNaN(s.Fps) ? 1 : Fps.FgFactor;
            try { s.Foreground = fgPid != 0 ? NameOf((int)fgPid) : ""; } catch { }

            lock (evLock) { s.Note = pendingNote; pendingNote = ""; }
            return s;
        }

        static double Nz(int v) { return v < 0 ? double.NaN : v; }
        static double MaxN(double a, double b) { if (double.IsNaN(b)) return a; return double.IsNaN(a) || b > a ? b : a; }

        readonly Dictionary<int, TimeSpan> prevCpu = new Dictionary<int, TimeSpan>();
        DateTime prevCpuAt = DateTime.MinValue;
        KeyValuePair<string, double> TopCpuProcess()
        {
            var now = DateTime.Now;
            double dt = (now - prevCpuAt).TotalMilliseconds;
            string bestName = ""; double best = 0;
            var cur = new Dictionary<int, TimeSpan>();
            uint fgPid = 0;
            try { Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out fgPid); } catch { }
            var game = Games.Current;
            int gamePid = game != null ? game.Pid : 0;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == 0) continue;
                    var t = p.TotalProcessorTime;
                    cur[p.Id] = t;
                    TimeSpan old;
                    if (prevCpuAt != DateTime.MinValue && prevCpu.TryGetValue(p.Id, out old))
                    {
                        double pct = (t - old).TotalMilliseconds / dt / Environment.ProcessorCount * 100.0;
                        if (pct > best) { best = pct; bestName = p.ProcessName; }
                        double core = pct * Environment.ProcessorCount;   // % di UN core
                        if (p.Id == fgPid) lastFgAt[p.Id] = now;
                        if (Stuckable.Contains(p.ProcessName)) CheckStuck(p, core);
                        else if (p.Id != fgPid && p.Id != gamePid) CheckHog(p, core);
                    }
                }
                catch { }
                finally { p.Dispose(); }
            }
            prevCpu.Clear(); foreach (var kv in cur) prevCpu[kv.Key] = kv.Value;
            foreach (var pid in stuckHist.Keys.Where(k => !cur.ContainsKey(k)).ToList()) stuckHist.Remove(pid);
            foreach (var pid in hogs.Keys.Where(k => !cur.ContainsKey(k)).ToList()) hogs.Remove(pid);
            foreach (var pid in lastFgAt.Keys.Where(k => !cur.ContainsKey(k)).ToList()) lastFgAt.Remove(pid);
            stuckFailed.RemoveWhere(k => !cur.ContainsKey(k));
            var hl = hogs.Values.Where(x => (DateTime.Now - x.Since).TotalMinutes >= 10).OrderByDescending(x => x.Sum / x.N).ToList();
            BackgroundHogs = string.Join(", ", hl.Take(3).Select(x => x.Name + " (" + Math.Round(x.Sum / x.N / 100.0, 1).ToString(CultureInfo.InvariantCulture) + " core da " + (int)(DateTime.Now - x.Since).TotalMinutes + " min)"));
            prevCpuAt = now;
            return new KeyValuePair<string, double>(bestName, bestName == "" ? double.NaN : Math.Min(100, best));
        }

        // ------------------------------------------------ Sblocco automatico
        // Processi di Windows che a volte restano bloccati in un ciclo e scaldano il PC senza motivo.
        // Si possono chiudere senza danni: Windows li riapre da solo quando servono.
        //   TextInputHost            = tastiera virtuale, pannello emoji (Win+.), cronologia appunti (Win+V)
        //   SearchHost               = pannello Cerca
        //   StartMenuExperienceHost  = menu Start
        //   ShellExperienceHost      = centro notifiche, calendario, impostazioni rapide
        //   CrossDeviceResume        = "riprendi dal telefono" (bug noto di Windows 11)
        //   Widgets / WidgetService  = pannello Widget
        //   PhoneExperienceHost      = Collegamento al telefono
        //   LockApp                  = schermata di blocco
        static readonly HashSet<string> Stuckable = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "TextInputHost", "SearchHost", "StartMenuExperienceHost", "ShellExperienceHost",
            "CrossDeviceResume", "Widgets", "WidgetService", "PhoneExperienceHost", "LockApp" };
        // Regole di prudenza: si chiude SOLO un vero blocco, mai un lavoro normale e mai mentre giochi.
        //  1) oltre meta' core in almeno il 90% dei controlli degli ultimi 60 s (controllo ogni 2 s);
        //  2) consumo PIATTO (un ciclo bloccato consuma sempre uguale; un lavoro vero va a scatti);
        //  3) non in primo piano da almeno 60 s (se lo stai usando, es. menu Start aperto, non si tocca);
        //  4) mai durante una sessione di gioco: si rimanda alla fine della partita;
        //  5) al massimo 3 chiusure all'ora per processo: se si riblocca sempre, avvisa e smette.
        const double StuckCorePct = 50;     // % di UN core
        const int StuckChecks = 30;         // 30 controlli ogni 2 s = 60 s
        readonly Dictionary<int, List<double>> stuckHist = new Dictionary<int, List<double>>();
        readonly Dictionary<int, DateTime> lastFgAt = new Dictionary<int, DateTime>();
        readonly Dictionary<string, List<DateTime>> kills = new Dictionary<string, List<DateTime>>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, DateTime> stuckNotified = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<int> stuckFailed = new HashSet<int>();   // non chiudibili: non riprovare

        static bool Flat(List<double> v)
        {
            double m = v.Average();
            if (m <= 0) return false;
            double sd = Math.Sqrt(v.Sum(x => (x - m) * (x - m)) / v.Count);
            return sd / m < 0.3;
        }

        void NotifyOnce(string name, string level, string text, int minutes)
        {
            DateTime last;
            if (stuckNotified.TryGetValue(name + level, out last) && (DateTime.Now - last).TotalMinutes < minutes) return;
            stuckNotified[name + level] = DateTime.Now;
            AddEvent(level, text);
        }

        void CheckStuck(Process p, double corePct)
        {
            if (stuckFailed.Contains(p.Id)) return;
            List<double> h;
            if (!stuckHist.TryGetValue(p.Id, out h)) stuckHist[p.Id] = h = new List<double>();
            h.Add(corePct);
            if (h.Count > StuckChecks) h.RemoveAt(0);
            if (h.Count < StuckChecks) return;
            if (h.Count(x => x >= StuckCorePct) < StuckChecks * 0.9 || !Flat(h)) return;    // lavoro vero o picco passeggero
            DateTime fg;
            if (lastFgAt.TryGetValue(p.Id, out fg) && (DateTime.Now - fg).TotalSeconds < 60) return;   // lo stai usando
            string name = p.ProcessName; double avg = h.Average();
            if (Games.InSession)
            {
                NotifyOnce(name, "INFO", name + " sembra bloccato (" + Math.Round(avg) + "% di un core, costante): non lo tocco mentre giochi, ricontrollo a fine partita.", 30);
                return;
            }
            List<DateTime> k;
            if (!kills.TryGetValue(name, out k)) kills[name] = k = new List<DateTime>();
            k.RemoveAll(x => (DateTime.Now - x).TotalMinutes > 60);
            if (k.Count >= 3)
            {
                NotifyOnce(name, "WARN", name + " si riblocca di continuo (3 volte nell'ultima ora): non lo chiudo più. Probabile problema di Windows: riavvia il PC e installa gli aggiornamenti.", 60);
                return;
            }
            h.Clear();
            try
            {
                p.Kill();
                k.Add(DateTime.Now);
                AddEvent("WARN", "Sbloccato " + name + ": era bloccato al " + Math.Round(avg) + "% di un core, costante, da oltre 60 s e non lo stavi usando. Chiuso, Windows lo riapre da solo.");
            }
            catch (Exception ex) { stuckFailed.Add(p.Id); AddEvent("WARN", name + " bloccato al " + Math.Round(avg) + "% ma non chiudibile: " + ex.Message); }
        }

        // ------------------------------------------------ Programmi che consumano tanto in background (SOLO segnalati, mai chiusi)
        // almeno un core intero, costante, per 10 minuti, non in primo piano e non il gioco
        public class Hog { public string Name; public DateTime Since; public double Sum; public int N, High; }
        readonly Dictionary<int, Hog> hogs = new Dictionary<int, Hog>();
        public volatile string BackgroundHogs = "";

        void CheckHog(Process p, double corePct)
        {
            Hog g;
            if (corePct < 90) { hogs.Remove(p.Id); return; }
            if (!hogs.TryGetValue(p.Id, out g)) hogs[p.Id] = g = new Hog { Name = p.ProcessName, Since = DateTime.Now };
            g.Sum += corePct; g.N++; g.High++;

        }
        // ------------------------------------------------ CSV (scrittura diretta su disco)
        static string F(double v, int dec)
        {
            return double.IsNaN(v) ? "" : Math.Round(v, dec).ToString(CultureInfo.InvariantCulture);
        }
        static string S(string s) { return (s ?? "").Replace(";", ",").Replace("\r", " ").Replace("\n", " "); }

        void WriteRow(Sample s)
        {
            lock (evLock)
            {
                try
                {
                    if (csv == null || s.Time.Date != csvDay)
                    {
                        if (csv != null) csv.Dispose();
                        csvDay = s.Time.Date;
                        CurrentCsv = Path.Combine(LogDir, "monitor_" + csvDay.ToString("yyyy-MM-dd") + ".csv");
                        bool fresh = !File.Exists(CurrentCsv) || new FileInfo(CurrentCsv).Length == 0;
                        // file di oggi creato da una versione precedente: riscrivo l'intestazione con le colonne nuove
                        // (l'analisi dei crash usa sempre l'ultima intestazione che trova)
                        bool oldHeader = !fresh && LastHeader(CurrentCsv) != Header;
                        var fs = new FileStream(CurrentCsv, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.WriteThrough);
                        csv = new StreamWriter(fs, new UTF8Encoding(false)) { AutoFlush = true };
                        if (fresh || oldHeader) csv.WriteLine(Header);
                    }
                    csv.WriteLine(string.Join(";", new string[] {
                        s.Time.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                        F(s.CpuUtil,1), F(s.CpuCoreMax,1), F(s.CpuPerf,1), F(s.CpuMhz,0), F(s.RamCommit,1), F(s.RamFreeMb,0),
                        F(s.Gpu3d,1), F(s.GpuAct,0), F(s.GpuMemAct,0), F(s.GpuPower,1), F(s.GpuPowerMax,0),
                        F(s.GpuClk,0), F(s.GpuClkMax,0), F(s.GpuMemClk,0), F(s.GpuVddc,0),
                        F(s.GpuTempEdge,0), F(s.GpuTempHot,0), F(s.GpuTempMem,0), F(s.GpuFanRpm,0), F(s.GpuFanPct,0),
                        F(s.VramMb,0), F(s.ThermalC,1), S(s.Foreground), S(s.TopCpuProc), F(s.TopCpuPct,1), S(s.TopGpuProc), F(s.TopGpuPct,1), S(s.Note),
                        F(s.Fps,1), F(s.Fps1Low,1), F(s.FrameTimeMs,2), F(s.CpuTctl,1), S(s.OptiScaler), F(s.FpsReal,1), double.IsNaN(s.Fps) ? "" : s.FgFactor.ToString() }));
                }
                catch { }
            }
        }

        static string LastHeader(string path)
        {
            string h = null;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null) if (line.StartsWith("timestamp")) h = line;
                }
            }
            catch { }
            return h;
        }

        // ------------------------------------------------ Registro eventi di Windows
        readonly HashSet<long> seen = new HashSet<long>();
        void PollEvents(ref DateTime since)
        {
            DateTime pollStart = DateTime.Now;
            string t = since.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            string sysQ = "*[System[TimeCreated[@SystemTime>='" + t + "'] and (" +
                "Provider[@Name='Microsoft-Windows-WHEA-Logger'] or " +
                "(Provider[@Name='Microsoft-Windows-Kernel-Power'] and (EventID=41 or EventID=42 or EventID=107 or EventID=109 or EventID=506 or EventID=507)) or " +
                "(Provider[@Name='Display'] and EventID=4101) or " +
                "(Provider[@Name='Microsoft-Windows-Kernel-Processor-Power'] and EventID=37) or " +
                "EventID=6008 or EventID=1001)]]";
            string appQ = "*[System[TimeCreated[@SystemTime>='" + t + "'] and (Provider[@Name='Application Error'] or Provider[@Name='Application Hang'])]]";
            ReadLog("System", sysQ, false);
            ReadLog("Application", appQ, true);
            since = pollStart.AddSeconds(-15);
        }

        void ReadLog(string log, string xpath, bool isApp)
        {
            try
            {
                using (var r = new EventLogReader(new EventLogQuery(log, PathType.LogName, xpath)))
                {
                    for (EventRecord e = r.ReadEvent(); e != null; e = r.ReadEvent())
                    {
                        using (e)
                        {
                            long id = (e.RecordId ?? 0) * 2 + (isApp ? 1 : 0);
                            if (!seen.Add(id)) continue;
                            string lvl = "INFO";
                            string prov = e.ProviderName ?? "";
                            if (prov.Contains("WHEA") || e.Id == 41 || e.Id == 6008 || e.Id == 1001) lvl = "ERR";
                            else if (isApp || e.Id == 4101 || e.Id == 37) lvl = "WARN";
                            string desc = "";
                            try { desc = e.FormatDescription() ?? ""; } catch { }
                            DateTime when = e.TimeCreated ?? DateTime.Now;
                            try
                            {
                                if (prov == "Display" && e.Id == 4101) Games.OnTdr(when);
                                if (isApp) Games.OnAppError(desc, when);
                            }
                            catch { }
                            desc = desc.Split('\n')[0].Trim();
                            if (desc.Length > 180) desc = desc.Substring(0, 180) + "...";
                            string label = prov.Replace("Microsoft-Windows-", "") + " " + e.Id;
                            AddEventLine(new EventLine { Time = e.TimeCreated ?? DateTime.Now, Level = lvl, Text = "[Windows " + label + "] " + desc });
                        }
                    }
                }
            }
            catch { }
        }
    }

    // ---------------------------------------------------------------- Analisi dei crash
    public static class Analyzer
    {
        static Dictionary<string, string> Data(EventRecord e)
        {
            var d = new Dictionary<string, string>();
            foreach (Match m in Regex.Matches(e.ToXml(), "<Data Name=['\"](\\w+)['\"]>([^<]*)</Data>"))
                d[m.Groups[1].Value] = m.Groups[2].Value;
            return d;
        }
        static long L(Dictionary<string, string> d, string k)
        {
            string v; if (!d.TryGetValue(k, out v)) return 0;
            long r; if (v.StartsWith("0x")) return long.TryParse(v.Substring(2), NumberStyles.HexNumber, null, out r) ? r : 0;
            return long.TryParse(v, out r) ? r : 0;
        }

        public static string SleepText(int s)
        {
            switch (s)
            {
                case 0: return "PC acceso e in uso";
                case 2: case 3: case 4: return "durante sospensione/ripresa";
                case 5: return "durante ibernazione/ripresa";
                case 6: return "durante lo spegnimento";
                default: return "stato " + s;
            }
        }

        public static List<CrashInfo> History(int days, string logDir)
        {
            var list = new List<CrashInfo>();
            string since = DateTime.UtcNow.AddDays(-days).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            var q = new EventLogQuery("System", PathType.LogName,
                "*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=41 and TimeCreated[@SystemTime>='" + since + "']]]");
            q.ReverseDirection = true;
            try
            {
                using (var r = new EventLogReader(q))
                    for (EventRecord e = r.ReadEvent(); e != null; e = r.ReadEvent())
                        using (e)
                        {
                            var d = Data(e);
                            var ci = new CrashInfo
                            {
                                BootTime = e.TimeCreated ?? DateTime.MinValue,
                                RecordId = e.RecordId ?? 0,
                                Bugcheck = (int)L(d, "BugcheckCode"),
                                SleepState = (int)L(d, "SleepInProgress"),
                                PowerButton = L(d, "PowerButtonTimestamp")
                            };
                            string rp = Path.Combine(logDir, "crash_" + ci.BootTime.ToString("yyyyMMdd_HHmmss") + ".txt");
                            if (File.Exists(rp)) { ci.ReportPath = rp; ci.Covered = true; ci.Verdict = ReadVerdict(rp); }
                            ci.Config = ConfigAt(logDir, ci.BootTime);
                            list.Add(ci);
                        }
            }
            catch { }
            return list;
        }

        public static void Cleanup(string dir, int days)
        {
            foreach (var f in Directory.GetFiles(dir, "monitor_*.csv"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-days)) File.Delete(f);
        }

        // Analizza l'ultimo Kernel-Power 41: se e' nuovo, crea il report con gli ultimi dati registrati
        public static CrashInfo AnalyzeLatest(string dir)
        {
            var hist = History(60, dir);
            if (hist.Count == 0) return null;
            var ci = hist[0];
            string stateFile = Path.Combine(dir, "ultimo_crash_analizzato.txt");
            string prev = File.Exists(stateFile) ? File.ReadAllText(stateFile).Trim() : "";
            string reportPath = Path.Combine(dir, "crash_" + ci.BootTime.ToString("yyyyMMdd_HHmmss") + ".txt");
            if (prev == ci.RecordId.ToString())
            {
                ci.IsNew = false;
                if (File.Exists(reportPath)) { ci.ReportPath = reportPath; ci.Verdict = ReadVerdict(reportPath); ci.Summary = "Diagnosi: " + ci.Verdict; }
                return ci;
            }
            ci.IsNew = true;
            File.WriteAllText(stateFile, ci.RecordId.ToString());

            // raccoglie le righe del CSV precedenti al riavvio (giorno del crash + giorno prima)
            var rows = new List<string>();
            string header = null;
            for (int back = 1; back >= 0; back--)
            {
                string f = Path.Combine(dir, "monitor_" + ci.BootTime.Date.AddDays(-back).ToString("yyyy-MM-dd") + ".csv");
                if (!File.Exists(f)) continue;
                using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (line.StartsWith("timestamp")) { header = line; continue; }
                        DateTime ts;
                        if (line.Length < 19 || !DateTime.TryParseExact(line.Substring(0, 19), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out ts)) continue;
                        if (ts >= ci.BootTime) break;
                        rows.Add(line);
                        if (rows.Count > 600) rows.RemoveRange(0, rows.Count - 300);
                    }
                }
            }
            if (rows.Count > 300) rows = rows.GetRange(rows.Count - 300, 300);
            if (header == null) header = Collector.Header;

            var sb = new StringBuilder();
            sb.AppendLine("==============================================================");
            sb.AppendLine("  BLACKBOX - REPORT SPEGNIMENTO ANOMALO");
            sb.AppendLine("==============================================================");
            sb.AppendLine("Riavvio rilevato alle:   " + ci.BootTime.ToString("dd/MM/yyyy HH:mm:ss") + "  (Kernel-Power 41)");
            sb.AppendLine("BugcheckCode:            " + ci.Bugcheck + (ci.Bugcheck == 0 ? "  -> nessuna schermata blu: perdita di alimentazione o reset hardware" : "  -> schermata blu (BSOD) 0x" + ci.Bugcheck.ToString("X")));
            sb.AppendLine("Stato al momento:        " + SleepText(ci.SleepState) + " (SleepInProgress=" + ci.SleepState + ")");
            sb.AppendLine("Tasto accensione:        " + (ci.PowerButton != 0 ? "PREMUTO (spegnimento forzato manuale)" : "non premuto"));
            sb.AppendLine("Configurazione attiva:   " + (ci.Config == "" ? "(non indicata)" : ci.Config));

            var cols = header.Split(';');
            if (rows.Count == 0)
            {
                sb.AppendLine();
                sb.AppendLine("BlackBox NON era in esecuzione prima di questo spegnimento: nessun dato registrato.");
                ci.Summary = "Spegnimento anomalo alle " + ci.BootTime.ToString("dd/MM HH:mm") + " - BlackBox non era attivo, nessun dato.";
            }
            else
            {
                var last = rows[rows.Count - 1].Split(';');
                DateTime lastTs = DateTime.ParseExact(last[0].Substring(0, 19), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                ci.LastSample = lastTs;
                ci.Covered = (ci.BootTime - lastTs).TotalMinutes < 15;
                int iFg = Array.IndexOf(cols, "app_primo_piano");
                ci.LastForeground = iFg >= 0 && iFg < last.Length ? last[iFg] : "";
                sb.AppendLine("Ultimo dato registrato:  " + lastTs.ToString("dd/MM/yyyy HH:mm:ss") + "  (" + Math.Round((ci.BootTime - lastTs).TotalSeconds) + " s prima del riavvio)");
                if (!ci.Covered) sb.AppendLine("ATTENZIONE: l'ultimo dato e' molto precedente al riavvio: BlackBox probabilmente era chiuso o il PC era in sospensione.");
                sb.AppendLine("App in primo piano:      " + ci.LastForeground);
                sb.AppendLine();
                sb.AppendLine("--- Ultimi 60 secondi prima dello spegnimento (media / massimo) ---");
                var recent = rows.Where(r0 => { DateTime t0; return DateTime.TryParseExact(r0.Substring(0, 19), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t0) && (lastTs - t0).TotalSeconds <= 60; }).Select(r0 => r0.Split(';')).ToList();
                string[][] stats = {
                    new[]{"cpu_util","CPU carico %"}, new[]{"cpu_core_max","CPU core piu' carico %"}, new[]{"cpu_mhz","CPU frequenza MHz"},
                    new[]{"gpu_3d_pct","GPU carico 3D %"}, new[]{"gpu_power_w","GPU potenza W"}, new[]{"gpu_power_peak_w","GPU picco potenza W"},
                    new[]{"gpu_clk_peak_mhz","GPU clock picco MHz"}, new[]{"gpu_vddc_mv","GPU tensione mV"},
                    new[]{"gpu_temp_edge","GPU temp °C"}, new[]{"gpu_temp_hotspot","GPU hotspot °C"}, new[]{"gpu_temp_mem","GPU temp memoria °C"},
                    new[]{"gpu_fan_rpm","GPU ventola rpm"}, new[]{"ram_commit_pct","RAM impegnata %"} };
                foreach (var st in stats)
                {
                    int ix = Array.IndexOf(cols, st[0]);
                    if (ix < 0) continue;
                    var vals = recent.Where(a => ix < a.Length && a[ix] != "").Select(a => double.Parse(a[ix], CultureInfo.InvariantCulture)).ToList();
                    if (vals.Count == 0) continue;
                    sb.AppendLine("  " + st[1].PadRight(26) + vals.Average().ToString("0.0", CultureInfo.InvariantCulture).PadLeft(8) + " / " + vals.Max().ToString("0", CultureInfo.InvariantCulture).PadLeft(6));
                }
                var verdict = Diagnose(ci, cols, rows, lastTs, ReadMaxPower(dir));
                ci.Verdict = verdict[0];
                sb.AppendLine();
                sb.AppendLine("--- DIAGNOSI AUTOMATICA ---");
                foreach (var v in verdict) sb.AppendLine("  " + v);

                int iNote = Array.IndexOf(cols, "note");
                var notes = rows.Select(a => a.Split(';')).Where(a => iNote >= 0 && iNote < a.Length && a[iNote] != "").Select(a => "  " + a[0] + "  " + a[iNote]).ToList();
                if (notes.Count > 0) { sb.AppendLine(); sb.AppendLine("--- Note/eventi negli ultimi minuti ---"); foreach (var n in notes) sb.AppendLine(n); }
                sb.AppendLine();
                sb.AppendLine("--- Ultimi " + rows.Count + " campioni (1 al secondo) ---");
                sb.AppendLine(header);
                foreach (var r0 in rows) sb.AppendLine(r0);

                ci.Summary = "Diagnosi: " + ci.Verdict + "  —  ultimo dato " + lastTs.ToString("dd/MM HH:mm:ss") + ", riavvio " + ci.BootTime.ToString("HH:mm:ss") +
                             " · " + (ci.Bugcheck == 0 ? "nessuna schermata blu" : "BSOD 0x" + ci.Bugcheck.ToString("X")) + " · " + SleepText(ci.SleepState) +
                             (ci.LastForeground != "" ? " · in primo piano: " + ci.LastForeground : "");
            }
            File.WriteAllText(reportPath, sb.ToString(), new UTF8Encoding(true));
            ci.ReportPath = reportPath;
            return ci;
        }

        public static string MaxPowerFile(string dir) { return Path.Combine(dir, "gpu_potenza_max.txt"); }

        public static double ReadMaxPower(string dir)
        {
            try { return double.Parse(File.ReadAllText(MaxPowerFile(dir)).Trim(), CultureInfo.InvariantCulture); }
            catch { return double.NaN; }
        }

        // Intervallo medio tra gli spegnimenti (giorni) negli ultimi 120 giorni, NaN se meno di 2
        public static double MeanCrashInterval(string dir)
        {
            var h = History(120, dir).Select(x => x.BootTime).OrderBy(t => t).ToList();
            if (h.Count < 2) return double.NaN;
            return (h[h.Count - 1] - h[0]).TotalDays / (h.Count - 1);
        }

        // Regole basate su cosa succedeva negli ultimi 10 secondi. Riga 0 = categoria sintetica.
        // Le soglie di potenza sono relative alla massima potenza GPU mai registrata su questo PC,
        // cosi' valgono per qualunque scheda video.
        public static List<string> Diagnose(CrashInfo ci, string[] cols, List<string> rows, DateTime lastTs, double everMaxPower)
        {
            var o = new List<string>();
            Func<string, int, double[]> col = (name, secs) =>
            {
                int ix = Array.IndexOf(cols, name);
                if (ix < 0) return new double[0];
                return rows.Select(r => r.Split(';')).Where(a =>
                {
                    DateTime t; return ix < a.Length && a[ix] != "" && DateTime.TryParseExact(a[0].Substring(0, 19), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t) && (lastTs - t).TotalSeconds <= secs;
                }).Select(a => double.Parse(a[ix], CultureInfo.InvariantCulture)).ToArray();
            };
            Func<double[], double> mx = a => a.Length > 0 ? a.Max() : double.NaN;
            Func<double[], double> av = a => a.Length > 0 ? a.Average() : double.NaN;

            double cpu10 = av(col("cpu_util", 10)), core10 = mx(col("cpu_core_max", 10));
            double gpu10 = av(col("gpu_3d_pct", 10)), gact10 = av(col("gpu_act_pct", 10));
            double pk10 = mx(col("gpu_power_peak_w", 10)), pw10 = av(col("gpu_power_w", 10)), pw60 = av(col("gpu_power_w", 60));
            double hot = mx(col("gpu_temp_hotspot", 30)), edge = mx(col("gpu_temp_edge", 30));
            var pkAll = col("gpu_power_peak_w", 10);
            double jump = pkAll.Length > 1 ? pkAll.Max() - pkAll.Min() : 0;
            bool gap = (ci.BootTime - lastTs).TotalMinutes > 3;
            Func<double, string> f0 = v => double.IsNaN(v) ? "n/d" : v.ToString("0", CultureInfo.InvariantCulture);

            o.Add("?");
            o.Add("Ultimi 10 s: CPU " + f0(cpu10) + "% (core max " + f0(core10) + "%), GPU " + f0(gpu10) + "%/" + f0(gact10) + "%, potenza GPU media " + f0(pw10) + " W, picco " + f0(pk10) + " W (salto " + f0(jump) + " W), hotspot max " + f0(hot) + " °C");

            if (ci.PowerButton != 0) { o[0] = "SPEGNIMENTO MANUALE"; o.Add("Il tasto di accensione risulta premuto: spegnimento forzato a mano, non un guasto."); return o; }
            if (ci.Bugcheck != 0) { o[0] = "SCHERMATA BLU 0x" + ci.Bugcheck.ToString("X"); o.Add("C'e' stato un BSOD: e' un problema software/driver o di instabilita' (undervolt/RAM). Cerca il codice 0x" + ci.Bugcheck.ToString("X") + "."); return o; }
            if (ci.SleepState != 0 || gap)
            {
                o[0] = "DURANTE TRANSIZIONE (sospensione/spegnimento)";
                o.Add("Il crash e' avvenuto " + SleepText(ci.SleepState) + (gap ? " (nessun dato nei minuti precedenti)" : "") + ".");
                o.Add("Sospetti: undervolt CPU (Curve Optimizer AMD / offset Intel) o profilo RAM (EXPO/XMP) instabili nei cambi di stato di alimentazione; BIOS non aggiornato.");
                return o;
            }
            if (!double.IsNaN(hot) && hot >= Limits.HotMax - 2) { o[0] = "SURRISCALDAMENTO GPU"; o.Add("Hotspot GPU a " + f0(hot) + " °C: protezione termica. Controlla ventole/pasta termica."); return o; }
            if (!double.IsNaN(edge) && edge >= Limits.EdgeBad) { o[0] = "SURRISCALDAMENTO GPU"; o.Add("Temperatura GPU a " + f0(edge) + " °C: protezione termica. Controlla ventole/pasta termica."); return o; }

            // potenza "alta" = vicina al massimo mai visto su questa scheda (minimo 100 W per avere un riferimento sensato)
            double refMax = !double.IsNaN(everMaxPower) && everMaxPower >= 100 ? everMaxPower : double.NaN;
            bool highPower = !double.IsNaN(pk10) && !double.IsNaN(refMax) && (pk10 >= 0.85 * refMax || (jump >= 0.5 * refMax && pk10 >= 0.6 * refMax));
            if (highPower)
            {
                o[0] = "PICCO DI POTENZA GPU -> ALIMENTATORE / GPU";
                o.Add("La GPU assorbiva " + f0(pk10) + " W (salto di " + f0(jump) + " W; massimo mai registrato su questo PC " + f0(refMax) + " W) subito prima dello spegnimento.");
                o.Add("Tipico della protezione dell'alimentatore (OCP) che scatta sui picchi, o di undervolt/overclock GPU troppo spinto sotto carico.");
                o.Add("Test: togli undervolt/overclock GPU; se continua, riduci il limite di potenza GPU del 10% (Adrenalin / NVIDIA App / Afterburner): se sparisce -> alimentatore.");
                return o;
            }
            if (!double.IsNaN(cpu10) && cpu10 < 25 && (double.IsNaN(gpu10) || gpu10 < 40))
            {
                o[0] = "CRASH A BASSO CARICO -> UNDERVOLT CPU / RAM";
                o.Add("PC quasi a riposo (CPU " + f0(cpu10) + "%, GPU " + f0(gpu10) + "%). L'alimentatore qui e' improbabile.");
                o.Add("Tipico di un undervolt CPU troppo aggressivo (Curve Optimizer AMD / offset Intel), instabile a basso carico, o del profilo RAM (EXPO/XMP).");
                o.Add("Test: riporta la CPU ai valori di fabbrica per una settimana; se continua, disattiva EXPO/XMP.");
                return o;
            }
            if (!double.IsNaN(cpu10) && cpu10 >= 60)
            {
                o[0] = "CRASH SOTTO CARICO CPU";
                o.Add("CPU al " + f0(cpu10) + "%: sospetti undervolt CPU, alimentatore o RAM.");
                return o;
            }
            o[0] = "CARICO MEDIO - NON CONCLUSIVO";
            o.Add("Nessun segnale netto: confronta con i report dei prossimi spegnimenti (la tabella nel monitor li mette a confronto).");
            return o;
        }

        public static string ReadVerdict(string path)
        {
            try
            {
                var lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length - 1; i++)
                    if (lines[i].StartsWith("--- DIAGNOSI")) return lines[i + 1].Trim();
                if (lines.Any(l => l.Contains("NON era in esecuzione"))) return "BlackBox non attivo";
            }
            catch { }
            return "";
        }

        // Registro delle configurazioni (undervolt/EXPO/...) per il confronto A/B
        public static string ConfigFile(string dir) { return Path.Combine(dir, "configurazioni.log"); }

        public static string ConfigAt(string dir, DateTime t)
        {
            string res = "";
            try
            {
                foreach (var l in File.ReadAllLines(ConfigFile(dir)))
                {
                    int p = l.IndexOf('|'); if (p < 0) continue;
                    DateTime ts;
                    if (DateTime.TryParseExact(l.Substring(0, p), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out ts) && ts <= t)
                        res = l.Substring(p + 1);
                }
            }
            catch { }
            return res;
        }

        public static DateTime ConfigSince(string dir)
        {
            DateTime res = DateTime.MinValue;
            try
            {
                foreach (var l in File.ReadAllLines(ConfigFile(dir)))
                {
                    int p = l.IndexOf('|'); DateTime ts;
                    if (p > 0 && DateTime.TryParseExact(l.Substring(0, p), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out ts)) res = ts;
                }
            }
            catch { }
            return res;
        }

        public static void SetConfig(string dir, string text)
        {
            File.AppendAllText(ConfigFile(dir), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "|" + text.Replace("\r", " ").Replace("\n", " ") + Environment.NewLine, new UTF8Encoding(false));
        }
    }
}
