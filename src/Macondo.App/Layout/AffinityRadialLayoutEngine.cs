using Macondo.App.Models;
using Macondo.Semantic.Model;

namespace Macondo.App.Layout;

/// <summary>
/// Radial layout engine for semantic affinity.
/// Keeps the clean circular shape, but arranges the containers angularly around the circumference
/// based on the weight of mutual interactions (Calls, Instantiates, UsesType) to minimize
/// the connecting wires that run through the center of the graph.
/// </summary>
public sealed class AffinityRadialLayoutEngine : IGraphLayoutEngine
{
    public static readonly AffinityRadialLayoutEngine Instance = new();

    void IGraphLayoutEngine.Apply(VisualCodeGraph graph) => Apply(graph);

    /// <summary>
    /// Calculate and apply the coordinates (X, Y) to all the nodes of the visual graph by arranging the modules on a circle
    /// ordered by mutual interaction affinity (minimization of central intersections).
    /// </summary>
    /// <param name="graph">The visual graph to position.</param>
    /// <param name="cancellationToken">Optional deletion token.</param>
    public static void Apply(VisualCodeGraph graph, CancellationToken cancellationToken = default)
    {
        // 1. Retrieval of the projects present in the visual graph
        var projects = graph.Nodes.Where(n => n.Kind == SymbolKind.Project).ToList();
        if (projects.Count == 0) return;

        var isMultiProject = projects.Count > 1;
        var projectMacroRadius = Math.Max(1200.0, projects.Count * 700.0);

        // 2. Processing each project independently
        for (var pIdx = 0; pIdx < projects.Count; pIdx++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var project = projects[pIdx];
            if (isMultiProject)
            {
                // Arranges the different projects on a macro-circumference
                var projAngle = 2 * Math.PI * pIdx / projects.Count;
                project.X = Math.Cos(projAngle) * projectMacroRadius;
                project.Y = Math.Sin(projAngle) * projectMacroRadius;
            }
            else
            {
                // Single project positioned at the absolute center (0, 0)
                project.X = 0;
                project.Y = 0;
            }

            var containers = graph.GetChildren(project.Id).ToList();
            if (containers.Count == 0) continue;

            // =========================================================================
            // STEP 1: Node membership map -> parent container
            // It allows you to trace any arc that starts/arrives from an internal member
            // (e.g. a method or a property) to the module or container class that encloses it.
            // =========================================================================
            var nodeToContainerMap = new Dictionary<Guid, Guid>();
            foreach (var c in containers)
            {
                nodeToContainerMap[c.Id] = c.Id;
                foreach (var m in graph.GetChildren(c.Id))
                {
                    nodeToContainerMap[m.Id] = c.Id;
                }
            }

            // =========================================================================
            // STEP 2: Calculation of the weighted affinity matrix between pairs of containers
            // Assign differentiated weights to the semantics of relationships for reflection
            // the intensity of the structural coupling:
            // - Calls (3.0): direct behavioral pairing
            // - Instantiates (2.5): creation dependency
            // - UsesType (1.5): data type dependency
            // =========================================================================
            var affinity = new Dictionary<(Guid, Guid), double>();
            void AddAffinity(Guid c1, Guid c2, double weight)
            {
                if (c1 == c2) return; // Ignora le auto-relazioni interne allo stesso modulo
                var key = c1.CompareTo(c2) < 0 ? (c1, c2) : (c2, c1); // Chiave canonica simmetrica
                affinity[key] = affinity.GetValueOrDefault(key) + weight;
            }

            foreach (var edge in graph.Edges)
            {
                if (nodeToContainerMap.TryGetValue(edge.SourceId, out var srcCont) &&
                    nodeToContainerMap.TryGetValue(edge.TargetId, out var tgtCont))
                {
                    var weight = edge.Kind switch
                    {
                        RelationshipKind.Calls => 3.0,
                        RelationshipKind.Instantiates => 2.5,
                        RelationshipKind.UsesType => 1.5,
                        _ => 1.0
                    };
                    AddAffinity(srcCont, tgtCont, weight);
                }
            }

            // Calculate the total affinity score for each container and prepare adjacency lists in O(M)
            var totalAffinityByContainer = containers.ToDictionary(c => c.Id, _ => 0.0);
            var adjacency = containers.ToDictionary(c => c.Id, _ => new List<(Guid NeighborId, double Weight)>());

            foreach (var (key, weight) in affinity)
            {
                if (totalAffinityByContainer.ContainsKey(key.Item1))
                    totalAffinityByContainer[key.Item1] += weight;
                if (totalAffinityByContainer.ContainsKey(key.Item2))
                    totalAffinityByContainer[key.Item2] += weight;

                if (adjacency.TryGetValue(key.Item1, out var list1))
                    list1.Add((key.Item2, weight));
                if (adjacency.TryGetValue(key.Item2, out var list2))
                    list2.Add((key.Item1, weight));
            }

            // Pre-sort adjacency lists once by decreasing weight for instant lookup O(1)
            foreach (var list in adjacency.Values)
                list.Sort((a, b) => b.Weight.CompareTo(a.Weight));

            // =========================================================================
            // STEP 3: Greedy sorting by affinity contiguity (Nearest Neighbor / TSP heuristic)
            // The goal is to place frequently interacting modules in corner slots
            // adjacent along the circumference, drastically reducing the cables that pass through the center.
            // =========================================================================
            var unvisited = new HashSet<Guid>(containers.Select(c => c.Id));
            var orderedContainers = new List<GraphNode>();
            var containerById = containers.ToDictionary(c => c.Id);

            while (unvisited.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var currentId = unvisited.OrderByDescending(id => totalAffinityByContainer.GetValueOrDefault(id)).First();
                unvisited.Remove(currentId);
                orderedContainers.Add(containerById[currentId]);

                // It runs through the chain connecting each time the unvisited neighbor with the highest affinity via the adjacency list
                while (unvisited.Count > 0)
                {
                    Guid? bestNeighbor = null;
                    if (adjacency.TryGetValue(currentId, out var neighbors))
                    {
                        foreach (var (nbrId, _) in neighbors)
                        {
                            if (unvisited.Contains(nbrId))
                            {
                                bestNeighbor = nbrId;
                                break;
                            }
                        }
                    }

                    // If no remaining node has direct affinity with the current node, it stops the sub-path
                    if (bestNeighbor == null)
                        break;

                    currentId = bestNeighbor.Value;
                    unvisited.Remove(currentId);
                    orderedContainers.Add(containerById[currentId]);
                }
            }

            // =========================================================================
            // STEP 4: Circular geometric positioning with the optimized sequence
            // =========================================================================
            var containerCount = orderedContainers.Count;
            var containerRingRadius = Math.Max(380.0, containerCount * 35.0);

            for (var i = 0; i < containerCount; i++)
            {
                var angle = 2 * Math.PI * i / containerCount;
                var container = orderedContainers[i];
                container.X = project.X + Math.Cos(angle) * containerRingRadius;
                container.Y = project.Y + Math.Sin(angle) * containerRingRadius;

                // Arranges child members (routines, properties, fields) on satellite orbits around the container
                var members = graph.GetChildren(container.Id).ToList();
                var memberCount = Math.Max(members.Count, 1);
                var memberRingRadius = Math.Max(85.0, memberCount * 14.0);

                for (var j = 0; j < members.Count; j++)
                {
                    var memberAngle = 2 * Math.PI * j / memberCount;
                    var member = members[j];
                    member.X = container.X + Math.Cos(memberAngle) * memberRingRadius;
                    member.Y = container.Y + Math.Sin(memberAngle) * memberRingRadius;
                }
            }
        }
    }
}
