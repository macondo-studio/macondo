using System.Collections.Concurrent;
using System.Threading;
using Macondo.Semantic.Model;

namespace Macondo.Semantic.Graph;

/// <summary>
/// Motore di interrogazione e algoritmi sul Call Graph.
/// 
/// FORNISCE FUNZIONALITÀ PER:
/// 1. Query Callers: identificare chi chiama una routine (diretti e indiretti/transitivi).
/// 2. Query Callees: identificare cosa viene chiamato da una routine.
/// 3. Execution Path Discovery: trovare tutti i cammini di esecuzione tra due routine (es. da entry point a database).
/// 4. Cycle Detection: individuare chiamate ricorsive e cicli di dipendenza nel grafo.
/// 5. Dependency Analysis: interrogare istanziazioni (INSTANTIATES) e dipendenze di tipo (USES_TYPE).
/// Include caching a grana fine (Memoization thread-safe) su tutte le sotto-query per massimizzare le performance.
/// </summary>
public sealed class CallGraphQueries
{
    private readonly CodeGraph _graph;

    // Cache a grana fine (Opzione 3)
    private readonly ConcurrentDictionary<Guid, IReadOnlyList<Symbol>> _directCallersCache = new();
    private readonly ConcurrentDictionary<Guid, IReadOnlyList<Symbol>> _directCalleesCache = new();
    private readonly ConcurrentDictionary<(Guid SourceId, Guid TargetId, int MaxDepth), IReadOnlyList<CallPath>> _executionPathsCache = new();
    private readonly ConcurrentDictionary<Guid, IReadOnlyList<Symbol>> _instantiatedClassesCache = new();
    private readonly ConcurrentDictionary<(Guid SymbolId, int MaxDepth), Impact.ImpactReport> _impactReportCache = new();

    public CallGraphQueries(CodeGraph graph)
    {
        _graph = graph;
    }

    /// <summary>
    /// Svuota tutte le cache interne delle sotto-query e dei report di impatto.
    /// </summary>
    public void ClearCache()
    {
        _directCallersCache.Clear();
        _directCalleesCache.Clear();
        _executionPathsCache.Clear();
        _instantiatedClassesCache.Clear();
        _impactReportCache.Clear();
    }

    // =========================================================================
    // QUERY CALLERS & CALLEES DIRETTI
    // =========================================================================

    /// <summary>
    /// Restituisce l'elenco di tutte le routine che chiamano direttamente la routine specificata (Who calls target?).
    /// Risultato memorizzato in cache thread-safe per accessi istantanei successivi.
    /// </summary>
    /// <param name="callee">La routine chiamata target.</param>
    public IReadOnlyList<Symbol> GetDirectCallers(Symbol callee)
    {
        return _directCallersCache.GetOrAdd(callee.Id, _ =>
            // Interroga tutte le relazioni di tipo Calls o HandlesEvent in cui il Target coincide con la routine callee
            _graph.Relationships
                .Where(r => (r.Kind == RelationshipKind.Calls || r.Kind == RelationshipKind.HandlesEvent) && r.TargetId == callee.Id)
                .Select(r => _graph.FindSymbolById(r.SourceId))
                .Where(s => s != null)
                .Select(s => s!)
                .DistinctBy(s => s.Id)
                .ToList());
    }

    /// <summary>
    /// Restituisce l'elenco di tutte le routine chiamate direttamente dalla routine specificata (What does source call?).
    /// Risultato memorizzato in cache thread-safe per accessi istantanei successivi.
    /// </summary>
    /// <param name="caller">La routine chiamante sorgente.</param>
    public IReadOnlyList<Symbol> GetDirectCallees(Symbol caller)
    {
        return _directCalleesCache.GetOrAdd(caller.Id, _ =>
            // Interroga tutte le relazioni di tipo Calls o HandlesEvent in cui la Source coincide con la routine caller
            _graph.Relationships
                .Where(r => (r.Kind == RelationshipKind.Calls || r.Kind == RelationshipKind.HandlesEvent) && r.SourceId == caller.Id)
                .Select(r => _graph.FindSymbolById(r.TargetId))
                .Where(s => s != null)
                .Select(s => s!)
                .DistinctBy(s => s.Id)
                .ToList());
    }


