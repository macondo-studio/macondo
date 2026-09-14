using FluentAssertions;
using Macondo.App.Layout;
using Macondo.App.Models;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.App;

public class GraphLayoutEngineTests
{
    private static VisualCodeGraph CreateSampleGraph(int nodeCount)
    {
        var graph = new VisualCodeGraph();
        for (int i = 0; i < nodeCount; i++)
        {
            var id = Guid.NewGuid();
            graph.Nodes.Add(new GraphNode
            {
                Id = id,
                Name = $"Node_{i}",
                QualifiedName = $"Module.Node_{i}",
                Kind = i == 0 ? SymbolKind.Module : SymbolKind.Method,
                X = 0,
                Y = 0
            });
        }

        if (nodeCount > 1)
        {
            for (int i = 1; i < nodeCount; i++)
            {
                graph.Edges.Add(new GraphEdge
                {
                    SourceId = graph.Nodes[0].Id,
                    TargetId = graph.Nodes[i].Id,
                    Kind = RelationshipKind.Calls
                });
            }
        }

        return graph;
    }

    [Theory]
    [InlineData(GraphLayoutMode.StandardRadial)]
    [InlineData(GraphLayoutMode.AffinityRadial)]
    [InlineData(GraphLayoutMode.ClusteredIslands)]
    [InlineData(GraphLayoutMode.ForceDirected)]
    public void LayoutEngines_ShouldHandleEmptyGraph_WithoutErrors(GraphLayoutMode mode)
    {
        var graph = new VisualCodeGraph();

        var act = () => GraphLayoutManager.ApplyLayout(graph, mode);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(GraphLayoutMode.StandardRadial)]
    [InlineData(GraphLayoutMode.AffinityRadial)]
    [InlineData(GraphLayoutMode.ClusteredIslands)]
    [InlineData(GraphLayoutMode.ForceDirected)]
    public void LayoutEngines_ShouldHandleSingleNode_WithoutErrors(GraphLayoutMode mode)
    {
        var graph = CreateSampleGraph(1);

        var act = () => GraphLayoutManager.ApplyLayout(graph, mode);
        act.Should().NotThrow();

        graph.Nodes[0].X.Should().NotBe(double.NaN);
        graph.Nodes[0].Y.Should().NotBe(double.NaN);
        double.IsInfinity(graph.Nodes[0].X).Should().BeFalse();
        double.IsInfinity(graph.Nodes[0].Y).Should().BeFalse();
    }

    [Theory]
    [InlineData(GraphLayoutMode.StandardRadial)]
    [InlineData(GraphLayoutMode.AffinityRadial)]
    [InlineData(GraphLayoutMode.ClusteredIslands)]
    [InlineData(GraphLayoutMode.ForceDirected)]
    public void LayoutEngines_ShouldGenerateValidCoordinates_ForMultipleNodes(GraphLayoutMode mode)
    {
        var graph = CreateSampleGraph(10);

        GraphLayoutManager.ApplyLayout(graph, mode);

        foreach (var node in graph.Nodes)
        {
            node.X.Should().NotBe(double.NaN);
            node.Y.Should().NotBe(double.NaN);
            double.IsInfinity(node.X).Should().BeFalse();
            double.IsInfinity(node.Y).Should().BeFalse();
        }
    }
}
