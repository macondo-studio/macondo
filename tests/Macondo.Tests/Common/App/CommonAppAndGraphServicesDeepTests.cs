using FluentAssertions;
using Macondo.App.Layout;
using Macondo.App.Models;
using Macondo.App.Services;
using Macondo.Semantic.Export;
using Macondo.Semantic.Impact;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.App;

public class CommonAppAndGraphServicesDeepTests
{
    #region Graph Layout Engine Topology Tests

    /// <summary>
    /// Verify that layout engines calculate valid, non-degenerate coordinates for a star topology (Hub & Spoke)
    /// with a central node connected to 25 peripheral nodes.
    /// </summary>
    [Theory]
    [InlineData(GraphLayoutMode.StandardRadial)]
    [InlineData(GraphLayoutMode.AffinityRadial)]
    [InlineData(GraphLayoutMode.ClusteredIslands)]
    [InlineData(GraphLayoutMode.ForceDirected)]
    public void LayoutEngines_ShouldHandleStarHubAndSpokeTopology(GraphLayoutMode mode)
    {
        // Arrange
        var graph = new VisualCodeGraph();
        var hubId = Guid.NewGuid();
        var hubNode = new GraphNode { Id = hubId, Name = "HubModule", QualifiedName = "Project.HubModule", Kind = SymbolKind.Module };
        graph.Nodes.Add(hubNode);

        for (int i = 0; i < 25; i++)
        {
            var spokeNode = new GraphNode { Id = Guid.NewGuid(), Name = $"Spoke_{i}", QualifiedName = $"Project.Spoke_{i}", Kind = SymbolKind.Method };
            graph.Nodes.Add(spokeNode);
            graph.Edges.Add(new GraphEdge { SourceId = hubId, TargetId = spokeNode.Id, Kind = RelationshipKind.Calls });
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

    /// <summary>
    /// Verify that layout engines handle a deep linear chain of nodes (A -> B -> C -> ... -> N).
    /// </summary>
    [Theory]
    [InlineData(GraphLayoutMode.StandardRadial)]
    [InlineData(GraphLayoutMode.AffinityRadial)]
    [InlineData(GraphLayoutMode.ClusteredIslands)]
    [InlineData(GraphLayoutMode.ForceDirected)]
    public void LayoutEngines_ShouldHandleDeepLinearChainTopology(GraphLayoutMode mode)
    {
        // Arrange
        var graph = new VisualCodeGraph();
        GraphNode? previousNode = null;

        for (int i = 0; i < 15; i++)
        {
            var node = new GraphNode { Id = Guid.NewGuid(), Name = $"Step_{i}", QualifiedName = $"Project.Step_{i}", Kind = SymbolKind.Method };
            graph.Nodes.Add(node);

            if (previousNode != null)
            {
                graph.Edges.Add(new GraphEdge { SourceId = previousNode.Id, TargetId = node.Id, Kind = RelationshipKind.Calls });
            }
            previousNode = node;
        }

        // Act & Assert
        var act = () => GraphLayoutManager.ApplyLayout(graph, mode);
        act.Should().NotThrow();

        foreach (var node in graph.Nodes)
        {
            node.X.Should().NotBe(double.NaN);
            node.Y.Should().NotBe(double.NaN);
        }
    }

    /// <summary>
    /// Verify that a cloud of disconnected nodes (without any edges) is placed without errors.
    /// </summary>
    [Theory]
    [InlineData(GraphLayoutMode.StandardRadial)]
    [InlineData(GraphLayoutMode.AffinityRadial)]
    [InlineData(GraphLayoutMode.ClusteredIslands)]
    [InlineData(GraphLayoutMode.ForceDirected)]
    public void LayoutEngines_ShouldHandleDisconnectedPointCloud(GraphLayoutMode mode)
    {
        // Arrange: 20 disconnected nodes
        var graph = new VisualCodeGraph();
        for (int i = 0; i < 20; i++)
        {
            graph.Nodes.Add(new GraphNode { Id = Guid.NewGuid(), Name = $"Isolated_{i}", QualifiedName = $"Isolated_{i}", Kind = SymbolKind.Class });
        }

        // Act & Assert
        var act = () => GraphLayoutManager.ApplyLayout(graph, mode);
        act.Should().NotThrow();

        foreach (var node in graph.Nodes)
        {
            node.X.Should().NotBe(double.NaN);
            node.Y.Should().NotBe(double.NaN);
        }
    }

    #endregion

    #region Export & Serialization Tests

    /// <summary>
    /// Verify that CodeGraphMarkdownExporter.ExportSlice handles isolated symbols without callers or called without throwing exceptions.
    /// </summary>
    [Fact]
    public void MarkdownExporter_ShouldExportSliceForIsolatedSymbol_Gracefully()
    {
        // Arrange
        var graph = new CodeGraph();
        var isolatedMethod = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "OrphanMethod",
            QualifiedName = "OrphanMod.OrphanMethod",
            FilePath = "OrphanMod.bas",
            Kind = SymbolKind.Method
        };
        graph.Symbols.Add(isolatedMethod);

        // Act
        var slice = CodeGraphMarkdownExporter.ExportSlice(graph, isolatedMethod);

        // Assert
        slice.Should().NotBeNullOrWhiteSpace();
        slice.Should().Contain("OrphanMethod");
    }

    /// <summary>
    /// Verify that JSON serialization/deserialization handles complex graphs with loops and all SymbolKind types
    /// (Class, Interface, Struct, Enum, Form, Module, Method, Property, Field, Constant).
    /// </summary>
    [Fact]
    public void JsonExporter_ShouldRoundtripSerializeAllSymbolKindsAndCyclicRelationships()
    {
        // Arrange
        var graph = new CodeGraph();

        var kinds = new[]
        {
            SymbolKind.Class,
            SymbolKind.Interface,
            SymbolKind.Struct,
            SymbolKind.Enum,
            SymbolKind.Form,
            SymbolKind.Module,
            SymbolKind.Method,
            SymbolKind.Property,
            SymbolKind.Field,
            SymbolKind.Constant
        };

        var symbols = new List<Symbol>();
        foreach (var kind in kinds)
        {
            var sym = new Symbol
            {
                Id = Guid.NewGuid(),
                Name = $"Sym_{kind}",
                QualifiedName = $"Project.Sym_{kind}",
                FilePath = $"Project.{kind}.cs",
                Kind = kind,
                Accessibility = Accessibility.Public
            };
            symbols.Add(sym);
            graph.Symbols.Add(sym);
        }

        // Add a relationship cycle
        graph.AddCalls(symbols[6], symbols[7]);
        graph.AddCalls(symbols[7], symbols[6]);

        // Act
        var json = CodeGraphJsonExporter.ExportToJson(graph, indented: false);
        var restored = CodeGraphJsonExporter.ImportFromJson(json);

        // Assert
        restored.Should().NotBeNull();
        restored.Symbols.Should().HaveCount(kinds.Length);
        restored.Relationships.Should().HaveCount(2);

        foreach (var original in symbols)
        {
            var copy = restored.FindSymbolById(original.Id);
            copy.Should().NotBeNull();
            copy!.Kind.Should().Be(original.Kind);
            copy.Name.Should().Be(original.Name);
        }
    }

    #endregion

    #region Cache and Shortcuts Parity Tests

    /// <summary>
    /// Verify that restoring an empty cache snapshot (0 symbols and 0 lines) occurs without errors.
    /// </summary>
    [Fact]
    public void ProjectCacheService_ShouldRestoreEmptySnapshot_WithoutErrors()
    {
        // Arrange
        var emptySnapshot = new ProjectCacheSnapshot
        {
            SchemaVersion = 1,
            ProjectName = "EmptyProj",
            FolderPath = @"C:\Empty",
            AnalyzedAtUtc = DateTime.UtcNow,
            Symbols = new List<Symbol>(),
            Relationships = new List<Relationship>(),
            LayoutNodes = new List<NodeLayoutSnapshot>(),
            BlastRadiusRows = new List<BlastRadiusRowSnapshot>()
        };

        // Act
        var (restoredGraph, restoredRows) = ProjectCacheService.Instance.RestoreGraphAndRows(emptySnapshot);

        // Assert
        restoredGraph.Should().NotBeNull();
        restoredRows.Should().BeEmpty();
    }

    /// <summary>
    /// Check that the catalog of keyboard shortcuts maintains perfect parity between Italian and English.
    /// </summary>
    [Fact]
    public void KeyboardShortcutsCatalog_ShouldMaintainParityBetweenItalianAndEnglish()
    {
        // Act
        var itShortcuts = KeyboardShortcutsCatalog.GetShortcuts(AppLanguage.Italian);
        var enShortcuts = KeyboardShortcutsCatalog.GetShortcuts(AppLanguage.English);

        // Assert
        itShortcuts.Should().HaveSameCount(enShortcuts);

        for (int i = 0; i < itShortcuts.Count; i++)
        {
            itShortcuts[i].KeyBadges.Should().HaveSameCount(enShortcuts[i].KeyBadges, "Il numero di combinazioni di tasti deve coincidere tra le due lingue");
            itShortcuts[i].KeyBadges.Should().NotBeEmpty();
        }
    }

    #endregion
}