    // =========================================================================
    // EXECUTION PATH DISCOVERY (Ricerca Cammini di Esecuzione)
    // =========================================================================

    /// <summary>
    /// Trova tutti i cammini di esecuzione aciclici da una routine sorgente (es. Form_Click)
    /// a una routine di destinazione (es. Database.Execute), fino a una profondità massima.
    /// I risultati vengono memorizzati in una cache con chiave (SourceId, TargetId, MaxDepth).
    /// </summary>
    /// <param name="source">Simbolo di partenza.</param>
    /// <param name="target">Simbolo di arrivo.</param>
    /// <param name="maxDepth">Profondità massima di ricerca.</param>
    /// <param name="cancellationToken">Token opzionale per annullare la ricerca.</param>
    /// <returns>Elenco dei cammini (<see cref="CallPath"/>) identificati.</returns>
    public IReadOnlyList<CallPath> FindExecutionPaths(
        Symbol source,
        Symbol target,
        int maxDepth = 10,
        CancellationToken cancellationToken = default)
    {
        var key = (source.Id, target.Id, maxDepth);
        if (_executionPathsCache.TryGetValue(key, out var cachedPaths))

            return cachedPaths;

        var paths = new List<CallPath>();
        var currentPath = new List<Symbol> { source };
        var visitedOnPath = new HashSet<Guid> { source.Id };

        // Avvia la ricerca in profondità ricorsiva con backtracking
        DfsFindPaths(source, target, currentPath, visitedOnPath, paths, maxDepth, cancellationToken);

        _executionPathsCache[key] = paths;
        return paths;
    }

    /// <summary>
    /// Esegue la ricerca in profondità (DFS) con backtracking per individuare tutti i cammini aciclici tra il nodo corrente e la destinazione.
    /// </summary>
    /// <param name="current">Nodo/routine corrente nella ricorsione DFS.</param>
    /// <param name="target">Nodo/routine di arrivo cercato.</param>
    /// <param name="currentPath">Lista ordinata dei simboli che formano il cammino parziale attivo.</param>
    /// <param name="visitedOnPath">Insieme dei nodi attualmente nel cammino per evitare cicli infiniti.</param>
    /// <param name="results">Collezione dei cammini completi individuati.</param>
    /// <param name="maxDepth">Profondità massima di risalita/discesa ammessa.</param>
    /// <param name="cancellationToken">Token per la cancellazione dell'operazione.</param>
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

        // 1. Limite di sicurezza: arresta la ricerca al raggiungimento di 50 cammini distinti per evitare esplosione combinatoria
        if (results.Count >= 50)
            return;

        // 2. Base case: se il nodo corrente coincide con la destinazione cercata, registra una copia del cammino completo
        if (current.Id == target.Id)
        {
            results.Add(new CallPath(currentPath));
            return;
        }

        // 3. Potatura (Pruning): interrompe l'esplorazione se il cammino parziale attivo supera la profondità massima consentita
        if (currentPath.Count > maxDepth)
            return;

        // 4. Esplora ricorsivamente tutti i chiamati diretti del nodo corrente
        var callees = GetDirectCallees(current);
        foreach (var callee in callees)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 5. Backtracking: evita cicli infiniti visitando solo nodi non già presenti nel cammino attivo
            if (!visitedOnPath.Contains(callee.Id))
            {
                visitedOnPath.Add(callee.Id);
                currentPath.Add(callee);

                // Passo ricorsivo in profondità
                DfsFindPaths(callee, target, currentPath, visitedOnPath, results, maxDepth, cancellationToken);

                // Ripristino dello stato (Backtracking) per consentire l'esplorazione di cammini alternativi
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

        // Inizializza tutti i nodi come White (non ancora visitati)
        var state = new Dictionary<Guid, NodeColor>();
        foreach (var sym in routineSymbols)
            state[sym.Id] = NodeColor.White;

        var cycles = new List<CallCycle>();
        var path = new List<Symbol>();

        // Esegue la DFS da ogni nodo non ancora visitato per coprire tutte le componenti connesse della foresta
        foreach (var sym in routineSymbols)
        {
            if (state[sym.Id] == NodeColor.White)

                DfsDetectCycles(sym, state, path, cycles);
        }

        return cycles;
    }

