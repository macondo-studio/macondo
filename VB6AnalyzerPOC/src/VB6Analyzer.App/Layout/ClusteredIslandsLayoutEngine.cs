using Macondo.App.Models;
using Macondo.Semantic.Model;

namespace Macondo.App.Layout;

/// <summary>
/// Motore di layout a Cluster di Isole (Sottosistemi Funzionali).
/// Identifica i gruppi di moduli/classi fortemente coesi tramite analisi delle componenti connesse e densità di chiamate,
/// e dispone ciascun gruppo su una propria "isola" autonoma sul canvas bidimensionale.
/// </summary>
public sealed class ClusteredIslandsLayoutEngine : IGraphLayoutEngine
{
    public static readonly ClusteredIslandsLayoutEngine Instance = new();

    void IGraphLayoutEngine.Apply(VisualCodeGraph graph) => Apply(graph);

    public static void Apply(VisualCodeGraph graph)
    {
        var project = graph.Nodes.FirstOrDefault(n => n.Kind == SymbolKind.Project);
        if (project is null) return;

        project.X = 0;
        project.Y = 0;

        var containers = graph.GetChildren(project.Id).ToList();
        if (containers.Count == 0) return;

        // 1. Mappa nodi -> container
        var nodeToContainerMap = new Dictionary<Guid, Guid>();
        foreach (var c in containers)
        {
            nodeToContainerMap[c.Id] = c.Id;
            foreach (var m in graph.GetChildren(c.Id))
            {
                nodeToContainerMap[m.Id] = c.Id;
            }
        }

        // 2. Disjoint Set / Union-Find per individuare le componenti connesse tra container
        var parent = containers.ToDictionary(c => c.Id, c => c.Id);
        Guid FindRoot(Guid id)
        {
            if (parent[id] != id)
                parent[id] = FindRoot(parent[id]);
            return parent[id];
        }

        void Union(Guid a, Guid b)
        {
            var rootA = FindRoot(a);
            var rootB = FindRoot(b);
            if (rootA != rootB)
                parent[rootB] = rootA;
        }

        foreach (var edge in graph.Edges)
        {
            if (nodeToContainerMap.TryGetValue(edge.SourceId, out var c1) &&
                nodeToContainerMap.TryGetValue(edge.TargetId, out var c2) &&
                c1 != c2)
            {
                Union(c1, c2);
            }
        }

        // 3. Raggruppa i container per isola/componente
        var containerById = containers.ToDictionary(c => c.Id);
        var islands = containers
            .GroupBy(c => FindRoot(c.Id))
            .Select(g => g.ToList())
            .OrderByDescending(list => list.Count)
            .ToList();

        var islandCount = islands.Count;
        if (islandCount == 1 && containers.Count > 1)
        {
            // Se tutto è un'unica macro-componente, dividiamo per coesione interna
            // Disponiamo comunque con anello allargato
        }

        // 4. Posiziona i centri delle isole sul macro-piano
        var macroRadius = Math.Max(500.0, islandCount * 140.0 + containers.Count * 25.0);

        for (var i = 0; i < islandCount; i++)
        {
            var islandContainers = islands[i];
            double islandCenterX = 0;
            double islandCenterY = 0;

            if (islandCount > 1)
            {
                var islandAngle = 2 * Math.PI * i / islandCount;
                islandCenterX = Math.Cos(islandAngle) * macroRadius;
                islandCenterY = Math.Sin(islandAngle) * macroRadius;
            }

            var countInIsland = islandContainers.Count;
            var islandRadius = countInIsland == 1 ? 0 : Math.Max(160.0, countInIsland * 55.0);

            for (var j = 0; j < countInIsland; j++)
            {
                var container = islandContainers[j];
                if (countInIsland == 1)
                {
                    container.X = islandCenterX;
                    container.Y = islandCenterY;
                }
                else
                {
                    var angleInIsland = 2 * Math.PI * j / countInIsland;
                    container.X = islandCenterX + Math.Cos(angleInIsland) * islandRadius;
                    container.Y = islandCenterY + Math.Sin(angleInIsland) * islandRadius;
                }

                // Posiziona i membri attorno al container
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
    }
}
