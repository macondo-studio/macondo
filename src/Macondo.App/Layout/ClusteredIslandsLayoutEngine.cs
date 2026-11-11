using Macondo.App.Models;
using Macondo.Semantic.Model;

namespace Macondo.App.Layout;

/// <summary>
/// Island Cluster layout engine (Functional Subsystems).
/// Identify highly cohesive module/class groups via connected component analysis and call density,
/// and places each group on its own autonomous "island" on the two-dimensional canvas.
/// </summary>
public sealed class ClusteredIslandsLayoutEngine : IGraphLayoutEngine
{
    public static readonly ClusteredIslandsLayoutEngine Instance = new();

    void IGraphLayoutEngine.Apply(VisualCodeGraph graph) => Apply(graph);

    /// <summary>
    /// Calculate and apply the coordinates (X, Y) to all the nodes of the visual graph by grouping the modules into autonomous functional islands.
    /// </summary>
    /// <param name="graph">The visual graph to position.</param>
    /// <param name="cancellationToken">Optional deletion token.</param>
    public static void Apply(VisualCodeGraph graph, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // 1. Retrieve all container nodes (classes, modules, forms, etc.)
        var containers = graph.Nodes.Where(n => n.IsContainer).ToList();
        if (containers.Count == 0) return;

        var projects = graph.Nodes.Where(n => n.Kind == SymbolKind.Project).ToList();

        // =========================================================================
        // STEP 1: Mapping node membership -> container
        // Maps both the containers themselves and their member nodes (methods, properties, fields)
        // to the ID of the respective parent container to be able to project edges between members.
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
        // STEP 2: Disjoint Set (Union-Find) Data Structure with Path Compression
        // Identify the connected components of the graph between different containers:
        // two modules belong to the same island if a semantic relationship exists
        // direct or indirect (call, instantiation, type used) between them.
        // =========================================================================
        var parent = containers.ToDictionary(c => c.Id, c => c.Id);

        // Recursive set root search function with path compression
        Guid FindRoot(Guid id)
        {
            if (parent[id] != id)
                parent[id] = FindRoot(parent[id]);
            return parent[id];
        }

        // Union of two disjoint sets
        void Union(Guid a, Guid b)
        {
            var rootA = FindRoot(a);
            var rootB = FindRoot(b);
            if (rootA != rootB)
                parent[rootB] = rootA;
        }

        // Merges containers connected by semantic edges in the graph
        foreach (var edge in graph.Edges)
        {
            if (nodeToContainerMap.TryGetValue(edge.SourceId, out var c1) &&
                nodeToContainerMap.TryGetValue(edge.TargetId, out var c2) &&
                c1 != c2)

                Union(c1, c2);
        }

        // =========================================================================
        // STEP 3: Grouping of containers into their respective islands/clusters
        // Sort islands in descending order by size (number of containers)
        // so as to place the most significant clusters first.
        // =========================================================================
        var containerById = containers.ToDictionary(c => c.Id);
        var islands = containers
            .GroupBy(c => FindRoot(c.Id))
            .Select(g => g.ToList())
            .OrderByDescending(list => list.Count)
            .ToList();

        var islandCount = islands.Count;

        // =========================================================================
        // STEP 4: Macro-geometric positioning of the island centroids
        // =========================================================================
        var macroRadius = Math.Max(500.0, islandCount * 140.0 + containers.Count * 25.0);

        for (var i = 0; i < islandCount; i++)
        {
            var islandContainers = islands[i];
            double islandCenterX = 0;
            double islandCenterY = 0;

            // If there are multiple islands, it distributes their centers along a macro-circumference;
            // if there is a single island, place the centroid at the origin (0, 0).
            if (islandCount > 1)
            {
                var islandAngle = 2 * Math.PI * i / islandCount;
                islandCenterX = Math.Cos(islandAngle) * macroRadius;
                islandCenterY = Math.Sin(islandAngle) * macroRadius;
            }

            var countInIsland = islandContainers.Count;
            var islandRadius = countInIsland == 1 ? 0 : Math.Max(160.0, countInIsland * 55.0);

            // =====================================================================
            // STEP 5: Positioning of the containers inside the single island
            // =====================================================================
            for (var j = 0; j < countInIsland; j++)
            {
                var container = islandContainers[j];
                if (countInIsland == 1)
                {
                    // Single container: positioned exactly in the center of the island
                    container.X = islandCenterX;
                    container.Y = islandCenterY;
                }
                else
                {
                    // Multi-containers: Arranged on a local ring around the island centroid
                    var angleInIsland = 2 * Math.PI * j / countInIsland;
                    container.X = islandCenterX + Math.Cos(angleInIsland) * islandRadius;
                    container.Y = islandCenterY + Math.Sin(angleInIsland) * islandRadius;
                }

                // =================================================================
                // STEP 6: Positioning satellite members around the container
                // =================================================================
                var members = graph.GetChildren(container.Id).ToList();
                var memberCount = Math.Max(members.Count, 1);
                var memberRingRadius = Math.Max(80.0, memberCount * 13.0);

                for (var mIdx = 0; mIdx < members.Count; mIdx++)
                {
                    var memberAngle = 2 * Math.PI * mIdx / memberCount;
                    var member = members[mIdx];
                    member.X = container.X + Math.Cos(memberAngle) * memberRingRadius;
                    member.Y = container.Y + Math.Sin(memberAngle) * memberRingRadius;
                }
            }
        }

        // =========================================================================
        // STEP 7: Positioning the Project nodes at the center of gravity of their modules
        // =========================================================================
        foreach (var proj in projects)
        {
            var projContainers = graph.GetChildren(proj.Id).ToList();
            if (projContainers.Count > 0)
            {
                proj.X = projContainers.Average(c => c.X);
                proj.Y = projContainers.Average(c => c.Y);
            }
            else
            {
                proj.X = 0;
                proj.Y = 0;
            }
        }
    }
}
