using System.Collections.Concurrent;
using System.Threading;
using Macondo.Semantic.Model;

namespace Macondo.Semantic.Graph;

/// <summary>
/// Query engine and algorithms on the Call Graph.
/// 
/// PROVIDES FEATURES FOR:
/// 1. Query Callers: Identify who calls a routine (direct and indirect/transitive).
/// 2. Query Callees: Identify what is called by a routine.
/// 3. Execution Path Discovery: find all execution paths between two routines (e.g. from entry point to database).
/// 4. Cycle Detection: Identify recursive calls and dependency loops in the graph.
/// 5. Dependency Analysis: Query instantiations (INSTANTIATES) and type dependencies (USES_TYPE).
/// Includes fine-grained caching (thread-safe memoization) on all subqueries to maximize performance.
/// </summary>
public sealed class CallGraphQueries
{
    private readonly CodeGraph _graph;

    // Fine-grained cache (Option 3)
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
    /// Clear all internal caches of subqueries and impact reports.
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
    /// Returns the list of all routines that directly call the specified routine (Who calls target?).
    /// Thread-safe cached result for subsequent instant access.
    /// </summary>
    /// <param name="callee">The routine called target.</param>
    public IReadOnlyList<Symbol> GetDirectCallers(Symbol callee)
    {
        return _directCallersCache.GetOrAdd(callee.Id, _ =>
            // Queries O(1) indexed incoming relations of type Calls or HandlesEvent
            _graph.GetIncomingRelationships(callee.Id)
                .Where(r => r.Kind == RelationshipKind.Calls || r.Kind == RelationshipKind.HandlesEvent)
                .Select(r => _graph.FindSymbolById(r.SourceId))
                .Where(s => s != null)
                .Select(s => s!)
                .DistinctBy(s => s.Id)
                .ToList());
    }

    /// <summary>
    /// Returns the list of all routines called directly from the specified routine (What does source call?).
    /// Thread-safe cached result for subsequent instant access.
    /// </summary>
    /// <param name="caller">The source calling routine.</param>
    public IReadOnlyList<Symbol> GetDirectCallees(Symbol caller)
    {
        return _directCalleesCache.GetOrAdd(caller.Id, _ =>
            // Queries O(1) indexed outgoing relations of type Calls or HandlesEvent
            _graph.GetOutgoingRelationships(caller.Id)
                .Where(r => r.Kind == RelationshipKind.Calls || r.Kind == RelationshipKind.HandlesEvent)
                .Select(r => _graph.FindSymbolById(r.TargetId))
                .Where(s => s != null)
                .Select(s => s!)
                .DistinctBy(s => s.Id)
                .ToList());
    }


    // =========================================================================
    // EXECUTION PATH DISCOVERY
    // =========================================================================

    /// <summary>
    /// Find all acyclic execution paths from a source routine (e.g. Form_Click)
    /// to a target routine (e.g. Database.Execute), up to a maximum depth.
    /// The results are stored in a keyed cache (SourceId, TargetId, MaxDepth).
    /// </summary>
    /// <param name="source">Start symbol.</param>
    /// <param name="target">Arrival symbol.</param>
    /// <param name="maxDepth">Maximum search depth.</param>
    /// <param name="cancellationToken">Optional token to cancel search.</param>
    /// <returns>List of identified paths (<see cref="CallPath"/>).</returns>
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

        // Start recursive depth search with backtracking
        DfsFindPaths(source, target, currentPath, visitedOnPath, paths, maxDepth, cancellationToken);

