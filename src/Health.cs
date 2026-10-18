using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Win32;

namespace BlackBox
{
    // ---------------------------------------------------------------- Controllo dello stato: "e' tutto ok?"
    // Ogni voce: livello (0 ok, 1 attenzione, 2 problema, -1 informazione), testo breve (overlay) e spiegazione.
    public class HealthItem
    {
        public int Level;
        public string Short, Text;
        public string Fix = "", FixLabel = "";   // azione del pulsante "Sistema" (vuoto = nessuna)
    }

    // ---------------------------------------------------------------- Limiti della scheda e della CPU di questo PC
    public static class Limits
    {
        // limiti scelti in base al modello riconosciuto (vedi Hardware.ChooseLimits)
        public static double HotMax { get { return Hardware.GpuHotMax; } }
        public static double HotWarn { get { return Hardware.GpuHotWarn; } }
        public static double HotBad { get { return Hardware.GpuHotMax - 3; } }
        public static double EdgeWarn { get { return Hardware.GpuEdgeWarn; } }
        public static double EdgeBad { get { return Hardware.GpuEdgeBad; } }
        public static double CpuMax { get { return Hardware.CpuMax; } }
        // differenza hotspot-edge tipica di questa scheda sotto carico, imparata dalle partite (NaN all'inizio)
        public static double TypicalDelta
        {
            get { double v; return double.TryParse(Settings.Get("gpu_delta_tipico", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : double.NaN; }
        }
        public static void LearnDelta(double sessionDelta)
        {
            if (double.IsNaN(sessionDelta) || sessionDelta <= 0) return;
            double old = TypicalDelta, v = double.IsNaN(old) ? sessionDelta : old * 0.8 + sessionDelta * 0.2;
            Settings.Set("gpu_delta_tipico", v.ToString("0.0", CultureInfo.InvariantCulture));
        }

    }

    public static class Health
    {
        static double vramTotalMb = -1;

        // memoria video totale della scheda (dal Registro, scritta dal driver); NaN se sconosciuta
        public static double VramTotalMb
        {
            get
            {
                if (vramTotalMb >= 0) return vramTotalMb == 0 ? double.NaN : vramTotalMb;
                vramTotalMb = 0;
                try
                {
                    using (var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                        foreach (var n in cls.GetSubKeyNames())
                            try
                            {
                                using (var k = cls.OpenSubKey(n))
                                {
                                    var v = k == null ? null : k.GetValue("HardwareInformation.qwMemorySize");
                                    if (v is long) vramTotalMb = Math.Max(vramTotalMb, (long)v / 1048576.0);
                                }
                            }
                            catch { }
                }
                catch { }
                return vramTotalMb == 0 ? double.NaN : vramTotalMb;
            }
        }

        static string N(double v, string f = "0") { return double.IsNaN(v) ? "n/d" : v.ToString(f, CultureInfo.CurrentCulture); }

        public static List<HealthItem> Check(Collector col)
        {
            var r = new List<HealthItem>();
            Action<int, string, string> add = (l, s, t) => r.Add(new HealthItem { Level = l, Short = s, Text = t });
            var h = col.GetHistory(30);
            var s0 = h.Length > 0 ? h[h.Length - 1] : null;
            if (s0 == null) { add(-1, "avvio...", "BlackBox sta raccogliendo i primi dati."); return r; }
            var c = col.Games.Current;
            bool playing = col.Games.InSession;
            Func<Func<Sample, double>, double> avg = f => { var v = h.Select(f).Where(x => !double.IsNaN(x)).ToList(); return v.Count > 0 ? v.Average() : double.NaN; };
            Func<Func<Sample, double>, double> max = f => { var v = h.Select(f).Where(x => !double.IsNaN(x)).ToList(); return v.Count > 0 ? v.Max() : double.NaN; };

            // ---- temperature della scheda video: MEDIA degli ultimi 30 s (un picco di un attimo non e' un problema)
            double hot = avg(x => x.GpuTempHot), edge = avg(x => x.GpuTempEdge), hotMax = max(x => x.GpuTempHot);
            if (double.IsNaN(hot) && double.IsNaN(edge)) add(-1, "GPU n/d", "Sensori della scheda video non disponibili.");
            else if (hot >= Limits.HotBad || edge >= Limits.EdgeBad) add(2, "GPU troppo calda " + N(hot) + "°", "Hotspot medio " + N(hot) + " °C negli ultimi 30 s: vicino al limite di " + N(Limits.HotMax) + " °C. Controlla ventole, flusso d'aria e pasta termica.");
            else if (hot >= Limits.HotWarn || edge >= Limits.EdgeWarn) add(1, "GPU calda " + N(hot) + "°", "Hotspot medio " + N(hot) + " °C: alto ma ancora nei limiti (questa scheda regge fino a " + N(Limits.HotMax) + " °C). Più aria nel case aiuta.");
            else add(0, "temperature GPU ok", "Temperature della scheda video ok: edge " + N(edge) + " °C, hotspot " + N(hot) + " °C (massimo " + N(hotMax) + " °C; limite " + N(Limits.HotMax) + " °C).");

            // ---- differenza hotspot - edge: solo sotto carico vero e confrontata con il valore tipico di QUESTA scheda
            var loaded = h.Where(x => x.Gpu3d > 85 && !double.IsNaN(x.GpuTempHot) && !double.IsNaN(x.GpuTempEdge)).ToList();
            if (loaded.Count >= 20)
            {
                double d = loaded.Average(x => x.GpuTempHot - x.GpuTempEdge), typ = Limits.TypicalDelta;
                double warnAt = Math.Max(32, double.IsNaN(typ) ? 0 : typ + 10), badAt = Math.Max(40, double.IsNaN(typ) ? 0 : typ + 15);
                string tipico = double.IsNaN(typ) ? "" : " (tipico per la tua scheda: " + N(typ) + " °C)";
                if (d >= badAt) add(2, "hotspot−edge " + N(d) + "°", "Differenza hotspot − edge di " + N(d) + " °C sotto carico" + tipico + ": molto più del normale, il dissipatore probabilmente non tocca bene il chip (pasta termica o montaggio).");
                else if (d >= warnAt) add(1, "hotspot−edge " + N(d) + "°", "Differenza hotspot − edge di " + N(d) + " °C sotto carico" + tipico + ": più alta del solito, da tenere d'occhio.");
                else add(0, "contatto dissipatore ok", "Differenza hotspot − edge " + N(d) + " °C sotto carico" + tipico + ": normale, il dissipatore lavora bene.");
            }

            // ---- temperatura CPU (limite del modello: 7800X3D 89 °C, Ryzen 7000/9000 95 °C per progetto, ...)
            double cpuT = avg(x => x.CpuTctl);
            if (double.IsNaN(cpuT)) add(-1, "CPU °C n/d", "Temperatura della CPU non disponibile: apri HWiNFO con la memoria condivisa (guida, punto 11).");
            else if (cpuT >= Limits.CpuMax) add(2, "CPU al limite " + N(cpuT) + "°", "CPU a " + N(cpuT) + " °C di media: al limite del modello (" + N(Limits.CpuMax) + " °C), sta rallentando per proteggersi. Controlla il dissipatore della CPU.");
            else if (cpuT >= Limits.CpuMax - 4) add(1, "CPU calda " + N(cpuT) + "°", "CPU a " + N(cpuT) + " °C di media: vicina al limite del modello (" + N(Limits.CpuMax) + " °C).");
            else add(0, "CPU ok " + N(cpuT) + "°", "Temperatura CPU ok: " + N(cpuT) + " °C (limite del modello " + N(Limits.CpuMax) + " °C, letta da " + CpuTemp.Source + ").");
            // ---- undervolt / overclock e stabilita'
            if (c != null)
            {
                int lv; string uv = c.UvVerdict(out lv);
                if (lv == 0 && (DateTime.Now - c.LastThrottle).TotalSeconds < 60 && c.LastThrottleThermal) { lv = 1; uv = "la scheda sta rallentando perché è troppo calda"; }
                add(lv, "UV/OC " + (lv == 0 ? "stabile" : lv == 1 ? "attenzione" : "problema"), "Undervolt/overclock: " + uv + ".");
            }
            else if (col.Games.TuningPending)
                add(-1, "UV/OC da ricontrollare", "Dopo l'ultimo crash controllerò alla prossima partita se Adrenalin ha rimesso i valori di fabbrica.");

            // ---- efficienza (FPS reali per watt) in partita
            if (playing)
            {
                double eff = avg(x => x.Gpu3d > 60 && x.GpuPower > 20 && !double.IsNaN(x.FpsReal) ? x.FpsReal / x.GpuPower : double.NaN);
                if (!double.IsNaN(eff))
                {
                    string cmp = "";
                    if (c != null && !double.IsNaN(c.BaseEff) && c.BaseEff > 0)
                    {
                        double d = (eff / c.BaseEff - 1) * 100;
                        cmp = " (" + (d >= 0 ? "+" : "") + N(d) + "% rispetto alle tue partite precedenti)";
                    }
                    add(-1, N(eff, "0.00") + " FPS/W", "Efficienza: " + N(eff, "0.00") + " FPS reali per watt" + cmp + ". Più è alta, meglio funziona l'undervolt.");
                }
            }

            // ---- fluidita'
            if (!double.IsNaN(s0.Fps) && !double.IsNaN(s0.Fps1Low))
            {
                double ratio = s0.Fps1Low / s0.Fps;
                bool stutter = ratio < 0.5 && s0.Fps1Low < 45;   // scatti: momenti peggiori molto sotto la media
                bool slow = !stutter && s0.Fps1Low < 30;         // non scatta, ma va lento in generale
                if (stutter || slow)
                {
                    string cause, fix;
                    StutterCause(col, s0, slow, out cause, out fix);
                    add(1, (stutter ? "scatti" : "lento") + " (1% low " + N(s0.Fps1Low) + "): " + cause,
                        (stutter ? "Il gioco scatta: nei momenti peggiori va a " + N(s0.Fps1Low) + " FPS contro una media di " + N(s0.Fps) + "."
                                 : "Il gioco va lento in generale: " + N(s0.Fps) + " FPS di media, " + N(s0.Fps1Low) + " nei momenti peggiori.") +
                        "\nCausa più probabile: " + cause + ".\nCosa fare: " + fix);
                }
                else add(0, "fluido", "Fluidità ok: 1% low " + N(s0.Fps1Low) + " su " + N(s0.Fps) + " FPS.");
            }
            if (s0.FgFactor > 1 && !double.IsNaN(s0.Fps))
                add(-1, "FG " + s0.FgFactor + "x", "Generazione di fotogrammi attiva (" + s0.FgFactor + "x): vedi " + N(s0.Fps) + " FPS, quelli calcolati davvero sono circa " + N(s0.FpsReal) + ". La reattività ai comandi segue quelli veri.");

            // ---- memoria
            double vt = VramTotalMb;
            if (!double.IsNaN(vt) && !double.IsNaN(s0.VramMb))
            {
                double p = s0.VramMb / vt * 100;
                bool stutter = !double.IsNaN(s0.Fps1Low) && !double.IsNaN(s0.Fps) && s0.Fps1Low < 45 && s0.Fps1Low / s0.Fps < 0.5;
                // i giochi riempiono la VRAM di proposito: e' un problema solo se intanto il gioco scatta
                if (p >= 95 && stutter) add(1, "VRAM piena " + N(p) + "%", "Memoria video al " + N(p) + "% e il gioco scatta: abbassa la qualità delle texture.");
                else if (p >= 95) add(-1, "VRAM " + N(p) + "%", "Memoria video al " + N(p) + "%: è normale che i giochi la riempiano, non scatta quindi va bene.");
                else add(0, "VRAM ok", "Memoria video: " + N(s0.VramMb / 1024, "0.0") + " GB su " + N(vt / 1024, "0") + " GB.");
            }
            if (s0.RamCommit >= 95) add(1, "RAM quasi piena", "Memoria del sistema impegnata al " + N(s0.RamCommit) + "%: chiudi i programmi che non servono.");

            // ---- programmi che consumano tanto in background: solo informazione, BlackBox non li chiude mai
            string hogs = col.BackgroundHogs;
            if (hogs != "") r.Add(new HealthItem { Level = -1, Short = "CPU in background", Fix = "taskmgr", FixLabel = "Apri Gestione attività", Text = "In background consumano molto da oltre 10 minuti: " + hogs + ". Può essere un lavoro normale (antivirus, aggiornamenti, download); se non sai cos'è e dura ore, chiudilo da Gestione attività. BlackBox non lo chiude da solo." });
            // ---- strumenti
            if (playing && double.IsNaN(s0.Fps)) add(-1, "FPS n/d", "FPS non disponibili: " + (Fps.ToolPresent ? (Fps.Status != "" ? Fps.Status : "in attesa dei fotogrammi") : "scarica PresentMon (guida, punto 12)") + ".");
            return r;
        }

        // Perche' l'1% low e' basso? Guarda l'ultimo minuto e sceglie la causa piu' probabile, con la soluzione.
        // L'ordine conta: prima le cause certe (inizio partita, memoria piena, calore), poi quelle di "collo di bottiglia".
        static void StutterCause(Collector col, Sample s0, bool slow, out string cause, out string fix)
        {
            var h = col.GetHistory(60);
            Func<Func<Sample, double>, double> avg = f => { var v = h.Select(f).Where(x => !double.IsNaN(x)).ToList(); return v.Count > 0 ? v.Average() : double.NaN; };
            var c = col.Games.Current;
            double vt = VramTotalMb, vram = h.Select(x => x.VramMb).Where(x => !double.IsNaN(x)).DefaultIfEmpty(double.NaN).Max();
            double coreMax = avg(x => x.CpuCoreMax), gpu = avg(x => x.Gpu3d), ram = avg(x => x.RamCommit);
            string hogs = col.BackgroundHogs;

            if (c != null && (DateTime.Now - c.Start).TotalMinutes < 3 && !slow)
            { cause = "inizio partita (compilazione degli shader e caricamenti)"; fix = "è normale nei primi minuti di gioco o dopo un aggiornamento del gioco o del driver: di solito sparisce da solo dopo qualche minuto."; return; }
            if (!double.IsNaN(vt) && !double.IsNaN(vram) && vram / vt >= 0.95)
            { cause = "memoria video (VRAM) piena"; fix = "abbassa la qualità delle texture di un livello nelle opzioni grafiche del gioco."; return; }
            if (c != null && (DateTime.Now - c.LastThrottle).TotalSeconds < 60 && c.LastThrottleThermal)
            { cause = "la scheda video rallenta per il calore"; fix = "migliora il raffreddamento: pulisci i filtri, alza le ventole del case o della scheda (Adrenalin → Prestazioni → Ottimizzazione), controlla la pasta termica."; return; }
            if (!double.IsNaN(ram) && ram >= 90)
            { cause = "memoria del sistema (RAM) quasi piena"; fix = "chiudi i programmi che non servono mentre giochi (browser con tante schede, launcher, programmi di registrazione)."; return; }
            if (hogs != "")
            { cause = "un programma in background usa molto il processore (" + hogs + ")"; fix = "se non ti serve, chiudilo da Gestione attività (Ctrl + Maiusc + Esc)."; return; }
            if (!double.IsNaN(coreMax) && !double.IsNaN(gpu) && coreMax >= 90 && gpu < 90)
            { cause = "processore al limite (la scheda video aspetta il processore)"; fix = "abbassa le opzioni che pesano sul processore (densità della folla, distanza visiva, fisica, ray tracing), chiudi i programmi in background; un limite agli FPS poco sotto la media rende tutto più regolare."; return; }
            if (s0.FgFactor > 1 && !double.IsNaN(s0.FpsReal) && s0.FpsReal < 60)
            { cause = "generazione di fotogrammi con pochi FPS veri (" + Math.Round(s0.FpsReal) + ")"; fix = "la generazione di fotogrammi funziona bene sopra i 60 FPS veri: abbassa la qualità grafica o l'upscaler (Qualità → Bilanciato) finché gli FPS reali superano 60."; return; }
            if (slow && !double.IsNaN(gpu) && gpu >= 95)
            { cause = "scheda video al massimo"; fix = "abbassa la qualità grafica o la risoluzione, oppure usa un upscaler (FSR / XeSS) in modalità Qualità o Bilanciato."; return; }
            cause = "picchi isolati (caricamenti di zone nuove, effetti pesanti)";
            fix = "se succede spesso, imposta un limite agli FPS poco sotto la media (nel gioco o con Radeon Chill in Adrenalin): i picchi si sentono molto meno.";
        }

        // riassunto: livello peggiore e frase breve
        public static string Summary(List<HealthItem> items, out int level)
        {
            level = items.Count == 0 ? 0 : items.Max(x => x.Level);
            var bad = items.Where(x => x.Level >= 1).OrderByDescending(x => x.Level).ToList();
            if (bad.Count == 0) return "tutto ok";
            return string.Join(" · ", bad.Take(2).Select(x => x.Short)) + (bad.Count > 2 ? " (+" + (bad.Count - 2) + ")" : "");
        }
    }
}
