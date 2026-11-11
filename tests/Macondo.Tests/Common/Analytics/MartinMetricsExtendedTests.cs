using FluentAssertions;
using Macondo.Semantic.Analytics.Martin;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class MartinMetricsExtendedTests
{
    /// <summary>
    /// Verifies that an isolated component without any input or output dependencies (Ca = 0, Ce = 0)
    /// abbia Instabilità = 0.0, Astrattezza = 0.0 e Distanza = 1.0.
    /// </summary>
    [Fact]
    public void MartinMetrics_ShouldCalculateZeroCouplingMetrics_ForIsolatedModule()
    {
        // Arrange
        var graph = new CodeGraph();
        var mod = new Symbol { Id = Guid.NewGuid(), Name = "IsolatedModule", QualifiedName = "IsolatedModule", FilePath = "IsolatedModule.bas", Kind = SymbolKind.Module, LineStart = 1, LineEnd = 50 };
        graph.Symbols.Add(mod);

        // Act
        var dataset = MartinMetricsCalculator.Analyze(graph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Items.Should().HaveCount(1);

        var item = dataset.Items[0];
        item.AfferentCoupling.Should().Be(0);
        item.EfferentCoupling.Should().Be(0);
        item.Instability.Should().Be(0.0);
        item.Abstractness.Should().Be(0.0);
        item.Distance.Should().Be(1.0); // |0 + 0 - 1| = 1.0
    }

    /// <summary>
    /// Verifies that an abstract interface (A = 1.0) to which multiple callers converge
    /// without output dependencies (Ca > 0, Ce = 0 => I = 0.0) lies exactly on the main sequence (A + I = 1.0 => D = 0.0).
    /// </summary>
    [Fact]
    public void MartinMetrics_ShouldCalculateZeroDistance_ForHighlyDependedInterface()
    {
        // Arrange: Abstract interface with A = 1.0, used by 3 modules (Ca = 3, Ce = 0 => I = 0.0) => D = |1.0 + 0.0 - 1| = 0.0
        var graph = new CodeGraph();
        var iface = new Symbol { Id = Guid.NewGuid(), Name = "IRepository", QualifiedName = "IRepository", FilePath = "IRepository.cls", Kind = SymbolKind.Interface, LineStart = 1, LineEnd = 20 };
        graph.Symbols.Add(iface);

        for (int i = 1; i <= 3; i++)
        {
            var caller = new Symbol { Id = Guid.NewGuid(), Name = $"Service_{i}", QualifiedName = $"Service_{i}", FilePath = $"Service_{i}.bas", Kind = SymbolKind.Module, LineStart = 1, LineEnd = 30 };
            graph.Symbols.Add(caller);
            graph.AddUsesType(caller, iface);
        }

        // Act
        var dataset = MartinMetricsCalculator.Analyze(graph);
        var ifaceItem = dataset.Items.FirstOrDefault(i => i.Id == iface.Id);

        // Assert
        ifaceItem.Should().NotBeNull();
        ifaceItem!.Abstractness.Should().Be(1.0);
        ifaceItem.AfferentCoupling.Should().Be(3);
        ifaceItem.EfferentCoupling.Should().Be(0);
        ifaceItem.Instability.Should().Be(0.0);
        ifaceItem.Distance.Should().Be(0.0, "Su A = 1.0 e I = 0.0 la distanza dalla retta principale è zero");
    }

    /// <summary>
    /// Verifies that an empty CodeGraph returns a consistent empty dataset without generating divisions by zero.
    /// </summary>
    [Fact]
    public void MartinMetrics_ShouldHandleEmptyGraph_Safely()
    {
        // Arrange
        var emptyGraph = new CodeGraph();

        // Act
        var dataset = MartinMetricsCalculator.Analyze(emptyGraph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Items.Should().BeEmpty();
        dataset.AverageDistance.Should().Be(0.0);
    }
}
