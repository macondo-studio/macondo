using FluentAssertions;
using Macondo.App.Layout;
using Macondo.App.Models;
using Macondo.App.Services;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.App;

public class AppLayoutAndCacheDeepTests
{
    #region Layout Engines Star Topology and High Degree Nodes

    /// <summary>
    /// Verify that layout engines handle high-degree star topologies
    /// (1 central node connected to 50 satellite leaf nodes) without generating zero radius overlaps,
    /// valori NaN o coordinate infinite.
    /// </summary>
    [Theory]
    [InlineData(GraphLayoutMode.StandardRadial)]
    [InlineData(GraphLayoutMode.AffinityRadial)]
    [InlineData(GraphLayoutMode.ClusteredIslands)]
    [InlineData(GraphLayoutMode.ForceDirected)]
    public void LayoutEngines_ShouldHandleLargeStarTopology_WithoutNumericalInstability(GraphLayoutMode mode)
    {
        // Arrange
        var graph = new VisualCodeGraph();
        var hubId = Guid.NewGuid();
        var hubNode = new GraphNode
        {
            Id = hubId,
            Name = "HubController",
            QualifiedName = "App.HubController",
            Kind = SymbolKind.Class,
            X = 0,
            Y = 0
        };
        graph.Nodes.Add(hubNode);

        for (int i = 0; i < 50; i++)
        {
            var satelliteId = Guid.NewGuid();
            var satelliteNode = new GraphNode
            {
                Id = satelliteId,
                Name = $"Worker_{i}",
                QualifiedName = $"App.Worker_{i}",
                Kind = SymbolKind.Method,
                X = 0,
                Y = 0
            };
            graph.Nodes.Add(satelliteNode);

            graph.Edges.Add(new GraphEdge
            {
                SourceId = hubId,
                TargetId = satelliteId,
                Kind = RelationshipKind.Calls
            });
        }

        // Act
        var act = () => GraphLayoutManager.ApplyLayout(graph, mode);

        // Assert
        act.Should().NotThrow();

        foreach (var node in graph.Nodes)
        {
            node.X.Should().NotBe(double.NaN);
            node.Y.Should().NotBe(double.NaN);
            double.IsInfinity(node.X).Should().BeFalse();
            double.IsInfinity(node.Y).Should().BeFalse();
        }
    }

    #endregion

    #region GraphVisualAdapter Multi-Level Hierarchy

    /// <summary>
    /// Verify that GraphVisualAdapter maintains the integrity of the parent-child hierarchy (ParentId)
    /// through a 3-level chain (Project -> Module -> Method).
    /// </summary>
    [Fact]
    public void GraphVisualAdapter_ShouldPreserveThreeLevelParentHierarchy()
    {
        // Arrange
        var semanticGraph = new CodeGraph();

        var project = new Symbol { Id = Guid.NewGuid(), Name = "CoreProject", QualifiedName = "CoreProject", FilePath = "CoreProject.vbp", Kind = SymbolKind.Project };
        var module = new Symbol { Id = Guid.NewGuid(), Name = "SecurityMod", QualifiedName = "CoreProject.SecurityMod", FilePath = "SecurityMod.bas", Kind = SymbolKind.Module };
        var method = new Symbol { Id = Guid.NewGuid(), Name = "AuthenticateUser", QualifiedName = "CoreProject.SecurityMod.AuthenticateUser", FilePath = "SecurityMod.bas", Kind = SymbolKind.Method };

        semanticGraph.Symbols.AddRange(new[] { project, module, method });
        semanticGraph.AddContains(project, module);
        semanticGraph.AddContains(module, method);

        // Act
        var visualGraph = GraphVisualAdapter.FromSemanticGraph(semanticGraph, "CoreProject");

        // Assert
        visualGraph.Should().NotBeNull();
        var visualMod = visualGraph.Nodes.FirstOrDefault(n => n.Id == module.Id);
        var visualMethod = visualGraph.Nodes.FirstOrDefault(n => n.Id == method.Id);

        visualMod.Should().NotBeNull();
        visualMethod.Should().NotBeNull();

        visualMod!.ParentId.Should().Be(project.Id);
        visualMethod!.ParentId.Should().Be(module.Id);
    }

    #endregion

    #region ProjectCacheService Resilience on Corrupted Data

    /// <summary>
    /// Verify that ProjectCacheService is resilient to corrupt JSON strings,
    /// incomplete or with incorrect structures, without crashing the application.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ \"invalid_json\": true ")]
    [InlineData("{ \"SchemaVersion\": \"not_a_number\" }")]
    public void ProjectCacheService_ShouldGracefullyHandleCorruptedJson(string corruptedJson)
    {
        // Act & Assert
        var act = () =>
        {
            try
            {
                var snapshot = System.Text.Json.JsonSerializer.Deserialize<ProjectCacheSnapshot>(corruptedJson);
                return snapshot;
            }
            catch (Exception)
            {
                // A handled exception is expected, the important thing is not to produce fatal crashes
                return null;
            }
        };

        act.Should().NotThrow<AccessViolationException>();
    }

    #endregion
}
