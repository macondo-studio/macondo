using VB6Analyzer.Semantic.Model;

namespace VB6Analyzer.Semantic.Graph;

/// <summary>
/// Motore di interrogazione e algoritmi sul Call Graph (sezione 16 del charter).
/// 
/// FORNISCE FUNZIONALITÀ PER:
/// 1. Query Callers: identificare chi chiama una routine (diretti e indiretti/transitivi).
/// 2. Query Callees: identificare cosa viene chiamato da una routine.
/// 3. Execution Path Discovery: trovare tutti i cammini di esecuzione tra due routine (es. da entry point a database).
/// 4. Cycle Detection: individuare chiamate ricorsive e cicli di dipendenza nel grafo.
/// 5. Dependency Analysis: interrogare istanziazioni (INSTANTIATES) e dipendenze di tipo (USES_TYPE).
/// </summary>
public sealed class CallGraphQueries
{
    private readonly CodeGraph _graph;

    public CallGraphQueries(CodeGraph graph)
    {
        _graph = graph;
    }

    // =========================================================================
    // QUERY CALLERS & CALLEES DIRETTI
    // =========================================================================

    /// <summary>
    /// Restituisce l'elenco di tutte le routine che chiamano direttamente la routine specificata (Who calls target?).
    /// </summary>
    /// <param name="callee">La routine chiamata target.</param>
    public IReadOnlyList<Symbol> GetDirectCallers(Symbol callee)
    {
        return _graph.Relationships
            .Where(r => r.Kind == RelationshipKind.Calls && r.TargetId == callee.Id)
            .Select(r => _graph.FindSymbolById(r.SourceId))
            .Where(s => s != null)
            .Select(s => s!)
            .DistinctBy(s => s.Id)
            .ToList();
    }

    /// <summary>
    /// Restituisce l'elenco di tutte le routine chiamate direttamente dalla routine specificata (What does source call?).
    /// </summary>
    /// <param name="caller">La routine chiamante sorgente.</param>
    public IReadOnlyList<Symbol> GetDirectCallees(Symbol caller)
    {
        return _graph.Relationships
            .Where(r => r.Kind == RelationshipKind.Calls && r.SourceId == caller.Id)
            .Select(r => _graph.FindSymbolById(r.TargetId))
            .Where(s => s != null)
            .Select(s => s!)
            .DistinctBy(s => s.Id)
            .ToList();
    }

    // =========================================================================
    // GERARCHIA TRANSITIVA (Albero di Chiamata)
    // =========================================================================

    /// <summary>
    /// Costruisce l'albero transitivo dei chiamati (callees) a partire dalla routine radice, fino a una profondità massima.
    /// </summary>
    public CallTreeNode GetTransitiveCalleesTree(Symbol root, int maxDepth = 10)
    {
        var visited = new HashSet<Guid>();
        return BuildCalleeTreeRecursive(root, visited, currentDepth: 0, maxDepth);
    }

    private CallTreeNode BuildCalleeTreeRecursive(Symbol current, HashSet<Guid> visited, int currentDepth, int maxDepth)
    {
        var node = new CallTreeNode { Symbol = current };
        if (currentDepth >= maxDepth || visited.Contains(current.Id))
            return node;

        visited.Add(current.Id);

        var callRels = _graph.Relationships
            .Where(r => r.Kind == RelationshipKind.Calls && r.SourceId == current.Id);

        foreach (var rel in callRels)
        {
            var targetSymbol = _graph.FindSymbolById(rel.TargetId);
            if (targetSymbol != null)
            {
                var childNode = BuildCalleeTreeRecursive(targetSymbol, new HashSet<Guid>(visited), currentDepth + 1, maxDepth);
                node.Children.Add(new CallTreeNode
                {
                    Symbol = targetSymbol,
                    CallLine = rel.Line,
                    Confidence = rel.Confidence,
                    Children = { }
                });
            }
        }

        return node;
    }

    /// <summary>
    /// Costruisce l'albero transitivo dei chiamanti (callers) che portano alla routine target specificata.
    /// </summary>
    public CallTreeNode GetTransitiveCallersTree(Symbol root, int maxDepth = 10)
    {
        var visited = new HashSet<Guid>();
        return BuildCallerTreeRecursive(root, visited, currentDepth: 0, maxDepth);
    }

    private CallTreeNode BuildCallerTreeRecursive(Symbol current, HashSet<Guid> visited, int currentDepth, int maxDepth)
    {
        var node = new CallTreeNode { Symbol = current };
        if (currentDepth >= maxDepth || visited.Contains(current.Id))
            return node;

        visited.Add(current.Id);

        var callerRels = _graph.Relationships
            .Where(r => r.Kind == RelationshipKind.Calls && r.TargetId == current.Id);

        foreach (var rel in callerRels)
        {
            var callerSymbol = _graph.FindSymbolById(rel.SourceId);
            if (callerSymbol != null)
            {
                var parentNode = BuildCallerTreeRecursive(callerSymbol, new HashSet<Guid>(visited), currentDepth + 1, maxDepth);
                node.Children.Add(new CallTreeNode
                {
                    Symbol = callerSymbol,
                    CallLine = rel.Line,
                    Confidence = rel.Confidence
                });
            }
        }

        return node;
    }

    // =========================================================================
    // EXECUTION PATH DISCOVERY (Ricerca Cammini di Esecuzione)
    // =========================================================================

