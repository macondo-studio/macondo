using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.DataFlow;

/// <summary>
/// Analizzatore poliglotto di Taint Tracking (Source-to-Sink) e anomalie di Data Flow (Def-Use).
/// 
/// METODOLOGIA:
/// 1. Taint Analysis (Vulnerabilità e Flusso Dati):
///    - Rileva Sorgenti non fidate (Taint Sources): Input utente (textbox, parametri URL, request HTTP), file system o rete.
///    - Rileva Punti Critici di esecuzione (Taint Sinks): Esecuzione query SQL dirette, comandi di sistema (Shell/Process), scritture su file.
///    - Ricerca Cammini di Propagazione: Esegue una ricerca BFS nel grafo delle invocazioni e degli accessi a campi/variabili (Calls, Reads, Writes)
///      per individuare catene dirette di propagazione tra sorgenti e sink senza sanificazione/validazione intermedia.
/// 2. Def-Use Anomalies (Dead Store):
///    - Individua variabili o campi che vengono valorizzati/scritti (Writes) ma il cui valore non viene mai letto successivamente (Reads).
/// </summary>
public static class TaintAnalyzer
{
    /// <summary>
    /// Pattern predefiniti per identificare sorgenti di input non controllate / esterne.
    /// </summary>
    private static readonly Dictionary<TaintSourceKind, List<TaintPattern>> DefaultSources = new()
    {
        [TaintSourceKind.UserInput] = new()
        {
            new TaintPattern { Name = "UI Controls", MatchingPattern = "txt*;*Text*;*Input*;*QueryString*;*Request*;*Param*" }
        },
        [TaintSourceKind.FileOrFileSystem] = new()
        {
            new TaintPattern { Name = "File Reads", MatchingPattern = "*ReadFile*;*ReadLine*;*ReadAllText*;*OpenText*;*FileReader*" }
        },
        [TaintSourceKind.NetworkOrApi] = new()
        {
            new TaintPattern { Name = "HTTP / API", MatchingPattern = "*HttpClient*;*GetAsync*;*Download*;*Socket*;*WebRequest*" }
        }
    };

    /// <summary>
    /// Pattern predefiniti per identificare punti critici di esecuzione (Sinks).
    /// </summary>
    private static readonly Dictionary<TaintSinkKind, List<TaintPattern>> DefaultSinks = new()
    {
        [TaintSinkKind.SqlExecution] = new()
        {
            new TaintPattern { Name = "SQL Sinks", MatchingPattern = "*ExecuteSql*;*ExecuteNonQuery*;*ExecuteReader*;*FromSqlRaw*;*Recordset.Open*;*cnn.Execute*" }
        },
        [TaintSinkKind.ShellOrProcessExecution] = new()
        {
            new TaintPattern { Name = "Shell / Process", MatchingPattern = "Shell;*Process.Start*;*Exec*;*Cmd*" }
        },
        [TaintSinkKind.FileSystemWrite] = new()
        {
            new TaintPattern { Name = "File Write", MatchingPattern = "*WriteAllText*;*WriteFile*;*CreateText*;*SaveAs*" }
        }
    };

