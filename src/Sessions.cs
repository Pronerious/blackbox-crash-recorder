using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace BlackBox
{
    // ---------------------------------------------------------------- Sessione di gioco
    public class GameSession
    {
        public string Game = "", ExePath = "", Config = "", EndReason = "";
        public int Pid;
        public Process Proc;
        public DateTime Start, End, LastFg, ExitedAt = DateTime.MinValue;
        public readonly List<Sample> Samples = new List<Sample>();   // i secondi con il gioco in primo piano
        public readonly List<string> Throttles = new List<string>();
        public readonly List<string> Crashes = new List<string>();
        public double ThrottleSec;
        public int Tdr;
        public string Tuning = "";
        public DateTime LastThrottle = DateTime.MinValue;
        public bool LastThrottleThermal;
        public double ThermalThrottleSec;
        public double BaseEff = double.NaN;   // FPS reali per watt nelle partite precedenti allo stesso gioco

        // giudizio sullo undervolt/overclock in questa partita: 0 = ok, 1 = attenzione, 2 = problema
        public string UvVerdict(out int level)
        {
            int min = (int)(LastFg - Start).TotalMinutes;
            if (Tuning.StartsWith("RESET")) { level = 2; return "UV/OC perso: Adrenalin ha rimesso i valori di fabbrica"; }
            if (Tdr > 0) { level = 2; return "UV/OC instabile: " + Tdr + " crash del driver in questa partita"; }
            if (Crashes.Count > 0) { level = 2; return "il gioco è andato in crash: possibile UV/OC troppo spinto"; }
            if (ThermalThrottleSec > 0) { level = 1; return "stabile, ma la scheda ha rallentato per il calore per " + Math.Round(ThermalThrottleSec) + " s"; }
            level = 0;
            if (ThrottleSec > 0) return "stabile da " + min + " min (qualche rallentamento per limite di potenza: normale con un undervolt)";
            return "stabile da " + min + " min (nessun crash, nessun throttling)";
        }
    }

    // Riconosce le sessioni di gioco e controlla la stabilita' (throttling, reset del tuning di Adrenalin).
    //   inizio : processo in primo piano che usa la GPU (3D) oltre il 60% per piu' di 20 s
    //   fine   : il gioco si chiude, oppure resta in secondo piano per piu' di 60 s
    // Gira nel thread del Collector: OnSample ogni secondo, OnTdr/OnAppError dal Registro eventi.
    public class GameTracker
    {
        const double StartGpu = 60, StartSecs = 20, BackgroundSecs = 60, ExitWaitSecs = 15;
        const double LoadGpu = 90, ThrottleGpu = 95, ThrottleRatio = 0.85; const int ThrottleSecs = 5;
        const int TuningSamples = 60; const double TuningMv = 35, TuningClk = 0.06;

        // programmi che non sono giochi anche se in primo piano
        static readonly HashSet<string> NotGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "BlackBox", "explorer", "dwm", "ApplicationFrameHost", "ShellExperienceHost", "SearchHost",
            "StartMenuExperienceHost", "LockApp", "TextInputHost", "Taskmgr", "Idle" };

        readonly Collector col;
        readonly string dir;
        readonly int selfPid = Process.GetCurrentProcess().Id;
        volatile GameSession current;

        int candPid, candMiss; DateTime candSince; readonly List<Sample> candSamples = new List<Sample>();
        readonly List<double> loadClk = new List<double>(); double typicalClk = double.NaN;
        int thrRun; DateTime thrStart; double thrMinClk, thrHot, thrPow, thrGpu;
        DateTime nextState = DateTime.MinValue;

        DateTime tuneSince = DateTime.MinValue; string tuneWhy = "";
        readonly List<double> tuneClk = new List<double>(), tuneMv = new List<double>();

        string StateFile { get { return Path.Combine(dir, "sessione_in_corso.txt"); } }
        string TuneFile { get { return Path.Combine(dir, "controllo_tuning.txt"); } }

        public GameTracker(Collector col)
        {
            this.col = col; dir = col.LogDir;
            RecoverInterrupted();
            try
            {
                var t = File.ReadAllText(TuneFile).Trim().Split(new[] { '|' }, 2);
                DateTime ts;
                if (DateTime.TryParseExact(t[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out ts))
                { tuneSince = ts; tuneWhy = t.Length > 1 ? t[1] : "un crash"; }
            }
            catch { }
        }

        public bool InSession { get { var c = current; return c != null && c.ExitedAt == DateTime.MinValue; } }
        public GameSession Current { get { return current; } }
        public bool TuningPending { get { return tuneSince != DateTime.MinValue; } }

        // testo per la finestra: "Cyberpunk2077 da 12 min" oppure ""
        public string StatusText
        {
            get
            {
                var c = current;
                if (c == null) return "";
                return c.Game + " da " + Dur(DateTime.Now - c.Start) + (c.ThrottleSec > 0 ? " · throttling " + Math.Round(c.ThrottleSec) + " s" : "");
            }
        }

        // ------------------------------------------------ ogni secondo
        public void OnSample(Sample s)
        {
            var c = current;
            if (c == null) { DetectStart(s); return; }

            if (c.ExitedAt != DateTime.MinValue)
            {
                // il gioco e' uscito: aspetto qualche secondo per leggere un eventuale errore dal Registro eventi
                if ((s.Time - c.ExitedAt).TotalSeconds >= ExitWaitSecs) { Close(c.Crashes.Count > 0 ? "il gioco si è chiuso con un errore" : "il gioco si è chiuso"); DetectStart(s); }
                return;
            }
            if (!Alive(c)) { c.ExitedAt = s.Time; EndThrottle(c); return; }

            if (s.ForegroundPid == c.Pid)
            {
                c.LastFg = s.Time;
                c.Samples.Add(s);
                CheckThrottle(c, s);
                CheckTuning(c, s);
            }
            else
            {
                EndThrottle(c);
                if ((s.Time - c.LastFg).TotalSeconds > BackgroundSecs) { Close("in secondo piano da più di " + BackgroundSecs + " s"); DetectStart(s); return; }
            }
            if (s.Time >= nextState) { WriteState(c, s.Time); nextState = s.Time.AddSeconds(30); }
        }

        // BlackBox si chiude (o Windows si spegne): chiude la sessione aperta e scrive il report
        public void Finish(string reason)
        {
            if (current != null) Close(reason);
        }

        void DetectStart(Sample s)
        {
            bool busy = !double.IsNaN(s.Fg3d) && s.Fg3d > StartGpu;
            if (s.ForegroundPid == 0 || s.ForegroundPid == selfPid || NotGames.Contains(s.Foreground)) { candPid = 0; return; }
            if (s.ForegroundPid != candPid)
            {
                candSamples.Clear(); candMiss = 0; candSince = s.Time;
                candPid = busy ? s.ForegroundPid : 0;
                if (busy) candSamples.Add(s);
                return;
            }
            candSamples.Add(s);
            if (busy) candMiss = 0;
            else if (++candMiss > 3) { candPid = 0; candSamples.Clear(); return; }   // tollera brevi cali (caricamenti)
            if ((s.Time - candSince).TotalSeconds >= StartSecs) Open(s);
        }

        void Open(Sample s)
        {
            var c = new GameSession { Game = s.Foreground, Pid = s.ForegroundPid, Start = candSince, LastFg = s.Time, Config = Analyzer.ConfigAt(dir, candSince) };
            try { c.Proc = Process.GetProcessById(c.Pid); } catch { }
            try { if (c.Proc != null) c.ExePath = c.Proc.MainModule.FileName; } catch { }
            c.Samples.AddRange(candSamples);
            var prev = SessionLog.Read(dir).Where(x => string.Equals(x.Game, c.Game, StringComparison.OrdinalIgnoreCase) && !double.IsNaN(x.Eff))
                                           .OrderByDescending(x => x.Start).Take(5).ToList();
            if (prev.Count > 0) c.BaseEff = prev.Average(x => x.Eff);
            candSamples.Clear(); candPid = 0;
            loadClk.Clear(); typicalClk = double.NaN; thrRun = 0;
            current = c;
            WriteState(c, s.Time);
            try { Fps.Start(c.Pid); } catch { }
            col.AddEvent("INFO", "Sessione di gioco iniziata: " + c.Game);
        }

        static bool Alive(GameSession c)
        {
            try { if (c.Proc != null) return !c.Proc.HasExited; } catch { }
            try { using (var p = Process.GetProcessById(c.Pid)) return string.Equals(p.ProcessName, c.Game, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        // ------------------------------------------------ throttling
        // GPU oltre il 95% ma clock sotto l'85% del clock tipico della sessione per piu' di 5 s
        void CheckThrottle(GameSession c, Sample s)
        {
            if (double.IsNaN(s.GpuClk) || double.IsNaN(s.Gpu3d)) { EndThrottle(c); return; }
            bool thr = !double.IsNaN(typicalClk) && s.Gpu3d > ThrottleGpu && s.GpuClk < ThrottleRatio * typicalClk;
            if (!thr)
            {
                EndThrottle(c);
                if (s.Gpu3d >= LoadGpu)
                {
                    loadClk.Add(s.GpuClk);
                    if (loadClk.Count > 600) loadClk.RemoveAt(0);   // ultimi 10 minuti sotto carico
                    if (loadClk.Count >= 30 && loadClk.Count % 10 == 0) typicalClk = Median(loadClk);
                }
                return;
            }
            if (thrRun == 0) { thrStart = s.Time; thrMinClk = s.GpuClk; thrHot = double.NaN; thrPow = double.NaN; thrGpu = s.Gpu3d; }
            thrRun++;
            thrMinClk = Math.Min(thrMinClk, s.GpuClk);
            thrHot = MaxN(thrHot, s.GpuTempHot); thrPow = MaxN(thrPow, s.GpuPower);
            if (thrRun >= ThrottleSecs) { c.LastThrottle = s.Time; c.LastThrottleThermal = !double.IsNaN(thrHot) && thrHot >= Limits.HotWarn; }
            if (thrRun == ThrottleSecs)
                col.AddEvent("WARN", "Throttling GPU in " + c.Game + ": clock " + N0(thrMinClk) + " MHz invece dei soliti ~" + N0(typicalClk) +
                                     " MHz con GPU al " + N0(thrGpu) + "% (hotspot " + N0(thrHot) + " °C, " + N0(thrPow) + " W) — " + ThrottleCause());
        }

        void EndThrottle(GameSession c)
        {
            if (thrRun >= ThrottleSecs)
            {
                c.ThrottleSec += thrRun;
                if (!double.IsNaN(thrHot) && thrHot >= Limits.HotWarn) c.ThermalThrottleSec += thrRun;
                c.Throttles.Add(thrStart.ToString("HH:mm:ss") + "  " + thrRun + " s  clock " + N0(thrMinClk) + " MHz invece di ~" + N0(typicalClk) +
                                " MHz, hotspot " + N0(thrHot) + " °C, " + N0(thrPow) + " W — " + ThrottleCause());
            }
            thrRun = 0;
        }

        string ThrottleCause()
        {
            if (!double.IsNaN(thrHot) && thrHot >= Limits.HotWarn) return "probabile limite termico";
            if (!double.IsNaN(thrPow) && !double.IsNaN(col.EverMaxPower) && thrPow >= 0.93 * col.EverMaxPower) return "probabile limite di potenza";
            return "causa non chiara (driver, limite di tensione o scena che cambia)";
        }

        // ------------------------------------------------ reset del tuning di Adrenalin
        // Dopo un crash del driver (TDR) o uno spegnimento anomalo Adrenalin spesso rimette i valori di fabbrica.
        // Alla prima sessione di gioco successiva confronto clock massimo e mV sotto carico con le sessioni precedenti.
        public void MarkTuningCheck(DateTime when, string why)
        {
            if (tuneSince != DateTime.MinValue && tuneSince >= when) return;
            tuneSince = when; tuneWhy = why;
            tuneClk.Clear(); tuneMv.Clear();
            try { File.WriteAllText(TuneFile, when.ToString("yyyy-MM-dd HH:mm:ss") + "|" + why); } catch { }
            col.AddEvent("INFO", "Dopo " + why + " controllerò alla prossima partita se Adrenalin ha rimesso i valori di fabbrica");
        }

        void CheckTuning(GameSession c, Sample s)
        {
            if (tuneSince == DateTime.MinValue || s.Time <= tuneSince || double.IsNaN(s.Gpu3d) || s.Gpu3d < LoadGpu) return;
            if (!double.IsNaN(s.GpuClkMax)) tuneClk.Add(s.GpuClkMax);
            if (!double.IsNaN(s.GpuVddc)) tuneMv.Add(s.GpuVddc);
            if (tuneClk.Count < TuningSamples || tuneMv.Count < TuningSamples) return;

            double clkNow = tuneClk.Max(), mvNow = tuneMv.Average();
            var prev = SessionLog.Read(dir).Where(x => x.Start < tuneSince &&!double.IsNaN(x.ClkMax) && !double.IsNaN(x.MvLoad))
                                           .OrderByDescending(x => x.Start).Take(5).ToList();
            string why = tuneWhy;
            tuneSince = DateTime.MinValue; tuneWhy = "";
            tuneClk.Clear(); tuneMv.Clear();
            try { File.Delete(TuneFile); } catch { }

            if (prev.Count == 0)
            {
                c.Tuning = "dopo " + why + ": nessuna partita precedente con cui confrontare (ora " + N0(mvNow) + " mV, " + N0(clkNow) + " MHz)";
                col.AddEvent("INFO", "Controllo del tuning dopo " + why + ": nessuna partita precedente con cui confrontare");
                return;
            }
            double clkBase = prev.Average(x => x.ClkMax), mvBase = prev.Average(x => x.MvLoad);
            double dMv = mvNow - mvBase, dClk = (clkNow - clkBase) / clkBase;
            string vals = "ora " + N0(mvNow) + " mV e " + N0(clkNow) + " MHz, prima di solito " + N0(mvBase) + " mV e " + N0(clkBase) + " MHz";
            if (dMv >= TuningMv || Math.Abs(dClk) >= TuningClk)
            {
                c.Tuning = "RESET: Adrenalin ha ripristinato i valori di fabbrica dopo " + why + " (" + vals + ")";
                col.AddEvent("WARN", "Adrenalin ha ripristinato i valori di fabbrica dopo un crash: lo UV/OC non è più attivo (" + vals + ")");
            }
            else
            {
                c.Tuning = "ok: dopo " + why + " il tuning è ancora attivo (" + vals + ")";
                col.AddEvent("INFO", "Controllo del tuning dopo " + why + ": UV/OC ancora attivo (" + vals + ")");
            }
        }

        // ------------------------------------------------ eventi dal Registro di Windows
        public void OnTdr(DateTime t)
        {
            var c = current;
            if (c != null) c.Tdr++;
            MarkTuningCheck(t, "il crash del driver video (TDR)");
        }

        public void OnAppError(string description, DateTime t)
        {
            var c = current;
            if (c == null || description == null || t < c.Start) return;
            if (description.IndexOf(c.Game + ".exe", StringComparison.OrdinalIgnoreCase) < 0) return;
            string first = description.Split('\n')[0].Trim();
            if (first.Length > 160) first = first.Substring(0, 160) + "...";
            c.Crashes.Add(t.ToString("HH:mm:ss") + "  " + first);
        }

        // ------------------------------------------------ fine sessione: report + riga in sessioni.csv
        void Close(string reason)
        {
            var c = current;
            if (c == null) return;
            EndThrottle(c);
            current = null;
            c.End = c.LastFg; c.EndReason = reason;
            try { if (c.Proc != null) c.Proc.Dispose(); } catch { }
            try { File.Delete(StateFile); } catch { }

            var all = c.Samples;
            var play = all.Where(x => !double.IsNaN(x.Gpu3d) && x.Gpu3d > StartGpu).ToList();
            if (play.Count == 0) play = all;
            var load = all.Where(x => !double.IsNaN(x.Gpu3d) && x.Gpu3d >= LoadGpu).ToList();

            double pAvg = Avg(play, x => x.GpuPower), pPk = Max(all, x => x.GpuPowerMax);
            double clkAvg = Avg(play, x => x.GpuClk), clkMax = Max(load, x => x.GpuClkMax);
            double mvAvg = Avg(play, x => x.GpuVddc), mvLoad = Avg(load, x => x.GpuVddc);
            double edge = Max(all, x => x.GpuTempEdge), hot = Max(all, x => x.GpuTempHot);
            double dAvg = Avg(play, x => x.GpuTempHot - x.GpuTempEdge), dMax = Max(all, x => x.GpuTempHot - x.GpuTempEdge);
            double vram = Max(all, x => x.VramMb) / 1024.0;
            double fps, fpsLow, fpsReal, fgShare;
            Fps.SessionStats(out fps, out fpsLow, out fpsReal, out fgShare);
            double eff = !double.IsNaN(fpsReal) && pAvg > 20 ? fpsReal / pAvg : double.NaN;   // FPS reali per watt
            bool fg = !double.IsNaN(fgShare) && fgShare > 0.2;
            try { Fps.Stop(); } catch { }
            string opti = all.Select(x => x.OptiScaler).LastOrDefault(x => !string.IsNullOrEmpty(x)) ?? "";
            if (load.Count < 30) clkMax = mvLoad = double.NaN;   // troppo poco carico per un confronto affidabile

            string name = "sessione_" + Safe(c.Game) + "_" + c.Start.ToString("yyyy-MM-dd_HHmm") + ".txt";
            var sb = new StringBuilder();
            sb.AppendLine("==============================================================");
            sb.AppendLine("  BLACKBOX - REPORT SESSIONE DI GIOCO");
            sb.AppendLine("==============================================================");
            sb.AppendLine("Gioco:                   " + c.Game + (c.ExePath != "" ? "  (" + c.ExePath + ")" : ""));
            sb.AppendLine("Inizio / fine:           " + c.Start.ToString("dd/MM/yyyy HH:mm") + " → " + c.End.ToString("HH:mm") + "  (" + Dur(c.End - c.Start) + ")");
            sb.AppendLine("Motivo della fine:       " + reason);
            sb.AppendLine("Configurazione attiva:   " + (c.Config == "" ? "(non indicata)" : c.Config));
            sb.AppendLine("OptiScaler:              " + (opti == "" ? "n/d" : opti));
            sb.AppendLine();
            sb.AppendLine("--- Scheda video in gioco (secondi con GPU oltre il " + StartGpu + "%) ---");
            Row(sb, "Potenza media / picco", N0(pAvg) + " / " + N0(pPk) + " W");
            Row(sb, "Clock medio", N0(clkAvg) + " MHz");
            Row(sb, "Clock massimo sotto carico", N0(clkMax) + " MHz");
            Row(sb, "Tensione media", N0(mvAvg) + " mV" + (double.IsNaN(mvLoad) ? "" : "  (sotto carico pieno " + N0(mvLoad) + " mV)"));
            Row(sb, "Temperatura edge massima", N0(edge) + " °C");
            Row(sb, "Hotspot massimo", N0(hot) + " °C");
            Row(sb, "Hotspot − edge medio / max", N0(dAvg) + " / " + N0(dMax) + " °C" + (double.IsNaN(Limits.TypicalDelta) ? "" : "  (tipico per la tua scheda " + N0(Limits.TypicalDelta) + " °C)"));
            double cpuT = Max(all, x => x.CpuTctl), cpuTAvg = Avg(play, x => x.CpuTctl);
            Row(sb, "CPU temperatura media / max", double.IsNaN(cpuT) ? "n/d (apri HWiNFO con la memoria condivisa)" : N0(cpuTAvg) + " / " + N0(cpuT) + " °C");
            Row(sb, "VRAM massima",double.IsNaN(vram) ? "n/d" : vram.ToString("0.0", CultureInfo.InvariantCulture) + " GB");
            sb.AppendLine();
            sb.AppendLine("--- FPS ---");
            Row(sb, "FPS medio / 1% low", double.IsNaN(fps) ? "n/d (" + (Fps.Status != "" ? Fps.Status : "nessun fotogramma letto") + ")" : N0(fps) + " / " + N0(fpsLow));
            if (fg) Row(sb, "Generazione di fotogrammi", "attiva (" + N0(fgShare * 100) + "% della partita): FPS calcolati davvero " + N0(fpsReal));
            else if (!double.IsNaN(fps)) Row(sb, "Generazione di fotogrammi", "non rilevata");
            sb.AppendLine();
            sb.AppendLine("--- Undervolt / overclock ---");
            int uvLevel; string uv = c.UvVerdict(out uvLevel);
            Row(sb, "Giudizio", (uvLevel == 0 ? "OK — " : uvLevel == 1 ? "ATTENZIONE — " : "PROBLEMA — ") + uv);
            if (!double.IsNaN(eff))
            {
                string cmp = "";
                if (!double.IsNaN(c.BaseEff) && c.BaseEff > 0)
                {
                    double d = (eff / c.BaseEff - 1) * 100;
                    cmp = "  (" + (d >= 0 ? "+" : "") + N0(d) + "% rispetto alle tue partite precedenti a " + c.Game + ")";
                }
                Row(sb, "Efficienza", eff.ToString("0.00", CultureInfo.InvariantCulture) + " FPS reali per watt" + cmp);
                sb.AppendLine("  (più è alta, meglio funziona l'undervolt: stessi FPS con meno watt. Confronta la stessa scena.)");
            }
            sb.AppendLine();
            sb.AppendLine("--- Stabilità ---");
            if (c.Throttles.Count == 0) sb.AppendLine("  Throttling: nessuno");
            else
            {
                sb.AppendLine("  Throttling: " + Volte(c.Throttles.Count) + ", " + Math.Round(c.ThrottleSec) + " s in totale");
                foreach (var t in c.Throttles.Take(30)) sb.AppendLine("    " + t);
                if (c.Throttles.Count > 30) sb.AppendLine("    ... e altre " + (c.Throttles.Count - 30));
            }
            sb.AppendLine("  Crash del driver video (TDR): " + c.Tdr);
            if (c.Crashes.Count == 0) sb.AppendLine("  Crash del gioco: nessuno");
            else { sb.AppendLine("  Crash del gioco: " + c.Crashes.Count); foreach (var t in c.Crashes) sb.AppendLine("    " + t); }
            if (c.Tuning != "") sb.AppendLine("  Tuning di Adrenalin: " + c.Tuning);

            var warn = new List<string>();
            // differenza hotspot-edge: confrontata con il valore tipico di questa scheda (impara partita dopo partita)
            var full = all.Where(x => x.Gpu3d > 85 && !double.IsNaN(x.GpuTempHot) && !double.IsNaN(x.GpuTempEdge)).ToList();
            double dLoad = full.Count >= 60 ? full.Average(x => x.GpuTempHot - x.GpuTempEdge) : double.NaN, typ = Limits.TypicalDelta;
            double dWarn = Math.Max(32, double.IsNaN(typ) ? 0 : typ + 10);
            if (!double.IsNaN(dLoad) && dLoad >= dWarn)
                warn.Add("Differenza hotspot − edge alta sotto carico (media " + N0(dLoad) + " °C" + (double.IsNaN(typ) ? "" : ", di solito " + N0(typ)) + "): il dissipatore potrebbe non toccare bene il chip. Controlla pasta termica e montaggio.");
            else Limits.LearnDelta(dLoad);   // partita normale: aggiorna il valore tipico
            if (c.ThermalThrottleSec > 0) warn.Add("La scheda ha rallentato per il calore per " + Math.Round(c.ThermalThrottleSec) + " s: vedi le cause sopra.");
            if (c.Tdr > 0) warn.Add("Il driver video si è bloccato " + Volte(c.Tdr) + ": se hai UV/OC attivo è probabilmente troppo spinto.");
            if (c.Tuning.StartsWith("RESET")) warn.Add("Dopo il crash Adrenalin ha tolto lo UV/OC: rimettilo (o salvane uno più prudente) in Adrenalin → Prestazioni → Ottimizzazione.");
            if (warn.Count > 0) { sb.AppendLine(); sb.AppendLine("--- AVVISI ---"); foreach (var w in warn) sb.AppendLine("  " + w); }

            string path = Path.Combine(dir, name);
            try { File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true)); } catch { path = ""; }

            SessionLog.Append(dir, new Dictionary<string, string> {
                { "inizio", c.Start.ToString("yyyy-MM-dd HH:mm:ss") }, { "fine", c.End.ToString("yyyy-MM-dd HH:mm:ss") }, { "gioco", c.Game },
                { "durata_min", F((c.End - c.Start).TotalMinutes, 1) }, { "potenza_media_w", F(pAvg, 1) }, { "potenza_picco_w", F(pPk, 0) },
                { "clock_medio_mhz", F(clkAvg, 0) }, { "clock_max_carico_mhz", F(clkMax, 0) }, { "mv_medio", F(mvAvg, 0) }, { "mv_carico", F(mvLoad, 0) },
                { "edge_max", F(edge, 0) }, { "hotspot_max", F(hot, 0) }, { "delta_hot_edge_max", F(dMax, 0) }, { "vram_max_gb", F(vram, 1) },
                { "fps_medio", F(fps, 1) }, { "fps_1low", F(fpsLow, 1) }, { "throttling_s", F(c.ThrottleSec, 0) }, { "tdr", c.Tdr.ToString() },
                { "crash_gioco", c.Crashes.Count.ToString() }, { "configurazione", c.Config }, { "optiscaler", opti }, { "fine_motivo", reason },
                { "report", path == "" ? "" : name }, { "fps_reali", F(fpsReal, 1) }, { "frame_gen", fg ? "si" : (double.IsNaN(fps) ? "" : "no") },
                { "fps_per_watt", double.IsNaN(eff) ? "" : Math.Round(eff, 3).ToString(CultureInfo.InvariantCulture) } });

            col.AddEvent(c.Tdr > 0 || c.Crashes.Count > 0 ? "WARN" : "INFO", "Sessione di gioco finita: " + c.Game + " (" + Dur(c.End - c.Start) + ", " + reason + ")" +
                         (c.Tdr > 0 ? ", " + c.Tdr + " crash del driver" : "") + (c.Crashes.Count > 0 ? ", gioco andato in crash" : "") +
                         (path != "" ? " — report " + name : ""));
        }

        // stato su disco: se il PC si spegne di colpo durante la partita, al riavvio la sessione non va persa
        void WriteState(GameSession c, DateTime now)
        {
            try { File.WriteAllText(StateFile, c.Start.ToString("yyyy-MM-dd HH:mm:ss") + "|" + now.ToString("yyyy-MM-dd HH:mm:ss") + "|" + c.Game + "|" + c.Config); } catch { }
        }

        void RecoverInterrupted()
        {
            try
            {
                if (!File.Exists(StateFile)) return;
                var p = File.ReadAllText(StateFile).Trim().Split(new[] { '|' }, 4);
                File.Delete(StateFile);
                DateTime a, b;
                if (p.Length < 3 || !DateTime.TryParseExact(p[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out a) ||
                    !DateTime.TryParseExact(p[1], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out b)) return;
                SessionLog.Append(dir, new Dictionary<string, string> {
                    { "inizio", p[0] }, { "fine", p[1] }, { "gioco", p[2] }, { "durata_min", F((b - a).TotalMinutes, 1) },
                    { "configurazione", p.Length > 3 ? p[3] : "" }, { "fine_motivo", "interrotta (spegnimento anomalo o BlackBox chiuso di colpo)" } });
                col.AddEvent("WARN", "La sessione di gioco di " + p[2] + " delle " + a.ToString("HH:mm") + " si è interrotta di colpo (spegnimento o chiusura forzata)");
            }
            catch { }
        }

        // ------------------------------------------------ utilita'
        static string Volte(int n) { return n == 1 ? "1 volta" : n + " volte"; }
        static void Row(StringBuilder sb, string label, string value) { sb.AppendLine("  " + label.PadRight(30) + value); }
        static double Avg(List<Sample> l, Func<Sample, double> f) { var v = l.Select(f).Where(x => !double.IsNaN(x)).ToList(); return v.Count > 0 ? v.Average() : double.NaN; }
        static double Max(List<Sample> l, Func<Sample, double> f) { var v = l.Select(f).Where(x => !double.IsNaN(x)).ToList(); return v.Count > 0 ? v.Max() : double.NaN; }
        static double MaxN(double a, double b) { if (double.IsNaN(b)) return a; return double.IsNaN(a) || b > a ? b : a; }
        static double Median(List<double> l) { var s = l.OrderBy(x => x).ToList(); return s[s.Count / 2]; }
        static string N0(double v) { return double.IsNaN(v) ? "n/d" : v.ToString("0", CultureInfo.InvariantCulture); }
        static string F(double v, int d) { return double.IsNaN(v) ? "" : Math.Round(v, d).ToString(CultureInfo.InvariantCulture); }
        public static string Dur(TimeSpan t)
        {
            if (t.TotalMinutes < 1) return Math.Max(0, (int)t.TotalSeconds) + " s";
            if (t.TotalHours < 1) return (int)t.TotalMinutes + " min";
            return (int)t.TotalHours + " h " + t.Minutes + " min";
        }
        static string Safe(string s)
        {
            foreach (var ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
            return s.Replace(' ', '_');
        }
    }

    // ---------------------------------------------------------------- Storico delle sessioni (sessioni.csv)
    public class SessionRow
    {
        public DateTime Start, End;
        public string Game = "", Config = "", Report = "";
        public double ClkMax = double.NaN, MvLoad = double.NaN, Eff = double.NaN;
        public int Tdr, Crash;
    }

    public class CrashItem
    {
        public DateTime Time;
        public string Game, Config, Kind;   // Kind: "TDR" (driver video) o "crash" (il gioco)
    }

    public static class SessionLog
    {
        public const string Header = "inizio;fine;gioco;durata_min;potenza_media_w;potenza_picco_w;clock_medio_mhz;clock_max_carico_mhz;mv_medio;mv_carico;edge_max;hotspot_max;delta_hot_edge_max;vram_max_gb;fps_medio;fps_1low;throttling_s;tdr;crash_gioco;configurazione;optiscaler;fine_motivo;report;fps_reali;frame_gen;fps_per_watt";

        public static string FilePath(string dir) { return Path.Combine(dir, "sessioni.csv"); }

        public static void Append(string dir, Dictionary<string, string> v)
        {
            try
            {
                string f = FilePath(dir);
                bool fresh = !File.Exists(f) || new FileInfo(f).Length == 0;
                // file di una versione precedente: riscrivo l'intestazione con le colonne nuove (la lettura usa l'ultima)
                if (!fresh && File.ReadLines(f).LastOrDefault(l => l.StartsWith("inizio;")) != Header) fresh = true;
                using (var fs = new FileStream(f, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, FileOptions.WriteThrough))
                using (var w = new StreamWriter(fs, new UTF8Encoding(false)))
                {
                    if (fresh) w.WriteLine(Header);
                    w.WriteLine(string.Join(";", Header.Split(';').Select(k =>
                    {
                        string x; return v.TryGetValue(k, out x) ? (x ?? "").Replace(";", ",").Replace("\r", " ").Replace("\n", " ") : "";
                    })));
                }
            }
            catch { }
        }

        public static List<SessionRow> Read(string dir)
        {
            var res = new List<SessionRow>();
            try
            {
                string f = FilePath(dir);
                if (!File.Exists(f)) return res;
                string[] lines;
                using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs)) lines = sr.ReadToEnd().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                string[] cols = Header.Split(';');
                foreach (var raw in lines)
                {
                    string l = raw.TrimEnd('\r');
                    if (l.StartsWith("inizio;")) { cols = l.Split(';'); continue; }
                    var a = l.Split(';');
                    Func<string, string> g = k => { int i = Array.IndexOf(cols, k); return i >= 0 && i < a.Length ? a[i] : ""; };
                    DateTime s, e;
                    if (!DateTime.TryParseExact(g("inizio"), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out s) ||
                        !DateTime.TryParseExact(g("fine"), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out e)) continue;
                    var r = new SessionRow { Start = s, End = e, Game = g("gioco"), Config = g("configurazione"), Report = g("report") };
                    double d; int n;
                    if (double.TryParse(g("clock_max_carico_mhz"), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) r.ClkMax = d;
                    if (double.TryParse(g("mv_carico"), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) r.MvLoad = d;
                    if (double.TryParse(g("fps_per_watt"), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) r.Eff = d;
                    if (int.TryParse(g("tdr"), out n)) r.Tdr = n;
                    if (int.TryParse(g("crash_gioco"), out n)) r.Crash = n;
                    res.Add(r);
                }
            }
            catch { }
            return res;
        }

        // Crash del driver video (Display 4101 dal Registro eventi) e crash dei giochi, con gioco e configurazione A/B
        public static List<CrashItem> Crashes(string dir, int days)
        {
            var res = new List<CrashItem>();
            var sessions = Read(dir);
            DateTime from = DateTime.Now.AddDays(-days);
            string since = DateTime.UtcNow.AddDays(-days).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            try
            {
                var q = new EventLogQuery("System", PathType.LogName,
                    "*[System[Provider[@Name='Display'] and EventID=4101 and TimeCreated[@SystemTime>='" + since + "']]]");
                using (var r = new EventLogReader(q))
                    for (EventRecord e = r.ReadEvent(); e != null; e = r.ReadEvent())
                        using (e)
                        {
                            DateTime t = e.TimeCreated ?? DateTime.MinValue;
                            var s = sessions.LastOrDefault(x => t >= x.Start.AddSeconds(-30) && t <= x.End.AddMinutes(2));
                            res.Add(new CrashItem { Time = t, Kind = "TDR", Game = s != null ? s.Game : "fuori dai giochi", Config = Cfg(Analyzer.ConfigAt(dir, t)) });
                        }
            }
            catch { }
            foreach (var s in sessions.Where(x => x.Start >= from && x.Crash > 0))
                for (int i = 0; i < s.Crash; i++)
                    res.Add(new CrashItem { Time = s.End, Kind = "crash", Game = s.Game, Config = Cfg(s.Config) });
            return res;
        }

        static string Cfg(string c) { return c == "" ? "(non indicata)" : c; }
    }
}
