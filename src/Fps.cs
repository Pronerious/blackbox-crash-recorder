using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace BlackBox
{
    // ---------------------------------------------------------------- Impostazioni (Documenti\BlackBox\impostazioni.ini, chiave=valore)
    public static class Settings
    {
        static string path;
        static readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly object lk = new object();

        public static string Path { get { return path; } }

        public static void Load(string dir)
        {
            path = System.IO.Path.Combine(dir, "impostazioni.ini");
            lock (lk)
            {
                values.Clear();
                try
                {
                    foreach (var l in File.ReadAllLines(path))
                    {
                        int p = l.IndexOf('=');
                        if (p > 0 && !l.TrimStart().StartsWith(";") && !l.TrimStart().StartsWith("#")) values[l.Substring(0, p).Trim()] = l.Substring(p + 1).Trim();
                    }
                }
                catch { }
            }
        }

        public static string Get(string key, string def)
        {
            lock (lk) { string v; return values.TryGetValue(key, out v) ? v : def; }
        }

        public static void Set(string key, string value)
        {
            lock (lk)
            {
                values[key] = value ?? "";
                try
                {
                    var sb = new StringBuilder("; Impostazioni di BlackBox (si possono modificare anche a mano, con BlackBox chiuso)\r\n");
                    foreach (var kv in values.OrderBy(k => k.Key)) sb.Append(kv.Key + "=" + kv.Value + "\r\n");
                    File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                }
                catch { }
            }
        }
    }

    // ---------------------------------------------------------------- Download da GitHub (solo quando l'utente preme un pulsante)
    public static class Net
    {
        public static string GetText(string url)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "BlackBox";
                wc.Encoding = Encoding.UTF8;
                return wc.DownloadString(url);
            }
        }

        public static void GetFile(string url, string dest)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "BlackBox";
                wc.DownloadFile(url, dest);
            }
        }

        // firma digitale valida (WinVerifyTrust) e intestata a chi ci aspettiamo
        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
        static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, ref WINTRUST_DATA data);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WINTRUST_FILE_INFO { public uint cbStruct; public string pcwszFilePath; public IntPtr hFile; public IntPtr pgKnownSubject; }
        [StructLayout(LayoutKind.Sequential)]
        struct WINTRUST_DATA
        {
            public uint cbStruct; public IntPtr pPolicyCallbackData, pSIPClientData; public uint dwUIChoice, fdwRevocationChecks, dwUnionChoice;
            public IntPtr pFile; public uint dwStateAction; public IntPtr hWVTStateData, pwszURLReference; public uint dwProvFlags, dwUIContext; public IntPtr pSignatureSettings;
        }

        public static bool SignedBy(string file, string organization)
        {
            IntPtr pFile = IntPtr.Zero;
            try
            {
                var fi = new WINTRUST_FILE_INFO { cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)), pcwszFilePath = file };
                pFile = Marshal.AllocHGlobal(Marshal.SizeOf(fi));
                Marshal.StructureToPtr(fi, pFile, false);
                var d = new WINTRUST_DATA { cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA)), dwUIChoice = 2, dwUnionChoice = 1, pFile = pFile };
                var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");   // WINTRUST_ACTION_GENERIC_VERIFY_V2
                if (WinVerifyTrust(IntPtr.Zero, action, ref d) != 0) return false;
                var cert = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(file);
                return cert.Subject.Contains("O=" + organization);
            }
            catch { return false; }
            finally { if (pFile != IntPtr.Zero) Marshal.FreeHGlobal(pFile); }
        }
    }

    // ---------------------------------------------------------------- Job Object: i processi figli muoiono con BlackBox
    static class Job
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr a, string name);
        [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int cls, ref EXT info, uint len);
        [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr proc);
        [StructLayout(LayoutKind.Sequential)]
        struct BASIC { public long PerProcessUserTimeLimit, PerJobUserTimeLimit; public uint LimitFlags; public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass, SchedulingClass; }
        [StructLayout(LayoutKind.Sequential)]
        struct IOC { public ulong a, b, c, d, e, f; }
        [StructLayout(LayoutKind.Sequential)]
        struct EXT { public BASIC Basic; public IOC Io; public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed; }

        static IntPtr handle = IntPtr.Zero;

        public static void Add(Process p)
        {
            try
            {
                if (handle == IntPtr.Zero)
                {
                    handle = CreateJobObject(IntPtr.Zero, null);
                    var info = new EXT();
                    info.Basic.LimitFlags = 0x2000;   // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                    SetInformationJobObject(handle, 9, ref info, (uint)Marshal.SizeOf(typeof(EXT)));
                }
                AssignProcessToJobObject(handle, p.Handle);
            }
            catch { }
        }
    }

    // ---------------------------------------------------------------- FPS e frame time con PresentMon (Intel, licenza MIT)
    // PresentMon gira solo durante una sessione di gioco, come processo figlio senza finestra.
    // Legge gli eventi ETW di Windows: serve essere amministratori oppure nel gruppo
    // "Performance Log Users" (S-1-5-32-559). BlackBox lo chiede una volta sola, su richiesta.
    public static class Fps
    {
        public const string PerfLogSid = "S-1-5-32-559";
        const string SessionName = "BlackBox";
        static string toolsDir;

        static Process pm;
        static int targetPid;
        static readonly object lk = new object();
        // fotogrammi degli ultimi 60 s
        struct Frame { public double T, Ft, Gpu; }   // istante (ms dall'inizio della cattura), durata, lavoro della GPU (ms)
        static readonly Queue<Frame> frames = new Queue<Frame>();
        // generazione di fotogrammi (FSR 3, DLSS FG via OptiScaler, XeFG): i fotogrammi generati costano pochissima GPU
        // (es. 8 ms quelli veri, 1 ms quelli generati) e si alternano con quelli veri. Soglia aggiornata ogni secondo.
        static long sessCounted; static double sessRealF, sessFgF; static int fgMiss;
        public static volatile int FgFactor = 1;
        // gioco in secondo piano (es. Alt+Tab): disegna pochissimo e falserebbe 1% low e medie, quindi quei fotogrammi non contano
        static volatile bool inFront = true;
        public static bool GameInFront
        {
            get { return inFront; }
            set
            {
                if (value == inFront) return;
                inFront = value;
                if (value) lock (lk) { frames.Clear(); lastLine = DateTime.MinValue; }   // si riparte puliti al ritorno in gioco
            }
        }
        static double lastT = double.NaN; static DateTime lastLine = DateTime.MinValue;
        // statistiche dell'intera sessione: istogramma del frame time a passi di 0,1 ms (fino a 250 ms)
        static readonly long[] hist = new long[2501];
        static long sessFrames; static double sessMs;
        static readonly Dictionary<string, long> chainCount = new Dictionary<string, long>();
        static string mainChain = "";
        static readonly double[] graph = new double[120]; static int graphPos;   // per il grafico dell'overlay

        public static volatile string Status = "";      // "" = funziona, altrimenti spiegazione
        public static volatile bool Denied;             // PresentMon non ha il permesso di leggere gli eventi

        public static void Init(string logDir)
        {
            toolsDir = Path.Combine(logDir, "tools");
            if (!ToolPresent) Status = "PresentMon non presente";
        }

        public static string ToolPath
        {
            get
            {
                string p = Settings.Get("presentmon", "");
                if (p != "" && File.Exists(p)) return p;
                return Path.Combine(toolsDir ?? "", "PresentMon.exe");
            }
        }
        public static bool ToolPresent { get { return File.Exists(ToolPath); } }
        public static string ToolVersion
        {
            get
            {
                try { string v = Path.Combine(Path.GetDirectoryName(ToolPath), "PresentMon.version"); if (File.Exists(v)) return File.ReadAllText(v).Trim(); } catch { }
                try { return FileVersionInfo.GetVersionInfo(ToolPath).ProductVersion ?? ""; } catch { return ""; }
            }
        }

        // ------------------------------------------------ permesso una tantum
        public static bool HasPermission
        {
            get
            {
                try
                {
                    var id = WindowsIdentity.GetCurrent();
                    if (new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator)) return true;
                    return id.Groups != null && id.Groups.Any(g => g.Value == PerfLogSid);
                }
                catch { return false; }
            }
        }

        // aggiunge l'utente al gruppo Performance Log Users: Windows chiede la conferma (UAC) una volta
        public static bool RequestPermission(out string error)
        {
            error = "";
            try
            {
                string user = WindowsIdentity.GetCurrent().Name.Replace("'", "''");
                string cmd = "Add-LocalGroupMember -SID " + PerfLogSid + " -Member '" + user + "' -ErrorAction SilentlyContinue";
                var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command \"" + cmd + "\"")
                { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                using (var p = Process.Start(psi)) p.WaitForExit(30000);
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }   // anche "operazione annullata dall'utente"
        }

        // ------------------------------------------------ download (pulsante)
        // ultima release stabile da github.com/GameTechDev/PresentMon, file PresentMon-<versione>-x64.exe firmato da Intel
        public static string Download()
        {
            Directory.CreateDirectory(toolsDir);
            string json = Net.GetText("https://api.github.com/repos/GameTechDev/PresentMon/releases/latest");
            var tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
            var url = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+/PresentMon-[0-9.]+-x64\\.exe)\"").Groups[1].Value;
            if (url == "") throw new Exception("nella release " + tag + " non trovo il file PresentMon-...-x64.exe");
            string tmp = Path.Combine(toolsDir, "PresentMon.download");
            Net.GetFile(url, tmp);
            if (!Net.SignedBy(tmp, "Intel Corporation")) { try { File.Delete(tmp); } catch { } throw new Exception("il file scaricato non ha una firma valida di Intel: scartato"); }
            string dest = Path.Combine(toolsDir, "PresentMon.exe");
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(tmp, dest);
            File.WriteAllText(Path.Combine(toolsDir, "PresentMon.version"), tag);
            Settings.Set("presentmon", "");
            Status = "";
            return tag;
        }

        // ------------------------------------------------ avvio / arresto (inizio e fine della sessione di gioco)
        public static void Start(int pid)
        {
            Stop();
            lock (lk)
            {
                frames.Clear(); lastT = double.NaN; lastLine = DateTime.MinValue;
                inFront = true;
                Array.Clear(hist, 0, hist.Length); sessFrames = 0; sessMs = 0; sessCounted = 0; sessRealF = 0; sessFgF = 0; fgMiss = 0; FgFactor = 1;
                chainCount.Clear(); mainChain = "";
                Array.Clear(graph, 0, graph.Length); graphPos = 0;
            }
            Denied = false;
            if (!ToolPresent) { Status = "PresentMon non presente"; return; }
            try
            {
                var psi = new ProcessStartInfo(ToolPath,
                    "--process_id " + pid + " --output_stdout --no_console_stats --v2_metrics --stop_existing_session --session_name " + SessionName + " --terminate_on_proc_exit")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
                var p = Process.Start(psi);
                Job.Add(p);
                try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                pm = p; targetPid = pid;
                Status = "in attesa dei primi fotogrammi";
                new Thread(() => ReadOut(p)) { IsBackground = true, Name = "BlackBoxFps" }.Start();
                new Thread(() => ReadErr(p)) { IsBackground = true, Name = "BlackBoxFpsErr" }.Start();
            }
            catch (Exception ex) { Status = "PresentMon non parte: " + ex.Message; pm = null; }
        }

        public static void Stop()
        {
            var p = pm; pm = null; targetPid = 0;
            if (p == null) return;
            try { if (!p.HasExited) p.Kill(); } catch { }
            try { p.Dispose(); } catch { }
            // chiude la sessione ETW rimasta aperta dopo l'uccisione del processo
            try
            {
                var psi = new ProcessStartInfo(ToolPath, "--terminate_existing_session --session_name " + SessionName) { UseShellExecute = false, CreateNoWindow = true };
                using (var c = Process.Start(psi)) c.WaitForExit(3000);
            }
            catch { }
        }

        static void ReadErr(Process p)
        {
            try
            {
                string line;
                while ((line = p.StandardError.ReadLine()) != null)
                {
                    string l = line.ToLowerInvariant();
                    if (l.Contains("access denied") || l.Contains("accesso negato") || l.Contains("performance log users") || (l.Contains("error") && l.Contains("privilege")))
                    { Denied = true; Status = "permesso mancante"; }
                }
            }
            catch { }
        }

        static void ReadOut(Process p)
        {
            try
            {
                string header = p.StandardOutput.ReadLine();
                if (header == null) { if (!Denied) Status = "PresentMon si è chiuso subito"; return; }
                var cols = header.Split(',');
                int iFt = Array.IndexOf(cols, "FrameTime"), iT = Array.IndexOf(cols, "CPUStartTime"), iPid = Array.IndexOf(cols, "ProcessID"), iSc = Array.IndexOf(cols, "SwapChainAddress"), iGpu = Array.IndexOf(cols, "GPUBusy");
                if (iGpu < 0) iGpu = Array.IndexOf(cols, "MsGPUBusy");
                double tScale = 1;
                if (iFt < 0) iFt = Array.IndexOf(cols, "MsBetweenPresents");            // formato 1.x
                if (iT < 0) iT = Array.IndexOf(cols, "CPUStartTimeInMs");
                if (iT < 0) iT = Array.IndexOf(cols, "TimeInMs");
                if (iT < 0) { iT = Array.IndexOf(cols, "TimeInSeconds"); tScale = 1000; }
                if (iFt < 0 || iT < 0) { Status = "formato di PresentMon non riconosciuto"; return; }
                int need = Math.Max(Math.Max(iFt, iT), Math.Max(iPid, iSc));
                string line;
                while ((line = p.StandardOutput.ReadLine()) != null)
                {
                    var a = line.Split(',');
                    if (a.Length <= need) continue;
                    if (iPid >= 0 && a[iPid] != targetPid.ToString()) continue;
                    double ft, t;
                    if (!double.TryParse(a[iFt], NumberStyles.Float, CultureInfo.InvariantCulture, out ft) ||
                        !double.TryParse(a[iT], NumberStyles.Float, CultureInfo.InvariantCulture, out t) || ft <= 0 || ft > 5000) continue;
                    double gpu;
                    if (iGpu < 0 || iGpu >= a.Length || !double.TryParse(a[iGpu], NumberStyles.Float, CultureInfo.InvariantCulture, out gpu)) gpu = double.NaN;
                    Add(t * tScale, ft, gpu, iSc >= 0 ? a[iSc] : "");
                }
                if (pm == p && !Denied) Status = "PresentMon si è fermato";
            }
            catch { }
        }

        // solo la catena di presentazione con piu' fotogrammi (il gioco), non overlay o launcher dello stesso processo
        static void Add(double t, double ft, double gpu, string chain)
        {
            if (!inFront) return;
            lock (lk)
            {
                long c; chainCount.TryGetValue(chain, out c); chainCount[chain] = ++c;
                if (mainChain == "" || (chain != mainChain && c > chainCount[mainChain] * 2)) mainChain = chain;
                if (chain != mainChain) return;
                frames.Enqueue(new Frame { T = t, Ft = ft, Gpu = gpu });
                while (frames.Count > 0 && frames.Peek().T < t - 60000) frames.Dequeue();
                lastT = t; lastLine = DateTime.UtcNow;
                hist[Math.Min(hist.Length - 1, (int)(ft * 10))]++;
                sessFrames++; sessMs += ft;
                Status = "";
            }
        }

        // ------------------------------------------------ valori (chiamati ogni secondo dal registratore)
        // FPS degli ultimi 2 s, 1% low sugli ultimi 60 s, frame time medio degli ultimi 2 s. NaN se non ci sono dati freschi.
        public static void Current(out double fps, out double low1, out double ftAvg, out double realFps)
        {
            fps = low1 = ftAvg = realFps = double.NaN;
            lock (lk)
            {
                if (pm == null || frames.Count == 0 || (DateTime.UtcNow - lastLine).TotalSeconds > 3) return;
                double from = lastT - 2000, sum = 0; int n = 0;
                var recent = frames.Where(f => f.T > from).ToList();
                foreach (var f in recent) { sum += f.Ft; n++; }
                if (n > 0) { fps = 1000.0 * n / sum; ftAvg = sum / n; }
                DetectFrameGen(recent);
                CountSession();
                realFps = FgFactor > 1 ? fps / FgFactor : fps;
                var all = frames.Select(f => f.Ft).OrderBy(v => v).ToList();
                if (all.Count >= 20) low1 = 1000.0 / all[(int)Math.Min(all.Count - 1, Math.Ceiling(all.Count * 0.99) - 1)];
                graph[graphPos] = ftAvg; graphPos = (graphPos + 1) % graph.Length;
            }
        }

        static void DetectFrameGen(List<Frame> recent)
        {
            var g = recent.Where(f => !double.IsNaN(f.Gpu)).Select(f => f.Gpu).ToList();
            if (g.Count < 30) { FgFactor = 1; return; }
            var sorted = g.OrderBy(x => x).ToList();
            double p75 = sorted[(int)(sorted.Count * 0.75)], thr = 0.35 * p75;
            int light = 0, alternations = 0;
            for (int i = 0; i < g.Count; i++)
                if (g[i] < thr) { light++; if (i > 0 && g[i - 1] >= thr) alternations++; }
            double frac = light / (double)g.Count;
            // fotogrammi "leggeri" tra il 35% e l'80% e quasi sempre subito dopo uno "pesante": generazione attiva
            if (p75 > 0.5 && frac >= 0.35 && frac <= 0.8 && alternations >= 0.6 * light)
            { FgFactor = Math.Max(2, (int)Math.Round(1 / (1 - frac))); fgMiss = 0; }
            else if (++fgMiss >= 3) FgFactor = 1;   // tolleranza: spenta solo dopo 3 secondi di fila senza il ritmo vero/generato
        }

        // i fotogrammi arrivati dall'ultimo secondo, divisi per il fattore di generazione del momento
        static void CountSession()
        {
            long nf = sessFrames - sessCounted; sessCounted = sessFrames;
            sessRealF += FgFactor > 1 ? nf / (double)FgFactor : nf;
            if (FgFactor > 1) sessFgF += nf;
        }

        // FPS medio e 1% low dell'intera sessione; realFps = senza i fotogrammi generati; fgShare = quota di partita con generazione
        public static void SessionStats(out double fps, out double low1, out double realFps, out double fgShare)
        {
            fps = low1 = realFps = fgShare = double.NaN;
            lock (lk)
            {
                if (sessFrames < 20 || sessMs <= 0) return;
                fps = 1000.0 * sessFrames / sessMs;
                CountSession();
                realFps = 1000.0 * sessRealF / sessMs;
                fgShare = sessFgF / sessFrames;
                long target = (long)Math.Ceiling(sessFrames * 0.99), acc = 0;
                for (int i = 0; i < hist.Length; i++) { acc += hist[i]; if (acc >= target) { low1 = 1000.0 / ((i + 0.5) / 10.0); break; } }
            }
        }

        public static bool Running { get { return pm != null; } }

        // durata degli ultimi n fotogrammi (per il grafico dell'overlay); vuoto se i dati non sono freschi
        public static double[] RecentFrameTimes(int n)
        {
            lock (lk)
            {
                if (pm == null || frames.Count == 0 || (DateTime.UtcNow - lastLine).TotalSeconds > 3) return new double[0];
                int skip = Math.Max(0, frames.Count - n);
                return frames.Skip(skip).Select(f => f.Ft).ToArray();
            }
        }
    }
}
