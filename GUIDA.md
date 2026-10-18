# Guida a BlackBox

BlackBox è la "scatola nera" del PC: registra di continuo cosa succede e, se il PC
si spegne o si riavvia da solo, ti dice cosa stava facendo un attimo prima.
In più sblocca da solo alcuni pezzi di Windows quando si bloccano e scaldano il PC,
e quando giochi scrive un report della partita e controlla che la scheda video sia stabile.

La versione breve è nel [README](README.md). Questa guida si apre anche dal
programma: pulsante **Guida** in alto, oppure tasto destro sull'icona → **Guida**.

---

## Indice

1. [Primo avvio](#1-primo-avvio)
2. [La finestra](#2-la-finestra)
3. [Se il PC si spegne da solo](#3-se-il-pc-si-spegne-da-solo)
4. [Il test A/B: capire quale modifica risolve](#4-il-test-ab-capire-quale-modifica-risolve)
5. [Sblocco automatico dei processi di Windows](#5-sblocco-automatico-dei-processi-di-windows)
6. [L'icona vicino all'orologio](#6-licona-vicino-allorologio)
7. [I file che crea](#7-i-file-che-crea)
8. [Problemi e soluzioni](#8-problemi-e-soluzioni)
9. [Privacy](#9-privacy)
10. [Sessioni di gioco](#10-sessioni-di-gioco)
11. [Temperatura della CPU (con HWiNFO)](#11-temperatura-della-cpu-con-hwinfo)
12. [FPS e frame time (con PresentMon)](#12-fps-e-frame-time-con-presentmon)
13. [Overlay in gioco](#13-overlay-in-gioco)
14. [Controllo dello stato e UV/OC](#14-controllo-dello-stato-e-uvoc)
15. [Il tuo PC: riconoscimento automatico](#15-il-tuo-pc-riconoscimento-automatico)
16. [Giochi & OptiScaler](#16-giochi--optiscaler)
17. [Partite: grafici, confronto undervolt e report da condividere](#17-partite-grafici-confronto-undervolt-e-report-da-condividere)
18. [Le parole difficili, spiegate](#18-le-parole-difficili-spiegate)

---

## 1. Primo avvio

**Con l'installer (consigliato):**

1. Fai doppio clic su `BlackBox-Setup-<versione>.exe`. Se Windows mostra un avviso
   blu: **Ulteriori informazioni → Esegui comunque**.
2. Lascia le spunte come sono e premi **Installa**. BlackBox si installa solo per il
   tuo utente, **senza permessi di amministratore**, in
   `%LocalAppData%\Programs\BlackBox`, con il collegamento nel menu Start e l'avvio
   automatico con Windows.
3. Per aggiornare basta aprire l'installer della versione nuova: chiude BlackBox,
   **disinstalla del tutto la versione vecchia** e installa la nuova da zero. I tuoi dati
   restano.
4. Per disinstallare: **Impostazioni → App → BlackBox → Disinstalla**. I tuoi dati in
   `Documenti\BlackBox` restano.

**Versione portatile:** metti `BlackBox.exe` in una cartella fissa (non in Download),
aprilo e clicca **Avvio automatico: OFF** finché diventa **ON**. Se poi sposti l'exe,
rimetti *Avvio automatico* su OFF e di nuovo su ON.

Al primo avvio BlackBox mostra cosa ha riconosciuto del tuo PC (punto 15).

## 2. La finestra

- **Otto riquadri in alto**: carico e frequenza della CPU, carico, potenza,
  temperatura e frequenza della scheda video, RAM e memoria video. La linea mostra
  gli ultimi due minuti.
  Nel riquadro della CPU c'è anche la temperatura, se HWiNFO è aperto (vedi punto 11).
- **Attività in questo momento**: l'app in primo piano, i programmi che stanno
  usando più processore e più scheda video, e la **sessione di gioco** in corso.
- **Spegnimenti anomali**: tutti gli spegnimenti improvvisi degli ultimi 120 giorni,
  con la diagnosi. Clicca la diagnosi per aprire il report completo.
  Sotto, i **crash del driver video (TDR) e dei giochi**, contati per gioco e per
  configurazione del test A/B.
- **Eventi**: sospensioni, errori del driver video, errori hardware, programmi che
  si sono chiusi da soli e gli sblocchi automatici. In rosso gli errori gravi,
  in arancione gli avvisi.

Chiudere la finestra **non** ferma BlackBox: continua a registrare vicino all'orologio.

## 3. Se il PC si spegne da solo

Alla riaccensione BlackBox mostra un riquadro rosso **Spegnimento anomalo rilevato**
e una notifica. Clicca **Apri report**: trovi

- quando è successo e se c'è stata una schermata blu;
- cosa stava facendo il PC nell'ultimo minuto (medie e massimi);
- la **diagnosi automatica**, cioè la causa più probabile:

| Diagnosi | Cosa vuol dire | Cosa provare |
|---|---|---|
| Picco di potenza GPU | la scheda video ha chiesto troppa corrente di colpo | togliere undervolt/overclock della scheda video; se continua, abbassare il limite di potenza del 10%: se sparisce è l'alimentatore |
| Crash a basso carico | il PC era quasi fermo | riportare la CPU ai valori di fabbrica; se continua, spegnere EXPO/XMP della RAM |
| Durante transizione | durante sospensione o spegnimento | stesse prove del punto sopra, e aggiornare il BIOS |
| Surriscaldamento GPU | la scheda video era troppo calda | pulire le ventole, controllare la pasta termica |
| Schermata blu | errore di Windows o di un driver | cercare il codice indicato |
| Spegnimento manuale | è stato premuto il tasto di accensione | nessun guasto |

La diagnosi è un'indicazione, non una certezza: conviene confrontare più report.

## 4. Il test A/B: capire quale modifica risolve

Nel riquadro **Configurazione attuale** scrivi come è impostato il PC, per esempio
`CO -20, EXPO ON, GPU UV -50mV`, e premi **Salva**.

Ogni volta che cambi qualcosa nel BIOS o nel programma della scheda video,
aggiorna la riga. BlackBox:

- collega ogni spegnimento alla configurazione attiva in quel momento;
- conta i giorni senza spegnimenti;
- ti dice quando sono passati abbastanza giorni per essere sicuri che la modifica
  ha risolto.

Cambia **una cosa sola alla volta**, altrimenti non si capisce quale ha funzionato.

## 5. Sblocco automatico dei processi di Windows

A volte alcuni pezzi di Windows restano bloccati in un ciclo: usano un core intero
del processore, le ventole girano forte e la temperatura sale, anche se non stai
facendo niente.

BlackBox li controlla ogni 2 secondi e ne chiude uno **solo se è davvero bloccato**,
cioè se valgono tutte queste condizioni:

1. resta sopra **metà di un core per almeno 60 secondi**;
2. il consumo è **piatto e costante**: un ciclo bloccato consuma sempre uguale,
   mentre un lavoro vero va a scatti;
3. **non lo stai usando**: se è in primo piano (per esempio hai il menu Start o la
   ricerca aperti) non viene toccato;
4. **non stai giocando**: durante una partita non chiude niente, segna il problema
   e ricontrolla a fine partita;
5. se lo stesso processo si riblocca **3 volte in un'ora**, smette di chiuderlo e ti
   avvisa: vuol dire che c'è un problema di Windows da sistemare.

Windows lo riapre da solo, pulito, appena serve. Ricevi una notifica e lo trovi negli
**Eventi** come *"Sbloccato …"*.

| Processo | Cos'è |
|---|---|
| TextInputHost | tastiera virtuale, emoji (Win + .), appunti (Win + V) |
| SearchHost | pannello Cerca |
| StartMenuExperienceHost | menu Start |
| ShellExperienceHost | centro notifiche, calendario, impostazioni rapide |
| CrossDeviceResume | "riprendi dal telefono" (bug noto di Windows 11) |
| Widgets, WidgetService | pannello Widget |
| PhoneExperienceHost | Collegamento al telefono |
| LockApp | schermata di blocco |

**Nessun altro programma viene mai chiuso**: giochi, browser, documenti aperti
non vengono toccati. Se un altro programma consuma almeno un core intero in
background per più di 10 minuti, BlackBox lo **segnala soltanto** nel controllo dello
stato: può essere un lavoro normale (antivirus, aggiornamenti), decidi tu. Al massimo, se in quel momento avevi aperto il pannello emoji
o il menu Start, si chiude e lo riapri.

Se nella lista eventi vedi lo stesso processo sbloccato molto spesso, è un bug di
Windows: installa gli aggiornamenti di Windows Update.

## 6. L'icona vicino all'orologio

| Azione | Cosa fa |
|---|---|
| Clic sinistro | apre la finestra |
| Passare sopra col mouse | potenza e temperatura della scheda video |
| Tasto destro → Apri monitor | apre la finestra |
| Tasto destro → Ultimo report | apre l'ultimo report di spegnimento |
| Tasto destro → Ultima sessione di gioco | apre il report dell'ultima partita |
| Tasto destro → Partite e confronti | grafici delle partite e confronto di due undervolt (punto 17) |
| Tasto destro → Copia l'ultimo report di crash | copia il report dell'ultimo spegnimento, da incollare dove chiedi aiuto (punto 17) |
| Tasto destro → Overlay in gioco | acceso/spento, posizione, dimensione del testo (punto 13) |
| Tasto destro → Giochi & OptiScaler | apre la gestione di OptiScaler (punto 16) |
| Tasto destro → Il tuo PC | hardware riconosciuto, controllo dello stato e pulsanti per sistemare (punti 14-15) |
| Tasto destro → Apri cartella log | apre la cartella dei dati |
| Tasto destro → Guida | apre questa guida |
| Tasto destro → Esci | ferma davvero BlackBox |

Se l'icona non si vede, cliccare la freccetta **^** vicino all'orologio.

## 7. I file che crea

Tutto sta in `Documenti\BlackBox`:

| File | Contenuto |
|---|---|
| `monitor_AAAA-MM-GG.csv` | un campione al secondo; si apre con Excel. Quelli più vecchi di 14 giorni vengono cancellati da soli |
| `eventi.log` | gli eventi della lista, compresi gli sblocchi |
| `crash_AAAAMMGG_HHMMSS.txt` | il report di ogni spegnimento anomalo |
| `configurazioni.log` | lo storico delle configurazioni del test A/B |
| `sessione_<gioco>_<data>.txt` | il report di ogni partita (punto 10) |
| `sessioni.csv` | una riga per partita: serve ai confronti e ai grafici (punto 17); si apre con Excel |
| `sessione_in_corso.txt`, `controllo_tuning.txt` | file di lavoro: si cancellano da soli |
| `impostazioni.ini` | le impostazioni di BlackBox |
| `tools\PresentMon.exe` | PresentMon, se l'hai scaricato (punto 12) |
| `GUIDA.txt` | questa guida, creata quando la apri dal programma |

## 8. Problemi e soluzioni

**Ho svuotato la cartella dei log: comparirà di nuovo l'avviso di un vecchio spegnimento?**
No. Se BlackBox non ha dati di quello spegnimento (o è più vecchio di un giorno), lo mette
nello storico senza riquadro rosso.

**Il report dice "BlackBox non era attivo".** Il programma era chiuso. Metti
*Avvio automatico* su ON.

**Dopo il riavvio BlackBox non parte da solo.** Probabilmente l'exe è stato
spostato: apri il programma e rimetti *Avvio automatico* su OFF e poi su ON.

**Non vedo potenza e temperatura della scheda video.** Funzionano solo con schede
AMD e NVIDIA con il driver installato.

**La temperatura della CPU dice "n/d".** BlackBox la prende da HWiNFO: apri HWiNFO
e controlla che la memoria condivisa sia accesa (punto 11).

**Gli FPS restano a "--".** Gli FPS si misurano solo durante una sessione di gioco.
Se il gioco è aperto da poco, aspetta 10 secondi. Se nella riga *FPS* c'è un pulsante,
segui il punto 12.

**L'overlay non si vede.** Controlla che sia acceso (pulsante *Overlay: ON* oppure
Ctrl + Shift + O) e che ci sia una sessione di gioco in corso. Se il gioco è in
**schermo intero esclusivo**, nelle opzioni video scegli **finestra senza bordi**
(*borderless*): in schermo intero esclusivo nessun overlay esterno può apparire.

**Non parte la sessione di gioco.** Parte se il gioco è in primo piano e: è a schermo
intero (almeno 15% di scheda video), oppure è un emulatore conosciuto, oppure usa la
scheda video oltre il 60%. Un gioco molto leggero in finestra può restare sotto: mettilo
a schermo intero (o senza bordi), oppure attiva *Mostra anche fuori dai giochi*
dell'overlay.

**BlackBox dà problemi con l'anti-cheat?** È fatto per non darne: non entra nei giochi,
non legge né scrive la loro memoria e non inietta niente. Del gioco chiede a Windows solo
il nome e il percorso del file, con il permesso più basso che esiste (lo stesso che usa
Gestione attività). L'overlay è una finestra trasparente sopra il gioco, non un aggancio
alla grafica come quelli di Steam o Discord. Gli FPS li misura PresentMon, lo strumento
ufficiale di Intel, dagli eventi di Windows. OptiScaler, che invece modifica i file del
gioco, è bloccato nei giochi con anti-cheat.
Se comunque un gioco si chiudesse all'avvio, chiudi BlackBox dal tasto destro → *Esci*
prima di giocare e scrivi il nome del gioco su GitHub (pagina *Issues*).

**Il PC continua a scaldare.** Apri la finestra e guarda *Più CPU*: è il programma
che sta consumando di più. Se non è nell'elenco del punto 5, BlackBox non lo
chiude: va chiuso a mano da Gestione attività (Ctrl + Shift + Esc).

## 9. Privacy

- Nessun invio di dati, nessun aggiornamento automatico. Le uniche connessioni a internet
  sono i download di PresentMon, OptiScaler e della tabella dei limiti da GitHub, e solo
  quando premi tu il pulsante.
- Legge solo i contatori di Windows, il driver della scheda video, il Registro eventi
  e, se aperti, i valori pubblicati da HWiNFO o LibreHardwareMonitor.
- L'unica azione sul sistema è lo sblocco del punto 5, limitato all'elenco fisso.
- Tutti i dati restano in `Documenti\BlackBox`.

## 10. Sessioni di gioco

Non devi fare niente: BlackBox capisce da solo quando giochi. La sessione (con overlay
e FPS) **parte**:

- dopo **5 secondi** se il gioco è a **schermo intero** (o finestra senza bordi) e usa la
  scheda video almeno al 15%;
- dopo **5 secondi** se è un **emulatore** (PCSX2, RetroArch, DuckStation, PPSSPP, RPCS3,
  Dolphin, Cemu, Ryujinx, Xenia, Xemu e altri), anche se usa pochissimo la scheda video;
- dopo **8 secondi** negli altri casi, se il programma in primo piano usa la scheda video
  oltre il 60%.

Browser, lettori video e app ingrandite non contano come giochi. La sessione **finisce**
quando chiudi il gioco o lo lasci in secondo piano per più di un minuto. Nella finestra la vedi alla riga *Sessione di gioco*.

A fine partita trovi il report (tasto destro sull'icona → **Ultima sessione di
gioco**). Dentro c'è:

- durata, potenza media e di picco, clock e tensione medi della scheda video;
- temperature massime: *edge* (la superficie del chip) e *hotspot* (il punto più caldo);
- la **differenza hotspot − edge**: se in media supera 25 °C il dissipatore potrebbe
  non toccare bene il chip (pasta termica o montaggio);
- VRAM massima, temperatura della CPU, configurazione A/B attiva;
- **throttling**: le volte in cui la scheda ha rallentato da sola pur essendo al
  massimo (oltre il 95% di carico ma con il clock sotto l'85% del solito per più di
  5 secondi), con la causa probabile: troppo calda o al limite di potenza;
- crash del driver video (TDR) e crash del gioco durante la partita.

**Reset del tuning di Adrenalin.** Dopo un crash del driver video o uno spegnimento
anomalo, Adrenalin spesso rimette da solo i valori di fabbrica e il tuo undervolt
o overclock sparisce senza avvisare. Alla partita successiva BlackBox confronta
tensione e clock con le partite precedenti; se sono cambiati ti avvisa con
*"Adrenalin ha ripristinato i valori di fabbrica dopo un crash: lo UV/OC non è più
attivo"*. Rimettilo in Adrenalin → Prestazioni → Ottimizzazione, magari un po' più
prudente.

Il confronto funziona solo se c'è almeno una partita registrata prima del crash.

## 11. Temperatura della CPU (con HWiNFO)

Per leggere la temperatura vera della CPU (Tctl/Tdie) servirebbe un driver speciale,
che BlackBox volutamente non installa. La prende quindi da **HWiNFO**, se è aperto:

HWiNFO può essere in italiano o in inglese: qui trovi tutti e due i nomi.

1. Apri HWiNFO64 e scegli **Solo sensori** (*Sensors only*).
2. In basso nella finestra dei sensori clicca l'**ingranaggio**: si apre
   **Impostazioni sensori** (*Sensor Settings*).
3. In quella finestra, in basso a destra, clicca **Impostazioni principali**
   (*Main Settings*).
4. Nella prima scheda, **Generale / Interfaccia utente** (*General / User Interface*),
   metti la spunta su **Supporto memoria condivisa** (*Shared Memory Support*).
5. Premi **OK**, poi di nuovo **OK** nella finestra *Impostazioni sensori*.
6. Dopo qualche secondo nel riquadro della CPU di BlackBox compare la temperatura.

In BlackBox, nel *Controllo stato*, il pulsante **Mostrami come** accanto a HWiNFO
mostra questi stessi passi.
**Il limite delle 12 ore.** Nella versione gratuita di HWiNFO la memoria condivisa si
spegne da sola **12 ore dopo l'avvio di HWiNFO**: la temperatura della CPU torna «n/d».
Non è un guasto. BlackBox se ne accorge e te lo dice nel *Controllo stato*.

- Per riaverla subito: tasto destro sull'icona di HWiNFO vicino all'orologio → **Esci**
  (*Exit*), poi riaprilo e scegli **Solo sensori**.
- Se spegni il PC ogni giorno quasi non te ne accorgi: a ogni avvio le 12 ore ripartono.
- Per non avere più il limite: **HWiNFO Pro** (a pagamento) oppure
  **LibreHardwareMonitor** (gratuito, senza limite di tempo).
- **Oppure lascia fare a BlackBox:** nel *Controllo stato*, alla voce *riavvio
  automatico HWiNFO*, premi **Attiva** e conferma la richiesta di Windows (una volta
  sola). Da lì, quando finiscono le 12 ore, BlackBox chiude e riapre HWiNFO da solo,
  **mai durante una partita** (aspetta che finisca). Funziona tramite un'«operazione
  pianificata» di Windows che sa fare solo questo; si spegne con **Disattiva** e la
  disinstallazione di BlackBox la toglie.
In alternativa va bene anche **LibreHardwareMonitor**: gratuito e senza limite di tempo.
Va aperto **come amministratore** (tasto destro → *Esegui come amministratore*); dalle sue
opzioni (*Options → Run On Windows Startup*) lo puoi far partire con Windows.
Senza nessuno dei due, la temperatura resta "n/d" e tutto il resto funziona.

## 12. FPS e frame time (con PresentMon)

Gli FPS li misura **PresentMon**, un programma gratuito di Intel (licenza MIT) che
legge da Windows quando il gioco mostra ogni fotogramma. Gira solo durante le
sessioni di gioco e si chiude da solo alla fine.

**La prima volta:**

1. Nella finestra, riquadro *Attività*, riga **FPS**: premi **Scarica PresentMon**.
   BlackBox lo scarica dalla pagina ufficiale (github.com/GameTechDev/PresentMon),
   controlla la firma digitale di Intel e lo mette in `Documenti\BlackBox\tools`.
2. Se compare **Abilita FPS (chiede admin una volta)**, premilo e conferma la finestra
   di Windows. Il tuo utente entra nel gruppo *Performance Log Users*, che può leggere
   questi dati. Poi **esci da Windows e rientra** (o riavvia). Su molti PC il permesso
   c'è già e il pulsante non compare.

Da lì in poi, mentre giochi la riga *FPS* mostra:

- **FPS**: fotogrammi al secondo degli ultimi 2 secondi;
- **1% low**: gli FPS dei momenti peggiori dell'ultimo minuto (l'1% dei fotogrammi più
  lenti). Se è molto più basso degli FPS, il gioco "scatta" anche se la media è alta;
- **ms**: il tempo medio di un fotogramma (frame time).

Il **1% low** è rosso sotto i 30, arancione sotto i 50. Quando il gioco è in secondo
piano (Alt+Tab) i suoi fotogrammi non contano, e al ritorno il calcolo riparte pulito.

**1% low basso: perché e cosa fare.** Se il gioco scatta (o va lento), BlackBox guarda
l'ultimo minuto e nel *Controllo stato* (e nell'ultima riga dell'overlay) ti dice la
causa più probabile e la soluzione:

| Cosa vede BlackBox | Causa | Cosa fare |
|---|---|---|
| partita iniziata da meno di 3 minuti | compilazione degli shader, caricamenti | niente: è normale e di solito sparisce dopo qualche minuto |
| memoria video quasi piena | VRAM piena | abbassa di un livello la qualità delle texture |
| la scheda rallenta per il calore | throttling termico | più aria: filtri puliti, ventole più alte, pasta termica |
| RAM quasi piena | memoria del sistema piena | chiudi browser, launcher e programmi che non servono |
| un programma pesante in background | CPU rubata al gioco | chiudilo da Gestione attività (Ctrl + Maiusc + Esc) |
| un core del processore al massimo, scheda video sotto il 90% | processore al limite | abbassa folla, distanza visiva, fisica, ray tracing; un limite agli FPS poco sotto la media aiuta |
| generazione di fotogrammi con meno di 60 FPS veri | pochi fotogrammi di partenza | abbassa la qualità o l'upscaler finché gli FPS veri superano 60 |
| scheda video al 95-100% e FPS bassi | scheda video al massimo | abbassa qualità o risoluzione, o usa FSR / XeSS in Qualità o Bilanciato |
| nessuna di queste | picchi isolati | limita gli FPS poco sotto la media (nel gioco o con Radeon Chill) |

FPS medio e 1% low dell'intera partita finiscono nel report della sessione e in
`sessioni.csv`.

**Generazione di fotogrammi (FSR 3, DLSS/XeSS Frame Generation).** BlackBox la
riconosce da solo: i fotogrammi generati costano pochissimo lavoro alla scheda video
e si alternano con quelli veri. Quando è attiva vedi per esempio
`FPS 210  FG 2x → 105 reali`: 210 sono quelli che vedi, 105 quelli calcolati davvero
(la reattività ai comandi segue questi). L'AFMF del driver (Adrenalin) invece non è
visibile a nessun programma esterno: lì gli FPS mostrati restano quelli del gioco.
Se preferisci usare un PresentMon che hai già, scrivi il percorso in
`Documenti\BlackBox\impostazioni.ini` alla riga `presentmon=` (con BlackBox chiuso).

## 13. Overlay in gioco

Un piccolo riquadro sopra il gioco, per esempio:

```
FPS 210  FG 2x → 105 reali   1% low 94
[grafico del frame time]   4,8 ms
GPU 56°C  hot 81°C  263 W  2650 MHz  985 mV
CPU 35%  72°C   RAM 61%   VRAM 9,8 GB
✔ tutto ok   UV/OC stabile   0,42 FPS/W
```

- Il **grafico** mostra la durata degli ultimi 120 fotogrammi: una linea piatta è
  fluida, i picchi verso l'alto sono gli scatti.
- L'ultima riga è il **controllo dello stato** (punto 14).
- I colori: verde va bene, arancione attenzione, rosso problema. Le soglie sono
  quelle del **tuo** hardware (punto 15), non numeri generici.
- Non si può cliccare e non ruba il mouse né la tastiera al gioco.

**Accendere e spegnere:** tasto rapido **Ctrl + Shift + O**, anche in gioco, oppure
il pulsante *Overlay* in alto nella finestra.

**Personalizzarlo:** tasto destro sull'icona di BlackBox → **Overlay in gioco**:

| Voce | Scelte |
|---|---|
| Forma | **Blocco** (più righe) o **Barra in lungo** (una riga sola, comoda in alto al centro) |
| Posizione | i quattro angoli, oppure in alto o in basso al centro |
| Dimensione del testo | piccolo, medio, grande, molto grande |
| Colore del testo / del grafico | bianco, giallo, ciano, verde, arancione, rosa... |
| Sfondo | scuro, semitrasparente, molto trasparente, nessuno |
| Cosa mostrare | spunta le righe che vuoi: FPS, grafico, GPU, CPU, controllo stato |
| Anteprima | lo fa vedere per 8 secondi anche senza giocare |

Ogni modifica si vede subito per qualche secondo.

**Perché pesa meno dell'overlay di Steam:** non entra dentro il gioco (nessuna DLL
iniettata, quindi niente conflitti con gli anti-cheat). È una finestra a parte,
disegnata dal processore e non dalla scheda video. Il gioco resta in *Independent
Flip*, la modalità più veloce, senza latenza in più. Misurato: circa **0,15%** della
CPU con l'overlay acceso.

**Mentre giochi BlackBox si fa da parte:**

- passa a **priorità bassa**, così il gioco ha sempre la precedenza;
- controlla i programmi aperti ogni 5 secondi (invece di 2) e il Registro eventi ogni
  30 (invece di 10);
- restituisce a Windows la memoria che non usa, anche ogni volta che chiudi la sua
  finestra;
- **non mostra notifiche**: le tiene da parte e te le mostra a fine partita.

Il tasto rapido si può cambiare in `impostazioni.ini`, riga `overlay_tasto=`
(per esempio `Ctrl+Alt+F10`), con BlackBox chiuso.

Con più monitor l'overlay compare **sul monitor dove c'è il gioco**.

**Limite:** nei giochi in *schermo intero esclusivo* l'overlay non appare; scegli
*finestra senza bordi* (borderless) nelle opzioni video del gioco. Vale anche per gli
emulatori: in PCSX2, DuckStation, RetroArch ecc. usa lo schermo intero «senza bordi» /
«borderless», non quello «esclusivo».

## 14. Controllo dello stato e UV/OC

Nella finestra, riga **Controllo stato** (e nell'ultima riga dell'overlay): **✔ tutto
ok**, oppure cosa non va. Cliccala per l'elenco completo:

- temperature della scheda video e della CPU (media degli ultimi 30 secondi, mai un
  picco di un attimo);
- **differenza hotspot − edge** confrontata con quella **tipica della tua scheda**,
  che BlackBox impara partita dopo partita;
- **undervolt/overclock**: *stabile* se non ci sono crash del driver, crash del gioco
  o rallentamenti per il calore. I rallentamenti per limite di potenza sono normali
  con un undervolt e non danno avvisi;
- **efficienza in FPS per watt** (FPS reali divisi per i watt della scheda): è il
  numero migliore per confrontare due undervolt. Più è alto, meglio è. Confronta
  sempre la stessa scena dello stesso gioco. Il report dice di quanto sei sopra o
  sotto le tue partite precedenti;
- fluidità (1% low), memoria video e RAM: avvisano solo se il gioco scatta davvero;
- programmi e impostazioni utili: HWiNFO, PresentMon, Modalità gioco, HAGS.

Il simbolo ℹ è solo un'informazione, non un problema.

**Pulsanti che sistemano da soli.** Nel controllo dello stato, accanto ai problemi che
si possono risolvere c'è un pulsante:

| Problema | Pulsante | Cosa fa |
|---|---|---|
| HAGS spento | **Accendi HAGS** | lo accende (Windows chiede una conferma da amministratore), poi propone il riavvio |
| Modalità gioco spenta | **Accendi** | la accende subito, senza permessi speciali |
| HWiNFO senza memoria condivisa | **Mostrami come** | ti mostra i 3 clic da fare in HWiNFO |
| HWiNFO chiuso / non installato | **Apri HWiNFO** / **Pagina di HWiNFO** | lo apre / apre la pagina ufficiale per scaricarlo |
| PresentMon mancante, permesso FPS | **Scarica PresentMon** / **Dai il permesso** | come al punto 12 |
| programma che consuma in background | **Apri Gestione attività** | decidi tu se chiuderlo |

BlackBox non cambia mai da solo le impostazioni di sicurezza di Windows (per esempio
l'Integrità della memoria): te le segnala soltanto.

## 15. Il tuo PC: riconoscimento automatico

A ogni avvio BlackBox riconosce processore, scheda video, driver, Adrenalin e
Windows, e usa i **limiti ufficiali di quei modelli**, presi da una tabella costruita
sulle schede tecniche dei produttori (amd.com, intel.com, nvidia.com). Alcuni esempi:

| Componente | Limite | Fonte |
|---|---|---|
| Ryzen 7000X3D (es. 7800X3D, 7950X3D) | 89 °C | amd.com |
| Ryzen 5000X3D | 90 °C | amd.com |
| Ryzen 5000 (5600X e serie G: 95 °C) | 90 °C | amd.com |
| Ryzen 3000, 7000, 8000G, 9000 (anche 9000X3D) | 95 °C | amd.com |
| Intel Core 10ª-14ª generazione (i9-12900KS: 90 °C) | 100 °C | intel.com |
| Intel Core Ultra 200 | 105 °C | intel.com |
| NVIDIA RTX 20/30/40/50, GTX 16 | modello per modello (es. RTX 5070 85 °C, RTX 4090 90 °C, RTX 3080 93 °C) | nvidia.com |
| Radeon RX 5000-9000 | hotspot: rallenta da 100 °C, limite 110 °C | strumento ufficiale AMD SMI |
| Intel Arc | 90 °C | supporto Intel |

Se un modello non è in tabella (per esempio uno uscito dopo), BlackBox usa una **stima
prudente** e lo scrive chiaramente («stima prudente: modello non verificato»). In
*Il tuo PC* vedi quale limite usa e la fonte.

**Aggiorna i limiti:** in *Il tuo PC* (tasto destro sull'icona) c'è il pulsante
**Aggiorna i limiti**: scarica la tabella più recente dal sito di BlackBox su GitHub,
**solo quando lo premi**. Con ogni nuova versione di BlackBox la tabella si aggiorna
anche da sola.
**Al primo avvio** (appena non stai giocando) mostra una finestra di benvenuto con
cosa ha riconosciuto e cosa conviene sistemare, con i pulsanti per farlo. La ritrovi
quando vuoi: tasto destro sull'icona → **Il tuo PC**, oppure cliccando la riga
*Controllo stato* nella finestra.

Se **cambi scheda video**, i valori imparati ripartono da capo. Se **aggiorni il
driver**, BlackBox controlla alla partita successiva se Adrenalin ha azzerato il tuo
UV/OC (succede spesso).

## 16. Giochi & OptiScaler

### Cos'è, in parole semplici

**OptiScaler** è un programma gratuito (licenza GPL) che si mette **nella cartella di un
gioco** e cambia l'upscaler che il gioco usa. Esempio: un gioco ha solo DLSS (che
funziona solo sulle NVIDIA): con OptiScaler, sulla tua Radeon, al posto di DLSS lavora
FSR 3.1/4 o XeSS. Risultato: più FPS con un'immagine pulita, e a volte la generazione
di fotogrammi.

### ⚠ La regola più importante: MAI nei giochi online

Gli **anti-cheat** dei giochi online controllano quali file usa il gioco. OptiScaler
è un file in più dentro il gioco: anche se non bara, l'anti-cheat può vederlo e
**bannarti l'account**, anche dopo una sola partita.

BlackBox **blocca da solo**:

- i giochi con un anti-cheat riconosciuto dai suoi file (Easy Anti-Cheat, BattlEye,
  EA AntiCheat, Vanguard, ACE, nProtect, XIGNCODE, EQU8, PunkBuster, FACEIT...);
- i giochi online noti anche se l'anti-cheat non lascia file (per esempio
  Counter-Strike 2, Dota 2 e gli altri con VAC di Steam, Call of Duty, Fortnite,
  Valorant, Apex, PUBG, Rainbow Six, GTA V, Battlefield, EA FC...);
- i giochi **a 32 bit**, dove OptiScaler non può funzionare.

Per questi giochi non compare nessun pulsante. **Se un gioco non bloccato lo usi
online, non installarlo lo stesso**: prima di installare BlackBox te lo chiede sempre.

### Come si fa, passo passo

Apri **Giochi & OptiScaler** (pulsante in alto nella finestra, oppure tasto destro
sull'icona di BlackBox).

1. **Scarica il pacchetto** (una volta sola): pulsante **Scarica l'ultima versione**.
   BlackBox lo prende dalla pagina ufficiale (github.com/optiscaler/OptiScaler), lo
   controlla e lo tiene in `Documenti\BlackBox\optiscaler`. La spunta *includi
   pre-release* lasciala spenta.
2. **Scegli il gioco** nell'elenco a sinistra. BlackBox trova da solo i giochi di
   Steam, Epic, GOG e quelli che hai già giocato; gli altri li aggiungi con **Aggiungi
   cartella di un gioco**. A destra vedi gli upscaler del gioco e un consiglio:

   | Il gioco ha | Consiglio |
   |---|---|
   | FSR 3.1 | di solito basta così (sulle RX 9000 attiva FSR 4 in Adrenalin) |
   | solo DLSS, XeSS o FSR 2 | **OptiScaler consigliato** |
   | nessuno | OptiScaler di solito non funziona |

3. **Chiudi il gioco** e premi **Installa**. Lascia il nome della DLL che propone
   BlackBox: sotto il menu c'è scritto quale consiglia **per quel gioco** e perché. Leggi la finestra di conferma e
   rispondi **Sì** solo se il gioco lo usi offline. Se nella cartella c'è già un file
   con lo stesso nome (spesso **ReShade**), BlackBox te lo dice: puoi scegliere un altro
   nome (winmm.dll) per tenerli tutti e due.
4. **Attivalo nel gioco** (BlackBox ti mostra questi passi dopo l'installazione):
   1. avvia il gioco e nelle opzioni grafiche scegli **DLSS** (ora compare anche sulla
      Radeon) o XeSS/FSR, con la qualità che vuoi;
   2. premi il tasto **Inizio (Home)**: si apre il menu di OptiScaler;
   3. alla voce **Upscalers** scegli FSR 3.1 / FSR 4 oppure XeSS;
   4. per la generazione di fotogrammi attiva nel gioco *DLSS Frame Generation*:
      OptiScaler la trasforma in quella di AMD (sezione *Frame Generation* del menu);
   5. premi di nuovo **Inizio (Home)** per chiudere il menu. Le scelte restano salvate.
5. **Controlla**: alla prossima partita BlackBox verifica da solo che OptiScaler sia
   caricato. Lo vedi nell'overlay (*OptiScaler: attivo*), nel report della partita e qui
   (*Ultima partita: ATTIVO*).

### Il «nome della DLL»: cos'è e quale scegliere

OptiScaler deve «travestirsi» da un file che il gioco carica da solo all'avvio. Quando
il gioco cerca quel file, Windows guarda prima nella sua cartella: così carica
OptiScaler. Le scelte, in ordine:

1. **dxgi.dll**: il più compatibile, per i giochi DirectX 11 e 12 (quasi tutti).
   **Consigliato.**
2. **winmm.dll**: giochi **Vulkan**, giochi del **Microsoft Store / Xbox**, oppure se
   nella cartella c'è già un dxgi.dll (di solito ReShade).
3. **version.dll**: seconda alternativa, se i primi due non vanno.
4. **dbghelp, d3d12, wininet, winhttp**: ultime prove.

BlackBox sceglie da solo quello giusto per ogni gioco. Se in una partita OptiScaler non
viene caricato o il gioco va in crash, alla volta successiva consiglia il nome dopo.

### fakenvapi: cos'è e perché

Alcuni giochi mostrano **DLSS** (e DLSS Frame Generation, Reflex) solo se credono di
avere una scheda **NVIDIA**. fakenvapi «fa finta» di essere il pezzo del driver NVIDIA
che il gioco interroga: così, su una Radeon o una Intel Arc, l'opzione DLSS compare nel
menu del gioco e OptiScaler la trasforma in FSR o XeSS (e DLSS Frame Generation nella
generazione di fotogrammi di AMD).

Serve **solo nei giochi che hanno DLSS**: BlackBox lo spunta da solo in quei giochi. Se
il gioco non ha DLSS non serve; con una scheda NVIDIA non serve mai.

### Se qualcosa va storto

| Cosa vedi | Cosa fare |
|---|---|
| *"installato ma NON caricato"* | il gioco non usa quel nome di DLL: **Rimuovi** e reinstalla con **winmm.dll** o **version.dll** |
| *"Crash causato da OptiScaler"* | stessa cura: altro nome di DLL, oppure **Rimuovi** |
| il gioco non parte | chiudilo, **Rimuovi**, riprova con un altro nome |
| vuoi tornare com'era | **Rimuovi**: la cartella del gioco torna esattamente com'era |
| è uscita una versione nuova | **Scarica l'ultima versione**, poi **Aggiorna** (le tue impostazioni restano) |

**Prima e dopo:** qui vedi le medie delle tue partite a quel gioco senza e con
OptiScaler (FPS, 1% low, watt). Confronta la stessa scena.

Se OptiScaler era già installato a mano, BlackBox non lo tocca (usa il file
`Remove_OptiScaler.bat` nella cartella del gioco).

## 17. Partite: grafici, confronto undervolt e report da condividere

Pulsante **Partite** in alto nella finestra (oppure tasto destro sull'icona → **Partite e
confronti**). Tutto viene calcolato dai file già salvati e **solo mentre questa finestra
è aperta**: durante il gioco non lavora e non toglie FPS.

**Grafici della partita.** A sinistra l'elenco delle partite (le più recenti in alto).
Clicchi una partita e vedi:

- il riassunto (PC, driver, configurazione, FPS medio e 1% low, watt, temperature,
  crash);
- quattro grafici lungo tutta la partita: **FPS** (con gli FPS reali se c'è la
  generazione di fotogrammi), **temperature** (hotspot, edge, CPU), **potenza** della
  scheda video e **clock**. I buchi verso il basso negli FPS sono gli scatti o i
  momenti in cui eri fuori dal gioco (Alt+Tab, menu).
- **Copia riassunto (da incollare)**: copia il riassunto, pronto da incollare in un forum,
  su Discord o in una chat con un assistente. **Apri report completo** apre il file
  della partita.

I grafici ci sono per le partite degli **ultimi 14 giorni** (i dati al secondo più vecchi
vengono cancellati per non riempire il disco); riassunto e confronto restano per sempre.

**Confronta due partite (undervolt).** Serve a capire quale impostazione va meglio:

1. Nella finestra principale scrivi la configurazione nel riquadro del test A/B
   (es. «UV 1080 mV») e premi **Salva**.
2. Gioca almeno 10 minuti, sempre nella stessa zona o scena del gioco.
3. Cambia l'impostazione in Adrenalin, scrivi la nuova configurazione (es. «UV 1050 mV»)
   e salva.
4. Rigioca la stessa scena per lo stesso tempo.
5. In *Partite → Confronta* scegli la partita A e la partita B.

BlackBox mette i valori uno accanto all'altro (verde = meglio, rosso = peggio) e dà un
giudizio:

- prima conta la **stabilità**: se una delle due ha avuto crash, vince l'altra;
- poi l'**efficienza** (FPS veri per ogni watt): sotto il 3% di differenza sono
  «praticamente uguali», perché è normale variazione;
- se sono **giochi diversi** non dà un vincitore; avvisa anche se una partita è più
  corta di 5 minuti, se la configurazione scritta è la stessa o se la generazione di
  fotogrammi è attiva solo in una delle due.

**Copia confronto (da incollare)** copia il confronto in testo.

**Report di crash da condividere.** Tasto destro sull'icona → **Copia l'ultimo report di
crash (da incollare)**: copia il report dell'ultimo spegnimento anomalo, da incollare
dove chiedi aiuto. I testi copiati contengono solo i dati del PC (modelli di processore e
scheda video, driver, valori misurati): il tuo nome utente, il nome del PC e la tua
cartella utente vengono sostituiti da «(nascosto)» e `%USERPROFILE%`.

## 18. Le parole difficili, spiegate

Nel *Controllo stato* ogni voce ha il pulsante **Cos'è?** con queste spiegazioni.

**FPS** — fotogrammi al secondo: quante immagini il gioco disegna ogni secondo. Più sono,
più è fluido.

**1% low** — gli FPS nei momenti peggiori (l'1% dei fotogrammi più lenti dell'ultimo
minuto). Se è molto più basso della media, il gioco «scatta» anche se la media è alta.

**Frame time** — quanti millisecondi servono per un fotogramma. 16,7 ms = 60 FPS,
8,3 ms = 120 FPS. Picchi nel grafico = scatti.

**Generazione di fotogrammi** (FSR 3, DLSS Frame Generation, AFMF) — la scheda inventa
un'immagine intermedia tra due vere: il gioco sembra più fluido, ma la risposta ai
comandi segue i fotogrammi veri (gli «FPS reali» di BlackBox).

**Upscaler** (FSR, DLSS, XeSS) — il gioco disegna a una risoluzione più bassa e
l'upscaler la ingrandisce in modo intelligente: molti più FPS con un'immagine quasi
uguale.

**HAGS** (Pianificazione GPU con accelerazione hardware) — di solito è il processore,
tramite Windows, a fare da «vigile urbano» per la scheda video, decidendo in che ordine
esegue i lavori dei programmi. Con HAGS acceso è la scheda video stessa a gestire la sua
coda. Risultato: un po' meno lavoro per il processore e un po' meno ritardo. Serve
soprattutto alla generazione di fotogrammi: DLSS 3 di NVIDIA lo richiede, per FSR 3 e
OptiScaler è consigliato. Non fa danni, vale dopo un riavvio e si spegne da Impostazioni
→ Sistema → Schermo → Grafica.

**Modalità gioco** — funzione di Windows che, mentre giochi, dà la precedenza al gioco e
rimanda i lavori in background (per esempio gli aggiornamenti).

**Temperatura edge e hotspot** — *edge* è la temperatura media della superficie del chip
della scheda video, *hotspot* il suo punto più caldo. Le schede reggono temperature alte
per progetto: le Radeon recenti fino a 110 °C di hotspot.

**Differenza hotspot − edge** — dice quanto bene il dissipatore tocca il chip. Se cresce
molto rispetto al solito, la pasta termica si sta seccando o il dissipatore non appoggia
bene.

**Tctl/Tdie** — la temperatura del processore che il processore stesso usa per
proteggersi: se arriva al limite del modello (es. 89 °C per il 7800X3D) rallenta da solo.

**Throttling** — la scheda video (o il processore) rallenta da sola per non superare un
limite di temperatura o di potenza. Per il calore è da sistemare; per il limite di
potenza è normale con un undervolt.

**Undervolt (UV) / overclock (OC)** — far lavorare la scheda con meno tensione (scalda e
consuma meno) o più veloce. Se si esagera diventa instabile.

**TDR** — il driver della scheda video si blocca e Windows lo riavvia: schermo nero per
un attimo o gioco chiuso. Spesso è un UV/OC troppo spinto.

**FPS per watt** — FPS veri divisi per i watt della scheda: più è alto, meglio funziona
l'undervolt. Confronta sempre la stessa scena.

**VRAM** — la memoria della scheda video. I giochi la riempiono di proposito: è un
problema solo se intanto il gioco scatta.

**RAM impegnata** — quanta memoria del sistema hanno chiesto i programmi aperti. Vicino
al 100% tutto rallenta.

**Core** — uno dei «motori» del processore (il 7800X3D ne ha 8, con 16 thread).

**Anti-cheat** — programma dei giochi online che controlla che nessuno bari. Vede anche
mod innocue come OptiScaler e può bannare l'account.

**PresentMon** — programma gratuito di Intel che misura FPS e frame time con precisione.

**HWiNFO** — programma gratuito che legge tutti i sensori del PC; la sua *memoria
condivisa* permette a BlackBox di leggere la temperatura del processore.

**Performance Log Users** — gruppo di Windows che permette di leggere gli eventi della
grafica: serve per gli FPS. Si chiede una volta.

**Integrità della memoria** — protezione di Windows contro driver malevoli. Può togliere
qualche FPS, ma è una scelta di sicurezza: BlackBox non la tocca.