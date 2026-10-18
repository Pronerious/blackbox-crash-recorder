using System;
using System.Linq;

namespace BlackBox
{
    // ---------------------------------------------------------------- Le parole difficili, spiegate
    // Per ogni voce del controllo dello stato: cos'e' e a cosa serve, in parole semplici (pulsante "Cos'e'?").
    // Gli stessi testi sono nella guida (punto 17).
    public static class Glossary
    {
        public const string Hags =
            "HAGS vuol dire «Pianificazione GPU con accelerazione hardware» (in inglese Hardware-Accelerated GPU Scheduling).\n" +
            "Di solito è il processore, tramite Windows, a fare da «vigile urbano» per la scheda video: decide in che ordine la scheda esegue i lavori di ogni programma. " +
            "Con HAGS acceso è la scheda video stessa a gestire la sua coda di lavori.\n" +
            "A cosa serve: un po' meno lavoro per il processore e un po' meno ritardo tra il tuo comando e l'immagine. Soprattutto serve alla generazione di fotogrammi: " +
            "quella di NVIDIA (DLSS 3) lo richiede, per quella di AMD (FSR 3) e per OptiScaler è consigliato.\n" +
            "È un'impostazione normale di Windows 10 e 11: non fa danni, vale dopo un riavvio e si spegne dallo stesso punto (Impostazioni → Sistema → Schermo → Grafica).";

        public const string GameMode =
            "La Modalità gioco è una funzione di Windows: quando giochi, Windows dà la precedenza al gioco e rimanda i lavori in background " +
            "(per esempio gli aggiornamenti di Windows Update). Così ci sono meno scatti. Fuori dai giochi non cambia niente. Si trova in Impostazioni → Giochi → Modalità gioco.";

        public const string Hwinfo =
            "HWiNFO è un programma gratuito che legge tutti i sensori del PC, compresa la temperatura vera del processore. " +
            "BlackBox non può leggerla da solo (servirebbe installare un «driver» speciale nel sistema, cosa che volutamente non fa) e quindi la chiede a HWiNFO.\n" +
            "La «memoria condivisa» («Supporto memoria condivisa», in inglese Shared Memory Support) è il permesso che HWiNFO dà agli altri programmi di leggere i suoi valori. Basta una spunta, una volta sola.\n" +
            "Attenzione: nella versione gratuita di HWiNFO la memoria condivisa si spegne da sola 12 ore dopo l'avvio di HWiNFO; chiudendo e riaprendo HWiNFO riparte. Senza limite: HWiNFO Pro (a pagamento) o LibreHardwareMonitor (gratuito).";

        public const string CpuTemp =
            "La temperatura del processore (Tctl/Tdie) è il valore che il processore usa per proteggersi: se arriva al suo limite rallenta da solo per non scaldare troppo.\n" +
            "Ogni modello ha il suo limite: il tuo è " + "{cpumax}" + " °C. Arrivare vicino al limite per un attimo è normale; restarci a lungo vuol dire raffreddamento insufficiente.";

        public const string GpuTemp =
            "La scheda video ha due temperature principali:\n" +
            "•  edge: la temperatura media della superficie del chip;\n" +
            "•  hotspot: il punto più caldo del chip, sempre più alto dell'edge.\n" +
            "Le schede reggono temperature alte per progetto: la tua fino a " + "{hotmax}" + " °C di hotspot. BlackBox avvisa solo se la media degli ultimi 30 secondi è davvero alta, non per un picco di un attimo.";

        public const string Delta =
            "È la differenza tra il punto più caldo del chip (hotspot) e la sua temperatura media (edge). Dice quanto bene il dissipatore «tocca» il chip.\n" +
            "Una differenza normale per la tua scheda è intorno a " + "{delta}" + " °C. Se cresce molto rispetto al solito, la pasta termica si sta seccando o il dissipatore non appoggia bene.";

        public const string UvOc =
            "Undervolt (UV): far lavorare la scheda video con meno tensione (meno volt). Scalda meno, consuma meno e spesso va uguale o meglio. Overclock (OC): farla andare più veloce.\n" +
            "Se si esagera, la scheda diventa instabile: il driver si blocca (schermo nero per un attimo, «TDR») o il gioco si chiude. " +
            "BlackBox guarda proprio questo: se non ci sono crash né rallentamenti per il calore, il tuo UV/OC è stabile.\n" +
            "Attenzione: dopo un crash Adrenalin spesso rimette i valori di fabbrica senza dirtelo. BlackBox se ne accorge e ti avvisa.";

        public const string Efficiency =
            "FPS per watt: quanti fotogrammi al secondo (veri) ottieni per ogni watt che consuma la scheda video. È il numero migliore per capire se un undervolt funziona: " +
            "stessi FPS con meno watt = numero più alto = undervolt riuscito. Confronta sempre la stessa scena dello stesso gioco.";

        public const string Fluidity =
            "FPS: fotogrammi al secondo, cioè quante immagini il gioco disegna ogni secondo. Più sono, più il gioco è fluido.\n" +
            "1% low: gli FPS nei momenti peggiori (l'1% dei fotogrammi più lenti dell'ultimo minuto). Se è molto più basso degli FPS medi, il gioco «scatta» anche se la media è alta. " +
            "Cause tipiche: caricamenti, memoria video piena, processore al limite, compilazione degli shader.";

