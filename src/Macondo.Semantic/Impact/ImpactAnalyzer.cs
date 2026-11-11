using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;

namespace Macondo.Semantic.Impact;

/// <summary>
/// Impact Analysis and Blast Radius calculation engine.
/// 
/// RESPONSABILITÀ:
/// 1. Calculating Transitive Callers: traverse the Call Graph backwards via BFS, calculating the distance (Depth)
/// of each caller from the target.
/// 2. Container and Touchpoint UI mapping: goes back to the classes, modules and forms (.frm) containers of the callers.
/// 3. Reconstruction of Execution Paths: identify all the call chains starting from the entry points
/// (e.g. form event handlers, vertex callers) and reach the parsed routine.
/// 4. Impact Analysis on Entire Types: aggregates the impact of instantiations (INSTANTIATES), type dependencies (USES_TYPE)
/// and calls to all members declared in the type.
/// </summary>
public sealed class ImpactAnalyzer
{
    private readonly CodeGraph _graph;
    private readonly CallGraphQueries _queries;

    public ImpactAnalyzer(CodeGraph graph)
    {
        _graph = graph;
        _queries = new CallGraphQueries(graph);
    }

    public ImpactAnalyzer(CodeGraph graph, CallGraphQueries queries)
    {
        _graph = graph;
        _queries = queries;
    }

    /// <summary>
    /// Performs full impact analysis (Blast Radius) on an arbitrary symbol.
    /// Route analysis based on symbol category (Routine, Type, or Field).
    /// </summary>
    /// <param name="target">The symbol selected for analysis.</param>
    /// <param name="maxDepth">Maximum depth of ascent in the graph (default 10).</param>
    /// <param name="cancellationToken">Optional token to cancel analysis.</param>
    public ImpactReport AnalyzeSymbolImpact(Symbol target, int maxDepth = 10, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 1. If the symbol is a routine (Method, Function or Property), calculate the blast radius based on the Call Graph
        if (target.Kind is SymbolKind.Method or SymbolKind.Property)
            return AnalyzeRoutineImpact(target, maxDepth, cancellationToken);

        // 2. If the symbol is a composite type, interface, or module (Class, Interface, Struct, Record, Form, Module, UserControl, UDT, Enum),
        // aggregates the impact of all its members and instantiations/type dependencies/implementations/inheritance
        if (target.Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.Module
                         or SymbolKind.UserControl or SymbolKind.Type or SymbolKind.Enum
                         or SymbolKind.Interface or SymbolKind.Struct or SymbolKind.Record)
            return AnalyzeTypeImpact(target, maxDepth, cancellationToken);

        // 3. If the target is a field, variable or constant, locate the containing container
        // and finds all external symbols that depend directly on the member's type
        var container = FindEnclosingContainer(target);
        var memberTypeDependents = GetTypeDependentsForSymbol(target);

        return new ImpactReport
        {
            TargetSymbol = target,
            DirectCallers = Array.Empty<Symbol>(),
            IndirectCallers = Array.Empty<ImpactedCaller>(),
            CallersByDepth = new Dictionary<int, IReadOnlyList<Symbol>>(),
            AffectedClasses = container?.Kind == SymbolKind.Class ? new[] { container } : Array.Empty<Symbol>(),
            AffectedModules = container?.Kind == SymbolKind.Module ? new[] { container } : Array.Empty<Symbol>(),
            AffectedForms = container?.Kind == SymbolKind.Form ? new[] { container } : Array.Empty<Symbol>(),
            AffectedUserControls = container?.Kind == SymbolKind.UserControl ? new[] { container } : Array.Empty<Symbol>(),
            AffectedExecutionPaths = Array.Empty<CallPath>(),
            MaxDependencyDepth = 0,
            Instantiators = Array.Empty<Symbol>(),
            TypeDependents = memberTypeDependents
        };
    }

