# Novità di BlackBox

I file di ogni versione (installer, exe portatile, guida) sono nella pagina
[Releases](https://github.com/Pronerious/blackbox-crash-recorder/releases).

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
