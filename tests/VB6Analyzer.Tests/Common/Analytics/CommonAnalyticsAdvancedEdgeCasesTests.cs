using FluentAssertions;
using Macondo.Semantic.Analytics.CodeCity;
using Macondo.Semantic.Analytics.ControlFlow;
using Macondo.Semantic.Analytics.DataFlow;
using Macondo.Semantic.Analytics.DataMatrix;
using Macondo.Semantic.Analytics.Martin;
using Macondo.Semantic.Analytics.Treemap;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class CommonAnalyticsAdvancedEdgeCasesTests
{
    #region Code City Edge Cases

    /// <summary>
    /// Verifica che CodeCityBuilder gestisca un grafo semantico completamente vuoto senza generare eccezioni,
    /// restituendo un dataset con zero distretti e zero edifici.
    /// </summary>
    [Fact]
    public void CodeCityBuilder_ShouldHandleEmptyGraph_Gracefully()
    {
        // Arrange
        var emptyGraph = new CodeGraph();

        // Act
        var dataset = CodeCityBuilder.Build(emptyGraph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.TotalDistrictsCount.Should().Be(0);
        dataset.TotalBuildingsCount.Should().Be(0);
        dataset.TotalLoc.Should().Be(0);
        dataset.Districts.Should().BeEmpty();
    }

    /// <summary>
    /// Verifica che simboli con 0 LOC o intervalli di riga non valorizzati ricevano comunque dimensioni minime positive
    /// (Width >= 1.0, Depth >= 1.0, Height >= 1.0) senza coordinate degenerate.
    /// </summary>
    [Fact]
    public void CodeCityBuilder_ShouldEnsureMinimumDimensions_ForZeroLocContainers()
    {
        // Arrange
        var graph = new CodeGraph();
        var emptyMod = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "EmptyMod",
            QualifiedName = "EmptyMod",
            FilePath = "EmptyMod.bas",
            Kind = SymbolKind.Module,
            LineStart = 0,
            LineEnd = 0
        };
        graph.Symbols.Add(emptyMod);

        // Act
        var dataset = CodeCityBuilder.Build(graph);

        // Assert
        dataset.AllBuildings.Should().HaveCount(1);
        var building = dataset.AllBuildings.First();
        building.Width.Should().BeGreaterThanOrEqualTo(1.0);
        building.Depth.Should().BeGreaterThanOrEqualTo(1.0);
        building.Height.Should().BeGreaterThanOrEqualTo(1.0);
    }

    #endregion

    #region Treemap Layout Edge Cases

    /// <summary>
    /// Verifica che TreemapLayoutEngine posizioni gerarchicamente i figli mantenendo i rettangoli
    /// strettamente all'interno dei confini del rettangolo contenitore genitore.
    /// </summary>
    [Fact]
    public void TreemapLayoutEngine_ShouldContainChildBounds_WithinParentViewport()
    {
        // Arrange
        var root = new TreemapItem
        {
            Id = Guid.NewGuid(),
            Name = "RootProject",
            Value = 300,
            MetricScore = 10.0,
            Children = new List<TreemapItem>
            {
                new() { Id = Guid.NewGuid(), Name = "ChildA", Value = 100, MetricScore = 3.0 },
                new() { Id = Guid.NewGuid(), Name = "ChildB", Value = 200, MetricScore = 7.0 }
            }
        };

        var viewport = new TreemapRect(0, 0, 800, 600);

        // Act
        TreemapLayoutEngine.Layout(root, viewport);

        // Assert
        root.Bounds.Width.Should().Be(800);
        root.Bounds.Height.Should().Be(600);

        foreach (var child in root.Children)
        {
            child.Bounds.X.Should().BeGreaterThanOrEqualTo(viewport.X);
            child.Bounds.Y.Should().BeGreaterThanOrEqualTo(viewport.Y);
            (child.Bounds.X + child.Bounds.Width).Should().BeLessThanOrEqualTo(viewport.X + viewport.Width + 0.1);
            (child.Bounds.Y + child.Bounds.Height).Should().BeLessThanOrEqualTo(viewport.Y + viewport.Height + 0.1);
        }
    }

    #endregion

    #region Martin Metrics Edge Cases

    /// <summary>
    /// Verifica che un modulo isolato privo di chiamanti e di chiamate (Ca = 0, Ce = 0)
    /// produca Instability = 0.0 e non provochi divisioni per zero (NaN / Infinity).
    /// </summary>
    [Fact]
    public void MartinMetrics_ShouldHandleZeroCoupling_WithoutDivisionByZero()
    {
        // Arrange
        var graph = new CodeGraph();
        var isolatedMod = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "IsolatedMod",
            QualifiedName = "IsolatedMod",
            FilePath = "IsolatedMod.bas",
            Kind = SymbolKind.Module,
            LineStart = 1,
            LineEnd = 50
        };
        graph.Symbols.Add(isolatedMod);

        // Act
        var dataset = MartinMetricsCalculator.Analyze(graph);

        // Assert
        dataset.Items.Should().HaveCount(1);
        var item = dataset.Items[0];
        item.AfferentCoupling.Should().Be(0);
        item.EfferentCoupling.Should().Be(0);
        item.Instability.Should().Be(0.0);
        double.IsNaN(item.Instability).Should().BeFalse();
        double.IsInfinity(item.Instability).Should().BeFalse();
    }

    #endregion

    #region CRUD Matrix Edge Cases

    /// <summary>
    /// Verifica il tracciamento di entità con operazioni esclusive di sola cancellazione (Delete-only)
    /// o solo aggiornamento (Update-only).
    /// </summary>
    [Fact]
    public void CrudMatrix_ShouldTrackUpdateOnlyAndDeleteOnlyEntities()
    {
        // Arrange
        var graph = new CodeGraph();

        var sessionEntity = new Symbol { Id = Guid.NewGuid(), Name = "UserSession", QualifiedName = "UserSession", FilePath = "Session.cs", Kind = SymbolKind.Class };
        var cacheEntity = new Symbol { Id = Guid.NewGuid(), Name = "AppCache", QualifiedName = "AppCache", FilePath = "Cache.cs", Kind = SymbolKind.Class };

        var updateSession = new Symbol { Id = Guid.NewGuid(), Name = "UpdateSessionHeartbeat", QualifiedName = "SessionRepo.UpdateSessionHeartbeat", FilePath = "SessionRepo.cs", Kind = SymbolKind.Method };
        var deleteCache = new Symbol { Id = Guid.NewGuid(), Name = "DeleteCacheEntry", QualifiedName = "CacheRepo.DeleteCacheEntry", FilePath = "CacheRepo.cs", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { sessionEntity, cacheEntity, updateSession, deleteCache });

        // Act
        var dataset = CrudMatrixAnalyzer.Analyze(graph);

        // Assert
        dataset.Should().NotBeNull();
        var sessionSummary = dataset.EntitySummaries.FirstOrDefault(s => s.EntityName.Contains("UserSession"));
        var cacheSummary = dataset.EntitySummaries.FirstOrDefault(s => s.EntityName.Contains("AppCache"));

        sessionSummary.Should().NotBeNull();
        sessionSummary!.UpdateCount.Should().Be(1);
        sessionSummary.CreateCount.Should().Be(0);
        sessionSummary.DeleteCount.Should().Be(0);

        cacheSummary.Should().NotBeNull();
        cacheSummary!.DeleteCount.Should().Be(1);
        cacheSummary.CreateCount.Should().Be(0);
        cacheSummary.UpdateCount.Should().Be(0);
    }

    #endregion

    #region Taint & Data Flow Broken Chains

    /// <summary>
    /// Verifica che in presenza di un anello mancante nella catena di chiamate tra la sorgente UI e il sink DB,
    /// TaintAnalyzer non segnali falsi percorsi di contaminazione.
    /// </summary>
    [Fact]
    public void TaintAnalyzer_ShouldNotReportTaint_WhenCallChainIsBroken()
    {
        // Arrange: uiField -> uiHandler (reads). dbSink is called by isolatedService (not called by uiHandler).
        var graph = new CodeGraph();

        var uiField = new Symbol { Id = Guid.NewGuid(), Name = "txtParam", QualifiedName = "Form1.txtParam", FilePath = "Form1.frm", Kind = SymbolKind.Field };
        var uiHandler = new Symbol { Id = Guid.NewGuid(), Name = "btnSubmit_Click", QualifiedName = "Form1.btnSubmit_Click", FilePath = "Form1.frm", Kind = SymbolKind.Method };
        var isolatedService = new Symbol { Id = Guid.NewGuid(), Name = "RunQuery", QualifiedName = "Service.RunQuery", FilePath = "Service.bas", Kind = SymbolKind.Method };
        var sqlSink = new Symbol { Id = Guid.NewGuid(), Name = "ExecuteSqlQuery", QualifiedName = "Db.ExecuteSqlQuery", FilePath = "Db.bas", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { uiField, uiHandler, isolatedService, sqlSink });
        graph.AddReads(uiHandler, uiField);
        graph.AddCalls(isolatedService, sqlSink); // uiHandler does NOT call isolatedService

        // Act
        var dataset = TaintAnalyzer.Analyze(graph);

        // Assert
        dataset.TaintPaths.Should().BeEmpty();
    }

    #endregion
}
