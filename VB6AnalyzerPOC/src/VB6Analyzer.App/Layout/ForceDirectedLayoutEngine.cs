using Macondo.App.Models;
using Macondo.Semantic.Model;

namespace Macondo.App.Layout;

/// <summary>
/// Motore di layout Force-Directed basato su modello fisico (Fruchterman-Reingold).
/// Applica forze repulsive tra tutti i container per evitare sovrapposizioni e forze attrattive elastiche
/// lungo le chiamate e relazioni semantiche, disponendo organicamente i nodi con convergenza rapida.
/// </summary>
public sealed class ForceDirectedLayoutEngine : IGraphLayoutEngine
{
    public static readonly ForceDirectedLayoutEngine Instance = new();

    void IGraphLayoutEngine.Apply(VisualCodeGraph graph) => Apply(graph);

    public static void Apply(VisualCodeGraph graph)
    {
        var project = graph.Nodes.FirstOrDefault(n => n.Kind == SymbolKind.Project);
        if (project is null) return;

        project.X = 0;
        project.Y = 0;

        var containers = graph.GetChildren(project.Id).ToList();
        if (containers.Count == 0) return;

        var n = containers.Count;
        var nodeToContainerMap = new Dictionary<Guid, Guid>();
        foreach (var c in containers)
        {
            nodeToContainerMap[c.Id] = c.Id;
            foreach (var m in graph.GetChildren(c.Id))
            {
                nodeToContainerMap[m.Id] = c.Id;
            }
        }

        // Posizionamento iniziale su cerchio per stabilità
        var initialRadius = Math.Max(350.0, n * 30.0);
        for (var i = 0; i < n; i++)
        {
            var angle = 2 * Math.PI * i / n;
            containers[i].X = Math.Cos(angle) * initialRadius;
            containers[i].Y = Math.Sin(angle) * initialRadius;
        }

        // Parametri di simulazione Fruchterman-Reingold
        var optimalDistance = Math.Max(220.0, Math.Sqrt((1200.0 * 1200.0) / Math.Max(n, 1)));
        var k = optimalDistance;
        var kSquared = k * k;

        var iterations = Math.Min(100, Math.Max(40, n * 2));
        var initialTemp = initialRadius * 0.4;
        var temp = initialTemp;
        var coolingFactor = 0.95;

        var dispX = new double[n];
        var dispY = new double[n];

        // Mappa archi tra container con pesi
        var containerEdges = new List<(int SrcIdx, int TgtIdx, double Weight)>();
        var containerIndexById = containers.Select((c, idx) => (c.Id, idx)).ToDictionary(x => x.Id, x => x.idx);

        foreach (var edge in graph.Edges)
        {
            if (nodeToContainerMap.TryGetValue(edge.SourceId, out var c1) &&
                nodeToContainerMap.TryGetValue(edge.TargetId, out var c2) &&
                c1 != c2 &&
                containerIndexById.TryGetValue(c1, out var idx1) &&
                containerIndexById.TryGetValue(c2, out var idx2))
            {
                var w = edge.Kind switch
                {
                    RelationshipKind.Calls => 3.0,
                    RelationshipKind.Instantiates => 2.5,
                    RelationshipKind.UsesType => 1.5,
                    _ => 1.0
                };
                containerEdges.Add((idx1, idx2, w));
            }
        }

        // Loop di simulazione fisica
        for (var iter = 0; iter < iterations; iter++)
        {
            Array.Clear(dispX, 0, n);
            Array.Clear(dispY, 0, n);

            // 1. Forze repulsive tra tutti i nodi container
            for (var i = 0; i < n; i++)
            {
                var c1 = containers[i];
                for (var j = i + 1; j < n; j++)
                {
                    var c2 = containers[j];
                    var dx = c1.X - c2.X;
                    var dy = c1.Y - c2.Y;
                    var dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist < 0.001)
                    {
                        dx = (i + 1) * 0.1;
                        dy = (j + 1) * 0.1;
                        dist = Math.Sqrt(dx * dx + dy * dy);
                    }

                    var repForce = kSquared / dist;
                    var fx = (dx / dist) * repForce;
                    var fy = (dy / dist) * repForce;

                    dispX[i] += fx;
                    dispY[i] += fy;
                    dispX[j] -= fx;
                    dispY[j] -= fy;
                }
            }

            // 2. Forze attrattive lungo gli archi semantici
            foreach (var (srcIdx, tgtIdx, weight) in containerEdges)
            {
                var c1 = containers[srcIdx];
                var c2 = containers[tgtIdx];
                var dx = c1.X - c2.X;
                var dy = c1.Y - c2.Y;
                var dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist < 0.001) continue;

                var attForce = (dist * dist / k) * weight;
                var fx = (dx / dist) * attForce;
                var fy = (dy / dist) * attForce;

                dispX[srcIdx] -= fx;
                dispY[srcIdx] -= fy;
                dispX[tgtIdx] += fx;
                dispY[tgtIdx] += fy;
            }

            // 3. Gravità centrale verso l'origine (0, 0)
            for (var i = 0; i < n; i++)
            {
                var c = containers[i];
                var dist = Math.Sqrt(c.X * c.X + c.Y * c.Y);
                if (dist > 1.0)
                {
                    dispX[i] -= (c.X / dist) * (dist * 0.05);
                    dispY[i] -= (c.Y / dist) * (dist * 0.05);
                }
            }

            // 4. Applicazione degli spostamenti limitati dalla temperatura
            for (var i = 0; i < n; i++)
            {
                var dLen = Math.Sqrt(dispX[i] * dispX[i] + dispY[i] * dispY[i]);
                if (dLen > 0.001)
                {
                    var step = Math.Min(dLen, temp);
                    containers[i].X += (dispX[i] / dLen) * step;
                    containers[i].Y += (dispY[i] / dLen) * step;
                }
            }

            // Raffreddamento
            temp *= coolingFactor;
        }

        // 5. Posizionamento dei membri attorno ai rispettivi container
        foreach (var container in containers)
        {
            var members = graph.GetChildren(container.Id).ToList();
            var memberCount = Math.Max(members.Count, 1);
            var memberRingRadius = Math.Max(80.0, memberCount * 13.0);

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
