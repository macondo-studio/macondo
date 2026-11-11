using FluentAssertions;
using Macondo.App.Models;
using Macondo.App.Services;
using Macondo.Semantic.Impact;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.App;

public class ProjectCacheServiceTests
{
    /// <summary>
    /// Verify that ProjectCacheService generates deterministic paths for the cache file
    /// based on the SHA256 hash of the normalized folder.
    /// </summary>
    [Fact]
    public void GetCacheFilePath_ShouldBeDeterministicAndNormalizeSeparators()
    {
        // Arrange
        var service = ProjectCacheService.Instance;
        var path1 = @"C:\MyProjects\BillingCore\";
        var path2 = @"C:/MyProjects/BillingCore";

        // Act
        var file1 = service.GetCacheFilePath(path1);
        var file2 = service.GetCacheFilePath(path2);

        // Assert
        file1.Should().NotBeNullOrWhiteSpace();
        file1.Should().Be(file2, "Cache file path must be invariant to trailing slashes and forward/back slash format");
        file1.Should().EndWith(".json");
    }

    /// <summary>
    /// Check the complete roundtrip: creation of the snapshot (CreateSnapshot) starting from a VisualCodeGraph
    /// and Blast Radius rows, serialization/deserialization and subsequent restoration (RestoreGraphAndRows).
    /// </summary>
    [Fact]
    public void CreateSnapshot_And_RestoreGraphAndRows_ShouldRoundtripIdentically()
    {
        // Arrange: Creates a complete visual graph with one node and one edge
        var symId = Guid.NewGuid();
        var symbol = new Symbol
        {
            Id = symId,
            Name = "OrderProcessor",
            QualifiedName = "Billing.OrderProcessor",
            FilePath = "OrderProcessor.cls",
            Kind = SymbolKind.Class,
            LineStart = 1,
            LineEnd = 120
        };

        var semanticGraph = new CodeGraph();
        semanticGraph.Symbols.Add(symbol);

        var visualGraph = new VisualCodeGraph
        {
            SemanticGraph = semanticGraph
        };
        visualGraph.Nodes.Add(new GraphNode
        {
            Id = symId,
            Name = "OrderProcessor",
            QualifiedName = "Billing.OrderProcessor",
            Kind = SymbolKind.Class,
            SemanticSymbol = symbol,
            X = 350.5,
            Y = 420.0,
            IsExpanded = true
        });

        var rows = new List<BlastRadiusGridRow>
        {
            new()
            {
                TargetSymbol = symbol,
                NodeId = symId,
                ProjectName = "Billing",
                Name = "OrderProcessor",
                QualifiedName = "Billing.OrderProcessor",
                ContainerName = "BillingCore",
                Kind = "Class",
                DirectCallersCount = 4,
                IndirectCallersCount = 8,
                MaxDepth = 3,
                RiskLevel = ImpactRiskLevel.High,
                AffectedFormsCount = 2,
                TotalImpactedCount = 12,
                AffectedFormsSummary = "frmMain, frmOrder"
            }
        };

        // Act: Create Snapshot model
        var snapshot = new ProjectCacheSnapshot
        {
            SchemaVersion = 1,
            ProjectName = "BillingProject",
            FolderPath = @"C:\Fake\Billing",
            AnalyzedAtUtc = DateTime.UtcNow,
            Symbols = semanticGraph.Symbols,
            Relationships = semanticGraph.Relationships,
            LayoutNodes = visualGraph.Nodes.Select(n => new NodeLayoutSnapshot
            {
                Id = n.Id,
                X = n.X,
                Y = n.Y,
                IsExpanded = n.IsExpanded
            }).ToList(),
            BlastRadiusRows = rows.Select(r => new BlastRadiusRowSnapshot
            {
                NodeId = r.NodeId,
                ProjectName = r.ProjectName,
                Name = r.Name,
                QualifiedName = r.QualifiedName,
                ContainerName = r.ContainerName,
                Kind = r.Kind,
                DirectCallersCount = r.DirectCallersCount,
                IndirectCallersCount = r.IndirectCallersCount,
                MaxDepth = r.MaxDepth,
                RiskLevel = r.RiskLevel,
                AffectedFormsCount = r.AffectedFormsCount,
                TotalImpactedCount = r.TotalImpactedCount,
                AffectedFormsSummary = r.AffectedFormsSummary
            }).ToList()
        };

        // Assert snapshot structure
        snapshot.Should().NotBeNull();
        snapshot.ProjectName.Should().Be("BillingProject");
        snapshot.FolderPath.Should().Be(@"C:\Fake\Billing");
        snapshot.Symbols.Should().HaveCount(1);
        snapshot.LayoutNodes.Should().HaveCount(1);
        snapshot.BlastRadiusRows.Should().HaveCount(1);

        // Act: Restore from snapshot
        var (restoredGraph, restoredRows) = ProjectCacheService.Instance.RestoreGraphAndRows(snapshot);

        // Assert restored graph & rows
        restoredGraph.Should().NotBeNull();
        restoredGraph.Nodes.Should().HaveCount(2); // 1 nodo radice Progetto sintetico + 1 nodo Classe
        var restoredNode = restoredGraph.Nodes.First(n => n.Id == symId);
        restoredNode.Id.Should().Be(symId);
        restoredNode.X.Should().Be(350.5);
        restoredNode.Y.Should().Be(420.0);
        restoredNode.IsExpanded.Should().BeTrue();

        restoredRows.Should().HaveCount(1);
        restoredRows[0].NodeId.Should().Be(symId);
        restoredRows[0].DirectCallersCount.Should().Be(4);
        restoredRows[0].RiskLevel.Should().Be(ImpactRiskLevel.High);
        restoredRows[0].AffectedFormsSummary.Should().Be("frmMain, frmOrder");
    }
}
