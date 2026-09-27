using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.Cohesion;

/// <summary>
/// Calcolatore poliglotto e deterministico delle metriche di coesione interna e suite Chidamber &amp; Kemerer (C&amp;K).
/// 
/// METRICHE CALCOLATE:
/// 1. LCOM4 (Lack of Cohesion in Methods 4 - Hitz &amp; Montazeri):
///    Costruisce un grafo non orientato in cui i nodi sono i metodi della classe. Esiste un arco tra due metodi se
///    accedono ad almeno un campo/stato comune o se uno chiama direttamente l'altro.
///    LCOM4 è il numero di componenti connesse: un valore pari a 1 indica una classe altamente coesa (Single Responsibility),
///    mentre valori superiori indicano responsabilità multiple scorporabili in classi separate.
/// 2. TCC (Tight Class Cohesion): rapporto tra le coppie di metodi direttamente connesse e il totale delle coppie possibili.
/// 3. LCC (Loose Class Cohesion): rapporto tra le coppie di metodi connesse direttamente o transitivamente e il totale delle coppie possibili.
/// 4. WMC (Weighted Methods per Class): somma delle complessità ciclomatiche di tutti i metodi della classe.
/// 5. CBO (Coupling Between Objects): numero di classi esterne a cui la classe è accoppiata tramite chiamate o tipi.
/// 6. RFC (Response for a Class): dimensione dell'insieme di risposta della classe (metodi propri + metodi esterni direttamente invocati).
/// </summary>
public static class CohesionCalculator
{
    private static readonly HashSet<SymbolKind> ContainerKinds = new()
    {
        SymbolKind.Class,
        SymbolKind.Module,
        SymbolKind.Form,
        SymbolKind.UserControl,
        SymbolKind.Struct,
        SymbolKind.Record
    };

    private static readonly HashSet<SymbolKind> RoutineKinds = new()
    {
        SymbolKind.Method,
        SymbolKind.Property
    };

    private static readonly HashSet<SymbolKind> StateKinds = new()
    {
        SymbolKind.Field,
        SymbolKind.Property,
        SymbolKind.Constant
    };

    /// <summary>
    /// Esegue l'analisi completa di coesione su tutti i contenitori del CodeGraph.
    /// Calcola metriche LCOM4 (componenti connesse), TCC, LCC e la suite C&amp;K (WMC, CBO, RFC).
    /// </summary>
    /// <param name="graph">Il CodeGraph contenente simboli e relazioni del progetto.</param>
    /// <returns>Il dataset <see cref="CohesionDataset"/> contenente i report di coesione per ogni contenitore.</returns>
    public static CohesionDataset Analyze(CodeGraph graph)
    {
        var dataset = new CohesionDataset();
        if (graph.Symbols.Count == 0)
            return dataset;

        var containers = graph.Symbols.Where(s => ContainerKinds.Contains(s.Kind)).ToList();
        if (containers.Count == 0)
            return dataset;

        // ---------------------------------------------------------------------
        // FASE 1: Mappatura globale membro -> container per calcolo rapido di CBO ed RFC
        // ---------------------------------------------------------------------
        var memberToContainerMap = new Dictionary<Guid, Guid>();
        foreach (var c in containers)
        {
            memberToContainerMap[c.Id] = c.Id;
            foreach (var child in graph.GetChildren(c.Id))
            {
                memberToContainerMap[child.Id] = c.Id;
            }
        }

        // ---------------------------------------------------------------------
        // FASE 2: Calcolo coesione per ciascun container
        // ---------------------------------------------------------------------
        foreach (var container in containers)
        {
            var children = graph.GetChildren(container.Id).ToList();
            var routines = children.Where(c => RoutineKinds.Contains(c.Kind)).ToList();
            var fields = children.Where(c => StateKinds.Contains(c.Kind)).ToList();

            var report = new ContainerCohesionReport
            {
                Container = container,
                MethodCount = routines.Count,
                FieldCount = fields.Count
            };

            // Se non ci sono metodi o c'è un solo metodo, LCOM4 è banale (1 cluster o 0)
            if (routines.Count == 0)
            {
                report.Lcom4 = 0;
                report.TightClassCohesion = 1.0;
                report.LooseClassCohesion = 1.0;
            }
            else if (routines.Count == 1)
            {
                report.Lcom4 = 1;
                report.TightClassCohesion = 1.0;
                report.LooseClassCohesion = 1.0;
                report.Clusters.Add(new CohesionCluster
                {
                    ClusterIndex = 1,
                    Methods = new List<Symbol> { routines[0] },
                    AccessedFields = fields,
                    SuggestedRefactoringName = container.Name
                });
            }
            else
            {
                // Calcola il grafo di coesione LCOM4, TCC e LCC
                CalculateLcom4AndCohesion(graph, container, routines, fields, report);
            }

            // Calcolo metriche C&K (WMC, CBO ed RFC)
            CalculateCkMetrics(graph, container, routines, memberToContainerMap, report);

            dataset.Items.Add(report);
        }

        // Ordina mettendo in cima i componenti a coesione più bassa (LCOM4 elevato, candidati ideali al refactoring)
        dataset.Items.Sort((a, b) =>
        {
            int cmp = b.Lcom4.CompareTo(a.Lcom4);
            if (cmp != 0) return cmp;
            return a.TightClassCohesion.CompareTo(b.TightClassCohesion);
        });

        return dataset;
    }