    /// <summary>
    /// Esegue il tracciamento dei percorsi da sorgenti non controllate a punti critici di esecuzione (Taint Analysis)
    /// e rileva anomalie Def-Use / Dead Store.
    /// </summary>
    /// <param name="graph">Il CodeGraph contenente simboli e relazioni del progetto.</param>
    /// <returns>Il dataset <see cref="DataFlowDataset"/> contenente i percorsi di taint e le anomalie def-use.</returns>
    public static DataFlowDataset Analyze(CodeGraph graph)
    {
        var dataset = new DataFlowDataset();
        if (graph.Symbols.Count == 0)
            return dataset;

        var symbolById = graph.Symbols.ToDictionary(s => s.Id);

        // ---------------------------------------------------------------------
        // FASE 1: Identifica le Sorgenti non fidate (Taint Sources)
        // ---------------------------------------------------------------------
        // Raccoglie controlli UI (textbox, campi testo), input da richiesta HTTP/parametri
        // e letture da file system o stream di rete.
        var sources = new List<(Symbol Symbol, TaintSourceKind Kind)>();
        foreach (var sym in graph.Symbols)
        {
            foreach (var (kind, patterns) in DefaultSources)
            {
                if (patterns.Any(p => p.Matches(sym)))
                {
                    sources.Add((sym, kind));
                    break;
                }
            }
        }

        // ---------------------------------------------------------------------
        // FASE 2: Identifica i Punti Critici di esecuzione (Taint Sinks)
        // ---------------------------------------------------------------------
        // Raccoglie metodi di esecuzione comandi SQL diretti, invocazioni Shell/Process
        // e operazioni di scrittura su file system senza sanificazione.
        var sinks = new List<(Symbol Symbol, TaintSinkKind Kind)>();
        foreach (var sym in graph.Symbols)
        {
            foreach (var (kind, patterns) in DefaultSinks)
            {
                if (patterns.Any(p => p.Matches(sym)))
                {
                    sinks.Add((sym, kind));
                    break;
                }
            }
        }

        // ---------------------------------------------------------------------
        // FASE 3: Costruzione del grafo di propagazione del flusso dati (Calls, Reads, Writes)
        // ---------------------------------------------------------------------
        // Gli archi di propagazione collegano un chiamante o una routine con i campi/variabili letti o scritti
        // e le successive chiamate a metodi a valle.
        var adj = new Dictionary<Guid, List<Guid>>();
        foreach (var sym in graph.Symbols)
            adj[sym.Id] = new List<Guid>();

        foreach (var rel in graph.Relationships)
        {
            if (rel.Kind is RelationshipKind.Calls or RelationshipKind.Reads or RelationshipKind.Writes)
            {
                if (adj.TryGetValue(rel.SourceId, out var neighbors))
                {
                    neighbors.Add(rel.TargetId);
                }
            }
        }

        // ---------------------------------------------------------------------
        // FASE 4: Ricerca BFS dei cammini minimi da ciascuna Source a ciascun Sink
        // ---------------------------------------------------------------------
        // Trova le catene dirette di propagazione da input utente a punti di esecuzione critica
        foreach (var (srcSym, srcKind) in sources)
        {
            foreach (var (sinkSym, sinkKind) in sinks)
            {
                if (srcSym.Id == sinkSym.Id)
                    continue;

                var path = FindShortestPath(srcSym.Id, sinkSym.Id, adj, symbolById);
                if (path != null && path.Count > 1)
                {
                    dataset.TaintPaths.Add(new TaintPath
                    {
                        SourceSymbol = srcSym,
                        SourceKind = srcKind,
                        SinkSymbol = sinkSym,
                        SinkKind = sinkKind,
                        PropagationChain = path
                    });
                }
            }
        }

        // ---------------------------------------------------------------------
        // FASE 5: Rilevamento anomalie Def-Use / Dead Store
        // ---------------------------------------------------------------------
        // Un campo o variabile locale costituisce una Dead Store quando riceve almeno una scrittura (Writes),
        // ma non viene mai letto (Reads) in nessun punto dell'applicazione.
        var fields = graph.Symbols.Where(s => s.Kind is SymbolKind.Field or SymbolKind.Property).ToList();
        var readTargets = graph.Relationships.Where(r => r.Kind == RelationshipKind.Reads).Select(r => r.TargetId).ToHashSet();
        var writeTargets = graph.Relationships.Where(r => r.Kind == RelationshipKind.Writes).Select(r => r.TargetId).ToHashSet();

        foreach (var f in fields)
        {
            bool isWritten = writeTargets.Contains(f.Id);
            bool isRead = readTargets.Contains(f.Id);

            if (isWritten && !isRead)
            {
                dataset.Anomalies.Add(new DefUseAnomaly
                {
                    VariableSymbol = f,
                    AnomalyType = "DeadStore",
                    Description = $"Il campo o variabile '{f.Name}' riceve assegnazioni/scritture ma non viene mai letto altrove nel programma."
                });
            }
        }

        // Ordina mettendo in cima i percorsi più diretti (minor numero di hop) verso sink critici
        dataset.TaintPaths.Sort((a, b) => a.HopCount.CompareTo(b.HopCount));
        return dataset;
    }

    /// <summary>
    /// Esegue la ricerca in ampiezza (BFS) per trovare il cammino più breve tra un nodo sorgente e un nodo target.
    /// Ricostruisce la sequenza dei simboli attraversati a ritroso dai puntatori dei predecessori.
    /// </summary>
    private static List<Symbol>? FindShortestPath(
        Guid startId,
        Guid targetId,
        Dictionary<Guid, List<Guid>> adj,
        Dictionary<Guid, Symbol> symbolById)
    {
        // 1. Mappa dei predecessori per ricostruire il cammino
        var prev = new Dictionary<Guid, Guid>();
        var visited = new HashSet<Guid> { startId };
        var queue = new Queue<Guid>();
        queue.Enqueue(startId);

        bool found = false;

        // 2. Esplorazione a livelli BFS
        while (queue.Count > 0)
        {
            var curr = queue.Dequeue();
            if (curr == targetId)
            {
                found = true;
                break;
            }

            if (adj.TryGetValue(curr, out var nextList))
            {
                foreach (var n in nextList)
                {
                    if (visited.Add(n))
                    {
                        prev[n] = curr;
                        queue.Enqueue(n);
                    }
                }
            }
        }

        if (!found) return null;

        // 3. Ricostruisce il cammino a ritroso dai puntatori prev
        var path = new List<Symbol>();
        var current = targetId;
        while (current != startId)
        {
            if (symbolById.TryGetValue(current, out var sym))
                path.Add(sym);

            current = prev[current];
        }

        if (symbolById.TryGetValue(startId, out var startSym))
            path.Add(startSym);

        path.Reverse();
        return path;
    }
}

