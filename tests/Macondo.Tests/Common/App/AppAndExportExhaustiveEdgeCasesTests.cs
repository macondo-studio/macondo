using System.Text.Json;
using FluentAssertions;
using Macondo.App.Layout;
using Macondo.App.Models;
using Macondo.App.Services;
using Macondo.Semantic.Analytics.ArchitecturalRules;
using Macondo.Semantic.Export;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Common.App;

public class AppAndExportExhaustiveEdgeCasesTests
{
    #region Complete Audit Report Serialization and Exporters

    /// <summary>
    /// Verify that AuditReportBuilder.RunAll generates a complete report
    /// serializable in both JSON and Markdown formats with all analysis sections populated.
    /// </summary>
    [Fact]
    public void AuditReportBuilder_ShouldProduceValidJsonAndMarkdownOutputs()
    {
        // Arrange
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();

        // Act
        var report = AuditReportBuilder.RunAll(graph);
        var json = report.ToJson(indented: true);
        var markdown = report.ToMarkdown();

        // Assert
        json.Should().NotBeNullOrWhiteSpace();
        markdown.Should().NotBeNullOrWhiteSpace();

        // Check JSON Structure
        var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("summary", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("deadCode", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("maintainability", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("dsm", out _).Should().BeTrue();

        // Check Markdown Headers and Sections
        markdown.Should().Contain("# Architecture & Code Quality Audit Report");
        markdown.Should().Contain("1. Executive Summary & Health Dashboard");
        markdown.Should().Contain("3. Maintainability & Technical Debt Candidates");
        markdown.Should().Contain("4. Dead Code & Orphaned Components");
    }

    #endregion

    #region Keyboard Shortcuts Catalog No-Collision Verification

    /// <summary>
    /// Check for conflicts or duplicate key combinations
    /// within the shortcut catalog for each supported language.
    /// </summary>
    [Theory]
    [InlineData(AppLanguage.Italian)]
    [InlineData(AppLanguage.English)]
    public void KeyboardShortcutsCatalog_ShouldHaveNoKeyBindingCollisions(AppLanguage lang)
    {
        // Act
        var shortcuts = KeyboardShortcutsCatalog.GetShortcuts(lang);

        // Assert
        shortcuts.Should().NotBeEmpty();

        var keyCombos = shortcuts
            .Select(s => string.Join("+", s.KeyBadges.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)))
            .ToList();

        // Each key combination must be unique
        keyCombos.Distinct().Count().Should().Be(keyCombos.Count, "Non devono esserci combinazioni di tasti duplicate");
    }

    #endregion

    #region GraphVisualAdapter Disconnected Islands and Deep Hierarchies

    /// <summary>
    /// Verify that GraphVisualAdapter correctly handles graphs with completely disconnected islands
    /// and structures with orphaned modules, assigning the correct node coordinates and hierarchies.
    /// </summary>
    [Fact]
    public void GraphVisualAdapter_ShouldHandleDisconnectedIslandsAndDeepTrees()
    {
        // Arrange
        var graph = new CodeGraph();

        // Island 1
        var modA = new Symbol { Id = Guid.NewGuid(), Name = "ModA", QualifiedName = "ModA", FilePath = "ModA.bas", Kind = SymbolKind.Module };
        var methA = new Symbol { Id = Guid.NewGuid(), Name = "MethA", QualifiedName = "ModA.MethA", FilePath = "ModA.bas", Kind = SymbolKind.Method };
        graph.Symbols.AddRange(new[] { modA, methA });
        graph.AddContains(modA, methA);

        // Island 2 (completely separate)
        var modB = new Symbol { Id = Guid.NewGuid(), Name = "ModB", QualifiedName = "ModB", FilePath = "ModB.bas", Kind = SymbolKind.Module };
        var methB = new Symbol { Id = Guid.NewGuid(), Name = "MethB", QualifiedName = "ModB.MethB", FilePath = "ModB.bas", Kind = SymbolKind.Method };
        graph.Symbols.AddRange(new[] { modB, methB });
        graph.AddContains(modB, methB);

        // Act
        var visualGraph = GraphVisualAdapter.FromSemanticGraph(graph, "DisconnectedProject");

        // Assert
        visualGraph.Should().NotBeNull();
        visualGraph.Nodes.Should().HaveCount(5); // 1 synthetic project root + 2 modules + 2 methods
        visualGraph.Edges.Should().HaveCount(4); // 2 Contains (Project->Mod) + 2 Contains (Mod->Meth)

        var rootNode = visualGraph.Nodes.First(n => n.Kind == SymbolKind.Project);
        var nodeA = visualGraph.Nodes.First(n => n.Id == modA.Id);
        var nodeB = visualGraph.Nodes.First(n => n.Id == modB.Id);

        nodeA.ParentId.Should().Be(rootNode.Id);
        nodeB.ParentId.Should().Be(rootNode.Id);
    }

    #endregion

    #region ProjectCacheService Snapshot Serialization Roundtrip

    /// <summary>
    /// Verify that saving and restoring ProjectCacheSnapshot remains intact
    /// all properties, node locations, analysis statistics and scan date.
    /// </summary>
    [Fact]
    public void ProjectCacheService_ShouldSerializeAndDeserializeSnapshotFaithfully()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), "Macondo_Cache_Roundtrip_" + Guid.NewGuid().ToString("N") + ".json");
        var symId = Guid.NewGuid();

        var snapshot = new ProjectCacheSnapshot
        {
            SchemaVersion = 1,
            ProjectName = "RoundtripProject",
            FolderPath = @"C:\Test\RoundtripProject",
            AnalyzedAtUtc = DateTime.UtcNow,
            Symbols = new List<Symbol>
            {
                new() { Id = symId, Name = "Helper", Kind = SymbolKind.Class, QualifiedName = "Helper", FilePath = "Helper.cs" }
            },
            Relationships = new List<Relationship>(),
            LayoutNodes = new List<NodeLayoutSnapshot>
            {
                new() { Id = symId, X = 100.5, Y = 200.5, IsExpanded = true }
            },
            BlastRadiusRows = new List<BlastRadiusRowSnapshot>()
        };

        try
        {
            // Act: Serialize to JSON and deserialize back
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(tempFile, json);

            var loadedJson = File.ReadAllText(tempFile);
            var restored = JsonSerializer.Deserialize<ProjectCacheSnapshot>(loadedJson);

            // Assert
            restored.Should().NotBeNull();
            restored!.ProjectName.Should().Be(snapshot.ProjectName);
            restored.FolderPath.Should().Be(snapshot.FolderPath);
            restored.Symbols.Should().HaveCount(1);
            restored.LayoutNodes.Should().HaveCount(1);

            var (graph, rows) = ProjectCacheService.Instance.RestoreGraphAndRows(restored);
            graph.Should().NotBeNull();
            graph.Nodes.Should().Contain(n => n.Id == symId && n.X == 100.5 && n.Y == 200.5);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    #endregion
}
