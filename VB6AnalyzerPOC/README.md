# VB6 Analyzer — Desktop Application (Macondo UI)

Questo modulo contiene l'interfaccia grafica desktop completa di **Macondo (VB6 Analyzer)**, sviluppata in **C# / .NET 8** con **Avalonia UI 11**.

> ⚠️ **REQUISITO FONDAMENTALE MULTI-LINGUA (i18n) PER SVILUPPATORI ED LLM:**  
> L'applicazione supporta nativamente il **bilinguismo completo (Italiano e Inglese)** tramite `Macondo.App.Services.LocalizationService`.  
> **Ogni modifica alla UI, nuovo controllo, messaggio di stato, intestazione di colonna, tooltip o finestra di dialogo DEVE utilizzare `LocalizationService.Instance.Get("Key")` ed avere la chiave registrata sia in `_itStrings` (Italiano) che in `_enStrings` (Inglese) in `LocalizationService.cs`. Non inserire mai testi hardcodati.**

---

## 🖥️ Panoramica dell'Applicazione

L'applicazione desktop fornisce una suite integrata per l'analisi architetturale, l'esplorazione del codice e la valutazione del rischio di modifica su progetti Microsoft Visual Basic 6.0 (`.vbp`, `.vbg`, `.frm`, `.bas`, `.cls`, `.ctl`).

La finestra principale (`MainWindow.axaml`) è strutturata su 3 aree principali:
1. **Pannello Sinistro — Project Explorer**: navigazione ad albero del workspace (Forms, Moduli, Classi, Controlli, UDT, Enums) con ricerca globale e menu contestuale per aprire file e cartelle nel filesystem.
2. **Area Centrale — Viste & Strumenti Analitici**:
   - **Vista Grafo (`GraphControl`)**: rendering vettoriale 2D ad alte prestazioni con zoom, pan, selezione nodi, evidenziazione di chiamanti (*Callers*) e chiamate (*Callees*), collasso/espansione dei container e 4 motori di layout.
   - **Matrice DSM (`DsmControl`)**: matrice di dipendenza tra moduli per identificare accoppiamenti e dipendenze circolari.
   - **Treemap Metriche (`TreemapControl`)**: mappa gerarchica delle dimensioni (LOC) e della complessità ciclomatica con drill-down.
   - **Hotspot di Rischio (`HotspotControl`)**: grafico di dispersione 2D che incrocia la Complessità Ciclomatica con la Frequenza di modifica/Fan-in.
   - **Flusso Schermate (`ScreenFlowControl`)**: mappa delle transizioni e navigazione tra le finestre UI.
   - **Control Flow Graph (`ControlFlowControl`)**: visualizzazione a blocchi base del flusso di controllo per singola routine.
   - **Griglia Blast Radius**: tabella interattiva con l'audit completo di tutte le routine e filtro per livello di rischio.
3. **Pannello Destro — Symbol Inspector & Blast Radius**:
   - Ispezione semantica dettagliata (visibilità, tipo, parametri, righe, chi chiama la routine e cosa viene chiamato).
   - Calcolo del **Blast Radius** con profondità massima, form impattate, classi toccate, catene di chiamata e livello di rischio (`LOW`, `MEDIUM`, `HIGH`, `CRITICAL`).
   - Generazione ed esportazione di **Context Slice per LLM** (Markdown mirato pronto da incollare nei prompt AI).

---

## 📐 Struttura del Progetto

