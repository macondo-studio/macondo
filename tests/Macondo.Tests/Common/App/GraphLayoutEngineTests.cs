using FluentAssertions;
using Macondo.App.Layout;
using Macondo.App.Models;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.App;

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

    /// <summary>
    /// Verifica che tutti i motori di layout (StandardRadial, AffinityRadial, ClusteredIslands, ForceDirected)
    /// gestiscano grafi vuoti senza generare eccezioni.
    /// </summary>
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

    /// <summary>
    /// Verifica che i motori di layout posizionino correttamente un grafo composto da un singolo nodo,
    /// producendo coordinate numeriche finite e non nulle.
    /// </summary>
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

    /// <summary>
    /// Verifica che per grafi multi-nodo vengano generate coordinate (X, Y) valide e non degenerate (non NaN, non Infinity).
    /// </summary>
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

    /// <summary>
    /// Verifica che il layout a isole raggruppate (ClusteredIslands) gestisca componenti connesse disgiunte (isole separate).
    /// </summary>
    [Fact]
    public void LayoutEngines_ShouldHandleDisconnectedIslands()
    {
        // Arrange: 2 separate clusters with no edges between them
        var graph = new VisualCodeGraph();
        var nodeA1 = new GraphNode { Id = Guid.NewGuid(), Name = "A1", QualifiedName = "ModA.A1", Kind = SymbolKind.Module };
        var nodeA2 = new GraphNode { Id = Guid.NewGuid(), Name = "A2", QualifiedName = "ModA.A2", Kind = SymbolKind.Method };
        var nodeB1 = new GraphNode { Id = Guid.NewGuid(), Name = "B1", QualifiedName = "ModB.B1", Kind = SymbolKind.Module };
        var nodeB2 = new GraphNode { Id = Guid.NewGuid(), Name = "B2", QualifiedName = "ModB.B2", Kind = SymbolKind.Method };

        graph.Nodes.AddRange(new[] { nodeA1, nodeA2, nodeB1, nodeB2 });
        graph.Edges.Add(new GraphEdge { SourceId = nodeA1.Id, TargetId = nodeA2.Id, Kind = RelationshipKind.Calls });
        graph.Edges.Add(new GraphEdge { SourceId = nodeB1.Id, TargetId = nodeB2.Id, Kind = RelationshipKind.Calls });

        // Act & Assert
        var act = () => GraphLayoutManager.ApplyLayout(graph, GraphLayoutMode.ClusteredIslands);
        act.Should().NotThrow();

        foreach (var node in graph.Nodes)
        {
            node.X.Should().NotBe(double.NaN);
            node.Y.Should().NotBe(double.NaN);
        }
    }
}
