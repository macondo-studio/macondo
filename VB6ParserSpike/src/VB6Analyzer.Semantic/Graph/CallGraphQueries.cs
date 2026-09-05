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
    // EXECUTION PATH DISCOVERY (Ricerca Cammini di Esecuzione)
    // =========================================================================

    /// <summary>
    /// Trova tutti i cammini di esecuzione aciclici da una routine sorgente (es. Form_Click)
    /// a una routine di destinazione (es. Database.Execute), fino a una profondità massima (sezione 16 del charter).
    /// </summary>
    /// <param name="source">Simbolo di partenza.</param>
    /// <param name="target">Simbolo di arrivo.</param>
    /// <param name="maxDepth">Profondità massima di ricerca.</param>
    /// <param name="cancellationToken">Token opzionale per annullare la ricerca.</param>
    /// <returns>Elenco dei cammini (<see cref="CallPath"/>) identificati.</returns>
    public IReadOnlyList<CallPath> FindExecutionPaths(Symbol source, Symbol target, int maxDepth = 10, CancellationToken cancellationToken = default)
    {
        var paths = new List<CallPath>();
        var currentPath = new List<Symbol> { source };
        var visitedOnPath = new HashSet<Guid> { source.Id };

        DfsFindPaths(source, target, currentPath, visitedOnPath, paths, maxDepth, cancellationToken);
        return paths;
    }

    private void DfsFindPaths(
        Symbol current,
        Symbol target,
        List<Symbol> currentPath,
        HashSet<Guid> visitedOnPath,
        List<CallPath> results,
        int maxDepth,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (results.Count >= 50)
            return;

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
            cancellationToken.ThrowIfCancellationRequested();

            if (!visitedOnPath.Contains(callee.Id))
            {
                visitedOnPath.Add(callee.Id);
                currentPath.Add(callee);

                DfsFindPaths(callee, target, currentPath, visitedOnPath, results, maxDepth, cancellationToken);

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


    // =========================================================================
    // IMPACT ANALYSIS & BLAST RADIUS (Milestone M6)
    // =========================================================================

    /// <summary>
    /// Esegue l'analisi completa del Blast Radius per il simbolo specificato.
    /// </summary>
    public Impact.ImpactReport GetImpactReport(Symbol symbol, int maxDepth = 10, CancellationToken cancellationToken = default)
    {
        var analyzer = new Impact.ImpactAnalyzer(_graph, this);
        return analyzer.AnalyzeSymbolImpact(symbol, maxDepth, cancellationToken);
    }
}
