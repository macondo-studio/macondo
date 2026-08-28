using VB6Analyzer.App.Models;

namespace VB6Analyzer.App.Layout;

/// <summary>
/// Layout radiale/gerarchico semplice: il Project al centro, i container
/// (Form/Class/Module) disposti su un anello, i loro Method su anelli più
/// piccoli attorno a ciascun container.
///
/// Deliberatamente semplice per il POC (sezione 23 elenca alternative future:
/// Sugiyama, force-directed, dependency layout). Qui l'obiettivo è solo
/// verificare che GraphControl possa disegnare/pan/zoom/selezionare ~500 nodi
/// con performance accettabile, non produrre un layout "bello".
/// </summary>
public static class RadialLayoutEngine
{
    public static void Apply(CodeGraph graph, double containerRingRadius = 420, double methodRingRadius = 90)
    {
        var project = graph.Nodes.FirstOrDefault(n => n.Kind == NodeKind.Project);
        if (project is null) return;

        project.X = 0;
        project.Y = 0;

        var containers = graph.GetChildren(project.Id).ToList();
        var containerCount = Math.Max(containers.Count, 1);

        for (var i = 0; i < containers.Count; i++)
        {
            var angle = 2 * Math.PI * i / containerCount;
            var container = containers[i];
            container.X = Math.Cos(angle) * containerRingRadius;
            container.Y = Math.Sin(angle) * containerRingRadius;

            var methods = graph.GetChildren(container.Id).ToList();
            var methodCount = Math.Max(methods.Count, 1);

            for (var j = 0; j < methods.Count; j++)
            {
                var methodAngle = 2 * Math.PI * j / methodCount;
                var method = methods[j];
                method.X = container.X + Math.Cos(methodAngle) * methodRingRadius;
                method.Y = container.Y + Math.Sin(methodAngle) * methodRingRadius;
            }
        }
    }
}
