using System.Text.Json;
using FluentAssertions;
using Macondo.App.Models;
using Macondo.App.Services;
using Macondo.Semantic.Impact;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.App;

public class SettingsAndCacheTests
{
    /// <summary>
    /// Verifica la serializzazione e deserializzazione JSON corretta dell'oggetto AppSettings (Lingua, Tema, preferenze).
    /// </summary>
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

    /// <summary>
    /// Verifica che tutte le 13 impostazioni di visibilità delle schede (Graph View e tab analitici)
    /// siano abilitate per impostazione predefinita (true) e vengano serializzate/deserializzate fedelmente.
    /// </summary>
    [Fact]
    public void AppSettings_ShouldPersistAllTabVisibilityFlags_AndDefaultToTrue()
    {
        // Arrange & Verify Defaults
        var defaultSettings = new AppSettings();
        defaultSettings.ShowTabGraph.Should().BeTrue();
        defaultSettings.ShowTabDsm.Should().BeTrue();
        defaultSettings.ShowTabTreemap.Should().BeTrue();
        defaultSettings.ShowTabHotspot.Should().BeTrue();
        defaultSettings.ShowTabScreenFlow.Should().BeTrue();
        defaultSettings.ShowTabControlFlow.Should().BeTrue();
        defaultSettings.ShowTabMartin.Should().BeTrue();
        defaultSettings.ShowTabDeadCode.Should().BeTrue();
        defaultSettings.ShowTabSharedState.Should().BeTrue();
        defaultSettings.ShowTabTemporalCoupling.Should().BeTrue();
        defaultSettings.ShowTabArchitecturalFlow.Should().BeTrue();
        defaultSettings.ShowTabCodeCity.Should().BeTrue();
        defaultSettings.ShowTabBlastRadiusGrid.Should().BeTrue();

        // Custom config with selective disabled tabs
        var custom = new AppSettings
        {
            ShowTabGraph = false,
            ShowTabDsm = false,
            ShowTabTreemap = true,
            ShowTabHotspot = false,
            ShowTabScreenFlow = true,
            ShowTabControlFlow = false,
            ShowTabMartin = true,
            ShowTabDeadCode = false,
            ShowTabSharedState = true,
            ShowTabTemporalCoupling = false,
            ShowTabArchitecturalFlow = true,
            ShowTabCodeCity = false,
            ShowTabBlastRadiusGrid = true
        };

        // Act
        var json = JsonSerializer.Serialize(custom);
        var restored = JsonSerializer.Deserialize<AppSettings>(json);

        // Assert
        restored.Should().NotBeNull();
        restored!.ShowTabGraph.Should().BeFalse();
        restored.ShowTabDsm.Should().BeFalse();
        restored.ShowTabTreemap.Should().BeTrue();
        restored.ShowTabHotspot.Should().BeFalse();
        restored.ShowTabScreenFlow.Should().BeTrue();
        restored.ShowTabControlFlow.Should().BeFalse();
        restored.ShowTabMartin.Should().BeTrue();
        restored.ShowTabDeadCode.Should().BeFalse();
        restored.ShowTabSharedState.Should().BeTrue();
        restored.ShowTabTemporalCoupling.Should().BeFalse();
        restored.ShowTabArchitecturalFlow.Should().BeTrue();
        restored.ShowTabCodeCity.Should().BeFalse();
        restored.ShowTabBlastRadiusGrid.Should().BeTrue();
    }

    /// <summary>
    /// Verifica che ProjectCacheService ripristini fedelmente il VisualCodeGraph, le coordinate dei nodi
    /// e le righe della griglia del Blast Radius a partire da uno snapshot serializzato su cache.
    /// </summary>
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
