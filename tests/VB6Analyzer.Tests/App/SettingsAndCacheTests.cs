using System.Text.Json;
using FluentAssertions;
using Macondo.App.Models;
using Macondo.App.Services;
using Macondo.Semantic.Impact;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.App;

public class SettingsAndCacheTests
{
    [Fact]
    public void AppSettings_ShouldSerializeAndDeserializeCorrectly()
    {
        // Arrange
        var settings = new AppSettings
        {
            Language = AppLanguage.English,
            Theme = AppTheme.DarkStudio,
            EnableHoverSelection = true,
            LastOpenedFolder = @"C:\Fake\Path\Project"
        };

        // Act
        var json = JsonSerializer.Serialize(settings);
        var deserialized = JsonSerializer.Deserialize<AppSettings>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Language.Should().Be(AppLanguage.English);
        deserialized.Theme.Should().Be(AppTheme.DarkStudio);
        deserialized.EnableHoverSelection.Should().BeTrue();
        deserialized.LastOpenedFolder.Should().Be(@"C:\Fake\Path\Project");
    }

    [Fact]
    public void ProjectCacheSnapshot_ShouldRestoreVisualGraphAndRows()
    {
        // Arrange
        var symId = Guid.NewGuid();
        var symbol = new Symbol
        {
            Id = symId,
            Kind = SymbolKind.Class,
            Name = "Customer",
            QualifiedName = "Customer",
            FilePath = "Customer.cls",
            LineStart = 1,
            LineEnd = 50
        };

        var snapshot = new ProjectCacheSnapshot
        {
            SchemaVersion = 1,
            ProjectName = "BillingCore",
            FolderPath = @"C:\Test\BillingCore",
            AnalyzedAtUtc = DateTime.UtcNow,
            Symbols = new List<Symbol> { symbol },
            Relationships = new List<Relationship>(),
            LayoutNodes = new List<NodeLayoutSnapshot>
            {
                new() { Id = symId, X = 150.0, Y = 250.0, IsExpanded = true }
            },
            BlastRadiusRows = new List<BlastRadiusRowSnapshot>
            {
                new()
                {
                    NodeId = symId,
                    ProjectName = "BillingCore",
                    Name = "Customer",
                    QualifiedName = "Customer",
                    ContainerName = "BillingCore",
                    Kind = "Class",
                    DirectCallersCount = 3,
                    IndirectCallersCount = 5,
                    MaxDepth = 2,
                    RiskLevel = ImpactRiskLevel.Medium,
                    AffectedFormsCount = 1,
                    TotalImpactedCount = 6,
                    AffectedFormsSummary = "MainForm"
                }
            }
        };

        // Act
        var (graph, rows) = ProjectCacheService.Instance.RestoreGraphAndRows(snapshot);

        // Assert
        graph.Should().NotBeNull();
        graph.Nodes.Should().Contain(n => n.Id == symId && n.X == 150.0 && n.Y == 250.0);
        rows.Should().HaveCount(1);
        rows[0].QualifiedName.Should().Be("Customer");
        rows[0].RiskLevel.Should().Be(ImpactRiskLevel.Medium);
    }
}
