using System;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;

namespace BlackBox
{
    // ---------------------------------------------------------------- Temperatura reale della CPU (Tctl/Tdie)
    // Leggerla direttamente richiede un driver kernel: BlackBox non lo installa. La prende da un programma
    // gia' aperto dall'utente:
    //   1) HWiNFO con "Shared Memory Support" attivo  -> memoria condivisa Global\HWiNFO_SENS_SM2
    //   2) LibreHardwareMonitor aperto                -> WMI root\LibreHardwareMonitor
    // Se nessuno dei due c'e': NaN, nessun errore.
    public static class CpuTemp
    {
        public static volatile string Source = "", Label = "";
        static long valueBits = BitConverter.DoubleToInt64Bits(double.NaN);
        public static double Value { get { return BitConverter.Int64BitsToDouble(Interlocked.Read(ref valueBits)); } }

        public static string HwinfoMapName = @"Global\HWiNFO_SENS_SM2";   // cambiabile solo per le prove
        static Thread thread;

        public static void Start()
        {
            if (thread != null) return;
            thread = new Thread(Run) { IsBackground = true, Name = "BlackBoxCpuTemp", Priority = ThreadPriority.BelowNormal };
            thread.Start();
        }

        static void Set(double v, string src, string label)
        {
            Interlocked.Exchange(ref valueBits, BitConverter.DoubleToInt64Bits(v));
            Source = src; Label = label;
        }

        static void Run()
        {
            DateTime nextLhm = DateTime.MinValue;
            bool lhmRunning = false;
            while (true)
            {
                try
                {
                    double v; string label;
                    if (Hwinfo.Read(out v, out label)) Set(v, "HWiNFO", label);
                    else
                    {
                        if (DateTime.Now >= nextLhm)
                        {
                            lhmRunning = Process.GetProcessesByName("LibreHardwareMonitor").Length > 0;
                            nextLhm = DateTime.Now.AddSeconds(10);
                        }
                        if (lhmRunning && Lhm.Read(out v, out label)) Set(v, "LibreHardwareMonitor", label);
                        else Set(double.NaN, "", "");
                    }
                }
                catch { Set(double.NaN, "", ""); }
                Thread.Sleep(1000);
            }
        }

        // ------------------------------------------------ HWiNFO (formato pubblico della memoria condivisa, struct packed 1)
        //  intestazione: firma 'HWiS', versione, revisione, poll_time (int64, secondi UTC),
        //                offset/dimensione/numero dei sensori, offset/dimensione/numero delle letture
        //  lettura:      tipo (1 = temperatura), indice sensore, id, etichetta originale [128], etichetta utente [128],
        //                unita' [16], valore, min, max, media (double)
        static class Hwinfo
        {
            const uint Signature = 0x53695748;   // "HWiS"
            static readonly string[] Wanted = { "CPU (Tctl/Tdie)", "CPU Die (average)", "CPU Package", "CPU (Tctl)", "CPU (Tdie)" };
            static MemoryMappedFile map;
            static MemoryMappedViewAccessor view;
            static DateTime nextOpen = DateTime.MinValue;
            static int cachedIdx = -1;

            static void Close()
            {
                try { if (view != null) view.Dispose(); } catch { }
                try { if (map != null) map.Dispose(); } catch { }
                view = null; map = null; cachedIdx = -1;
            }

            static bool Open()
            {
                if (view != null) return true;
                if (DateTime.Now < nextOpen) return false;
                nextOpen = DateTime.Now.AddSeconds(5);
                try
                {
                    map = MemoryMappedFile.OpenExisting(HwinfoMapName, MemoryMappedFileRights.Read);
                    view = map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                    return true;
                }
                catch { Close(); return false; }
            }

            static string Ansi(long pos, int len)
            {
                var b = new byte[len];
                view.ReadArray(pos, b, 0, len);
                int z = Array.IndexOf(b, (byte)0);
                return Encoding.Default.GetString(b, 0, z < 0 ? len : z);
            }

            public static bool Read(out double value, out string label)
            {
                value = double.NaN; label = "";
                if (!Open()) return false;
                try
                {
                    if (view.ReadUInt32(0) != Signature) { Close(); return false; }   // 'DEAD' = HWiNFO ha spento la memoria condivisa
                    long poll = view.ReadInt64(12);
                    var polled = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(poll);
                    if (Math.Abs((DateTime.UtcNow - polled).TotalSeconds) > 15) { Close(); return false; }   // HWiNFO chiuso o fermo
                    long offR = view.ReadUInt32(32); int szR = (int)view.ReadUInt32(36); int nR = (int)view.ReadUInt32(40);
                    if (szR < 316 || nR <= 0 || offR + (long)szR * nR > view.Capacity) { Close(); return false; }

                    if (cachedIdx >= 0 && cachedIdx < nR)
                    {
                        long b = offR + (long)cachedIdx * szR;
                        string l = Ansi(b + 12, 128);
                        if (view.ReadUInt32(b) == 1 && Wanted.Contains(l)) { value = view.ReadDouble(b + 284); label = l; return Valid(value); }
                    }
                    cachedIdx = -1;
                    int best = int.MaxValue;
                    for (int i = 0; i < nR; i++)
                    {
                        long b = offR + (long)i * szR;
                        if (view.ReadUInt32(b) != 1) continue;
                        int rank = Array.IndexOf(Wanted, Ansi(b + 12, 128));
                        if (rank >= 0 && rank < best) { best = rank; cachedIdx = i; }
                    }
                    if (cachedIdx < 0) return false;
                    long bb = offR + (long)cachedIdx * szR;
                    value = view.ReadDouble(bb + 284); label = Wanted[best];
                    return Valid(value);
                }
                catch { Close(); return false; }
            }
        }

        // ------------------------------------------------ LibreHardwareMonitor (WMI, solo se il programma e' aperto)
        static class Lhm
        {
            public static bool Read(out double value, out string label)
            {
                value = double.NaN; label = "";
                try
                {
                    using (var s = new ManagementObjectSearcher(@"root\LibreHardwareMonitor", "SELECT Name, Value, Identifier FROM Sensor WHERE SensorType='Temperature'"))
                    {
                        int best = int.MaxValue;
                        foreach (ManagementObject o in s.Get())
                            using (o)
                            {
                                string id = (o["Identifier"] ?? "").ToString().ToLowerInvariant(), name = (o["Name"] ?? "").ToString();
                                if (!id.Contains("cpu")) continue;
                                int rank = name.Contains("Tctl") ? 0 : name.Contains("Tdie") ? 1 : name.Contains("Package") ? 2 : -1;
                                if (rank < 0 || rank >= best) continue;
                                best = rank; label = name; value = Convert.ToDouble(o["Value"]);
                            }
                    }
                }
                catch { return false; }
                return Valid(value);
            }
        }

        static bool Valid(double v) { return !double.IsNaN(v) && v > 0 && v < 150; }
    }
}
