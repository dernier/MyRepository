# AppPhotoImmibili

App per smartphone (.NET MAUI, Android e iOS) che guida gli agenti immobiliari a fotografare
correttamente gli interni: in bolla, alla giusta altezza, con le inquadrature giuste, senza
controluce, mosso o oggetti fuori posto, con le foto già catalogate per immobile e ambiente e caricate
in secondo piano sul gestionale dell'agenzia.

## Architettura

```
┌──────────────────────────────────────────────────────────────────────┐
│                   Interfaccia utente (.NET MAUI, MVVM)               │
│   [Griglia e maschere]  [Livella / orizzonte]  [Altezza]  [Revisione]│
├──────────────────────────────────────────────────────────────────────┤
│ CameraView (CommunityToolkit.Maui.Camera)  │ Sensori (Microsoft.Maui │
│  → CameraX su Android, AVFoundation su iOS │  .Devices.Sensors)      │
├──────────────────────────────────────────────────────────────────────┤
│                   AppPhotoImmibili.Core (net10.0, testata)           │
│ Assetto · Altezza · Nitidezza · Esposizione · Linee verticali (Hough)│
│ Correzione prospettica e tonale (SkiaSharp) · EXIF · Catalogo · ZIP  │
│ Oggetti di disturbo: ONNX Runtime sul telefono + Claude nel cloud    │
│ Coda di caricamento (SQLite) · Uploader HTTP                         │
└──────────────────────────────────────────────────────────────────────┘
```

| Area tecnologica | Scelta |
|---|---|
| Fotocamera e anteprima | `CommunityToolkit.Maui.Camera` (`CameraView`), che usa CameraX e AVFoundation. Obiettivo più ampio selezionato in automatico. |
| Livella giroscopica | `Accelerometer` per beccheggio e rollio (filtro passa-basso), `Gyroscope` per il rischio di mosso, `Barometer` per l'altezza. |
| Sovrapposizioni grafiche | `Microsoft.Maui.Graphics` (`GraphicsView`, disegno accelerato) sopra l'anteprima; SkiaSharp per l'elaborazione delle foto. |
| Linee verticali | Trasformata di Hough in C# (senza OpenCV), ristretta alle direzioni quasi verticali. |
| AI on-device | ONNX Runtime con modelli YOLOv8/YOLO11; accelerazione NNAPI (Android) e CoreML (iOS). |
| AI nel cloud (facoltativa) | Claude (visione) per ciò che un modello generico non vede: tavoletta alzata, disordine, riflessi. |
| Metadati | Segmento EXIF scritto direttamente nel JPEG finale. |
| Sincronizzazione | Coda persistente in SQLite, caricamento HTTP multipart in secondo piano, con attese crescenti. |

## Funzionalità

### 1. Assistenza all'inquadratura in tempo reale
| Funzione | Come funziona |
|---|---|
| **Livella e inclinometro** | L'accelerometro calcola inclinazione (pitch) e rollio. Sull'anteprima: linea d'orizzonte, guide verticali che mostrano quanto convergono i muri e bolla. Tutto diventa **verde** entro la tolleranza (±1° di default). Il pulsante di scatto attende fino a 4 s che il telefono sia in bolla. |
| **Altezza di scatto (120–140 cm)** | Con il barometro: si appoggia il telefono a terra, si azzera e l'app mostra l'altezza stimata (precisione indicativa ±10–20 cm). Senza barometro: con il telefono in bolla la linea d'orizzonte è all'altezza della fotocamera e deve tagliare gli stipiti delle porte a circa 3/5. |
| **Maschere di composizione** | "Tre pareti" (parete di fondo e due laterali, per la profondità) e "Spigolo" (angolo della stanza al centro). Griglia dei terzi sempre attiva. |
| **Obiettivo più ampio** | Seleziona automaticamente lo zoom minimo (ultra-grandangolo, se presente). Suggerisce di tenere il telefono in orizzontale. |

