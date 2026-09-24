using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.DataFlow;

/// <summary>
/// Analizzatore poliglotto di Taint Tracking (Source-to-Sink) e anomalie di Data Flow (Def-Use).
/// </summary>
public static class TaintAnalyzer
{
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
    public static DataFlowDataset Analyze(CodeGraph graph)
    {
        var dataset = new DataFlowDataset();
        if (graph.Symbols.Count == 0)
            return dataset;

        var symbolById = graph.Symbols.ToDictionary(s => s.Id);

        // 1. Identifica le Sorgenti (Sources)
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

        // 2. Identifica i Punti Critici (Sinks)
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

        // 3. Costruisce il grafo delle adiacenze per la ricerca dei cammini
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

        // 4. Esegue ricerca BFS da ciascuna sorgente verso i sink
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

        // 5. Rileva anomalie Def-Use / Dead Store (campi/variabili scritte ma mai lette)
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

        // Ordina mettendo in cima i percorsi più diretti verso sink critici
        dataset.TaintPaths.Sort((a, b) => a.HopCount.CompareTo(b.HopCount));
        return dataset;
    }

    private static List<Symbol>? FindShortestPath(
        Guid startId,
        Guid targetId,
        Dictionary<Guid, List<Guid>> adj,
        Dictionary<Guid, Symbol> symbolById)
    {
        var prev = new Dictionary<Guid, Guid>();
        var visited = new HashSet<Guid> { startId };
        var queue = new Queue<Guid>();
        queue.Enqueue(startId);

        bool found = false;
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
