# VB6 Analyzer — M0 Technology POC

Questo è il POC descritto nella sezione 41 del project charter: **non fa parsing VB6**,
serve solo a validare lo stack (C# + Avalonia) prima di investire nel parser reale.

> ⚠️ **Nota**: questo codice è stato scritto in un ambiente senza SDK .NET installato e
> senza accesso a NuGet, quindi non è stato possibile compilarlo/eseguirlo qui. È scritto
> per essere corretto e idiomatico per Avalonia 11 / .NET 8, ma **va validato con una
> build reale in locale** — è esattamente il tipo di verifica che il POC deve fare.

## Prerequisiti

- [.NET 8 SDK](https://dotnet.microsoft.com/download) (o superiore)
- Windows, Linux o macOS (il target primario resta Windows, ma Avalonia è cross-platform
  e questo aiuta a sviluppare anche da altre macchine)

## Eseguire in sviluppo

```bash
cd src/VB6Analyzer.App
dotnet run
```

Al primo avvio genera un `CodeGraph` fittizio (~500 nodi: 1 Project, ~18 Form/Class/Module,
ognuno con 20-30 Method) e lo visualizza.

## Cosa provare

| Interazione | Come |
|---|---|
| Pan | click sinistro + trascina sullo sfondo |
| Zoom | rotellina del mouse (centrato sul cursore) |
| Selezione nodo | click su un nodo — evidenzia archi/nodi collegati e popola l'Inspector |
| Expand/collapse | doppio click su un nodo Form/Class/Module (i Method compaiono/scompaiono) |
| Focus da Explorer | click su un item nell'albero a sinistra: centra il grafo su quel nodo |
| Nuovo grafo | pulsante "Rigenera grafo" nella toolbar |

## Checklist POC (sezione 41 del charter) — stato

| # | Punto | Stato in questo codice |
|---|---|---|
| 1 | App Avalonia minimale | ✅ `Program.cs` / `App.axaml` |
| 2 | CodeGraph fittizio | ✅ `Generation/FakeGraphGenerator.cs` |
| 3 | ~500 nodi | ✅ default: 1 + 18 container + ~480 metodi ≈ 500 |
| 4 | Visualizzarli | ✅ `Controls/GraphControl.cs` (custom render, no libreria terza parte) |
| 5 | Pan | ✅ |
| 6 | Zoom | ✅ (centrato sul cursore) |
| 7 | Selezione nodo | ✅ + evidenziazione relazioni |
| 8 | Evidenziare relazioni | ✅ callers/callees evidenziati alla selezione |
| 9 | Espandere/collassare gruppi | ✅ doppio click sui container |
| 10 | Verificare performance | ⚠️ **da fare manualmente** — con 500 nodi dovrebbe essere fluido; se non lo è, va capito se il collo di bottiglia è il rendering per-frame (ridisegno completo ad ogni InvalidateVisual) o altro |
| 11 | Provare Native AOT | ⚠️ **da fare manualmente**, vedi sotto |
| 12 | Dimensione EXE | ⚠️ **da misurare dopo publish**, vedi sotto |

## Publish — Single file, self-contained

```bash
dotnet publish src/VB6Analyzer.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o out/win-x64
```

Verificare poi:
- dimensione di `out/win-x64/VB6Analyzer.exe`
- tempo di avvio (a freddo e a caldo)

## Publish — Native AOT (da validare, sezione 34/39)

```bash
dotnet publish src/VB6Analyzer.App -c Release -r win-x64 -p:PublishAot=true -o out/win-x64-aot
```

Avalonia 11 dichiara supporto ad AOT, ma va verificato con le combinazioni di pacchetti
usate qui. Possibili problemi noti da controllare se la build fallisce o crasha a runtime:
- warning/errori di trimming legati a `Avalonia.Diagnostics` (è incluso solo in Debug,
  non dovrebbe interferire con Release/AOT, ma controllare)
- eventuali binding non compilati (qui `AvaloniaUseCompiledBindingsByDefault=true` è già
  attivo per limitare la reflection a runtime)

Se AOT si rivela problematico, il fallback è single-file self-contained "normale"
(comando sopra), che rimane comunque coerente con l'obiettivo "single EXE portable"
della sezione 5 — solo senza i benefici di startup/size di AOT.

## Struttura del codice

```
src/VB6Analyzer.App/
  Models/
    GraphNode.cs, GraphEdge.cs, CodeGraph.cs   — modello dati (Symbol/Relationship semplificati)
    ExplorerTreeItem.cs                        — adapter per il TreeView dell'Explorer
  Generation/
    FakeGraphGenerator.cs                      — genera dati fittizi (NON parsing VB6 reale)
  Layout/
    RadialLayoutEngine.cs                      — calcola le posizioni (separato dal model e dal renderer)
  Controls/
    GraphControl.cs                            — custom control: input, pan, zoom, selection, rendering
  MainWindow.axaml(.cs)                        — Explorer | Graph | Inspector, come da sezione 25
```

La separazione Model / Layout / Renderer rispecchia deliberatamente la sezione 23 del
charter, così che in futuro sia facile sostituire `RadialLayoutEngine` con un layout
gerarchico/Sugiyama senza toccare `GraphControl`, e sostituire il generatore fittizio
con il vero output del Semantic Analyzer senza toccare né layout né rendering.

## Cosa NON fa questo POC (di proposito)

- Nessun parsing di `.vbp`/`.frm`/`.cls`/`.bas` — è compito di M1
- Nessun symbol resolution reale — M3
- Nessuna persistenza — coerente con la sezione 19 (non richiesta in V1)
- Nessuna AI — esclusa dalla V1 (sezione 35)
- Layout molto semplice (radiale a due livelli), non ottimizzato per grafi grandi o
  irregolari — la sezione 23 lascia aperta la scelta dell'algoritmo definitivo

## Prossimi passi suggeriti

1. Validare in locale performance / AOT / dimensione EXE (i tre punti ⚠️ sopra)
2. In parallelo: spike di ricerca su parser VB6 (Tree-sitter vs ANTLR vs esistenti) —
   questione aperta della sezione 39, indipendente da questo POC
3. Se il POC è soddisfacente → si passa a M1 (parser reale) mantenendo questa stessa
   separazione architetturale (il vero Analyzer sostituirà solo `FakeGraphGenerator`)