    /// <summary>
    /// Calculate the Blast Radius for a single routine (Sub, Function, Property).
    /// Traverse the Call Graph backwards via BFS to find all direct and transitive callers,
    /// it goes back to the impacted UI and logical containers and reconstructs the complete execution paths.
    /// </summary>
    private ImpactReport AnalyzeRoutineImpact(Symbol routine, int maxDepth = 10, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 1. Retrieve direct callers (Depth 1) via Call Graph queries
        var directCallers = _queries.GetDirectCallers(routine).ToList();
        var indirectCallers = new List<ImpactedCaller>();
        var callersByDepth = new Dictionary<int, List<Symbol>>();

        // Initialize depth grouping 1
        if (directCallers.Count > 0)
            callersByDepth[1] = new List<Symbol>(directCallers);

        // 2. Performs a backward BFS visit to compute all transitive callers (Depth > 1)
        // Maintains a set of visited IDs to prevent loops when there is recursion or loops in the graph
        var visited = new HashSet<Guid> { routine.Id };
        foreach (var caller in directCallers)
        {
            visited.Add(caller.Id);
        }

        var queue = new Queue<(Symbol Symbol, int Depth)>();
        foreach (var caller in directCallers)
        {
            queue.Enqueue((caller, 1));
        }

        var maxReachedDepth = directCallers.Count > 0 ? 1 : 0;

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (current, depth) = queue.Dequeue();

            // Stops branch expansion if the required maximum ascent depth has been reached
            if (depth >= maxDepth)
                continue;

            // Query callers of the current node
            var nextCallers = _queries.GetDirectCallers(current);
            foreach (var nextCaller in nextCallers)
            {
                // If the caller has not yet been visited, it records it at the increased depth level
                if (!visited.Contains(nextCaller.Id))
                {
                    visited.Add(nextCaller.Id);
                    var nextDepth = depth + 1;
                    maxReachedDepth = Math.Max(maxReachedDepth, nextDepth);

                    if (!callersByDepth.TryGetValue(nextDepth, out var list))
                    {
                        list = new List<Symbol>();
                        callersByDepth[nextDepth] = list;
                    }
                    list.Add(nextCaller);

                    // Register the indirect caller with the belonging container
                    indirectCallers.Add(new ImpactedCaller
                    {
                        Symbol = nextCaller,
                        Depth = nextDepth,
                        EnclosingContainer = FindEnclosingContainer(nextCaller)
                    });

                    // Append the node to continue exploration backwards through the graph
                    queue.Enqueue((nextCaller, nextDepth));
                }
            }
        }

        // 3. Mapping and categorization of impacted containers (Forms, Classes, Modules, UserControl)
        var allImpactedRoutines = directCallers.Concat(indirectCallers.Select(ic => ic.Symbol)).ToList();
        var affectedContainers = allImpactedRoutines
            .Select(FindEnclosingContainer)
            .Where(c => c != null)
            .Select(c => c!)
            .DistinctBy(c => c.Id)
            .ToList();

        // If the routine itself resides in a container, it also includes its native container among the impacted components
        var routineContainer = FindEnclosingContainer(routine);
        if (routineContainer != null && !affectedContainers.Any(c => c.Id == routineContainer.Id))
            affectedContainers.Add(routineContainer);

        var affectedForms = affectedContainers.Where(c => c.Kind == SymbolKind.Form).ToList();
        var affectedClasses = affectedContainers.Where(c => c.Kind == SymbolKind.Class).ToList();
        var affectedModules = affectedContainers.Where(c => c.Kind == SymbolKind.Module).ToList();
        var affectedUserControls = affectedContainers.Where(c => c.Kind == SymbolKind.UserControl).ToList();

        // 4. Reconstruction of all execution paths from the root callers (Entry Points) up to the target routine
        // A caller is considered root if it has no parents within the sub-graph of impacted callers
        var executionPathsList = new List<CallPath>();
        var callerIds = new HashSet<Guid>(allImpactedRoutines.Select(c => c.Id));
        var rootCallers = allImpactedRoutines
            .Where(c => _queries.GetDirectCallers(c).All(parent => !callerIds.Contains(parent.Id)))
            .ToList();

        foreach (var root in rootCallers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Runs DFS to find all acyclic paths from the root to the routine
            var rootPaths = _queries.FindExecutionPaths(root, routine, maxDepth, cancellationToken);
            executionPathsList.AddRange(rootPaths);
        }

