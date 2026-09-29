# InquadraCasa

App per smartphone (.NET MAUI, Android e iOS) che guida gli agenti immobiliari a fotografare
correttamente gli interni: in bolla, alla giusta altezza, con le inquadrature giuste, senza
controluce, mosso o oggetti fuori posto, con le foto già catalogate per immobile e ambiente.

## Funzionalità

### 1. Assistenza all'inquadratura in tempo reale
| Funzione | Come funziona |
|---|---|
| **Livella e inclinometro** | L'accelerometro calcola inclinazione (pitch) e rollio. Sull'anteprima: linea d'orizzonte, guide verticali che mostrano quanto convergono i muri e bolla. Tutto diventa **verde** entro la tolleranza (±1° di default). Il pulsante di scatto attende fino a 4 s che il telefono sia in bolla. |
| **Altezza di scatto (120–140 cm)** | Con il barometro: si appoggia il telefono a terra, si azzera e l'app mostra l'altezza stimata (precisione indicativa ±10–20 cm). Senza barometro: con il telefono in bolla la linea d'orizzonte è all'altezza della fotocamera e deve tagliare gli stipiti delle porte a circa 3/5. |
| **Maschere di composizione** | "Tre pareti" (parete di fondo + due laterali, per la profondità) e "Spigolo" (angolo della stanza al centro). Griglia dei terzi sempre attiva. |
| **Obiettivo più ampio** | Seleziona automaticamente lo zoom minimo (ultra-grandangolo, se presente). Suggerisce di tenere il telefono in orizzontale. |

### 2. Controllo qualità dopo ogni scatto
| Funzione | Come funziona |
|---|---|
| **Nitidezza / micro-mosso** | Varianza del Laplaciano per riquadri, sul telefono, in meno di un secondo. In più il giroscopio segnala se il telefono si muoveva al momento dello scatto. |
| **Controluce** | Istogramma: rileva zone bruciate in un ambiente scuro, capisce se la finestra è a sinistra, al centro o a destra e suggerisce HDR, luci accese e da quale angolazione riscattare. |
| **Elementi di disturbo** | La foto ridotta viene analizzata con Claude (visione): tavoletta alzata, cavi a vista, disordine, flaconi, riflessi del fotografo… con posizione e azione da fare. Senza rete o senza chiave il flusso continua senza questo controllo. |

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
  con l'originale in `_originali/` e i metadati (assetto, altezza, esiti) nel database locale SQLite.
- **Correzione prospettica** con l'assetto registrato dai sensori: omografia di rotazione pura
  (simula una fotocamera perfettamente in bolla), ricentraggio e ritaglio senza bordi neri. Oltre 12°
  non corregge e suggerisce di riscattare.
- **Bilanciamento luminosità**: livelli, gamma verso una mediana luminosa e bilanciamento del bianco
  parziale contro la dominante calda delle lampadine.
- **Esportazione ZIP** con `manifest.json`, da condividere con l'ufficio (mail, WhatsApp, Drive…).

## Struttura

```
src/InquadraCasa.Core        Logica senza dipendenze da MAUI (testabile): assetto, altezza, nitidezza,
                             esposizione, correzioni, catalogo, esportazione, SQLite, client Claude
src/InquadraCasa.App         App .NET MAUI: fotocamera (CommunityToolkit CameraView), overlay, pagine MVVM
tests/InquadraCasa.Core.Tests Test xUnit
```

## Compilare ed eseguire

Requisiti: .NET 10 SDK e workload MAUI (`dotnet workload install maui-android maui-ios`),
Android SDK (installato da Visual Studio o Android Studio); per iOS un Mac con Xcode.

```bash
dotnet test tests/InquadraCasa.Core.Tests                      # test della logica
dotnet build src/InquadraCasa.App -t:Run -f net10.0-android    # avvio su telefono/emulatore Android
dotnet build src/InquadraCasa.App -t:Run -f net10.0-ios        # da macOS
dotnet build src/InquadraCasa.App -p:TypeCheck=true            # solo controllo di compilazione, senza SDK mobili
```

La GitHub Action `CI` esegue i test e produce l'APK Android come artefatto.

## Configurazione

In **Impostazioni**:
- **Chiave API Anthropic** per il controllo degli oggetti di disturbo (salvata nello storage sicuro del
  sistema). In produzione è meglio indicare un **endpoint proxy** dell'agenzia che custodisca la chiave:
  una chiave distribuita dentro un'app può essere estratta.
- **Calibra livella**: telefono appoggiato in piano (verifica il segno dell'accelerometro sulla
  piattaforma), poi contro uno stipite verticale (azzera lo scostamento del sensore).
- Obiettivo di riferimento (campo visivo usato per la correzione prospettica), tolleranza della livella,
  intervallo di altezza, correzioni automatiche.

## Limiti noti e prossimi passi

- **HDR**: `CameraView` non espone il controllo dell'HDR né dell'esposizione, quindi l'app lo
  **suggerisce** dopo aver rilevato il controluce. Il passo successivo è un'anteprima nativa
  (CameraX Extensions su Android, AVFoundation su iOS) per attivarlo in automatico o fare bracketing.
- Il controllo del controluce avviene **dopo** lo scatto: il componente fotocamera non fornisce i
  fotogrammi dell'anteprima. Con la fotocamera nativa si potrà analizzare l'anteprima in tempo reale.
- La correzione prospettica usa i sensori, non le linee dell'immagine: se il campo visivo impostato
  è molto diverso da quello reale la correzione è approssimata. Evoluzione: rifinitura con rilevamento
  delle linee verticali.
- L'altezza da barometro è indicativa; con ARCore/ARKit si può misurare la distanza dal pavimento.
- L'app non è ancora stata provata su dispositivi reali: la logica è coperta da test, la UI va
  verificata sul campo (in particolare il segno dei sensori su iOS, gestito dalla calibrazione).
