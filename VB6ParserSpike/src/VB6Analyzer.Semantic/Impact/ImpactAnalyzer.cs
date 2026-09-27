using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;

namespace Macondo.Semantic.Impact;

/// <summary>
/// Motore di calcolo dell'Impact Analysis e del Blast Radius.
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
#if !DEBUG
        cancellationToken.ThrowIfCancellationRequested();
#endif

        // 1. Se il simbolo è una routine (Metodo, Funzione o Proprietà), calcola il blast radius basato sul Call Graph
        if (target.Kind is SymbolKind.Method or SymbolKind.Property)
        {
            return AnalyzeRoutineImpact(target, maxDepth, cancellationToken);
        }

        // 2. Se il simbolo è un tipo composito o modulo (Classe, Form, Modulo, UserControl, UDT, Enum),
        //    aggrega l'impatto di tutti i suoi membri e delle istanziazioni / dipendenze di tipo
        if (target.Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.Module
                         or SymbolKind.UserControl or SymbolKind.Type or SymbolKind.Enum)
        {
            return AnalyzeTypeImpact(target, maxDepth, cancellationToken);
        }

        // 3. Se il target è un campo, variabile o costante, individua il container contenitore
        //    e trova tutti i simboli esterni che dipendono direttamente dal tipo del membro
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
    /// Attraversa il Call Graph a ritroso tramite BFS per trovare tutti i chiamanti diretti e transitivi,
    /// risale ai container UI e logici impattati e ricostruisce i cammini completi di esecuzione.
    /// </summary>
    private ImpactReport AnalyzeRoutineImpact(Symbol routine, int maxDepth = 10, CancellationToken cancellationToken = default)
    {
#if !DEBUG
        cancellationToken.ThrowIfCancellationRequested();
#endif

        // 1. Recupera i chiamanti diretti (Depth 1) tramite le query del Call Graph
        var directCallers = _queries.GetDirectCallers(routine).ToList();
        var indirectCallers = new List<ImpactedCaller>();
        var callersByDepth = new Dictionary<int, List<Symbol>>();

        // Inizializza il raggruppamento per profondità 1
        if (directCallers.Count > 0)
        {
            callersByDepth[1] = new List<Symbol>(directCallers);
        }

        // 2. Esegue una visita BFS all'indietro per calcolare tutti i chiamanti transitivi (Depth > 1)
        //    Mantiene un set di ID visitati per prevenire loop in presenza di ricorsione o cicli nel grafo
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
#if !DEBUG
            cancellationToken.ThrowIfCancellationRequested();
#endif

            var (current, depth) = queue.Dequeue();

            // Interrompe l'espansione del ramo se è stata raggiunta la profondità massima di risalita richiesta
            if (depth >= maxDepth)
                continue;

            // Interroga i chiamanti del nodo corrente
            var nextCallers = _queries.GetDirectCallers(current);
            foreach (var nextCaller in nextCallers)
            {
                // Se il chiamante non è ancora stato visitato, lo registra al livello di profondità incrementato
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

                    // Registra il chiamante indiretto con il container di appartenenza
                    indirectCallers.Add(new ImpactedCaller
                    {
                        Symbol = nextCaller,
                        Depth = nextDepth,
                        EnclosingContainer = FindEnclosingContainer(nextCaller)
                    });

                    // Accoda il nodo per continuare l'esplorazione a ritroso nel grafo
                    queue.Enqueue((nextCaller, nextDepth));
                }
            }
        }

        // 3. Mappatura e categorizzazione dei container impattati (Form, Classi, Moduli, UserControl)
        var allImpactedRoutines = directCallers.Concat(indirectCallers.Select(ic => ic.Symbol)).ToList();
        var affectedContainers = allImpactedRoutines
            .Select(FindEnclosingContainer)
            .Where(c => c != null)
            .Select(c => c!)
            .DistinctBy(c => c.Id)
            .ToList();

        // Se la routine stessa risiede in un container, include anche il suo container nativo tra i componenti impattati
        var routineContainer = FindEnclosingContainer(routine);
        if (routineContainer != null && !affectedContainers.Any(c => c.Id == routineContainer.Id))
        {
            affectedContainers.Add(routineContainer);
        }

        var affectedForms = affectedContainers.Where(c => c.Kind == SymbolKind.Form).ToList();
        var affectedClasses = affectedContainers.Where(c => c.Kind == SymbolKind.Class).ToList();
        var affectedModules = affectedContainers.Where(c => c.Kind == SymbolKind.Module).ToList();
        var affectedUserControls = affectedContainers.Where(c => c.Kind == SymbolKind.UserControl).ToList();

        // 4. Ricostruzione di tutti i cammini di esecuzione dai chiamanti radice (Entry Points) fino alla routine target
        //    Un chiamante è considerato radice se non ha genitori all'interno del sotto-grafo dei chiamanti impattati
        var executionPathsList = new List<CallPath>();
        var callerIds = new HashSet<Guid>(allImpactedRoutines.Select(c => c.Id));
        var rootCallers = allImpactedRoutines
            .Where(c => _queries.GetDirectCallers(c).All(parent => !callerIds.Contains(parent.Id)))
            .ToList();

        foreach (var root in rootCallers)
        {
#if !DEBUG
            cancellationToken.ThrowIfCancellationRequested();
#endif
            // Esegue DFS per trovare tutti i percorsi aciclici dalla radice alla routine
            var rootPaths = _queries.FindExecutionPaths(root, routine, maxDepth, cancellationToken);
            executionPathsList.AddRange(rootPaths);
        }

        // Deduplica i cammini di esecuzione in base alla loro rappresentazione stringa
        var executionPaths = executionPathsList.DistinctBy(p => p.ToString()).ToList();

        // 5. Verifica dipendenze di tipo e istanziazioni dirette sulla routine
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
    /// Aggrega tre pilastri di impatto:
    /// 1. L'impatto di tutti i metodi e proprietà membri dichiarati nel tipo.
    /// 2. I simboli che istanziano esplicitamente il tipo (relazione INSTANTIATES).
    /// 3. I simboli che utilizzano il tipo come tipo di variabile, parametro o ritorno (relazione USES_TYPE).
    /// </summary>
    private ImpactReport AnalyzeTypeImpact(Symbol typeSymbol, int maxDepth = 10, CancellationToken cancellationToken = default)
    {
#if !DEBUG
        cancellationToken.ThrowIfCancellationRequested();
#endif

        var members = _graph.GetChildren(typeSymbol.Id).ToList();
        var directCallersSet = new HashSet<Guid>();
        var directCallersList = new List<Symbol>();
        var indirectCallersList = new List<ImpactedCaller>();
        var allExecutionPaths = new List<CallPath>();
        var callersByDepth = new Dictionary<int, List<Symbol>>();

        var maxDepthFound = 0;

        // ---------------------------------------------------------------------
        // PILASTRO 1: Aggrega l'impatto di tutti i metodi e proprietà membri del tipo
        // ---------------------------------------------------------------------
        foreach (var member in members.Where(m => m.Kind is SymbolKind.Method or SymbolKind.Property))
        {
#if !DEBUG
            cancellationToken.ThrowIfCancellationRequested();
#endif

            var memberReport = AnalyzeRoutineImpact(member, maxDepth, cancellationToken);

            // Raccoglie i chiamanti diretti del membro evitando duplicazioni
            foreach (var dc in memberReport.DirectCallers)
            {
                if (directCallersSet.Add(dc.Id))
                    directCallersList.Add(dc);
            }

            // Raccoglie i chiamanti indiretti del membro
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

        // ---------------------------------------------------------------------
        // PILASTRO 2: Individua i componenti che istanziano esplicitamente questo tipo (INSTANTIATES)
        // ---------------------------------------------------------------------
        var instantiators = _graph.Relationships
            .Where(r => r.Kind == RelationshipKind.Instantiates && r.TargetId == typeSymbol.Id)
            .Select(r => _graph.FindSymbolById(r.SourceId))
            .Where(s => s != null)
            .Select(s => s!)
            .DistinctBy(s => s.Id)
            .ToList();

        // ---------------------------------------------------------------------
        // PILASTRO 3: Individua i simboli che usano questo tipo come tipo di campo/parametro/ritorno (USES_TYPE)
        // ---------------------------------------------------------------------
        var typeDependents = _graph.Relationships
            .Where(r => r.Kind == RelationshipKind.UsesType && r.TargetId == typeSymbol.Id)
            .Select(r => _graph.FindSymbolById(r.SourceId))
            .Where(s => s != null)
            .Select(s => s!)
            .DistinctBy(s => s.Id)
            .ToList();

        // ---------------------------------------------------------------------
        // Aggregazione complessiva e mappatura dei container impattati
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

        // Raggruppa i chiamanti per livello di profondità
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
    /// Risale la gerarchia delle relazioni CONTAINS per individuare il modulo, classe, form o user control contenitore di un simbolo.
    /// Se il simbolo passato è già un container di primo livello, lo restituisce direttamente.
    /// </summary>
    private Symbol? FindEnclosingContainer(Symbol symbol)
    {
        // Se il simbolo è già un contenitore di primo livello, termina la risalita
        if (symbol.Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.Module
                        or SymbolKind.UserControl or SymbolKind.Project)
        {
            return symbol;
        }

        // Cerca la relazione genitore CONTAINS (Source CONTAINS Target)
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


    /// <summary>
    /// Recupera tutti i simboli (campi, variabili, routine) che dichiarano o utilizzano direttamente il simbolo specificato come tipo (<c>USES_TYPE</c>).
    /// </summary>
    /// <param name="symbol">Il simbolo di tipo (Class, Form, UDT, Enum) da interrogare.</param>
    /// <returns>La lista dei simboli dipendenti dal tipo.</returns>
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
