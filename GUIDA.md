# Guida a BlackBox

BlackBox è la "scatola nera" del PC: registra di continuo cosa succede e, se il PC
si spegne o si riavvia da solo, ti dice cosa stava facendo un attimo prima.
In più sblocca da solo alcuni pezzi di Windows quando si bloccano e scaldano il PC.

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
- **Attività in questo momento**: l'app in primo piano e i programmi che stanno
  usando più processore e più scheda video.
- **Spegnimenti anomali**: tutti gli spegnimenti improvvisi degli ultimi 120 giorni,
  con la diagnosi. Clicca la diagnosi per aprire il report completo.
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

BlackBox li controlla ogni 2 secondi. Se uno di questi resta sopra **metà di un
core per più di 60 secondi di fila**, lo chiude. Windows lo riapre da solo, pulito,
appena serve. Ricevi una notifica e lo trovi negli **Eventi** come
*"Sbloccato …"*.

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
non vengono toccati. Al massimo, se in quel momento avevi aperto il pannello emoji
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
| `GUIDA.txt` | questa guida, creata quando la apri dal programma |

## 8. Problemi e soluzioni

**Il report dice "BlackBox non era attivo".** Il programma era chiuso. Metti
*Avvio automatico* su ON.

**Dopo il riavvio BlackBox non parte da solo.** Probabilmente l'exe è stato
spostato: apri il programma e rimetti *Avvio automatico* su OFF e poi su ON.

**Non vedo potenza e temperatura della scheda video.** Funzionano solo con schede
AMD e NVIDIA con il driver installato.

**La temperatura della CPU non c'è.** Per leggerla servirebbero permessi di
amministratore; BlackBox volutamente non li chiede.

**Un gioco si chiude all'avvio.** Alcuni giochi online con anti-cheat non vogliono
programmi che leggono i sensori. Chiudi BlackBox dal tasto destro → *Esci* prima
di giocare, e riaprilo dopo.

**Il PC continua a scaldare.** Apri la finestra e guarda *Più CPU*: è il programma
che sta consumando di più. Se non è nell'elenco del punto 5, BlackBox non lo
chiude: va chiuso a mano da Gestione attività (Ctrl + Shift + Esc).

## 9. Privacy

- Nessuna connessione a internet, nessun invio di dati, nessun aggiornamento automatico.
- Legge solo i contatori di Windows, il driver della scheda video e il Registro eventi.
- L'unica azione sul sistema è lo sblocco del punto 5, limitato all'elenco fisso.
- Tutti i dati restano in `Documenti\BlackBox`.
