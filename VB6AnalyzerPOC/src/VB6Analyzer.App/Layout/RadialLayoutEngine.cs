using Macondo.App.Models;
using Macondo.Semantic.Model;

namespace Macondo.App.Layout;

/// <summary>
/// Motore di layout radiale/gerarchico standard per il CodeGraph visuale (sezioni 21 e 23 del charter).
/// Posiziona:
/// - Il nodo Progetto al centro (0, 0).
/// - I moduli, classi, form, UDT ed enum su un anello principale concentrico.
/// - I metodi, proprietà, campi e costanti su orbite satelliti attorno a ciascun modulo di appartenenza.
/// </summary>
public sealed class RadialLayoutEngine : IGraphLayoutEngine
{
    public static readonly RadialLayoutEngine Instance = new();

    void IGraphLayoutEngine.Apply(VisualCodeGraph graph) => Apply(graph);

    /// <summary>
    /// Calcola e applica le coordinate (X, Y) a tutti i nodi del grafo visuale in modalità radiale concentrica standard.
    /// </summary>
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
            var containerCount = Math.Max(containers.Count, 1);

            // Calcola il raggio dell'anello dei moduli in proporzione al numero di container
            var containerRingRadius = Math.Max(380.0, containerCount * 35.0);

            for (var i = 0; i < containers.Count; i++)
            {
                var angle = 2 * Math.PI * i / containerCount;
                var container = containers[i];
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
