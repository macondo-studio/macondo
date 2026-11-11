using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.Treemap;

/// <summary>
/// Treemap model builder from any semantic CodeGraph.
/// </summary>
public static class TreemapModelBuilder
{
    /// <summary>
    /// Builds the <see cref="TreemapItem"/> hierarchical tree at two levels (Project -> Modules -> Members) by aggregating LOC and complexity.
    /// </summary>
    /// <param name="graph">The CodeGraph from which to extract the symbols.</param>
    /// <returns>The root node of the Treemap ready for layout calculation.</returns>
    public static TreemapItem Build(CodeGraph graph)
    {
        // 1. Global root node of the entire project
        var root = new TreemapItem
        {
            Id = Guid.Empty,
            Name = "Root",
            QualifiedName = "Project",
            Kind = SymbolKind.Project,
            Depth = 0
        };

        // 2. Collects all primary containers (Classes, Forms, Modules, UserControl, UDT, Interfaces, Structures, Records)
        var containers = graph.Symbols
            .Where(s => s.Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.Module or SymbolKind.UserControl or SymbolKind.Type or SymbolKind.Interface or SymbolKind.Struct or SymbolKind.Record)
            .OrderBy(c => c.Name)
            .ToList();

        if (containers.Count == 0)

            containers = graph.Symbols.Where(s => s.Kind != SymbolKind.Project).ToList();

        // O(1) indexing of outgoing calls for each source symbol to avoid O(R) scans in the loop
        var outgoingCallsCount = new Dictionary<Guid, int>();
        foreach (var r in graph.Relationships)
        {
            if (r.Kind == RelationshipKind.Calls)

                outgoingCallsCount[r.SourceId] = outgoingCallsCount.GetValueOrDefault(r.SourceId) + 1;
        }

        // 3. Constructs the child tree and calculates the weight (estimated LOC) and complexity metrics
        foreach (var container in containers)
        {
            var containerItem = new TreemapItem
            {
                Id = container.Id,
                Name = container.Name,
                QualifiedName = container.QualifiedName,
                Kind = container.Kind,
                Depth = 1,
                Parent = root
            };

            var children = graph.GetChildren(container.Id)
                .Where(c => c.Kind is SymbolKind.Method or SymbolKind.Property or SymbolKind.Field)
                .ToList();

            if (children.Count > 0)
            {
                foreach (var child in children)
                {
                    // LOC calculation or weight based on lines of code or call complexity in O(1)
                    int lineCount = Math.Max(1, child.LineEnd - child.LineStart + 1);
                    outgoingCallsCount.TryGetValue(child.Id, out int callees);
                    double complexityScore = 1.0 + (callees * 1.5) + (child.Parameters.Count * 0.8);

                    var childItem = new TreemapItem
                    {
                        Id = child.Id,
                        Name = child.Name,
                        QualifiedName = child.QualifiedName,
                        Kind = child.Kind,
                        Value = lineCount,
                        MetricScore = complexityScore,
                        Depth = 2,
                        Parent = containerItem
                    };

                    containerItem.Children.Add(childItem);
                }

                containerItem.Value = containerItem.Children.Sum(c => c.Value);
                containerItem.MetricScore = containerItem.Children.Average(c => c.MetricScore);
            }
            else
            {
                int lineCount = Math.Max(1, container.LineEnd - container.LineStart + 1);
                containerItem.Value = lineCount;
                containerItem.MetricScore = 1.0;
            }

            root.Children.Add(containerItem);
        }

        // Aggregate the totals over the root
        root.Value = root.Children.Sum(c => c.Value);
        root.MetricScore = root.Children.Count > 0 ? root.Children.Average(c => c.MetricScore) : 1.0;

        return root;
    }
}