```
src/VB6Analyzer.App/
├── Controls/                         # Custom controls vettoriali per le varie viste
│   ├── GraphControl.cs               # Canvas del grafo interattivo (pan, zoom, rendering custom)
│   ├── DsmControl.cs                 # Rendering e interazione matrice DSM
│   ├── TreemapControl.cs             # Rendering Treemap gerarchica con drill-down
│   ├── HotspotControl.cs             # Rendering scatter plot Hotspot di rischio
│   ├── ScreenFlowControl.cs          # Rendering grafo di flusso schermate UI
│   └── ControlFlowControl.cs         # Rendering grafo CFG (Basic Blocks)
├── Layout/                           # Motori di calcolo delle coordinate del grafo
│   ├── IGraphLayoutEngine.cs         # Interfaccia comune di layout
│   ├── GraphLayoutManager.cs         # Dispatcher dei layout disponibili
│   ├── RadialLayoutEngine.cs         # Layout Radiale Standard
│   ├── AffinityRadialLayoutEngine.cs # Layout Radiale per Affinità di chiamate
│   ├── ClusteredIslandsLayoutEngine.cs# Layout Cluster a Isole (Sottosistemi)
│   └── ForceDirectedLayoutEngine.cs  # Layout Force-Directed (simulazione fisica a molle)
├── Models/                           # Modelli di presentazione per la UI
│   ├── VisualCodeGraph.cs            # Grafo visuale arricchito con coordinate e stato visivo
│   ├── GraphNode.cs & GraphEdge.cs   # Nodi e archi visualizzati
│   ├── ExplorerTreeItem.cs           # Nodi dell'albero Project Explorer
│   ├── BlastRadiusGridRow.cs         # Righe della tabella di audit Blast Radius
│   └── SearchResultItem.cs           # Risultati della ricerca rapida
├── Services/                         # Servizi applicativi
│   ├── LocalizationService.cs        # Motore i18n (Italiano / Inglese)
│   ├── SettingsService.cs            # Persistenza configurazione e preferenze utente
│   ├── ThemeService.cs               # Gestione dei 5 temi grafici scuri e chiari
│   ├── ProjectAnalysisService.cs     # Orchestratore asincrono del parsing e analisi semantica
│   ├── ProjectCacheService.cs        # Caching persistente dei progetti analizzati
│   ├── GitService.cs & SvnService.cs # Integrazione con i sistemi di controllo versione
│   └── UpdateService.cs              # Controllo e notifica nuovi aggiornamenti
├── Views/                            # Finestre secondarie e dialog
│   ├── SettingsWindow.axaml(.cs)     # Finestra Impostazioni (Temi, Lingua, Comportamento, Scorciatoie)
│   └── InfoWindow.axaml(.cs)         # Finestra Guida, concetti e statistiche
├── MainWindow.axaml(.cs)             # Finestra principale con gestione layout, menu e comandi
├── App.axaml(.cs)                    # Entry point Avalonia e risorse condivise
└── Program.cs                        # Entry point dell'applicazione
```

---

## 🌐 Linee Guida per il Multi-Lingua (`LocalizationService`)

Tutti i testi dell'applicazione sono centralizzati in `Services/LocalizationService.cs`.

### Come usare le traduzioni nel codice C#:
```csharp
// Recupero di una stringa semplice
string title = LocalizationService.Instance["Inspector_Title"];

// Recupero con formattazione e parametri
string status = LocalizationService.Instance.Get("Status_BlastReportFormat", symbolName, directCount, indirectCount, formsCount, riskLevel);
```

### Come aggiungere nuove voci:
In `LocalizationService.cs`, aggiungere la chiave con la stringa tradotta in **entrambi** i dizionari:
```csharp
// In _itStrings:
["MiaChiave_Esempio"] = "Testo descrittivo in italiano con parametro {0}",

// In _enStrings:
["MiaChiave_Esempio"] = "Descriptive English text with parameter {0}",
```

---

## 🚀 Esecuzione e Build

### Avvio in modalità sviluppo
```bash
cd src/VB6Analyzer.App
dotnet run
```

### Build eseguibile singolo per Windows x64
Dalla root del repository:
```powershell
.\publish-single-file.ps1
```
oppure:
```bash
dotnet publish src/VB6Analyzer.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ../../publish/win-x64
```
L'eseguibile autonomo `Macondo.exe` sarà generato nella cartella `publish/win-x64/`.
