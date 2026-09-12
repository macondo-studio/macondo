# Macondo — Visual Basic 6 Analyzer & Architecture Explorer

> ⚠️ **REQUISITO FONDAMENTALE PER SVILUPPATORI E ASSISTENTI AI (LLM)**:  
> **Ogni modifica al codice, nuova funzionalità, componente UI o messaggio utente DEVE supportare nativamente il multi-lingua (Italiano + Inglese)** tramite `LocalizationService`. Non inserire mai stringhe utente hardcodate. Consulta la sezione [Supporto Multi-Lingua (i18n) & Regole per LLM](#supporto-multi-lingua-i18n--regole-per-llm) prima di qualsiasi modifica.

---

## 📌 Cos'è Macondo / VB6 Analyzer

**Macondo (VB6 Analyzer)** è un tool desktop avanzato (.NET 8 + Avalonia UI) per l'analisi statica, il reverse engineering e la visualizzazione architetturale di grandi codebase legacy in **Microsoft Visual Basic 6.0** (`.vbp`, `.vbg`, `.frm`, `.bas`, `.cls`, `.ctl`, `.dob`, `.pag`).

L'applicazione permette di:
1. **Analizzare singoli progetti (`.vbp`), gruppi di progetti (`.vbg`) o intere cartelle** contenenti centinaia di moduli VB6.
2. **Costruire un Grafo Semantico in memoria (`CodeGraph`)** con risoluzione di simboli, tipi, chiamate e istanziazioni cross-project.
3. **Esplorare visivamente l'architettura** tramite grafi interattivi, matrici di dipendenza (DSM), mappe di complessità (Treemap), grafi di rischio (Hotspots), flussi di navigazione UI (Screen Flow) e grafi di flusso di controllo (CFG).
4. **Calcolare l'impatto delle modifiche (Blast Radius)** per individuare chiamanti diretti/indiretti, form UI impattate, classi toccate e livello di rischio prima di toccare una riga di codice legacy.
5. **Generare contesti semantici ottimizzati per LLM (AI Context Slices & Repo Maps)** da iniettare in prompt per modelli AI generativi.

---

## 🏗️ Architettura della Soluzione

La soluzione è suddivisa in tre componenti modulari (.NET 8):

```
Vb6Analyzer/
├── README.md                                 # Questo file (panoramica globale e guida LLM)
├── VB6Analyzer-Project.md                    # Charter e specifiche architetturali complete
├── publish-single-file.ps1                   # Script di build per release EXE standalone
│
├── VB6AnalyzerPOC/                           # Frontend Desktop Desktop UI (Avalonia 11)
│   ├── README.md                             # Documentazione specifica della UI
│   ├── VB6AnalyzerPOC.sln                    # Solution completa (App + Parser + Semantic)
│   └── src/VB6Analyzer.App/
│       ├── Controls/                         # Custom controls: Graph, DSM, Treemap, Hotspots, ScreenFlow, CFG
│       ├── Layout/                           # Layout Engines (Radial, Affinity, Islands, Force-Directed)
│       ├── Models/                           # Modelli di visualizzazione (VisualCodeGraph, BlastRadiusGridRow)
│       ├── Services/                         # LocalizationService, SettingsService, CacheService, VcsService
│       ├── Views/                            # Finestre modali (SettingsWindow, InfoWindow)
│       └── MainWindow.axaml(.cs)             # Finestra principale a 3 pannelli (Explorer, Canvas/Tabs, Inspector)
│
└── VB6ParserSpike/                           # Core Engine: Parsing & Analisi Semantica
    ├── README.md                             # Documentazione specifica del motore semantico
    ├── VB6ParserSpike.sln                    # Solution per test e spike da console
    └── src/
        ├── VB6Analyzer.Parser/               # Grammar ANTLR4 per VB6 e codice generato
        ├── VB6Analyzer.Parser.Spike/         # Test fixture e verifiche sintattiche
        ├── VB6Analyzer.Semantic/             # Symbol extraction, Scope resolution, Call Graph, Blast Radius
        └── VB6Analyzer.Semantic.Spike/       # Test fixture per verifiche semantiche
```

---

## ⚡ Funzionalità Principali

### 1. Canvas del Grafo Interattivo (`GraphControl`)
- **Rendering ad alte prestazioni** basato su rendering vettoriale custom Avalonia (supporta fluidamente grafi da oltre 500+ nodi).
- **Pan & Zoom** fluidi, centrati sul cursore del mouse con zoom reset.
- **Evidenziazione dinamica delle relazioni**: alla selezione di una routine vengono evidenziati i chiamanti (*Callers* in ambra) e le chiamate effettuate (*Callees* in ciano).
- **Espansione / Compressione** dei contenitori (Form, Modulo, Classe) con doppio clic.
- **4 Algoritmi di Layout**:
  - *Radiale Standard*: disposizione concentrica gerarchica.
  - *Radiale per Affinità*: raggruppamento per coesione di chiamate.
  - *Cluster a Isole (Sottosistemi)*: suddivisione in macro-aree architetturali disconnesse.
  - *Force-Directed (Fisica a Molle)*: simulazione fisica di attrazione/repulsione.

### 2. Project Explorer & Ricerca Globale
- Albero del workspace raggruppato logicamente (Forms, Moduli, Classi, Controlli Utente, UDT, Enums).
- Ricerca rapida simboli con evidenziazione immediata nel grafo.
- Integrazione col filesystem: apertura diretta del file originale o della cartella contenitrice.

### 3. Symbol Inspector & Analizzatore Blast Radius
- **Dettagli Simbolo**: tipo di ritorno, parametri formali, visibilità (Public/Private/Friend), righe sorgente.
- **Blast Radius Engine**: calcolo ricorsivo dell'impatto con profondità massima, conteggio chiamanti diretti e indiretti, form e classi impattate, classificazione del rischio (`LOW`, `MEDIUM`, `HIGH`, `CRITICAL`).
- **Call Chains**: visualizzazione dei percorsi completi di invocazione a partire da bottoni/eventi UI fino alla routine selezionata.

### 4. Viste Analitiche Avanzate (Tab Centrali)
- **Matrice DSM (Design Structure Matrix)**: matrice di dipendenza tra moduli per individuare dipendenze circolari e accoppiamento.
- **Treemap Metriche**: mappa ad albero proporzionale a LOC/dimensione e colorata per Complessità Ciclomatica (CC) con drill-down interattivo.
- **Risk Hotspots**: scatter plot 2D per identificare routine ad alto rischio (Complessità Ciclomatica elevata vs Frequenza modifiche / Churn / Fan-In).
- **Flusso Schermate (Screen Flow)**: diagramma di navigazione tra Form VB6, transizioni UI, bottoni di trigger e form di avvio.
- **Control Flow Graph (CFG)**: grafo di flusso a blocchi base (Basic Blocks) per singola routine con calcolo della Complessità Ciclomatica.
- **Griglia Blast Radius Tabellare**: audit completo e ordinabile di tutte le routine del workspace con filtro per livello di rischio e ricerca.

### 5. Esportazione & Integrazione con LLM / AI
- **Esporta Repo Map (Markdown)**: esporta l'intera architettura del progetto in formato Markdown compatto e token-efficient, ideale per fare system prompt o reference per LLM.
- **Esporta / Importa Grafo (JSON)**: serializzazione completa del `CodeGraph` per analisi esterne o persistenza.
- **Context Slice LLM (1-Click)**: estrae il subgrafo semantico isolato del simbolo selezionato (comprensivo di definizioni, chiamanti, chiamate e classi istanziate) e lo copia negli appunti o lo salva in `.md` pronto da incollare in ChatGPT / Claude / Gemini.

### 6. Strumenti Operativi & Cache
- **Integrazione VCS (Git & SVN)**: rilevamento automatico del branch/trunk corrente e cambio branch direttamente dall'interfaccia (`git checkout` / `svn switch`).
- **Cache Persistente dei Progetti**: salvataggio snapshot in `%APPDATA%\Macondo\cache\` con rilevamento modifiche su disco per tempi di apertura istantanei su progetti enormi.
- **Temi Grafici**: Dark Modern (predefinito), Dark Studio, One Dark Pro, Forest Dark, Light Modern.

---

## 🌐 Supporto Multi-Lingua (i18n) & Regole per LLM

> [!IMPORTANT]
> **REQUISITO MANDATORIO PER QUALSIASI MODIFICA AL CODICE:**  
> L'applicazione supporta nativamente il **bilinguismo completo (Italiano e Inglese)** tramite la classe singleton `Macondo.App.Services.LocalizationService`.

Quando si implementa una modifica o si aggiunge codice:

1. **Mai inserire stringhe fisse/hardcoded nella UI o nel codice C#**:
   - Qualsiasi testo destinato all'utente (etichette, titoli di finestre, voci di menu, tooltip, messaggi di stato, descrizioni di colonne, placeholder, messaggi di errore, alert) deve essere recuperato tramite `LocalizationService.Instance.Get("Key")` o `LocalizationService.Instance["Key"]`.
2. **Aggiornare SEMPRE entrambi i dizionari in `LocalizationService.cs`**:
   - `_itStrings`: traduzione in lingua Italiana.
   - `_enStrings`: traduzione corrispondente in lingua Inglese con la **stessa identica chiave**.
3. **Gestione dei parametri formattati**:
   - Usare placeholder standard (`{0}`, `{1}`, ecc.) e richiamare `LocalizationService.Instance.Get("Key_Name", arg0, arg1)`.
4. **Convenzione di Naming delle Chiavi**:
   - `Menu_*` per voci della barra dei menu.
   - `Tooltip_*` per suggerimenti e descrizioni al passaggio del mouse.
   - `Status_*` per messaggi nella barra di stato o log.
   - `Tab_*` per intestazioni dei tab centrali.
   - `Inspector_*` per etichette e sezioni del pannello Inspector.
   - `Explorer_*` per voci del pannello Project Explorer.
   - `Grid_*` / `Col_*` per etichette e colonne delle griglie.
   - `Theme_*` / `Lang_*` / `Settings_*` per opzioni di configurazione.
   - `Loading_*` per titoli e sottotitoli delle operazioni asincrone.

---

## 🚀 Come Compilare ed Eseguire

### Prerequisiti
- **.NET 8.0 SDK** (o superiore).
- **Java JRE/JDK 11+** (richiesto **solo** per la compilazione ANTLR durante la build, non necessario a runtime).

### Esecuzione in Sviluppo
```bash
# Eseguire l'applicazione desktop
cd VB6AnalyzerPOC/src/VB6Analyzer.App
dotnet run

# Eseguire i test/spike del parser
cd VB6ParserSpike/src/VB6Analyzer.Parser.Spike
dotnet run

# Eseguire i test/spike dell'analisi semantica
cd VB6ParserSpike/src/VB6Analyzer.Semantic.Spike
dotnet run
```

### Pubblicazione EXE Standalone (Single-File)
Per generare l'eseguibile unico autonomo per Windows x64:
```powershell
.\publish-single-file.ps1
```
L'eseguibile generato sarà disponibile in `publish/win-x64/Macondo.exe`.

---

## 🤖 Guida per Modelli AI / LLM

Se sei un assistente AI che sta leggendo questo repository per eseguire un task o implementare una modifica:
- **Consulta sempre questo file e `VB6Analyzer-Project.md`** per comprendere lo scope e le decisioni architetturali del progetto.
- **Mantieni la separazione a livelli**:
  - `VB6Analyzer.Parser`: solo grammatica e parse tree ANTLR (nessuna logica di business o UI).
  - `VB6Analyzer.Semantic`: modelli `CodeGraph`, risoluzione semantica, calcolo metriche e impatti (indipendente dalla UI).
  - `VB6Analyzer.App`: interfaccia Avalonia, controlli grafici, gestione interazioni, caching e localizzazione.
- **Ricorda la regola i18n**: Ogni singolo testo visualizzato dall'utente deve avere la controparte in `_itStrings` e `_enStrings` in `LocalizationService.cs`.