### 2. Controllo qualità dopo ogni scatto
| Funzione | Come funziona |
|---|---|
| **Nitidezza / micro-mosso** | Varianza del Laplaciano per riquadri, sul telefono, in meno di un secondo. Il giroscopio segnala anche se il telefono si muoveva al momento dello scatto. |
| **Controluce** | Istogramma: rileva zone bruciate in un ambiente scuro, capisce se la finestra è a sinistra, al centro o a destra e suggerisce HDR, luci accese e da quale angolazione riscattare. |
| **Elementi di disturbo sul telefono** | Modello ONNX (YOLOv8/YOLO11) eseguito in locale, senza rete: persone, animali, bottiglie e flaconi, stoviglie, borse, giocattoli… con posizione nell'inquadratura e azione da fare. Con un modello addestrato sulle foto immobiliari riconosce anche tavoletta alzata, cavi, disordine, panni, spazzatura (vedi sotto). |
| **Elementi di disturbo nel cloud** | Facoltativo: la foto ridotta viene analizzata con Claude, che riconosce anche disordine, letto disfatto, riflessi del fotografo. I due esiti vengono uniti. |

La schermata di revisione mostra l'esito e consiglia se **riscattare prima di cambiare ambiente**.

### 3. Flusso guidato
- Scelta della tipologia (monolocale, bilocale, trilocale, quadrilocale, villa, ufficio, negozio) con il
  numero di panoramiche per ambiente (es. 2 soggiorno, 1 cucina, 2 camera padronale, 1 bagno).
- Consigli specifici per ambiente e promemoria dei **dettagli di pregio** (finiture, vista dal balcone…).
- Alla fine di ogni ambiente l'app propone il dettaglio da scattare o il prossimo ambiente.
- Ambienti aggiungibili, rinominabili ed eliminabili.

### 4. Catalogazione e post-produzione automatica
- Ogni foto è salvata come
  `Immobili/RIF-1234_via-roma-10-milano/03_cucina/RIF-1234_03_cucina_panoramica_01.jpg`
  con l'originale in `_originali/` e i metadati nel database locale SQLite.
- **Correzione prospettica** con l'assetto registrato dai sensori: omografia di rotazione pura
  (simula una fotocamera perfettamente in bolla), ricentraggio e ritaglio senza bordi neri. Oltre 12°
  non corregge e suggerisce di riscattare.
- **Affinamento con le linee verticali**: la trasformata di Hough trova spigoli, stipiti e mobili; una
  regressione della loro pendenza rispetto alla posizione stima inclinazione e rollio reali. Se la stima è
  affidabile e vicina a quella dei sensori (entro 3°) la correzione usa quella, e le verticali risultano
  esattamente parallele anche quando il sensore è impreciso.
- **Bilanciamento luminosità**: livelli, gamma verso una mediana luminosa e bilanciamento del bianco
  parziale contro la dominante calda delle lampadine.
- **Metadati EXIF** nel JPEG finale: descrizione (immobile · ambiente), agente (Artist), parole chiave
  visibili in Esplora risorse, data di scatto e, in `UserComment`, un JSON con codice immobile, indirizzo,
  ambiente, tipo e numero di scatto, inclinazione, rollio, altezza, nitidezza e correzioni applicate.
- **Esportazione ZIP** con `manifest.json`, da condividere con l'ufficio (mail, WhatsApp, Drive…).

### 5. Caricamento in secondo piano sul gestionale
Ogni foto confermata entra in una coda salvata nel database: l'agente continua a scattare mentre l'app
carica, dopo ogni scatto, al ritorno della rete e ogni 3 minuti (facoltativamente solo con Wi-Fi).
Dopo un errore di rete la foto viene ritentata con attese crescenti (1, 2, 4… fino a 60 minuti); gli errori
definitivi (token non valido, dati rifiutati) restano segnalati finché l'agente non preme "Riprova".
Su iOS il caricamento in corso può finire anche se l'app viene chiusa (`BeginBackgroundTask`).

Contratto HTTP atteso dal gestionale:

```
POST {indirizzo}/immobili/{codice}/foto
Authorization: Bearer {token}
Idempotency-Key: {uid della foto}
Content-Type: multipart/form-data
  metadati = JSON (gli stessi dati scritti nell'EXIF)
  foto     = JPEG per il web (lato lungo 2048 px, qualità 85, con EXIF)
```

Risposte: `2xx` caricata; `409` già presente (considerata caricata); `401/403/404/400/413/422` errore
definitivo; `408/429/5xx` e assenza di rete: nuovo tentativo più tardi. È accettato solo `https`.

## Modello ONNX per gli oggetti di disturbo

Il modello **non è incluso** nel repository. Per usarlo:

1. Esporta un modello YOLO in ONNX, per esempio
   `pip install ultralytics && yolo export model=yolo11n.pt format=onnx imgsz=640`
   (≈ 10 MB, pochi decimi di secondo sul telefono).
2. Importalo da **Impostazioni → Importa modello .onnx**, oppure includilo nell'app copiandolo in
   `src/AppPhotoImmibili.App/Resources/Raw/distrattori.onnx`.