    /// <summary>
    /// Visita ricorsiva in profondità (DFS a 3 colori) per identificare back-edge e cicli nel Call Graph.
    /// </summary>
    /// <param name="current">Nodo corrente in esame.</param>
    /// <param name="state">Mappa di colorazione dei nodi: <c>White</c> (non visitato), <c>Gray</c> (in elaborazione nello stack), <c>Black</c> (completamente esplorato).</param>
    /// <param name="path">Sequenza corrente dei nodi nel ramo di visita.</param>
    /// <param name="cycles">Lista dei cicli di chiamata rilevati.</param>
    /// <remarks>
    /// Un arco verso un nodo marcato <c>Gray</c> rappresenta un back-edge che chiude un ciclo di chiamate o ricorsione.
    /// </remarks>
    private void DfsDetectCycles(
        Symbol current,
        Dictionary<Guid, NodeColor> state,
        List<Symbol> path,
        List<CallCycle> cycles)
    {
        // 1. Marca il nodo corrente come Gray (in elaborazione nello stack di ricorsione attivo)
        state[current.Id] = NodeColor.Gray;
        path.Add(current);

        // 2. Esplora le chiamate uscenti
        var callees = GetDirectCallees(current);
        foreach (var callee in callees)
        {
            if (state.TryGetValue(callee.Id, out var calleeState))
            {
                if (calleeState == NodeColor.Gray)
                {
                    // Back-edge individuato: il nodo callee è già nello stack corrente (chiude un ciclo o ricorsione mutua)
                    var cycleStartIndex = path.FindIndex(s => s.Id == callee.Id);
                    if (cycleStartIndex >= 0)
                    {
                        var cycleNodes = path.Skip(cycleStartIndex).Append(callee).ToList();
                        cycles.Add(new CallCycle(cycleNodes));
                    }
                }
                else if (calleeState == NodeColor.White)

    DfsDetectCycles(callee, state, path, cycles);
            }
        }

        // 3. Post-processing: rimuove il nodo dal path attivo e lo marca Black (visita del sottoalbero completata)
        path.RemoveAt(path.Count - 1);
        state[current.Id] = NodeColor.Black;
    }

    // =========================================================================
    // DEPENDENCY QUERIES (Instantiates & UsesType)
    // =========================================================================

    /// <summary>
    /// Restituisce tutte le classi istanziate direttamente dal simbolo specificato (relazione INSTANTIATES).
    /// Risultato memorizzato in cache thread-safe per accessi istantanei successivi.
    /// </summary>
    public IReadOnlyList<Symbol> GetInstantiatedClasses(Symbol source) =>
        _instantiatedClassesCache.GetOrAdd(source.Id, _ =>
            _graph.Relationships
                .Where(r => r.Kind == RelationshipKind.Instantiates && r.SourceId == source.Id)
                .Select(r => _graph.FindSymbolById(r.TargetId))
                .Where(s => s != null)
                .Select(s => s!)
                .DistinctBy(s => s.Id)
                .ToList());


    // =========================================================================
    // IMPACT ANALYSIS & BLAST RADIUS
    // =========================================================================

    /// <summary>
    /// Esegue l'analisi completa del Blast Radius per il simbolo specificato.
    /// Risultato memorizzato in cache thread-safe per accessi istantanei successivi.
    /// </summary>
    public Impact.ImpactReport GetImpactReport(Symbol symbol, int maxDepth = 10, CancellationToken cancellationToken = default)
    {
        var key = (symbol.Id, maxDepth);
        if (_impactReportCache.TryGetValue(key, out var cachedReport))

            return cachedReport;

        var analyzer = new Impact.ImpactAnalyzer(_graph, this);
        var report = analyzer.AnalyzeSymbolImpact(symbol, maxDepth, cancellationToken);

        _impactReportCache[key] = report;
        return report;
    }

    /// <summary>
    /// Verifica se il report di impatto per il simbolo specificato è già presente nella cache.
    /// </summary>
    public bool TryGetCachedImpactReport(Symbol symbol, int maxDepth, out Impact.ImpactReport? report) =>
        _impactReportCache.TryGetValue((symbol.Id, maxDepth), out report);
}
