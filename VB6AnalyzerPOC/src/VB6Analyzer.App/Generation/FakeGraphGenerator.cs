using Macondo.App.Layout;
using Macondo.App.Models;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;

namespace Macondo.App.Generation;

/// <summary>
/// Generatore di grafi sintetici/fittizi per test di carico, benchmark UI e modalità demo (sezione 41 del charter).
/// Produce una struttura gerarchica realistica (~500 nodi) con Progetto, Form, Classi, Moduli, Metodi,
/// Proprietà, Campi e relazioni semantiche di chiamata (CALLS), contenimento (CONTAINS) e istanziazione (INSTANTIATES).
/// </summary>
public static class FakeGraphGenerator
{
    /// <summary>
    /// Genera un <see cref="VisualCodeGraph"/> sintetico con un numero configurabile di container e metodi.
    /// </summary>
    /// <param name="seed">Seed del generatore di numeri casuali (per riproducibilità deterministica).</param>
    /// <param name="containerCount">Numero di classi/form/moduli da generare (default 24).</param>
    /// <param name="methodsPerContainer">Numero medio di metodi/membri per ciascun container (default 18).</param>
    public static VisualCodeGraph Generate(int seed = 42, int containerCount = 24, int methodsPerContainer = 18)
    {
        var random = new Random(seed);
        var semanticGraph = new Semantic.Model.CodeGraph();
        var visualGraph = new VisualCodeGraph
        {
            SemanticGraph = semanticGraph,
            Queries = new CallGraphQueries(semanticGraph)
        };

        // 1. Nodo Radice di Progetto
        var projectNode = new GraphNode
        {
            Id = Guid.NewGuid(),
            Name = "SyntheticProject",
            QualifiedName = "SyntheticProject",
            Kind = SymbolKind.Project,
            IsExpanded = true
        };
        visualGraph.Nodes.Add(projectNode);

        var containerNodes = new List<GraphNode>();
        var methodNodes = new List<GraphNode>();

        var containerPrefixes = new[] { "frmOrder", "frmCustomer", "frmInvoice", "frmReport", "frmLogin", "frmMain",
                                        "clsCustomer", "clsOrder", "clsInvoice", "clsPayment", "clsSecurity", "clsValidation",
                                        "modDatabase", "modUtils", "modGlobals", "modLogger", "modExport", "modConfig" };

        var verbList = new[] { "Process", "Validate", "Calculate", "Execute", "Save", "Load", "Delete",
                               "Format", "Log", "Init", "Reset", "Export", "Import", "Update", "Check" };

        var nounList = new[] { "Data", "Order", "Customer", "Invoice", "Record", "Report", "Transaction", "Item", "Status", "Total" };

        // 2. Genera i Container (Form, Class, Module)
        for (var i = 0; i < containerCount; i++)
        {
            var baseName = i < containerPrefixes.Length
                ? containerPrefixes[i]
                : $"Module_{i + 1}";

            var kind = baseName.StartsWith("frm")
                ? SymbolKind.Form
                : (baseName.StartsWith("cls") ? SymbolKind.Class : SymbolKind.Module);

            var containerSymbol = new Symbol
            {
                Id = Guid.NewGuid(),
                Name = baseName,
                QualifiedName = baseName,
                Kind = kind,
                Accessibility = Accessibility.Public,
                FilePath = $"{baseName}.{(kind == SymbolKind.Form ? "frm" : (kind == SymbolKind.Class ? "cls" : "bas"))}",
                LineStart = 1,
                LineEnd = 100
            };
            semanticGraph.Symbols.Add(containerSymbol);

            var containerNode = new GraphNode
            {
                Id = containerSymbol.Id,
                Name = baseName,
                QualifiedName = baseName,
                Kind = kind,
                SemanticSymbol = containerSymbol,
                ParentId = projectNode.Id,
                IsExpanded = true
            };

            containerNodes.Add(containerNode);
            visualGraph.Nodes.Add(containerNode);

            // Relazione Contains: Project -> Container
            visualGraph.Edges.Add(new GraphEdge
            {
                SourceId = projectNode.Id,
                TargetId = containerNode.Id,
                Kind = RelationshipKind.Contains,
                Confidence = ResolutionConfidence.Exact
            });

            // 3. Genera i membri interni (Metodi e Proprietà)
            for (var m = 0; m < methodsPerContainer; m++)
            {
                var verb = verbList[random.Next(verbList.Length)];
                var noun = nounList[random.Next(nounList.Length)];
                var methodName = $"{verb}{noun}_{m + 1}";
                var memberKind = (m % 5 == 0) ? SymbolKind.Property : SymbolKind.Method;

                var memberSymbol = new Symbol
                {
                    Id = Guid.NewGuid(),
                    Name = methodName,
                    QualifiedName = $"{baseName}.{methodName}",
                    Kind = memberKind,
                    Accessibility = (m % 3 == 0) ? Accessibility.Private : Accessibility.Public,
                    FilePath = containerSymbol.FilePath,
                    LineStart = 10 + m * 8,
                    LineEnd = 10 + m * 8 + 6
                };
                semanticGraph.Symbols.Add(memberSymbol);
                semanticGraph.AddContains(containerSymbol, memberSymbol, memberSymbol.LineStart);

                var methodNode = new GraphNode
                {
                    Id = memberSymbol.Id,
                    Name = methodName,
                    QualifiedName = $"{baseName}.{methodName}",
                    Kind = memberKind,
                    SemanticSymbol = memberSymbol,
                    ParentId = containerNode.Id,
                    IsExpanded = false
                };

                methodNodes.Add(methodNode);
                visualGraph.Nodes.Add(methodNode);

                // Relazione Contains: Container -> Member
                visualGraph.Edges.Add(new GraphEdge
                {
                    SourceId = containerNode.Id,
                    TargetId = methodNode.Id,
                    Kind = RelationshipKind.Contains,
                    Confidence = ResolutionConfidence.Exact,
                    Line = memberSymbol.LineStart
                });
            }
        }

        // 4. Genera chiamate tra routine (CALLS)
        var totalCalls = containerCount * methodsPerContainer * 2;
        for (var c = 0; c < totalCalls; c++)
        {
            var caller = methodNodes[random.Next(methodNodes.Count)];
            var callee = methodNodes[random.Next(methodNodes.Count)];

            // Evita auto-chiamate
            if (caller.Id == callee.Id) continue;

            var line = random.Next(1, 80);

            if (caller.SemanticSymbol != null && callee.SemanticSymbol != null)
            {
                semanticGraph.AddCalls(caller.SemanticSymbol, callee.SemanticSymbol, line, ResolutionConfidence.Exact);
            }

            visualGraph.Edges.Add(new GraphEdge
            {
                SourceId = caller.Id,
                TargetId = callee.Id,
                Kind = RelationshipKind.Calls,
                Confidence = ResolutionConfidence.Exact,
                Line = line
            });
        }

        // 5. Genera istanziazioni (INSTANTIATES)
        var classContainers = containerNodes.Where(n => n.Kind == SymbolKind.Class).ToList();
        if (classContainers.Count > 0)
        {
            for (var inst = 0; inst < containerCount; inst++)
            {
                var sourceMethod = methodNodes[random.Next(methodNodes.Count)];
                var targetClass = classContainers[random.Next(classContainers.Count)];

                if (sourceMethod.SemanticSymbol != null && targetClass.SemanticSymbol != null)
                {
                    semanticGraph.AddInstantiates(sourceMethod.SemanticSymbol, targetClass.SemanticSymbol, 15);
                }

                visualGraph.Edges.Add(new GraphEdge
                {
                    SourceId = sourceMethod.Id,
                    TargetId = targetClass.Id,
                    Kind = RelationshipKind.Instantiates,
                    Confidence = ResolutionConfidence.Exact,
                    Line = 15
                });
            }
        }

        // 6. Calcola il layout radiale e finalizza il grafo
        RadialLayoutEngine.Apply(visualGraph);
        visualGraph.Invalidate();

        return visualGraph;
    }
}