        _executionPathsCache[key] = paths;
        return paths;
    }

    /// <summary>
    /// Performs depth-first search (DFS) with backtracking to locate all acyclic paths between the current node and the destination.
    /// </summary>
    /// <param name="current">Current node/routine in DFS recursion.</param>
    /// <param name="target">Arrival node/routine searched.</param>
    /// <param name="currentPath">Ordered list of symbols that form the active partial path.</param>
    /// <param name="visitedOnPath">Set of nodes currently on the path to avoid infinite cycles.</param>
    /// <param name="results">Collection of complete paths identified.</param>
    /// <param name="maxDepth">Maximum ascent/descent depth permitted.</param>
    /// <param name="cancellationToken">Token for canceling the operation.</param>
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

        // 1. Safety limit: stop the search when 50 distinct paths are reached to avoid combinatorial explosion
        if (results.Count >= 50)
            return;

        // 2. Base case: if the current node coincides with the searched destination, records a copy of the complete path
        if (current.Id == target.Id)
        {
            results.Add(new CallPath(currentPath));
            return;
        }

        // 3. Pruning: stops exploration if the active partial path exceeds the maximum permitted depth
        if (currentPath.Count > maxDepth)
            return;

        // 4. Recursively explore all direct callees of the current node
        var callees = GetDirectCallees(current);
        foreach (var callee in callees)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 5. Backtracking: avoid infinite cycles by visiting only nodes not already present in the active path
            if (!visitedOnPath.Contains(callee.Id))
            {
                visitedOnPath.Add(callee.Id);
                currentPath.Add(callee);

                // Depth-depth recursive step
                DfsFindPaths(callee, target, currentPath, visitedOnPath, results, maxDepth, cancellationToken);

                // Restoration of the state (Backtracking) to allow the exploration of alternative paths
                currentPath.RemoveAt(currentPath.Count - 1);
                visitedOnPath.Remove(callee.Id);
            }
        }
    }

    // =========================================================================
    // CYCLE DETECTION (Cycle Detection and Recursion)
    // =========================================================================

    /// <summary>
    /// Identifies all direct invocation or mutual recursion cycles present in the Call Graph.
    /// It uses the Depth-First Search algorithm with 3-state node marking (White, Gray, Black).
    /// </summary>
    public IReadOnlyList<CallCycle> DetectCycles()
    {
        var routineSymbols = _graph.Symbols
            .Where(s => s.Kind is SymbolKind.Method or SymbolKind.Property)
            .ToList();

        // Initialize all nodes as White (not yet visited)
        var state = new Dictionary<Guid, NodeColor>();
        foreach (var sym in routineSymbols)
            state[sym.Id] = NodeColor.White;

        var cycles = new List<CallCycle>();
        var path = new List<Symbol>();

        // Performs DFS from each node not yet visited to cover all connected components of the forest
        foreach (var sym in routineSymbols)
        {
            if (state[sym.Id] == NodeColor.White)

                DfsDetectCycles(sym, state, path, cycles);
        }

        return cycles;
    }

    /// <summary>
    /// Recursive depth scanning (3-color DFS) to identify back-edges and loops in the Call Graph.
    /// </summary>
    /// <param name="current">Current node under examination.</param>
    /// <param name="state">Node color map: <c>White</c> (not visited), <c>Gray</c> (processing in stack), <c>Black</c> (fully explored).</param>
    /// <param name="path">Current sequence of nodes in the visit branch.</param>
    /// <param name="cycles">List of call cycles detected.</param>
    /// <remarks>
    /// An edge to a node marked <c>Gray</c> represents a back-edge that closes a loop of calls or recursion.
    /// </remarks>
    private void DfsDetectCycles(
        Symbol current,
        Dictionary<Guid, NodeColor> state,
        List<Symbol> path,
        List<CallCycle> cycles)
    {
        // 1. Mark current node as Gray (processing in active recursion stack)
        state[current.Id] = NodeColor.Gray;
        path.Add(current);

        // 2. Explore outgoing calls
        var callees = GetDirectCallees(current);
        foreach (var callee in callees)
        {
            if (state.TryGetValue(callee.Id, out var calleeState))
            {
                if (calleeState == NodeColor.Gray)
                {
                    // Back-edge detected: callee node is already in the current stack (closes a loop or mutual recursion)
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

        // 3. Post-processing: removes the node from the active path and marks it Black (subtree visit completed)
        path.RemoveAt(path.Count - 1);
        state[current.Id] = NodeColor.Black;
    }

    // =========================================================================
    // DEPENDENCY QUERIES (Instantiates & UsesType)
    // =========================================================================

    /// <summary>
    /// Returns all classes instantiated directly from the specified symbol (INSTANTIATES relation).
    /// Thread-safe cached result for subsequent instant access.
    /// </summary>
    public IReadOnlyList<Symbol> GetInstantiatedClasses(Symbol source) =>
        _instantiatedClassesCache.GetOrAdd(source.Id, _ =>
            _graph.GetOutgoingRelationships(source.Id)
                .Where(r => r.Kind == RelationshipKind.Instantiates)
                .Select(r => _graph.FindSymbolById(r.TargetId))
                .Where(s => s != null)
                .Select(s => s!)
                .DistinctBy(s => s.Id)
                .ToList());


    // =========================================================================
    // IMPACT ANALYSIS & BLAST RADIUS
    // =========================================================================

    /// <summary>
    /// Performs full Blast Radius analysis for the specified symbol.
    /// Thread-safe cached result for subsequent instant access.
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
    /// Checks whether the impact report for the specified symbol is already present in the cache.
    /// </summary>
    public bool TryGetCachedImpactReport(Symbol symbol, int maxDepth, out Impact.ImpactReport? report) =>
        _impactReportCache.TryGetValue((symbol.Id, maxDepth), out report);
}