    /// <summary>
    /// Trova tutti i cammini di esecuzione aciclici da una routine sorgente (es. Form_Click)
    /// a una routine di destinazione (es. Database.Execute), fino a una profondità massima (sezione 16 del charter).
    /// </summary>
    /// <param name="source">Simbolo di partenza.</param>
    /// <param name="target">Simbolo di arrivo.</param>
    /// <param name="maxDepth">Profondità massima di ricerca.</param>
    /// <returns>Elenco dei cammini (<see cref="CallPath"/>) identificati.</returns>
    public IReadOnlyList<CallPath> FindExecutionPaths(Symbol source, Symbol target, int maxDepth = 10)
    {
        var paths = new List<CallPath>();
        var currentPath = new List<Symbol> { source };
        var visitedOnPath = new HashSet<Guid> { source.Id };

        DfsFindPaths(source, target, currentPath, visitedOnPath, paths, maxDepth);
        return paths;
    }

    private void DfsFindPaths(
        Symbol current,
        Symbol target,
        List<Symbol> currentPath,
        HashSet<Guid> visitedOnPath,
        List<CallPath> results,
        int maxDepth)
    {
        if (current.Id == target.Id)
        {
            results.Add(new CallPath(currentPath));
            return;
        }

        if (currentPath.Count > maxDepth)
            return;

        var callees = GetDirectCallees(current);
        foreach (var callee in callees)
        {
            if (!visitedOnPath.Contains(callee.Id))
            {
                visitedOnPath.Add(callee.Id);
                currentPath.Add(callee);

                DfsFindPaths(callee, target, currentPath, visitedOnPath, results, maxDepth);

                currentPath.RemoveAt(currentPath.Count - 1);
                visitedOnPath.Remove(callee.Id);
            }
        }
    }

    // =========================================================================
    // CYCLE DETECTION (Rilevamento Cicli e Ricorsione)
    // =========================================================================

    /// <summary>
    /// Identifica tutti i cicli di invocazione diretta o mutua ricorsione presenti nel Call Graph.
    /// Utilizza l'algoritmo Depth-First Search con marcatura dei nodi a 3 stati (White, Gray, Black).
    /// </summary>
    public IReadOnlyList<CallCycle> DetectCycles()
    {
        var routineSymbols = _graph.Symbols
            .Where(s => s.Kind is SymbolKind.Method or SymbolKind.Property)
            .ToList();

        var state = new Dictionary<Guid, NodeColor>();
        foreach (var sym in routineSymbols)
            state[sym.Id] = NodeColor.White;

        var cycles = new List<CallCycle>();
        var path = new List<Symbol>();

        foreach (var sym in routineSymbols)
        {
            if (state[sym.Id] == NodeColor.White)
            {
                DfsDetectCycles(sym, state, path, cycles);
            }
        }

        return cycles;
    }

    private enum NodeColor { White, Gray, Black }

    private void DfsDetectCycles(
        Symbol current,
        Dictionary<Guid, NodeColor> state,
        List<Symbol> path,
        List<CallCycle> cycles)
    {
        state[current.Id] = NodeColor.Gray;
        path.Add(current);

        var callees = GetDirectCallees(current);
        foreach (var callee in callees)
        {
            if (state.TryGetValue(callee.Id, out var calleeState))
            {
                if (calleeState == NodeColor.Gray)
                {
                    // Ciclo rilevato: estrae il segmento dal nodo che chiude il ciclo fino alla fine
                    var cycleStartIndex = path.FindIndex(s => s.Id == callee.Id);
                    if (cycleStartIndex >= 0)
                    {
                        var cycleNodes = path.Skip(cycleStartIndex).Append(callee).ToList();
                        cycles.Add(new CallCycle(cycleNodes));
                    }
                }
                else if (calleeState == NodeColor.White)
                {
                    DfsDetectCycles(callee, state, path, cycles);
                }
            }
        }

        path.RemoveAt(path.Count - 1);
        state[current.Id] = NodeColor.Black;
    }

    // =========================================================================
    // DEPENDENCY QUERIES (Instantiates & UsesType)
    // =========================================================================

    /// <summary>
    /// Restituisce tutte le classi istanziate direttamente dal simbolo specificato.
    /// </summary>
    public IReadOnlyList<Symbol> GetInstantiatedClasses(Symbol source) =>
        _graph.Relationships
            .Where(r => r.Kind == RelationshipKind.Instantiates && r.SourceId == source.Id)
            .Select(r => _graph.FindSymbolById(r.TargetId))
            .Where(s => s != null)
            .Select(s => s!)
            .DistinctBy(s => s.Id)
            .ToList();

    /// <summary>
    /// Restituisce tutti i tipi utilizzati dal simbolo specificato (variabili, parametri, ritorni).
    /// </summary>
    public IReadOnlyList<Symbol> GetTypeDependencies(Symbol source) =>
        _graph.Relationships
            .Where(r => r.Kind == RelationshipKind.UsesType && r.SourceId == source.Id)
            .Select(r => _graph.FindSymbolById(r.TargetId))
            .Where(s => s != null)
            .Select(s => s!)
            .DistinctBy(s => s.Id)
            .ToList();

    // =========================================================================
    // IMPACT ANALYSIS & BLAST RADIUS (Milestone M6)
    // =========================================================================

    /// <summary>
    /// Esegue l'analisi completa del Blast Radius per il simbolo specificato.
    /// </summary>
    public Impact.ImpactReport GetImpactReport(Symbol symbol, int maxDepth = 10)
    {
        var analyzer = new Impact.ImpactAnalyzer(_graph, this);
        return analyzer.AnalyzeSymbolImpact(symbol, maxDepth);
    }
}
