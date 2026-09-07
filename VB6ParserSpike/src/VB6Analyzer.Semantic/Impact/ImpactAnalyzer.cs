using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;

namespace Macondo.Semantic.Impact;

/// <summary>
/// Motore di calcolo dell'Impact Analysis e del Blast Radius (sezione 17 e Milestone M6 del charter).
/// 
/// RESPONSABILITÀ:
/// 1. Calcolo Chiamanti Transitivi: attraversa il Call Graph a ritroso tramite BFS, calcolando la distanza (Depth)
///    di ciascun chiamante dal target.
/// 2. Mappatura Container e Touchpoint UI: risale alle classi, moduli e form (.frm) contenitori dei chiamanti.
/// 3. Ricostruzione Cammini di Esecuzione: individua tutte le call chain che partono dai punti di ingresso
///    (es. gestori eventi form, chiamanti di vertice) e raggiungono la routine analizzata.
/// 4. Analisi d'Impatto su Interi Tipi: aggrega l'impatto di istanziazioni (INSTANTIATES), dipendenze di tipo (USES_TYPE)
///    e chiamate a tutti i membri dichiarati nel tipo.
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
    /// Esegue l'analisi di impatto completa (Blast Radius) su un simbolo arbitrario.
    /// Instrada l'analisi in base alla categoria del simbolo (Routine, Tipo o Campo).
    /// </summary>
    /// <param name="target">Il simbolo selezionato per l'analisi.</param>
    /// <param name="maxDepth">Profondità massima di risalita nel grafo (default 10).</param>
    /// <param name="cancellationToken">Token opzionale per annullare l'analisi.</param>
    public ImpactReport AnalyzeSymbolImpact(Symbol target, int maxDepth = 10, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (target.Kind is SymbolKind.Method or SymbolKind.Property)
        {
            return AnalyzeRoutineImpact(target, maxDepth, cancellationToken);
        }

        if (target.Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.Module
                         or SymbolKind.UserControl or SymbolKind.Type or SymbolKind.Enum)
        {
            return AnalyzeTypeImpact(target, maxDepth, cancellationToken);
        }

        // Calcola il Blast Radius per un campo o costante.
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
    /// Calcola il Blast Radius per una singola routine (Sub, Function, Property).
    /// </summary>
    public ImpactReport AnalyzeRoutineImpact(Symbol routine, int maxDepth = 10, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var directCallers = _queries.GetDirectCallers(routine).ToList();
        var indirectCallers = new List<ImpactedCaller>();
        var callersByDepth = new Dictionary<int, List<Symbol>>();

        // Inizializza profondità 1
        if (directCallers.Count > 0)
        {
            callersByDepth[1] = new List<Symbol>(directCallers);
        }

        // BFS all'indietro per calcolare tutti i chiamanti transitivi
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

            if (depth >= maxDepth)
                continue;

            var nextCallers = _queries.GetDirectCallers(current);
            foreach (var nextCaller in nextCallers)
            {
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

                    indirectCallers.Add(new ImpactedCaller
                    {
                        Symbol = nextCaller,
                        Depth = nextDepth,
                        EnclosingContainer = FindEnclosingContainer(nextCaller)
                    });

                    queue.Enqueue((nextCaller, nextDepth));
                }
            }
        }

        // Mappatura dei container impattati (Form, Classi, Moduli)
        var allImpactedRoutines = directCallers.Concat(indirectCallers.Select(ic => ic.Symbol)).ToList();
        var affectedContainers = allImpactedRoutines
            .Select(FindEnclosingContainer)
            .Where(c => c != null)
            .Select(c => c!)
            .DistinctBy(c => c.Id)
            .ToList();

        var affectedForms = affectedContainers.Where(c => c.Kind == SymbolKind.Form).ToList();
        var affectedClasses = affectedContainers.Where(c => c.Kind == SymbolKind.Class).ToList();
        var affectedModules = affectedContainers.Where(c => c.Kind == SymbolKind.Module).ToList();
        var affectedUserControls = affectedContainers.Where(c => c.Kind == SymbolKind.UserControl).ToList();

        // Ricostruzione di tutti i cammini di esecuzione dai chiamanti di vertice al target
        var executionPathsList = new List<CallPath>();
        var callerIds = new HashSet<Guid>(allImpactedRoutines.Select(c => c.Id));
        var rootCallers = allImpactedRoutines
            .Where(c => _queries.GetDirectCallers(c).All(parent => !callerIds.Contains(parent.Id)))
            .ToList();

        foreach (var root in rootCallers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rootPaths = _queries.FindExecutionPaths(root, routine, maxDepth, cancellationToken);
            executionPathsList.AddRange(rootPaths);
        }

        var executionPaths = executionPathsList.DistinctBy(p => p.ToString()).ToList();

        // Dipendenze di tipo e istanziazioni dirette
        var instantiators = _queries.GetDirectCallers(routine); // per routine tipicamente vuoto o delegate
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
    /// Calcola il Blast Radius per un intero tipo di dato o modulo (Class, Form, Module, Type, Enum).
    /// </summary>
    public ImpactReport AnalyzeTypeImpact(Symbol typeSymbol, int maxDepth = 10, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var members = _graph.GetChildren(typeSymbol.Id).ToList();
        var directCallersSet = new HashSet<Guid>();
        var directCallersList = new List<Symbol>();
        var indirectCallersList = new List<ImpactedCaller>();
        var allExecutionPaths = new List<CallPath>();
        var callersByDepth = new Dictionary<int, List<Symbol>>();

        var maxDepthFound = 0;

        // 1. Aggrega l'impatto di tutti i metodi/proprietà membri del tipo
        foreach (var member in members.Where(m => m.Kind is SymbolKind.Method or SymbolKind.Property))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var memberReport = AnalyzeRoutineImpact(member, maxDepth, cancellationToken);

            foreach (var dc in memberReport.DirectCallers)
            {
                if (directCallersSet.Add(dc.Id))
                    directCallersList.Add(dc);
            }

            foreach (var ic in memberReport.IndirectCallers)
            {
                if (!directCallersSet.Contains(ic.Symbol.Id) && indirectCallersList.All(x => x.Symbol.Id != ic.Symbol.Id))
                {
                    indirectCallersList.Add(ic);
                }
            }

            maxDepthFound = Math.Max(maxDepthFound, memberReport.MaxDependencyDepth);
            allExecutionPaths.AddRange(memberReport.AffectedExecutionPaths);
        }

        // 2. Classi che istanziano esplicitamente questo tipo (INSTANTIATES)
        var instantiators = _graph.Relationships
            .Where(r => r.Kind == RelationshipKind.Instantiates && r.TargetId == typeSymbol.Id)
            .Select(r => _graph.FindSymbolById(r.SourceId))
            .Where(s => s != null)
            .Select(s => s!)
            .DistinctBy(s => s.Id)
            .ToList();

        // 3. Simboli che usano questo tipo come tipo di campo/parametro/ritorno (USES_TYPE)
        var typeDependents = _graph.Relationships
            .Where(r => r.Kind == RelationshipKind.UsesType && r.TargetId == typeSymbol.Id)
            .Select(r => _graph.FindSymbolById(r.SourceId))
            .Where(s => s != null)
            .Select(s => s!)
            .DistinctBy(s => s.Id)
            .ToList();

        // Raggruppa tutti i simboli impattati
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

        // Raggruppa per profondità
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
    /// Risale la relazione CONTAINS per individuare il modulo, classe o form contenitore di un simbolo.
    /// </summary>
    private Symbol? FindEnclosingContainer(Symbol symbol)
    {
        if (symbol.Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.Module
                        or SymbolKind.UserControl or SymbolKind.Project)
        {
            return symbol;
        }

        var parentRel = _graph.Relationships
            .FirstOrDefault(r => r.Kind == RelationshipKind.Contains && r.TargetId == symbol.Id);

        if (parentRel != null)
        {
            var parent = _graph.FindSymbolById(parentRel.SourceId);
            if (parent != null)
                return FindEnclosingContainer(parent);
        }

        return null;
    }


    private IReadOnlyList<Symbol> GetTypeDependentsForSymbol(Symbol symbol)
    {
        return _graph.Relationships
            .Where(r => r.Kind == RelationshipKind.UsesType && r.TargetId == symbol.Id)
            .Select(r => _graph.FindSymbolById(r.SourceId))
            .Where(s => s != null)
            .Select(s => s!)
            .DistinctBy(s => s.Id)
            .ToList();
    }
}
