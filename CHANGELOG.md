# Novità di BlackBox

I file di ogni versione (installer, exe portatile, guida) sono nella pagina
[Releases](https://github.com/Pronerious/blackbox-crash-recorder/releases).

## 1.2.9 — 19/10/2026
- OptiScaler bloccato anche nei giochi **sempre online con controlli sul server** (ban dell'account anche senza anti-cheat sul PC): **Forza Horizon 4/5/6**, Forza Motorsport, The Crew Motorfest, Diablo IV, World of Warcraft, Path of Exile, Black Desert, Genshin Impact, Honkai: Star Rail, Zenless Zone Zero, Wuthering Waves, Helldivers 2, Throne and Liberty, Wardogs. Prima Forza Horizon 6 non era bloccato.

## 1.2.8 — 18/10/2026
- **Niente più falsi confronti di efficienza.** Le partite brevi (meno di 5 minuti, spesso solo menu con FPS bloccati e pochi watt) non contano più come base di confronto. Prima una partita normale di 2 ore poteva risultare «-35%» senza motivo.
- L'efficienza (FPS per watt) conta solo i secondi in cui si gioca davvero, non menu e caricamenti.
- Nel report: «in linea» se la differenza è sotto il 5%; altrimenti dice se la configurazione era la stessa (quindi scene diverse) o un'altra.
- Il «prima e dopo» di OptiScaler ignora le partite brevi; il valore dal vivo nel Controllo stato avvisa che cambia con la scena.
- Finestra principale: la scritta del test A/B non è più tagliata («2 ore senza spegnimenti» invece di «0.1 giorni»); riquadri in alto più bassi, così sotto c'è più spazio; i pulsanti vanno a capo invece di coprire il titolo; la finestra non si stringe più sotto la misura in cui tutto è leggibile (sugli schermi piccoli si apre a tutto schermo).

## 1.2.7 — 18/10/2026
- **Nuova finestra «Partite»** (pulsante *Partite* o tasto destro sull'icona → *Partite e confronti*):
  - **grafici di ogni partita**: FPS (e FPS reali con la generazione di fotogrammi), temperature hotspot/edge/CPU, watt e clock della scheda video;
  - **confronto di due partite** per capire quale undervolt va meglio: valori affiancati in verde/rosso e giudizio (prima la stabilità, poi gli FPS per watt; sotto il 3% «praticamente uguali»; nessun vincitore tra giochi diversi);
  - **riassunti da copiare e incollare** (partita, confronto, ultimo report di crash) con nome utente, nome del PC e cartelle nascosti.
  Tutto calcolato dai file già salvati e solo a finestra aperta: nessun effetto sul gioco.
- **Ancora più tranquillo con gli anti-cheat**: del gioco BlackBox chiede a Windows solo nome e percorso del file, con il permesso più basso (lo stesso di Gestione attività); il controllo dei file di OptiScaler avviene solo nei giochi dove l'hai installato.
- Grafica: menu a tendina, barre di scorrimento e suggerimenti scuri, coerenti con il resto del programma.
- Guida: nuovo punto 17 (Partite) e risposta alla domanda «BlackBox dà problemi con l'anti-cheat?».

## 1.2.6 — 18/10/2026
- **Emulatori riconosciuti** (PCSX2, RetroArch, DuckStation, PPSSPP, RPCS3, Dolphin, Cemu, Ryujinx, Xenia, Xemu e altri): overlay e FPS anche se usano poco la scheda video.
- **Giochi a schermo intero** (o senza bordi) riconosciuti dal 15% di scheda video; browser, lettori video e app ingrandite esclusi.
- **Overlay e FPS più rapidi**: la sessione parte in 5 secondi (schermo intero ed emulatori) o 8 (gli altri), non più 20.
- Con più monitor l'overlay compare **sul monitor del gioco**.
- Corretto l'errore «il thread chiamante non riesce ad accedere a questo oggetto» installando OptiScaler (non veniva copiato nessun file).

## 1.2.5 — 18/10/2026
- Dopo aver svuotato la cartella dei log (o al primo avvio) non compare più il riquadro rosso per un vecchio spegnimento di cui BlackBox non ha dati: lo spegnimento resta comunque nello storico.

## 1.2.4 — 18/10/2026
- Riconosce il **limite delle 12 ore di HWiNFO gratuito** (la temperatura della CPU sparisce) e lo spiega.
- **Riavvio automatico di HWiNFO** facoltativo: consenso di Windows una volta, poi BlackBox lo riavvia da solo quando servono, mai durante una partita.
- Passi di HWiNFO con i nomi in italiano e in inglese (*Impostazioni principali → Supporto memoria condivisa*).

## 1.2.3 — 18/10/2026
- **Limiti di temperatura ufficiali per modello** (`data/limiti.json`): Ryzen 3000-9000, Intel 10ª-14ª generazione e Core Ultra, NVIDIA RTX 20-50 e GTX 16, Radeon RX, Intel Arc, con la fonte per ogni riga; stima prudente dichiarata per i modelli non in tabella.
- Pulsante **Aggiorna i limiti** (solo su richiesta).
- OptiScaler: **nome della DLL consigliato per ogni gioco** con il motivo, spiegazioni di nome DLL e fakenvapi.

## 1.2.2 — 18/10/2026
- Pulsante **«Cos'è?»** su ogni voce del Controllo stato, con spiegazioni in parole semplici.
- **Causa e soluzione quando l'1% low è basso.**
- Guida: «Le parole difficili, spiegate».

## 1.2.1 — 18/10/2026
- Finestra **Controllo stato / Il tuo PC** con i **pulsanti che sistemano** (HAGS, Modalità gioco, HWiNFO, PresentMon); benvenuto del primo avvio corretto.
- **OptiScaler a prova di errore**: giochi online noti e a 32 bit bloccati, conferma «solo offline», avviso ReShade, istruzioni per attivarlo nel gioco.
- L'installer **disinstalla prima la versione vecchia**.
- Revisione completa: niente falsi allarmi sul reset del tuning tra giochi diversi, crash dei giochi letti subito, report non perso alla chiusura.

## 1.2.0 — 18/10/2026
- **Sessioni di gioco** con report di fine partita, throttling, avviso «Adrenalin ha rimesso i valori di fabbrica».
- **FPS, 1% low e frame time** con PresentMon; **generazione di fotogrammi** riconosciuta (FPS reali).
- **Overlay in gioco** personalizzabile e leggerissimo (senza DLL nel gioco).
- **Controllo dello stato** e giudizio su undervolt/overclock (FPS per watt).
- **Temperatura vera della CPU** da HWiNFO o LibreHardwareMonitor.
- **Giochi & OptiScaler**: installa / aggiorna / rimuovi con copie di sicurezza, blocco anti-cheat.
- **Riconoscimento del PC** con limiti per modello; sblocco dei processi di Windows più prudente.
- **Installer** senza amministratore.

## 1.1.1 — 12/10/2026
- Guida completa in italiano (`GUIDA.md`), anche dentro il programma.

## 1.1.0 — 12/10/2026
- Sblocco automatico dei processi di Windows che restano bloccati e scaldano il PC.

## 1.0.0 — 28/09/2026
- Prima versione: registrazione ogni secondo su disco, sensori GPU AMD e NVIDIA, report e diagnosi dopo gli spegnimenti anomali, test A/B, icona nell'area di notifica.