Un modello COCO pre-addestrato riconosce solo alcune classi utili (persone, animali, bottiglie,
stoviglie, borse, giocattoli…). Per tavoletta alzata, cavi a vista, disordine, panni, spazzatura serve un
modello addestrato su foto immobiliari: le classi `toilet_seat_up`, `cable`, `clutter`, `trash`, `laundry`,
`shoes`, `detergent`, `unmade_bed`, `open_door_cabinet`, `photographer_reflection` (o i nomi italiani
`tavoletta_alzata`, `cavi`, `disordine`, `spazzatura`, `panni`, `scarpe`) sono già tradotte in indicazioni
per l'agente (`DistractorCatalog`). I nomi delle classi vengono letti dai metadati del modello.

> Licenza: i pesi YOLOv8/YOLO11 di Ultralytics sono AGPL-3.0; per un'app distribuita senza pubblicarne il
> codice serve la licenza commerciale Ultralytics oppure un modello con licenza permissiva.

## Struttura

```
AppPhotoImmibili.sln
src/AppPhotoImmibili.Core          Logica senza dipendenze da MAUI (testabile): assetto, altezza, nitidezza,
                                   esposizione, linee verticali, correzioni, EXIF, catalogo, esportazione,
                                   SQLite, rilevamento ONNX, client Claude, coda di caricamento
src/AppPhotoImmibili.App           App .NET MAUI: fotocamera, overlay, pagine MVVM, servizi di piattaforma
tests/AppPhotoImmibili.Core.Tests  Test xUnit (con un finto modello ONNX generato da Assets/make_tiny_yolo.py)
```

## Compilare ed eseguire

Requisiti: .NET 10 SDK e workload MAUI (`dotnet workload install maui-android maui-ios`),
Android SDK (installato da Visual Studio o Android Studio); per iOS un Mac con Xcode.

```bash
dotnet test tests/AppPhotoImmibili.Core.Tests                      # test della logica
dotnet build src/AppPhotoImmibili.App -t:Run -f net10.0-android    # avvio su telefono/emulatore Android
dotnet build src/AppPhotoImmibili.App -t:Run -f net10.0-ios        # da macOS
dotnet build src/AppPhotoImmibili.App -p:TypeCheck=true            # solo controllo di compilazione, senza SDK mobili
```

La GitHub Action `CI` esegue i test, produce l'APK Android come artefatto e compila l'app per il
simulatore iOS.

## Configurazione

In **Impostazioni**:
- **Controllo oggetti di disturbo**: sul telefono, nel cloud o entrambi; import del modello ONNX.
- **Chiave API Anthropic** per il controllo nel cloud (salvata nello storage sicuro del sistema). In
  produzione è meglio indicare un **endpoint proxy** dell'agenzia che custodisca la chiave: una chiave
  distribuita dentro un'app può essere estratta.
- **Caricamento sul gestionale**: indirizzo https, token (storage sicuro), solo Wi-Fi, nome dell'agente.
- **Calibra livella**: telefono appoggiato in piano (verifica il segno dell'accelerometro sulla
  piattaforma), poi contro uno stipite verticale (azzera lo scostamento del sensore).
- Obiettivo di riferimento (campo visivo usato per la correzione prospettica), tolleranza della livella,
  intervallo di altezza, correzioni automatiche e affinamento con le linee verticali.

## Limiti noti e prossimi passi

- **HDR e analisi dell'anteprima**: `CameraView` non espone il controllo dell'HDR né i fotogrammi
  dell'anteprima, quindi il controluce è rilevato **dopo** lo scatto e l'HDR viene **suggerito**. Il passo
  successivo è un Custom Handler nativo (CameraX con `ImageAnalysis` ed Extensions su Android,
  `AVCaptureSession` con `AVCaptureVideoDataOutput` su iOS) per analizzare l'anteprima in tempo reale,
  attivare l'HDR o fare bracketing.
- **Caricamento su Android con app chiusa**: la coda riprende all'apertura successiva; per caricare anche
  con l'app chiusa si può aggiungere un `Worker` di WorkManager che richiami lo stesso `SyncEngine`.
- L'altezza da barometro è indicativa; con ARCore/ARKit si può misurare la distanza dal pavimento.
- L'app non è ancora stata provata su dispositivi reali: la logica è coperta da test, la UI va
  verificata sul campo (in particolare il segno dei sensori su iOS, gestito dalla calibrazione).
