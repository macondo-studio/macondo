using FluentAssertions;
using Macondo.Semantic.Analytics.Hotspots;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class HotspotAnalysisTests
{
    [Fact]
    public void HotspotAnalyzer_ShouldCalculateDangerZoneAndMetrics_FromGraph()
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
    public void HotspotAnalyzer_ShouldIncorporateVcsChurnHistory_IntoRiskScore()
    {
        // Arrange
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();
        var sampleRoutine = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && !string.IsNullOrEmpty(s.FilePath));
        sampleRoutine.Should().NotBeNull();

        var churnHistory = new Dictionary<string, int>
        {
            [sampleRoutine!.FilePath!] = 50 // High churn
        };

        // Act
        var hotspotsNoChurn = HotspotAnalyzer.Analyze(graph);
        var hotspotsWithChurn = HotspotAnalyzer.Analyze(graph, churnHistory);

        // Assert
        var itemNoChurn = hotspotsNoChurn.Items.First(i => i.Id == sampleRoutine.Id);
        var itemWithChurn = hotspotsWithChurn.Items.First(i => i.Id == sampleRoutine.Id);

        itemWithChurn.ChurnOrFanIn.Should().BeGreaterThan(itemNoChurn.ChurnOrFanIn);
    }

    [Fact]
    public void HotspotAnalyzer_ShouldClampRiskScoresBetween0And100()
    {
        // Arrange
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();

        // Act
        var hotspots = HotspotAnalyzer.Analyze(graph);

        // Assert
        foreach (var item in hotspots.Items)
        {
            item.RiskScore.Should().BeInRange(0.0, 100.0);
            item.Complexity.Should().BeGreaterThanOrEqualTo(1.0);
            item.SizeMetric.Should().BeGreaterThanOrEqualTo(4.0);
        }
    }
}