        // Deduplicates execution paths based on their string representation
        var executionPaths = executionPathsList.DistinctBy(p => p.ToString()).ToList();

        // 5. Check type dependencies and direct instantiations on the routine
        var instantiators = _graph.Relationships
            .Where(r => r.Kind == RelationshipKind.Instantiates && r.TargetId == routine.Id)
            .Select(r => _graph.FindSymbolById(r.SourceId))
            .Where(s => s != null)
            .Select(s => s!)
            .DistinctBy(s => s.Id)
            .ToList();
        var typeDependents = GetTypeDependentsForSymbol(routine);

        return new ImpactReport
        {
            TargetSymbol = routine,
            DirectCallers = directCallers,
            IndirectCallers = indirectCallers,
            CallersByDepth = callersByDepth.ToDictionary(k => k.Key, v => (IReadOnlyList<Symbol>)v.Value),
            AffectedClasses = affectedClasses,
            AffectedModules = affectedModules,
            AffectedForms = affectedForms,
            AffectedUserControls = affectedUserControls,
            AffectedExecutionPaths = executionPaths,
            MaxDependencyDepth = maxReachedDepth,
            Instantiators = instantiators,
            TypeDependents = typeDependents
        };
    }

    /// <summary>
    /// Calculates the Blast Radius for an entire data type or module (Class, Form, Module, Type, Enum).
    /// It aggregates three pillars of impact:
    /// 1. The impact of all member methods and properties declared in the type.
    /// 2. Symbols that explicitly instantiate the type (INSTANTIATES relationship).
    /// 3. Symbols that use the type as a variable, parameter, or return type (USES_TYPE relationship).
    /// </summary>
    private ImpactReport AnalyzeTypeImpact(Symbol typeSymbol, int maxDepth = 10, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var members = _graph.GetChildren(typeSymbol.Id).ToList();
        var directCallersSet = new HashSet<Guid>();
        var directCallersList = new List<Symbol>();
        var indirectCallersList = new List<ImpactedCaller>();
        var allExecutionPaths = new List<CallPath>();
        var callersByDepth = new Dictionary<int, List<Symbol>>();

        var maxDepthFound = 0;

        // ---------------------------------------------------------------------
        // PILLAR 1: Aggregate the impact of all member methods and properties of the type
        // ---------------------------------------------------------------------
        foreach (var member in members.Where(m => m.Kind is SymbolKind.Method or SymbolKind.Property))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var memberReport = AnalyzeRoutineImpact(member, maxDepth, cancellationToken);

            // Collects the member's direct callers avoiding duplication
            foreach (var dc in memberReport.DirectCallers)
            {
                if (directCallersSet.Add(dc.Id))
                    directCallersList.Add(dc);
            }

            // Collects the member's indirect callers
            foreach (var ic in memberReport.IndirectCallers)
            {
                if (!directCallersSet.Contains(ic.Symbol.Id) && indirectCallersList.All(x => x.Symbol.Id != ic.Symbol.Id))

                    indirectCallersList.Add(ic);
            }

            maxDepthFound = Math.Max(maxDepthFound, memberReport.MaxDependencyDepth);
            allExecutionPaths.AddRange(memberReport.AffectedExecutionPaths);
        }

        // ---------------------------------------------------------------------
        // PILLAR 2: Identify the components that explicitly instantiate this type (INSTANTIATES)
        // ---------------------------------------------------------------------
        var instantiators = _graph.GetIncomingRelationships(typeSymbol.Id)
            .Where(r => r.Kind == RelationshipKind.Instantiates)
            .Select(r => _graph.FindSymbolById(r.SourceId))
            .Where(s => s != null)
            .Select(s => s!)
            .DistinctBy(s => s.Id)
            .ToList();

        // ---------------------------------------------------------------------
        // PILLAR 3: Identify symbols that use this type (USES_TYPE, IMPLEMENTS, INHERITS)
        // ---------------------------------------------------------------------
        var typeDependents = _graph.GetIncomingRelationships(typeSymbol.Id)
            .Where(r => r.Kind is RelationshipKind.UsesType or RelationshipKind.Implements or RelationshipKind.Inherits)
            .Select(r => _graph.FindSymbolById(r.SourceId))
            .Where(s => s != null)
            .Select(s => s!)
            .DistinctBy(s => s.Id)
            .ToList();

        // ---------------------------------------------------------------------
        // Overall aggregation and mapping of impacted containers
        // ---------------------------------------------------------------------
        var allImpacted = directCallersList
            .Concat(indirectCallersList.Select(i => i.Symbol))
            .Concat(instantiators)
            .Concat(typeDependents)
            .ToList();

        var affectedContainers = allImpacted
            .Select(FindEnclosingContainer)
            .Where(c => c != null && c.Id != typeSymbol.Id)
            .Select(c => c!)
            .DistinctBy(c => c.Id)
            .ToList();

        // Group callers by depth level
        if (directCallersList.Count > 0)
            callersByDepth[1] = directCallersList;

        foreach (var ic in indirectCallersList)
        {
            if (!callersByDepth.TryGetValue(ic.Depth, out var list))
            {
                list = new List<Symbol>();
                callersByDepth[ic.Depth] = list;
            }
            list.Add(ic.Symbol);
        }

        return new ImpactReport
        {
            TargetSymbol = typeSymbol,
            DirectCallers = directCallersList,
            IndirectCallers = indirectCallersList,
            CallersByDepth = callersByDepth.ToDictionary(k => k.Key, v => (IReadOnlyList<Symbol>)v.Value),
            AffectedClasses = affectedContainers.Where(c => c.Kind == SymbolKind.Class).ToList(),
            AffectedModules = affectedContainers.Where(c => c.Kind == SymbolKind.Module).ToList(),
            AffectedForms = affectedContainers.Where(c => c.Kind == SymbolKind.Form).ToList(),
            AffectedUserControls = affectedContainers.Where(c => c.Kind == SymbolKind.UserControl).ToList(),
            AffectedExecutionPaths = allExecutionPaths.DistinctBy(p => p.ToString()).ToList(),
            MaxDependencyDepth = Math.Max(maxDepthFound, (instantiators.Count > 0 || typeDependents.Count > 0) ? 1 : 0),
            Instantiators = instantiators,
            TypeDependents = typeDependents
        };
    }


    // =========================================================================
    // HELPER PRIVATI
    // =========================================================================

    /// <summary>
    /// Goes up the hierarchy of CONTAINS relations to identify the module, class, form or user control containing a symbol.
    /// If the passed symbol is already a top-level container, it returns it directly.
    /// </summary>
    private Symbol? FindEnclosingContainer(Symbol symbol)
    {
        // If the symbol is already a top-level container, the ascent ends
        if (symbol.Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.Module
                        or SymbolKind.UserControl or SymbolKind.Project
                        or SymbolKind.Interface or SymbolKind.Struct or SymbolKind.Record)
            return symbol;

        // Search for parent CONTAINS relationship (Source CONTAINS Target)
        var parentRel = _graph.GetIncomingRelationships(symbol.Id)
            .FirstOrDefault(r => r.Kind == RelationshipKind.Contains);

        if (parentRel != null)
        {
            var parent = _graph.FindSymbolById(parentRel.SourceId);
            if (parent != null)
                return FindEnclosingContainer(parent);
        }

        return null;
    }


    /// <summary>
    /// Retrieves all symbols (fields, variables, routines) that declare or directly use the specified symbol as a type (<c>USES_TYPE</c>).
    /// </summary>
    /// <param name="symbol">The type symbol (Class, Form, UDT, Enum) to query.</param>
    /// <returns>The list of type-dependent symbols.</returns>
    private IReadOnlyList<Symbol> GetTypeDependentsForSymbol(Symbol symbol)
    {
        return _graph.GetIncomingRelationships(symbol.Id)
            .Where(r => r.Kind == RelationshipKind.UsesType)
            .Select(r => _graph.FindSymbolById(r.SourceId))
            .Where(s => s != null)
            .Select(s => s!)
            .DistinctBy(s => s.Id)
            .ToList();
    }
}