        public const string FrameGen =
            "Generazione di fotogrammi (FSR 3, DLSS Frame Generation, AFMF): la scheda inventa un'immagine intermedia tra due immagini vere. Il gioco sembra più fluido (FPS quasi doppi), " +
            "ma la risposta ai comandi resta quella dei fotogrammi veri. Per questo BlackBox mostra anche gli «FPS reali».";

        public const string Vram =
            "VRAM: la memoria della scheda video, dove il gioco tiene texture e modelli. I giochi la riempiono di proposito, quindi vederla quasi piena è normale. " +
            "Diventa un problema solo se intanto il gioco scatta: allora conviene abbassare la qualità delle texture.";

        public const string Ram =
            "RAM impegnata: quanta memoria del sistema hanno chiesto tutti i programmi aperti. Se si avvicina al 100%, Windows deve usare il disco al posto della memoria e tutto rallenta. " +
            "Chiudere i programmi che non servono (browser con tante schede, launcher) libera memoria.";

        public const string PresentMon =
            "PresentMon è un piccolo programma gratuito di Intel che legge da Windows quando il gioco mostra ogni fotogramma: è il modo più preciso per misurare FPS e frame time, " +
            "usato anche dai siti di recensioni. BlackBox lo scarica solo se premi il pulsante, lo controlla (firma digitale di Intel) e lo usa solo mentre giochi.";

        public const string FpsPermission =
            "Per leggere gli FPS, Windows deve permettere al tuo utente di vedere gli «eventi della grafica». Il permesso si chiama «Performance Log Users» (Utenti registro prestazioni): " +
            "è un gruppo normale di Windows che dà solo questo diritto di lettura. Si chiede una volta, con una conferma da amministratore; poi bisogna uscire da Windows e rientrare.";

        public const string MemoryIntegrity =
            "Integrità della memoria (Sicurezza di Windows → Sicurezza dispositivi → Isolamento core): protegge Windows da driver e programmi malevoli. " +
            "Può togliere qualche FPS, ma è una scelta di sicurezza: BlackBox non la tocca e non consiglia di spegnerla.";

        public const string Background =
            "Un programma che, mentre non lo usi, consuma per molti minuti almeno un intero «core» del processore (uno dei suoi motori). " +
            "Può essere un lavoro normale (antivirus, aggiornamenti, download) oppure un programma bloccato. BlackBox non lo chiude mai da solo: in Gestione attività vedi cos'è e decidi tu.";

        public const string TuningCheck =
            "Dopo un crash del driver video o uno spegnimento improvviso, Adrenalin (il programma della scheda AMD) spesso rimette da solo i valori di fabbrica, " +
            "cancellando il tuo undervolt/overclock. Alla prossima partita BlackBox confronta tensione e clock con le partite precedenti e ti dice se è successo.";

        public const string Lhm =
            "LibreHardwareMonitor è un programma gratuito, alternativa a HWiNFO, che legge i sensori del PC. Per far leggere i suoi valori a BlackBox va aperto come amministratore.";

        // spiegazione per una voce del controllo dello stato (null = nessuna)
        public static string For(HealthItem it)
        {
            string s = ((it.Short ?? "") + " " + (it.Fix ?? "")).ToLowerInvariant();
            string t;
            if (s.Contains("hags")) t = Hags;
            else if (s.StartsWith("scatti") || s.StartsWith("lento") || s.Contains("fluido")) t = Fluidity;
            else if (s.Contains("modalità gioco") || s.Contains("gamemode")) t = GameMode;
            else if (s.Contains("hwinfo")) t = Hwinfo;
            else if (s.Contains("libre")) t = Lhm;
            else if (s.Contains("presentmon") || s.Contains("fps n/d")) t = PresentMon;
            else if (s.Contains("permesso fps") || s.Contains("fpsperm")) t = FpsPermission;
            else if (s.Contains("integrità")) t = MemoryIntegrity;
            else if (s.Contains("hotspot") || s.Contains("dissipatore")) t = Delta;
            else if (s.Contains("gpu")) t = GpuTemp;
            else if (s.Contains("cpu in background") || s.Contains("taskmgr")) t = Background;
            else if (s.Contains("cpu") || s.Contains("temperatura cpu")) t = CpuTemp;
            else if (s.Contains("uv/oc")) t = s.Contains("ricontrollare") ? TuningCheck : UvOc;
            else if (s.Contains("fps/w")) t = Efficiency;
            else if (s.StartsWith("fg ")) t = FrameGen;
            else if (s.Contains("vram")) t = Vram;
            else if (s.Contains("ram")) t = Ram;
            else return null;
            return t.Replace("{cpumax}", Limits.CpuMax.ToString("0"))
                    .Replace("{hotmax}", Limits.HotMax.ToString("0"))
                    .Replace("{delta}", double.IsNaN(Limits.TypicalDelta) ? "20-25" : Limits.TypicalDelta.ToString("0"));
        }
    }
}
