using VB6Analyzer.App.Models;

namespace VB6Analyzer.App.Generation;

/// <summary>
/// Genera un CodeGraph fittizio per validare rendering/layout/interazione
/// del GraphControl (sezione 41, punti 2-3: "creare un CodeGraph fittizio,
/// generare ~500 nodi"). NON fa parsing VB6: quello è compito del vero
/// Analyzer (M1-M4), fuori dallo scope di questo POC.
/// </summary>
public static class FakeGraphGenerator
{
    private static readonly string[] ContainerPrefixes =
    {
        "frm", "cls", "mod", "ctl"
    };

    private static readonly string[] NameFragments =
    {
        "Order", "Customer", "Invoice", "Payment", "Repository", "Service",
        "Validator", "Logger", "Config", "Report", "User", "Session",
        "Database", "Export", "Import", "Print", "Search", "Cache"
    };

    private static readonly string[] MethodVerbs =
    {
        "Save", "Load", "Validate", "Insert", "Update", "Delete", "Calculate",
        "Refresh", "Init", "Close", "Open", "Execute", "Build", "Render",
        "Parse", "Format", "Check", "Notify", "Handle", "Process"
    };

    /// <summary>
    /// Genera un CodeGraph fittizio. containerCount controlla il numero di
    /// Form/Class/Module; methodsPerContainerRange controlla i figli di ciascuno.
    /// Con i default si ottengono circa 500 nodi complessivi.
    /// </summary>
    public static CodeGraph Generate(
        int containerCount = 18,
        (int min, int max) methodsPerContainerRange = default,
        int callEdgesPerMethod = 2,
        int? seed = null)
    {
        if (methodsPerContainerRange == default)
            methodsPerContainerRange = (40, 50);

        var random = seed.HasValue ? new Random(seed.Value) : new Random();
        var graph = new CodeGraph();

        var project = new GraphNode
        {
            Id = Guid.NewGuid(),
            Name = "SampleVB6Project",
            Kind = NodeKind.Project,
            ParentId = null,
            IsExpanded = true
        };
        graph.Nodes.Add(project);

        var allMethods = new List<GraphNode>();

        for (var i = 0; i < containerCount; i++)
        {
            var kind = PickContainerKind(i);
            var container = new GraphNode
            {
                Id = Guid.NewGuid(),
                Name = MakeContainerName(kind, random),
                Kind = kind,
                ParentId = project.Id,
                IsExpanded = false
            };
            graph.Nodes.Add(container);

            graph.Edges.Add(new GraphEdge
            {
                SourceId = project.Id,
                TargetId = container.Id,
                Kind = EdgeKind.Contains
            });

            var methodCount = random.Next(methodsPerContainerRange.min, methodsPerContainerRange.max + 1);
            for (var m = 0; m < methodCount; m++)
            {
                var method = new GraphNode
                {
                    Id = Guid.NewGuid(),
                    Name = MakeMethodName(random),
                    Kind = NodeKind.Method,
                    ParentId = container.Id
                };
                graph.Nodes.Add(method);
                allMethods.Add(method);

                graph.Edges.Add(new GraphEdge
                {
                    SourceId = container.Id,
                    TargetId = method.Id,
                    Kind = EdgeKind.Contains
                });
            }
        }

        // CALLS fittizi tra metodi, per avere un call graph da esplorare
        // (sezione 16). Nessun significato semantico reale: solo dati di prova.
        foreach (var method in allMethods)
        {
            var callCount = random.Next(0, callEdgesPerMethod + 1);
            for (var c = 0; c < callCount; c++)
            {
                var target = allMethods[random.Next(allMethods.Count)];
                if (target.Id == method.Id) continue;

                graph.Edges.Add(new GraphEdge
                {
                    SourceId = method.Id,
                    TargetId = target.Id,
                    Kind = EdgeKind.Calls
                });
            }
        }

        graph.Invalidate();
        return graph;
    }

    private static NodeKind PickContainerKind(int index) => (index % 3) switch
    {
        0 => NodeKind.Form,
        1 => NodeKind.Class,
        _ => NodeKind.Module
    };

    private static string MakeContainerName(NodeKind kind, Random random)
    {
        var prefix = kind switch
        {
            NodeKind.Form => "frm",
            NodeKind.Class => "cls",
            NodeKind.Module => "mod",
            _ => "ctl"
        };
        var fragment = NameFragments[random.Next(NameFragments.Length)];
        return $"{prefix}{fragment}";
    }

    private static string MakeMethodName(Random random)
    {
        var verb = MethodVerbs[random.Next(MethodVerbs.Length)];
        var fragment = NameFragments[random.Next(NameFragments.Length)];
        return $"{verb}{fragment}";
    }
}
