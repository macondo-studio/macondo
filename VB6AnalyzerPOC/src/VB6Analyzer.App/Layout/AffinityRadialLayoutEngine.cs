using Macondo.App.Models;
using Macondo.Semantic.Model;

namespace Macondo.App.Layout;

/// <summary>
/// Motore di layout radiale per affinità semantica.
/// Mantiene la forma circolare pulita, ma ordina angolarmente i container lungo la circonferenza
/// in base al peso delle interazioni reciproche (Calls, Instantiates, UsesType) per minimizzare
/// i cavi di connessione che attraversano il centro del grafo.
/// </summary>
public sealed class AffinityRadialLayoutEngine : IGraphLayoutEngine
{
    public static readonly AffinityRadialLayoutEngine Instance = new();

    void IGraphLayoutEngine.Apply(VisualCodeGraph graph) => Apply(graph);

    public static void Apply(VisualCodeGraph graph)
    {
        var projects = graph.Nodes.Where(n => n.Kind == SymbolKind.Project).ToList();
        if (projects.Count == 0) return;

        var isMultiProject = projects.Count > 1;
        var projectMacroRadius = Math.Max(1200.0, projects.Count * 700.0);

        for (var pIdx = 0; pIdx < projects.Count; pIdx++)
        {
            var project = projects[pIdx];
            if (isMultiProject)
            {
                var projAngle = 2 * Math.PI * pIdx / projects.Count;
                project.X = Math.Cos(projAngle) * projectMacroRadius;
                project.Y = Math.Sin(projAngle) * projectMacroRadius;
            }
            else
            {
                project.X = 0;
                project.Y = 0;
            }

            var containers = graph.GetChildren(project.Id).ToList();
            if (containers.Count == 0) continue;

            // 1. Mappa di appartenenza: da qualsiasi nodo membro/container al suo ID container di appartenenza
            var nodeToContainerMap = new Dictionary<Guid, Guid>();
            foreach (var c in containers)
            {
                nodeToContainerMap[c.Id] = c.Id;
                foreach (var m in graph.GetChildren(c.Id))
                {
                    nodeToContainerMap[m.Id] = c.Id;
                }
            }

            // 2. Matrice delle affinità tra container
            var affinity = new Dictionary<(Guid, Guid), double>();
            void AddAffinity(Guid c1, Guid c2, double weight)
            {
                if (c1 == c2) return;
                var key = c1.CompareTo(c2) < 0 ? (c1, c2) : (c2, c1);
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

            // Calcola l'affinità totale di ciascun container
            var totalAffinityByContainer = containers.ToDictionary(
                c => c.Id,
                c => affinity.Where(kv => kv.Key.Item1 == c.Id || kv.Key.Item2 == c.Id).Sum(kv => kv.Value)
            );

            // 3. Ordinamento greedy per contiguità di affinità (Greedy TSP path / Nearest Neighbor)
            var unvisited = new HashSet<Guid>(containers.Select(c => c.Id));
            var orderedContainers = new List<GraphNode>();

            var containerById = containers.ToDictionary(c => c.Id);

            while (unvisited.Count > 0)
            {
                // Inizia un nuovo blocco/componente dal nodo non visitato con la massima affinità
                var currentId = unvisited.OrderByDescending(id => totalAffinityByContainer.GetValueOrDefault(id)).First();
                unvisited.Remove(currentId);
                orderedContainers.Add(containerById[currentId]);

                while (unvisited.Count > 0)
                {
                    // Cerca il vicino non visitato con la massima affinità verso il nodo corrente
                    var nextNeighbor = unvisited
                        .Select(candId =>
                        {
                            var key = currentId.CompareTo(candId) < 0 ? (currentId, candId) : (candId, currentId);
                            var aff = affinity.GetValueOrDefault(key, 0.0);
                            return new { Id = candId, Affinity = aff };
                        })
                        .Where(x => x.Affinity > 0)
                        .OrderByDescending(x => x.Affinity)
                        .FirstOrDefault();

                    if (nextNeighbor == null)
                        break; // Nessun altro nodo connesso direttamente a questo cluster

                    currentId = nextNeighbor.Id;
                    unvisited.Remove(currentId);
                    orderedContainers.Add(containerById[currentId]);
                }
            }

            // 4. Posizionamento geometrico circolare con l'ordine ottimizzato
            var containerCount = orderedContainers.Count;
            var containerRingRadius = Math.Max(380.0, containerCount * 35.0);

            for (var i = 0; i < containerCount; i++)
            {
                var angle = 2 * Math.PI * i / containerCount;
                var container = orderedContainers[i];
                container.X = project.X + Math.Cos(angle) * containerRingRadius;
                container.Y = project.Y + Math.Sin(angle) * containerRingRadius;

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