    /// <summary>
    /// Calcola il grafo non orientato di connettività tra i metodi del contenitore e ne individua le componenti connesse (LCOM4, TCC, LCC).
    /// Due metodi sono connessi se condividono l'accesso ad almeno un campo/stato o se si chiamano direttamente.
    /// </summary>
    private static void CalculateLcom4AndCohesion(
        CodeGraph graph,
        Symbol container,
        List<Symbol> routines,
        List<Symbol> fields,
        ContainerCohesionReport report)
    {
        var routineIds = routines.Select(r => r.Id).ToHashSet();
        var fieldIds = fields.Select(f => f.Id).ToHashSet();
        var routineMap = routines.ToDictionary(r => r.Id);
        var fieldMap = fields.ToDictionary(f => f.Id);

        // 1. Mappe di accesso: Routine -> Campi letti/scritti e Routine -> Chiamate interne
        var routineFields = routines.ToDictionary(r => r.Id, _ => new HashSet<Guid>());
        var routineInternalCalls = routines.ToDictionary(r => r.Id, _ => new HashSet<Guid>());

        foreach (var rel in graph.Relationships)
        {
            if (routineIds.Contains(rel.SourceId))
            {
                if ((rel.Kind == RelationshipKind.Reads || rel.Kind == RelationshipKind.Writes) && fieldIds.Contains(rel.TargetId))
                {
                    routineFields[rel.SourceId].Add(rel.TargetId);
                }
                else if (rel.Kind == RelationshipKind.Calls && routineIds.Contains(rel.TargetId) && rel.TargetId != rel.SourceId)
                {
                    routineInternalCalls[rel.SourceId].Add(rel.TargetId);
                }
            }
        }

        // 2. Costruzione della matrice di adiacenza non orientata per LCOM4
        // Due metodi sono adiacenti se:
        // - Accedono ad almeno un campo comune (f1 ∩ f2 ≠ ∅)
        // - Oppure uno chiama direttamente l'altro (r1 -> r2 o r2 -> r1)
        int n = routines.Count;
        var adj = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var r in routines)
            adj[r.Id] = new HashSet<Guid>();

        int directConnectedPairs = 0;
        int totalPossiblePairs = (n * (n - 1)) / 2;

        for (int i = 0; i < n; i++)
        {
            var r1 = routines[i].Id;
            var f1 = routineFields[r1];

            for (int j = i + 1; j < n; j++)
            {
                var r2 = routines[j].Id;
                var f2 = routineFields[r2];

                bool shareField = f1.Overlaps(f2);
                bool callEachOther = routineInternalCalls[r1].Contains(r2) || routineInternalCalls[r2].Contains(r1);

                if (shareField || callEachOther)
                {
                    adj[r1].Add(r2);
                    adj[r2].Add(r1);
                    directConnectedPairs++;
                }
            }
        }

        // 3. Calcolo delle Componenti Connesse tramite visita BFS -> LCOM4
        // Ogni componente connessa rappresenta un cluster di metodi fortemente coesi tra loro
        var visited = new HashSet<Guid>();
        int clusterIdx = 1;

