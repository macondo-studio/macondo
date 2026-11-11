using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.Cohesion;

/// <summary>
/// Polyglot and deterministic calculator of internal cohesion metrics and Chidamber & Kemerer (C&K) suite.
/// 
/// METRICHE CALCOLATE:
/// 1. LCOM4 (Lack of Cohesion in Methods 4 - Hitz & Montazeri):
/// Constructs an undirected graph where the nodes are the methods of the class. There is an arc between two methods if
/// access at least one common field/state or if one calls the other directly.
/// LCOM4 is the number of connected components: a value of 1 indicates a highly cohesive class (Single Responsibility),
/// while higher values indicate multiple responsibilities that can be separated into separate classes.
/// 2. TCC (Tight Class Cohesion): ratio between directly connected pairs of methods and the total possible pairs.
/// 3. LCC (Loose Class Cohesion): ratio between directly or transitively connected pairs of methods and the total number of possible pairs.
/// 4. WMC (Weighted Methods per Class): sum of the cyclomatic complexities of all the methods of the class.
/// 5. CBO (Coupling Between Objects): Number of external classes to which the class is coupled via calls or types.
/// 6. RFC (Response for a Class): size of the class response set (own methods + directly invoked external methods).
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
    /// Performs full cohesion analysis on all CodeGraph containers.
    /// Calculate metrics LCOM4 (connected components), TCC, LCC and the C&K suite (WMC, CBO, RFC).
    /// </summary>
    /// <param name="graph">The CodeGraph containing project symbols and relationships.</param>
    /// <returns>The <see cref="CohesionDataset"/> dataset containing the cohesion reports for each container.</returns>
    public static CohesionDataset Analyze(CodeGraph graph)
    {
        var dataset = new CohesionDataset();
        if (graph.Symbols.Count == 0)
            return dataset;

        var containers = graph.Symbols.Where(s => ContainerKinds.Contains(s.Kind)).ToList();
        if (containers.Count == 0)
            return dataset;

        // ---------------------------------------------------------------------
        // PHASE 1: Global mapping member -> container for quick calculation of CBO and RFC
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
        // PHASE 2: Cohesion calculation for each container
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

            // If there are no methods or there is only one method, LCOM4 is trivial (1 cluster by default)
            if (routines.Count == 0)
            {
                report.Lcom4 = 1;
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
                // Compute the LCOM4, ​​TCC and LCC cohesion graph
                CalculateLcom4AndCohesion(graph, container, routines, fields, report);
            }

            // Calculation of C&K metrics (WMC, CBO and RFC)
            CalculateCkMetrics(graph, container, routines, memberToContainerMap, report);

            dataset.Items.Add(report);
        }

        // Sort by placing the lowest cohesion components at the top (high LCOM4, ideal candidates for refactoring)
        dataset.Items.Sort((a, b) =>
        {
            int cmp = b.Lcom4.CompareTo(a.Lcom4);
            if (cmp != 0) return cmp;
            return a.TightClassCohesion.CompareTo(b.TightClassCohesion);
        });

        return dataset;
    }

    /// <summary>
    /// It calculates the undirected connectivity graph between the container methods and identifies its connected components (LCOM4, TCC, LCC).
    /// Two methods are connected if they share access to at least one field/state or if they call each other directly.
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

        // 1. Access maps: Routines -> Read/Written Fields and Routines -> Internal Calls
        var routineFields = routines.ToDictionary(r => r.Id, _ => new HashSet<Guid>());
        var routineInternalCalls = routines.ToDictionary(r => r.Id, _ => new HashSet<Guid>());

        foreach (var rel in graph.Relationships)
        {
            if (routineIds.Contains(rel.SourceId))
            {
                if ((rel.Kind == RelationshipKind.Reads || rel.Kind == RelationshipKind.Writes) && fieldIds.Contains(rel.TargetId))

                    routineFields[rel.SourceId].Add(rel.TargetId);

                else if (rel.Kind == RelationshipKind.Calls && routineIds.Contains(rel.TargetId) && rel.TargetId != rel.SourceId)

    routineInternalCalls[rel.SourceId].Add(rel.TargetId);
            }
        }

        // 2. Construction of the undirected adjacency matrix for LCOM4
        // Two methods are adjacent if:
        // - They access at least one common field (f1 ∩ f2 ≠ ∅)
        // - Or one calls the other directly (r1 -> r2 or r2 -> r1)
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

        // 3. Calculation of Connected Components via BFS -> LCOM4 visit
        // Each connected component represents a cluster of methods that are strongly cohesive with each other
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

                    cluster.Methods.Add(rSym);

                foreach (var fId in routineFields[currId])
                {
                    clusterFieldIds.Add(fId);
                }

                foreach (var neighbor in adj[currId])
                {
                    if (visited.Add(neighbor))

                        queue.Enqueue(neighbor);
                }
            }

            foreach (var fId in clusterFieldIds)
            {
                if (fieldMap.TryGetValue(fId, out var fSym))

                    cluster.AccessedFields.Add(fSym);
            }

            report.Clusters.Add(cluster);
        }

        report.Lcom4 = report.Clusters.Count;

        // 4. TCC (Tight Class Cohesion): directly connected pairs / total pairs
        report.TightClassCohesion = totalPossiblePairs > 0
            ? Math.Clamp((double)directConnectedPairs / totalPossiblePairs, 0.0, 1.0)
            : 1.0;

        // 5. LCC (Loose Class Cohesion): transitively connected pairs (in the same connected component) / total pairs
        int reachablePairs = 0;
        foreach (var cluster in report.Clusters)
        {
            int size = cluster.Methods.Count;
            if (size > 1)

                reachablePairs += (size * (size - 1)) / 2;
        }

        report.LooseClassCohesion = totalPossiblePairs > 0
            ? Math.Clamp((double)reachablePairs / totalPossiblePairs, 0.0, 1.0)
            : 1.0;
    }

    /// <summary>
    /// Calculate the Chidamber & Kemerer metrics of coupling and complexity:
    /// - WMC (Weighted Methods per Class): cumulative complexity of the methods
    /// - CBO (Coupling Between Objects): number of coupled external classes
    /// - RFC (Response for a Class): number of own methods + called externally
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

        // 1. WMC calculation (sum of estimated complexities for each method)
        foreach (var r in routines)
        {
            int methodLoc = Math.Max(1, r.LineEnd - r.LineStart + 1);
            int estimatedComplexity = Math.Max(1, Math.Min(15, methodLoc / 10));
            totalWmc += estimatedComplexity;
        }

        // 2. Computing CBO and RFC by inspecting the call relations of the graph
        foreach (var rel in graph.Relationships)
        {
            if (routineIds.Contains(rel.SourceId))
            {
                if (rel.Kind == RelationshipKind.Calls)

                    responseMethods.Add(rel.TargetId);

                if (memberToContainerMap.TryGetValue(rel.TargetId, out var targetContId) && targetContId != container.Id)

                    externalContainersCoupled.Add(targetContId);
            }
            else if (rel.SourceId == container.Id)
            {
                if (memberToContainerMap.TryGetValue(rel.TargetId, out var targetContId) && targetContId != container.Id)

                    externalContainersCoupled.Add(targetContId);
            }
        }

        report.WeightedMethodsCount = Math.Max(report.MethodCount, totalWmc);
        report.CouplingBetweenObjects = externalContainersCoupled.Count;
        report.ResponseForClass = responseMethods.Count;
    }
}

