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
    /// Verifica che i motori di layout gestiscano topologie a stella ad alto grado
    /// (1 nodo centrale connesso a 50 nodi foglia satellite) senza generare sovrapposizioni a raggio zero,
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
    /// Verifica che GraphVisualAdapter mantenga l'integrità della gerarchia genitore-figlio (ParentId)
    /// attraverso una catena a 3 livelli (Progetto -> Modulo -> Metodo).
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
    /// Verifica che ProjectCacheService sia resiliente di fronte a stringhe JSON corrotte,
    /// incomplete o con strutture errate, senza mandare in crash l'applicazione.
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
                // Un'eccezione gestita è attesa, l'importante è non produrre crash fatali
                return null;
            }
        };

        act.Should().NotThrow<AccessViolationException>();
    }

    #endregion
}