        foreach (var routine in routines)
        {
            if (visited.Contains(routine.Id))
                continue;

            var cluster = new CohesionCluster
            {
                ClusterIndex = clusterIdx++,
                SuggestedRefactoringName = $"{container.Name}_Part{clusterIdx - 1}"
            };

            var queue = new Queue<Guid>();
            queue.Enqueue(routine.Id);
            visited.Add(routine.Id);

            var clusterFieldIds = new HashSet<Guid>();

            while (queue.Count > 0)
            {
                var currId = queue.Dequeue();
                if (routineMap.TryGetValue(currId, out var rSym))
                {
                    cluster.Methods.Add(rSym);
                }

                foreach (var fId in routineFields[currId])
                {
                    clusterFieldIds.Add(fId);
                }

                foreach (var neighbor in adj[currId])
                {
                    if (visited.Add(neighbor))
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }

            foreach (var fId in clusterFieldIds)
            {
                if (fieldMap.TryGetValue(fId, out var fSym))
                {
                    cluster.AccessedFields.Add(fSym);
                }
            }

            report.Clusters.Add(cluster);
        }

        report.Lcom4 = report.Clusters.Count;

        // 4. TCC (Tight Class Cohesion): coppie direttamente connesse / coppie totali
        report.TightClassCohesion = totalPossiblePairs > 0
            ? Math.Clamp((double)directConnectedPairs / totalPossiblePairs, 0.0, 1.0)
            : 1.0;

        // 5. LCC (Loose Class Cohesion): coppie transitivamente connesse (nella stessa componente connessa) / coppie totali
        int reachablePairs = 0;
        foreach (var cluster in report.Clusters)
        {
            int size = cluster.Methods.Count;
            if (size > 1)
            {
                reachablePairs += (size * (size - 1)) / 2;
            }
        }

        report.LooseClassCohesion = totalPossiblePairs > 0
            ? Math.Clamp((double)reachablePairs / totalPossiblePairs, 0.0, 1.0)
            : 1.0;
    }

    /// <summary>
    /// Calcola le metriche Chidamber &amp; Kemerer di accoppiamento e complessità:
    /// - WMC (Weighted Methods per Class): complessità cumulativa dei metodi
    /// - CBO (Coupling Between Objects): numero di classi esterne accoppiate
    /// - RFC (Response for a Class): numero di metodi propri + chiamati all'esterno
    /// </summary>
    private static void CalculateCkMetrics(
        CodeGraph graph,
        Symbol container,
        List<Symbol> routines,
        Dictionary<Guid, Guid> memberToContainerMap,
        ContainerCohesionReport report)
    {
        var routineIds = routines.Select(r => r.Id).ToHashSet();
        var externalContainersCoupled = new HashSet<Guid>();
        var responseMethods = new HashSet<Guid>(routineIds);

        int totalWmc = 0;

        // 1. Calcolo WMC (somma delle complessità stimate per ciascun metodo)
        foreach (var r in routines)
        {
            int methodLoc = Math.Max(1, r.LineEnd - r.LineStart + 1);
            int estimatedComplexity = Math.Max(1, Math.Min(15, methodLoc / 10));
            totalWmc += estimatedComplexity;
        }

        // 2. Calcolo CBO ed RFC tramite ispezione delle relazioni di chiamata del grafo
        foreach (var rel in graph.Relationships)
        {
            if (routineIds.Contains(rel.SourceId))
            {
                if (rel.Kind == RelationshipKind.Calls)
                {
                    responseMethods.Add(rel.TargetId);
                }

                if (memberToContainerMap.TryGetValue(rel.TargetId, out var targetContId) && targetContId != container.Id)
                {
                    externalContainersCoupled.Add(targetContId);
                }
            }
            else if (rel.SourceId == container.Id)
            {
                if (memberToContainerMap.TryGetValue(rel.TargetId, out var targetContId) && targetContId != container.Id)
                {
                    externalContainersCoupled.Add(targetContId);
                }
            }
        }

        report.WeightedMethodsCount = Math.Max(report.MethodCount, totalWmc);
        report.CouplingBetweenObjects = externalContainersCoupled.Count;
        report.ResponseForClass = responseMethods.Count;
    }
}

