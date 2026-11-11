using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.DataFlow;

/// <summary>
/// Taint Tracking (Source-to-Sink) and Data Flow Anomaly (Def-Use) polyglot analyzer.
/// 
/// METODOLOGIA:
/// 1. Taint Analysis (Vulnerabilità e Flusso Dati):
/// - Detect Untrusted Sources: User input (textbox, URL parameters, HTTP requests), file system or network.
/// - Detect Critical Execution Points (Taint Sinks): Execution of direct SQL queries, system commands (Shell/Process), writing to files.
/// - Propagation Path Search: Performs a BFS search in the graph of invocations and accesses to fields/variables (Calls, Reads, Writes)
/// to identify direct propagation chains between sources and sinks without intermediate sanitization/validation.
/// 2. Def-Use Anomalies (Dead Store):
/// - Identifies variables or fields that are valued/written (Writes) but whose value is never read subsequently (Reads).
/// </summary>
public static class TaintAnalyzer
{
    /// <summary>
    /// Predefined patterns to identify uncontrolled/external input sources.
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
    /// Predefined patterns to identify execution critical points (Sinks).
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
    /// Traces paths from uncontrolled sources to critical execution points (Taint Analysis)
    /// e rileva anomalie Def-Use / Dead Store.
    /// </summary>
    /// <param name="graph">The CodeGraph containing project symbols and relationships.</param>
    /// <returns>The <see cref="DataFlowDataset"/> dataset containing the taint paths and def-use anomalies.</returns>
    public static DataFlowDataset Analyze(CodeGraph graph)
    {
        var dataset = new DataFlowDataset();
        if (graph.Symbols.Count == 0)
            return dataset;

        var symbolById = graph.Symbols.ToDictionary(s => s.Id);

        // ---------------------------------------------------------------------
        // STEP 1: Identify Untrusted Sources (Taint Sources)
        // ---------------------------------------------------------------------
        // Collects UI controls (textboxes, text fields), input from HTTP request/parameters
        // and reads from file systems or network streams.
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
        // PHASE 2: Identify the Critical Execution Points (Taint Sinks)
        // ---------------------------------------------------------------------
        // Collects direct SQL command execution methods, Shell/Process invocations
        // and file system write operations without sanitization.
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
        // PHASE 3: Construction of the data flow propagation graph (Calls, Reads, Writes)
        // ---------------------------------------------------------------------
        // Propagation arcs connect a caller or routine with the read or written fields/variables
        // and subsequent calls to downstream methods.
        var adj = new Dictionary<Guid, List<Guid>>();
        foreach (var sym in graph.Symbols)
            adj[sym.Id] = new List<Guid>();

        foreach (var rel in graph.Relationships)
        {
            if (rel.Kind is RelationshipKind.Calls or RelationshipKind.Writes)
            {
                if (adj.TryGetValue(rel.SourceId, out var neighbors))

                    neighbors.Add(rel.TargetId);
            }
            else if (rel.Kind is RelationshipKind.Reads)
            {
                if (adj.TryGetValue(rel.TargetId, out var neighbors))

                    neighbors.Add(rel.SourceId);
            }
        }

        // ---------------------------------------------------------------------
        // PHASE 4: BFS search of shortest paths from each Source to each Sink
        // ---------------------------------------------------------------------
        // Find direct chains of propagation from user input to critical execution points
        foreach (var (srcSym, srcKind) in sources)
        {
            foreach (var (sinkSym, sinkKind) in sinks)
            {
                if (srcSym.Id == sinkSym.Id)
                    continue;

                var path = FindShortestPath(srcSym.Id, sinkSym.Id, adj, symbolById);
                if (path != null && path.Count > 1)

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

        // ---------------------------------------------------------------------
        // STEP 5: Def-Use / Dead Store anomaly detection
        // ---------------------------------------------------------------------
        // A local field or variable constitutes a Dead Store when it receives at least one write (Writes),
        // but it is never Reads anywhere in the application.
        var fields = graph.Symbols.Where(s => s.Kind is SymbolKind.Field or SymbolKind.Property).ToList();
        var readTargets = graph.Relationships.Where(r => r.Kind == RelationshipKind.Reads).Select(r => r.TargetId).ToHashSet();
        var writeTargets = graph.Relationships.Where(r => r.Kind == RelationshipKind.Writes).Select(r => r.TargetId).ToHashSet();

        foreach (var f in fields)
        {
            bool isWritten = writeTargets.Contains(f.Id);
            bool isRead = readTargets.Contains(f.Id);

            if (isWritten && !isRead)

                dataset.Anomalies.Add(new DefUseAnomaly
                {
                    VariableSymbol = f,
                    AnomalyType = "DeadStore",
                    Description = $"The field or variable '{f.Name}' is written/assigned to but never read anywhere in the program."
                });
        }

        // Sort by placing the most direct (fewest hops) paths to critical sinks at the top
        dataset.TaintPaths.Sort((a, b) => a.HopCount.CompareTo(b.HopCount));
        return dataset;
    }

    /// <summary>
    /// Performs breadth-first search (BFS) to find the shortest path between a source node and a target node.
    /// Reconstructs the sequence of symbols traversed backwards by the pointers of the predecessors.
    /// </summary>
    private static List<Symbol>? FindShortestPath(
        Guid startId,
        Guid targetId,
        Dictionary<Guid, List<Guid>> adj,
        Dictionary<Guid, Symbol> symbolById)
    {
        // 1. Map of predecessors to reconstruct the path
        var prev = new Dictionary<Guid, Guid>();
        var visited = new HashSet<Guid> { startId };
        var queue = new Queue<Guid>();
        queue.Enqueue(startId);

        bool found = false;

        // 2. Exploration at BFS levels
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

        // 3. Reconstructs the backward path from the previous pointers
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

