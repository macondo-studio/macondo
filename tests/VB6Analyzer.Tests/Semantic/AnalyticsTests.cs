using FluentAssertions;
using Macondo.Semantic.Analytics.ControlFlow;
using Macondo.Semantic.Analytics.Dsm;
using Macondo.Semantic.Analytics.Hotspots;
using Macondo.Semantic.Analytics.ScreenFlow;
using Macondo.Semantic.Analytics.Treemap;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Semantic;

public class AnalyticsTests
{
    [Fact]
    public void DsmMatrix_ShouldBuildCorrectMatrix_FromGraph()
    {
        // Arrange
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();

        // Act
        var dsm = DsmMatrixBuilder.Build(graph);

        // Assert
        dsm.Should().NotBeNull();
        dsm.Size.Should().BeGreaterThan(0);
        dsm.Items.Should().NotBeEmpty();
        dsm.TotalDependencies.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Treemap_ShouldBuildAndLayoutSquarifiedTreemap()
    {
        // Arrange
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();

        // Act
        var treemap = TreemapModelBuilder.Build(graph);
        TreemapLayoutEngine.Layout(treemap, new TreemapRect(0, 0, 1000, 600));

        // Assert
        treemap.Should().NotBeNull();
        treemap.Value.Should().BeGreaterThan(0);
        treemap.Children.Should().NotBeEmpty();

        foreach (var child in treemap.Children)
        {
            child.Bounds.Width.Should().BeGreaterThanOrEqualTo(0);
            child.Bounds.Height.Should().BeGreaterThanOrEqualTo(0);
            child.Bounds.X.Should().BeInRange(0, 1000);
            child.Bounds.Y.Should().BeInRange(0, 600);
        }
    }

    [Fact]
    public void HotspotAnalyzer_ShouldCalculateDangerZoneAndMetrics()
    {
        // Arrange
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();

        // Act
        var hotspots = HotspotAnalyzer.Analyze(graph);

        // Assert
        hotspots.Should().NotBeNull();
        hotspots.Items.Should().NotBeEmpty();
        hotspots.ThresholdX.Should().BeGreaterThanOrEqualTo(0);
        hotspots.ThresholdY.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void ScreenFlow_ShouldExtractScreensAndTransitions()
    {
        // Arrange
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();

        // Act
        var screenFlow = ScreenFlowBuilder.Build(graph);

        // Assert
        screenFlow.Should().NotBeNull();
        // 15_UIEventsAndExpressions.frm exists in Fixtures
        screenFlow.Screens.Should().NotBeEmpty();
    }

    [Fact]
    public void ControlFlowBuilder_ShouldBuildCFGAndCalculateComplexity()
    {
        // Arrange
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();
        var sampleRoutine = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method);
        sampleRoutine.Should().NotBeNull();

        // Act
        var cfg = ControlFlowBuilder.BuildForSymbol(sampleRoutine!, graph);

        // Assert
        cfg.Should().NotBeNull();
        cfg.ProcedureName.Should().Be(sampleRoutine!.Name);
        cfg.Blocks.Should().NotBeEmpty();
        cfg.CyclomaticComplexity.Should().BeGreaterThanOrEqualTo(1);
    }
}
