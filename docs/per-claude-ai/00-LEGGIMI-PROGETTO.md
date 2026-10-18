# BlackBox — scheda del progetto (per il progetto su claude.ai)

Questo file spiega a Claude (su claude.ai, fuori da Claude Code) com'è fatto BlackBox, così
può rispondere alle domande del proprietario senza vedere il PC.

## Come preparare il progetto su claude.ai (lo fa il proprietario, una volta)

1. Sul PC, nella cartella del progetto, esegui:
   `powershell -ExecutionPolicy Bypass -File .\per-claude-ai.ps1`
   Si crea (o si aggiorna) la cartella `per-claude-ai\` con tutti i file da caricare.
2. Su claude.ai: **Progetti → Crea progetto**, nome «BlackBox».
3. **Istruzioni del progetto**: apri `per-claude-ai\ISTRUZIONI-DA-INCOLLARE.txt`, copia tutto
   il testo e incollalo.
4. **Conoscenza del progetto**: trascina dentro **tutti** i file della cartella
   `per-claude-ai\` (tranne ISTRUZIONI-DA-INCOLLARE.txt, che è già nelle istruzioni).
5. Dopo ogni nuova versione: riesegui lo script, togli i vecchi file dal progetto e
   carica i nuovi.

## Che cos'è

BlackBox è un programma per Windows (un solo `.exe`, C# per .NET Framework 4.x, interfaccia
WPF scura, testi in italiano) che:

- registra ogni secondo CPU, GPU, RAM e VRAM su disco e, dopo uno spegnimento improvviso
  (Kernel-Power 41), scrive un report con diagnosi automatica;
- quando l'utente gioca riconosce da solo la **sessione di gioco** e dà: FPS / 1% low /
  frame time (PresentMon di Intel), riconoscimento della **generazione di fotogrammi**,
  **overlay** in gioco, **controllo dello stato**, giudizio su **undervolt/overclock**
  (stabilità ed efficienza in FPS reali per watt), report di fine partita;
- gestisce **OptiScaler** nei giochi (installa / aggiorna / rimuove con copie di
  sicurezza, blocca i giochi con anti-cheat, controlla che sia caricato);
- riconosce l'hardware e usa i limiti di quel modello (niente falsi allarmi).

Proprietario: non è un programmatore. PC: Ryzen 7 7800X3D, Radeon RX 7800 XT Sapphire Nitro+,
32 GB, Windows 11. Repository: https://github.com/Pronerious/blackbox-crash-recorder
(branch `main`). Versione attuale: **1.2.4** (release del 18/10/2026).

## Regole fisse

- **Nessun amministratore**: il programma e l'installer funzionano senza. Unica eccezione
  facoltativa: una volta, per gli FPS, aggiungere l'utente al gruppo *Performance Log Users*.
- **Privacy**: nessuna telemetria. Le uniche connessioni di rete sono i download che
  l'utente avvia con un pulsante, dalle release ufficiali su GitHub: PresentMon
  (verificato con la firma digitale di Intel) e OptiScaler (verificato con SHA-256).
- **Leggerezza in gioco**: non deve mai togliere FPS. Misurato circa 0,15–0,3% di CPU con
  l'overlay; in partita priorità bassa, controlli più radi, niente notifiche.
- **Niente falsi allarmi**: soglie sul modello di CPU/GPU e medie di 30 secondi.
- **OptiScaler a prova di errore**: bloccato nei giochi con anti-cheat (file) e nei giochi online noti (elenco `OnlineGamesList` in Opti.cs, es. VAC) e nei giochi a 32 bit; conferma «solo offline» prima di installare; avviso se c'è già un file con lo stesso nome (ReShade).
- **Mai chiudere programmi dell'utente**: lo sblocco automatico tocca solo un elenco fisso
  di componenti di Windows, mai durante una partita.
- La **guida è dentro l'exe** (`GUIDA.md` incorporata, pulsante Guida) e va aggiornata a
  ogni modifica, insieme al README.
- Nessuna dipendenza esterna, nessun NuGet: si compila con il `csc.exe` di Windows.
- Colonne nuove dei CSV sempre **in coda**, per non rompere i file vecchi.

## Dove sta cosa (codice)

| File | Contenuto |
|---|---|
| `src/App.cs` | avvio, finestra principale, icona nella barra, riquadri, controllo stato, benvenuto del primo avvio, versione (`AssemblyVersion`) |
| `src/MainWindow.xaml` | aspetto della finestra principale (stili `Card`, `H`, pulsanti) |
| `src/Core.cs` | registratore: contatori di Windows (PDH), sensori GPU AMD (ADL) e NVIDIA (NVML), CSV, Registro eventi, sblocco prudente, analisi dei crash e diagnosi |
| `src/Sessions.cs` | sessioni di gioco, throttling, reset del tuning di Adrenalin, giudizio UV/OC, report di fine partita, `sessioni.csv` |
| `src/Fps.cs` | impostazioni (`impostazioni.ini`), download da GitHub, PresentMon (FPS, 1% low, generazione di fotogrammi), permesso una tantum |
| `src/Overlay.cs` | overlay in gioco (finestra trasparente click-through, tasto rapido, personalizzazione) |
| `src/Health.cs` | controllo dello stato e limiti (`Limits`) |
| `src/LimitsDb.cs` + `data/limiti.json` | tabella dei limiti di temperatura ufficiali (AMD, Intel, NVIDIA) con fonte per ogni riga; pulsante «Aggiorna i limiti» la scarica dal repository (solo su richiesta) |
| `src/Glossary.cs` | spiegazioni in parole semplici di ogni termine (pulsante «Cos'è?»), uguali al punto 17 della guida |
| `src/StatusWindow.cs` | finestra «Controllo dello stato» / benvenuto, con i pulsanti che sistemano i problemi |
| `src/Hardware.cs` | riconoscimento di CPU / GPU / driver / Windows e limiti per modello; impostazioni utili di Windows (HAGS, Modalità gioco) e loro correzione (`ApplyFix`) |
| `src/CpuTemp.cs` | temperatura CPU da HWiNFO (memoria condivisa) o LibreHardwareMonitor |
| `src/Opti.cs` | OptiScaler: ricerca giochi, upscaler, anti-cheat, pacchetto, installa/rimuovi/aggiorna, controllo caricamento |
| `src/OptiWindow.cs` | finestra «Giochi & OptiScaler» |
| `setup/Setup.cs` | installer senza amministratore (anche disinstallatore; `/silent`, `/uninstall /quiet`) |
| `build.ps1` | compila `dist\BlackBox.exe` e `dist\BlackBox-Setup-<versione>.exe` |

## Comandi (sul PC)

- Compilare: `powershell -ExecutionPolicy Bypass -File .\build.ps1` (con BlackBox chiuso,
  oppure lo chiude l'installer).
- Aggiornare la copia installata: `dist\BlackBox-Setup-<versione>.exe /silent`.
- BlackBox è installato in `%LocalAppData%\Programs\BlackBox` e parte da lì con Windows.
- Opzioni: `--tray`, `--logdir <cartella>`, `--exit` (chiude BlackBox aperto).

## Dati dell'utente (Documenti\BlackBox)

`monitor_AAAA-MM-GG.csv` (1 riga al secondo), `eventi.log`, `crash_*.txt`,
`sessione_<gioco>_<data>.txt`, `sessioni.csv`, `configurazioni.log` (test A/B),
`impostazioni.ini` (overlay, valori imparati, hardware visto), `tools\PresentMon.exe`,
`optiscaler\<versione>\`.

Chiavi utili di `impostazioni.ini`: `overlay_attivo`, `overlay_forma` (blocco/barra),
`overlay_posizione`, `overlay_testo`, `overlay_colore_testo`, `overlay_colore_grafico`,
`overlay_sfondo`, `overlay_mostra`, `overlay_quando`, `overlay_tasto`, `presentmon`,
`gpu_delta_tipico` (differenza hotspot-edge imparata), `hw_gpu`, `hw_driver`, `hw_cpu`,
`giochi_visti`, `opti_cartelle`, `opti_esito:<cartella>`.

## Soglie principali (data/limiti.json, Hardware.cs, Health.cs)

I limiti per modello stanno in `data/limiti.json` (verificati sulle pagine ufficiali il 18/10/2026; regole provate dall'alto, vince la prima). Per aggiungere un modello nuovo: aggiungere una riga con fonte e cambiare "versione" (data) nel file, poi pubblicare su main: gli utenti la ricevono con «Aggiorna i limiti».


- Radeon RDNA (RX 5000–9000): hotspot max 110 °C → attenzione da 100, problema da 107
  (media 30 s). NVIDIA: temperatura chip attenzione 83, problema 88.
- CPU: Ryzen 7000X3D 89 °C, 5000X3D 90, Ryzen 7000/9000 95 (ci arrivano per progetto),
  Intel 100, Core Ultra 105. Attenzione a limite − 4.
- Differenza hotspot − edge: attenzione oltre max(32, tipico + 10), problema oltre
  max(40, tipico + 15), solo con GPU > 85%. Sulla Nitro+ dell'utente è tipicamente ~23 °C.
- Throttling: GPU > 95% e clock < 85% del tipico per oltre 5 s; avviso solo se da calore.
- Reset del tuning: dopo TDR / spegnimento anomalo / aggiornamento driver, alla partita
  seguente: tensione più alta di 35 mV o clock diverso del 6% rispetto alle partite
  precedenti.
- Generazione di fotogrammi: fotogrammi «leggeri» (lavoro GPU < 35% del 75° percentile)
  tra il 35% e l'80% e alternati a quelli pesanti.

## Situazione al 18/10/2026

- **Versione 1.2.4** pubblicata; il repository è **pubblico** (dal 18/10, storia controllata: nessun dato
  personale). Le novità di ogni versione sono in `CHANGELOG.md`.
- BlackBox è **installato** sul PC dell'utente con l'installer (`%LocalAppData%\Programs\BlackBox`, avvio
  automatico da lì). La cartella `dist\` del progetto serve solo per compilare.
- Sul PC dell'utente: HWiNFO con memoria condivisa attiva e **riavvio automatico di HWiNFO** attivo
  (operazione pianificata «BlackBox\Riavvia HWiNFO»); temperatura CPU letta (~54 °C a riposo).
  **HAGS è ancora spento**: si accende dal Controllo stato (pulsante «Accendi HAGS», poi riavvio).
- L'1% low rosso visto dall'utente il 17/10 era tutto dovuto all'Alt+Tab: durante il gioco vero nessuno scatto.
- Il vecchio script «Monitor Spegnimenti» in Esecuzione automatica è stato tolto (collegamento nel Cestino).
- Idee per il futuro: firma digitale degli exe (niente avviso SmartScreen), grafici delle sessioni nella
  finestra, aggiornamento del programma con un pulsante (solo su richiesta).