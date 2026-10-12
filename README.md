# BlackBox — la scatola nera del tuo PC

Il PC si spegne o si riavvia da solo, senza schermata blu e senza lasciare tracce?
**BlackBox** registra di continuo lo stato del sistema, come la scatola nera di un aereo.
Dopo uno spegnimento anomalo crea un **report con gli ultimi minuti prima del crash** e una **diagnosi automatica** della causa più probabile.

> 🇬🇧 *BlackBox is a lightweight Windows "flight recorder": it logs CPU/GPU/RAM state every second straight to disk and, after an unexpected shutdown (Kernel-Power 41), produces a report of the last minutes before the crash with an automatic diagnosis (PSU / GPU power spike, CPU undervolt, RAM profile, overheating, sleep transitions, BSOD). It also restarts known Windows shell components (TextInputHost, SearchHost, …) when they get stuck burning a CPU core for over a minute. 100% local, no network, no telemetry. UI in Italian.*

---

## Cosa fa

- **Registrazione ogni secondo, direttamente su disco** (scrittura senza cache): anche se la corrente salta di colpo resta l'ultimo secondo prima dello spegnimento.
- **CPU**: carico totale, core più carico, frequenza reale.
- **GPU**: carico, **potenza con picchi letti 5 volte al secondo**, frequenze, tensione, temperature (edge / hotspot / memoria), ventola.
  - Schede **AMD** (tramite il driver, `atiadlxx.dll`) e **NVIDIA** (tramite `nvml.dll`).
- **RAM / VRAM**, app in primo piano, processi che usano più CPU e GPU.
- **Eventi di Windows**: sospensione e ripresa, crash del driver video (TDR), errori hardware WHEA, schermate blu, crash delle app, buchi nei dati.
- **Report automatico** al riavvio dopo un Kernel-Power 41, con:
  - medie e massimi dell'ultimo minuto;
  - ultimi 5 minuti di dati;
  - **diagnosi**: picco di potenza GPU → alimentatore, crash a basso carico → undervolt CPU/RAM, surriscaldamento, durante sospensione/spegnimento, BSOD, spegnimento manuale.
  - Le soglie di potenza sono **relative alla tua scheda** (massimo mai registrato su quel PC), quindi valgono per qualsiasi GPU.
- **Test A/B**: scrivi la configurazione attuale (es. `CO -20, EXPO ON, GPU UV -50mV`). BlackBox associa ogni spegnimento alla configurazione attiva e conta i giorni stabili, così capisci quale modifica risolve davvero.
- **Storico** di tutti gli spegnimenti anomali degli ultimi 120 giorni.
- **Sblocco automatico dei processi di Windows impazziti**: alcuni componenti di Windows a volte restano bloccati in un ciclo, usano un core intero del processore e scaldano il PC senza motivo. Se uno di questi resta sopra il 50% di un core per più di 60 secondi di fila, BlackBox lo chiude, lo scrive negli eventi e mostra una notifica. Windows lo riapre da solo, pulito, appena serve. Controllati:

  | Processo | Cos'è |
  |---|---|
  | `TextInputHost` | tastiera virtuale, pannello emoji (Win + .), cronologia appunti (Win + V) |
  | `SearchHost` | pannello Cerca |
  | `StartMenuExperienceHost` | menu Start |
  | `ShellExperienceHost` | centro notifiche, calendario, impostazioni rapide |
  | `CrossDeviceResume` | "riprendi dal telefono" (bug noto di Windows 11) |
  | `Widgets`, `WidgetService` | pannello Widget |
  | `PhoneExperienceHost` | Collegamento al telefono |
  | `LockApp` | schermata di blocco |

  Nessun altro processo viene mai toccato: i tuoi programmi e i giochi non vengono chiusi.
- **Area di notifica**: chiudi la finestra e continua a registrare in background; notifiche quando rileva un problema; avvio automatico con Windows (opzionale).

## Privacy

- **Nessuna connessione di rete, nessuna telemetria, nessun aggiornamento automatico.**
- L'unica azione sul sistema è lo sblocco automatico descritto sopra, limitato all'elenco fisso di componenti di Windows.
- Legge solo i contatori di prestazioni di Windows, il driver della scheda video già installato e il Registro eventi.
- Tutti i dati restano in `Documenti\BlackBox` sul tuo PC. I CSV più vecchi di 14 giorni vengono cancellati automaticamente.
- Il codice sorgente è tutto qui: puoi leggerlo e compilarlo da solo.

## Requisiti

- Windows 10 o 11, 64 bit.
- .NET Framework 4.x (già incluso in Windows).
- Non servono privilegi di amministratore.

## Installazione

1. Scarica `BlackBox.exe` dalla pagina **[Releases](../../releases)**.
2. Mettilo in una cartella permanente (es. `C:\Programmi\BlackBox` o `Documenti\BlackBox-app`).
3. Avvialo. Per registrare anche dopo i riavvii, attiva **"Avvio automatico"** dall'interfaccia.

> Windows SmartScreen potrebbe mostrare un avviso perché l'exe non è firmato digitalmente: clicca **Ulteriori informazioni → Esegui comunque**, oppure compilalo tu dal sorgente.

## Compilare dal sorgente

Nessuna dipendenza da scaricare: usa il compilatore C# incluso in Windows.

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

L'eseguibile viene creato in `dist\BlackBox.exe`.

## Uso

La guida completa, passo passo, è in **[GUIDA.md](GUIDA.md)** e si apre anche dal programma (pulsante **Guida** o tasto destro sull'icona → **Guida**).

| Azione | Effetto |
|---|---|
| Chiudi / riduci a icona la finestra | va nell'area di notifica e **continua a registrare** |
| Clic sinistro sull'icona | riapre la finestra |
| Clic destro sull'icona | menu: apri, ultimo report, cartella log, guida, esci |
| **Esci** dal menu | ferma davvero la registrazione |

Opzioni da riga di comando:

- `--tray`: avvia direttamente nell'area di notifica (usato dall'avvio automatico).
- `--logdir <cartella>`: usa una cartella dati diversa da `Documenti\BlackBox`.

### File generati (in `Documenti\BlackBox`)

| File | Contenuto |
|---|---|
| `monitor_AAAA-MM-GG.csv` | un campione al secondo (separatore `;`) |
| `eventi.log` | eventi di sistema rilevati |
| `crash_AAAAMMGG_HHMMSS.txt` | report di ogni spegnimento anomalo |
| `configurazioni.log` | storico delle configurazioni per il test A/B |

## Limiti

- La **temperatura della CPU** non viene letta: richiederebbe privilegi di amministratore o un driver di terze parti.
- I sensori della GPU funzionano con schede **AMD** e **NVIDIA**. Con altre schede vengono registrati solo carico e VRAM.
- La diagnosi è un'indicazione basata su regole, non una certezza: confronta più report e usa il test A/B cambiando una cosa alla volta.

## Licenza

[MIT](LICENSE)
