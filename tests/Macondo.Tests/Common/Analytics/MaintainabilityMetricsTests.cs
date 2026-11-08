using FluentAssertions;
using Macondo.Semantic.Analytics.Maintainability;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class MaintainabilityMetricsTests
{
    /// <summary>
    /// Verifica il calcolo delle metriche di Halstead e del Maintainability Index normalizzato [0..100].
    /// </summary>
    [Fact]
    public void Maintainability_ShouldCalculateHalsteadAndMaintainabilityIndex()
    {
        // Arrange
        var graph = new CodeGraph();
        var cls = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "OrderProcessor",
            QualifiedName = "OrderProcessor",
            FilePath = "OrderProcessor.cs",
            Kind = SymbolKind.Class,
            LineStart = 1,
            LineEnd = 150
        };

        var mth = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "ProcessOrder",
            QualifiedName = "OrderProcessor.ProcessOrder",
            FilePath = "OrderProcessor.cs",
            Kind = SymbolKind.Method,
            LineStart = 10,
            LineEnd = 80
        };

        graph.Symbols.AddRange(new[] { cls, mth });
        graph.AddContains(cls, mth);

        // Act
        var dataset = MaintainabilityCalculator.Analyze(graph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Items.Should().HaveCount(1);

        var item = dataset.Items[0];
        item.MaintainabilityIndex.Should().BeInRange(0.0, 100.0);
        item.Halstead.Volume.Should().BeGreaterThan(0.0);
        item.Halstead.Difficulty.Should().BeGreaterThan(0.0);
        item.Halstead.Effort.Should().BeGreaterThan(0.0);
        item.Halstead.EstimatedBugs.Should().BeGreaterThan(0.0);
    }

    /// <summary>
    /// Verifica il calcolo del Maintainability Index sulle fixture di progetto.
    /// </summary>
    [Fact]
    public void Maintainability_ShouldAnalyzeProjectFixtures()
    {
        // Arrange
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();

        // Act
        var dataset = MaintainabilityCalculator.Analyze(graph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Items.Should().NotBeEmpty();
        dataset.AverageMaintainabilityIndex.Should().BeInRange(0.0, 100.0);
        (dataset.HighCount + dataset.ModerateCount + dataset.LowCount).Should().Be(dataset.Items.Count);
    }
}
