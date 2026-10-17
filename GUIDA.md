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

---

## 1. Primo avvio

1. Metti `BlackBox.exe` in una cartella fissa (non in Download, non sul desktop).
2. Fai doppio clic. Se Windows mostra un avviso blu: **Ulteriori informazioni → Esegui comunque**.
3. Clicca **Avvio automatico: OFF** finché diventa **ON**: così BlackBox parte da
   solo a ogni accensione e non perde nessuno spegnimento.

Non servono permessi di amministratore e non si installa niente.

> Se sposti `BlackBox.exe` in un'altra cartella, apri il programma e rimetti
> **Avvio automatico** su OFF e poi di nuovo su ON, così Windows sa dove trovarlo.

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
| Tasto destro → Overlay in gioco | acceso/spento, posizione, dimensione del testo (punto 13) |
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
| `sessioni.csv` | una riga per partita: serve ai confronti; si apre con Excel |
| `sessione_in_corso.txt`, `controllo_tuning.txt` | file di lavoro: si cancellano da soli |
| `impostazioni.ini` | le impostazioni di BlackBox |
| `tools\PresentMon.exe` | PresentMon, se l'hai scaricato (punto 12) |
| `GUIDA.txt` | questa guida, creata quando la apri dal programma |

## 8. Problemi e soluzioni

**Il report dice "BlackBox non era attivo".** Il programma era chiuso. Metti
*Avvio automatico* su ON.

**Dopo il riavvio BlackBox non parte da solo.** Probabilmente l'exe è stato
spostato: apri il programma e rimetti *Avvio automatico* su OFF e poi su ON.

**Non vedo potenza e temperatura della scheda video.** Funzionano solo con schede
AMD e NVIDIA con il driver installato.

**La temperatura della CPU dice "n/d".** BlackBox la prende da HWiNFO: apri HWiNFO
e controlla che la memoria condivisa sia accesa (punto 11).

**Gli FPS restano a "--".** Gli FPS si misurano solo durante una sessione di gioco.
Se il gioco è aperto da poco, aspetta 20 secondi. Se nella riga *FPS* c'è un pulsante,
segui il punto 12.

**L'overlay non si vede.** Controlla che sia acceso (pulsante *Overlay: ON* oppure
Ctrl + Shift + O) e che ci sia una sessione di gioco in corso. Se il gioco è in
**schermo intero esclusivo**, nelle opzioni video scegli **finestra senza bordi**
(*borderless*): in schermo intero esclusivo nessun overlay esterno può apparire.

**Non parte la sessione di gioco.** Parte solo se il gioco è in primo piano e usa la
scheda video oltre il 60% per 20 secondi. Giochi molto leggeri (o con FPS bloccati
bassi) possono restare sotto: è normale.

**Un gioco si chiude all'avvio.** Alcuni giochi online con anti-cheat non vogliono
programmi che leggono i sensori. Chiudi BlackBox dal tasto destro → *Esci* prima
di giocare, e riaprilo dopo.

**Il PC continua a scaldare.** Apri la finestra e guarda *Più CPU*: è il programma
che sta consumando di più. Se non è nell'elenco del punto 5, BlackBox non lo
chiude: va chiuso a mano da Gestione attività (Ctrl + Shift + Esc).

## 9. Privacy

- Nessun invio di dati, nessun aggiornamento automatico. L'unica connessione a internet
  è il download di PresentMon da GitHub, e solo quando premi tu il pulsante.
- Legge solo i contatori di Windows, il driver della scheda video, il Registro eventi
  e, se aperti, i valori pubblicati da HWiNFO o LibreHardwareMonitor.
- L'unica azione sul sistema è lo sblocco del punto 5, limitato all'elenco fisso.
- Tutti i dati restano in `Documenti\BlackBox`.

## 10. Sessioni di gioco

Non devi fare niente: BlackBox capisce da solo quando giochi. La sessione **parte**
quando il programma in primo piano usa la scheda video oltre il 60% per più di
20 secondi, e **finisce** quando chiudi il gioco o lo lasci in secondo piano per più
di un minuto. Nella finestra la vedi alla riga *Sessione di gioco*.

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

1. Apri HWiNFO64 e scegli **Sensors only** (solo sensori).
2. Nella finestra dei sensori clicca l'ingranaggio **Settings** (in basso).
3. Metti la spunta su **Shared Memory Support** e premi **OK**.
4. Dopo qualche secondo nel riquadro della CPU di BlackBox compare la temperatura.

Nella versione gratuita di HWiNFO la memoria condivisa si spegne da sola dopo
12 ore: se la temperatura torna "n/d", rimetti la spunta.

In alternativa va bene anche **LibreHardwareMonitor** aperto.
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

**Limite:** nei giochi in *schermo intero esclusivo* l'overlay non appare; scegli
*finestra senza bordi* (borderless) nelle opzioni video del gioco.

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

## 15. Il tuo PC: riconoscimento automatico

A ogni avvio BlackBox riconosce processore, scheda video, driver, Adrenalin e
Windows, e sceglie i limiti giusti per quei modelli. Per esempio:

| Componente | Limite usato |
|---|---|
| Radeon RX 5000/6000/7000/9000 | hotspot fino a 110 °C: attenzione da 100, problema da 107 |
| NVIDIA | temperatura del chip: attenzione da 83, problema da 88 °C |
| Ryzen 7000X3D (es. 7800X3D) | CPU fino a 89 °C |
| Ryzen 7000/9000 | CPU fino a 95 °C (ci arrivano per progetto) |
| Intel Core / Core Ultra | CPU fino a 100 / 105 °C |

**Al primo avvio** mostra cosa ha riconosciuto e cosa conviene sistemare. Lo trovi
sempre anche nel controllo dello stato, sotto *Il tuo PC*.

Se **cambi scheda video**, i valori imparati ripartono da capo. Se **aggiorni il
driver**, BlackBox controlla alla partita successiva se Adrenalin ha azzerato il tuo
UV/OC (succede spesso).