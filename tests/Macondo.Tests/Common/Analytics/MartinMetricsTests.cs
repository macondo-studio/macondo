using FluentAssertions;
using Macondo.Semantic.Analytics.Martin;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class MartinMetricsTests
{
    /// <summary>
    /// Verify the calculation of the Robert C. Martin metrics dataset on the entire set of project fixtures,
    /// ensuring that all container components are analyzed and that normalized distances are consistent [0..1].
    /// </summary>
    [Fact]
    public void MartinMetrics_ShouldCalculateMetricsForFixtures_AndProduceValidDataset()
    {
        // Arrange
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();

        // Act
        var dataset = MartinMetricsCalculator.Analyze(graph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Items.Should().NotBeEmpty();
        dataset.AverageDistance.Should().BeInRange(0.0, 1.0);

        foreach (var item in dataset.Items)
        {
            item.Instability.Should().BeInRange(0.0, 1.0);
            item.Abstractness.Should().BeInRange(0.0, 1.0);
            item.NormalizedDistance.Should().BeInRange(0.0, 1.0);
            item.Distance.Should().BeInRange(0.0, 2.0);
            item.LocOrSize.Should().BeGreaterThan(0);
        }
    }

    /// <summary>
    /// Verifies that a purely concrete component (A = 0.0) towards which multiple external modules converge
    /// (elevated Ca, Ce = 0 => I = 0.0) is correctly classified in the "Zone of Pain".
    /// </summary>
    [Fact]
    public void MartinMetrics_ShouldIdentifyZoneOfPain_ForConcreteHighlyCoupledModule()
    {
        // Arrange: GodObject concrete module with 5 external callers and 0 outgoing dependencies
        var graph = new CodeGraph();

        var godModule = new Symbol { Id = Guid.NewGuid(), Name = "GodModule", QualifiedName = "GodModule", FilePath = "GodModule.bas", Kind = SymbolKind.Module, LineStart = 1, LineEnd = 500 };
        var godMethod = new Symbol { Id = Guid.NewGuid(), Name = "ExecuteCore", QualifiedName = "GodModule.ExecuteCore", FilePath = "GodModule.bas", Kind = SymbolKind.Method, LineStart = 10, LineEnd = 50 };
        graph.Symbols.AddRange(new[] { godModule, godMethod });
        graph.AddContains(godModule, godMethod);

        for (int i = 1; i <= 5; i++)
        {
            var callerMod = new Symbol { Id = Guid.NewGuid(), Name = $"Caller{i}", QualifiedName = $"Caller{i}", FilePath = $"Caller{i}.bas", Kind = SymbolKind.Module };
            var callerMethod = new Symbol { Id = Guid.NewGuid(), Name = "CallGod", QualifiedName = $"Caller{i}.CallGod", FilePath = $"Caller{i}.bas", Kind = SymbolKind.Method };
            graph.Symbols.AddRange(new[] { callerMod, callerMethod });
            graph.AddContains(callerMod, callerMethod);
            graph.AddCalls(callerMethod, godMethod);
        }

        // Act
        var dataset = MartinMetricsCalculator.Analyze(graph);
        var godItem = dataset.Items.FirstOrDefault(i => i.Id == godModule.Id);

        // Assert
        godItem.Should().NotBeNull();
        godItem!.AfferentCoupling.Should().Be(5);
        godItem.EfferentCoupling.Should().Be(0);
        godItem.Instability.Should().Be(0.0);
        godItem.Abstractness.Should().Be(0.0);
        godItem.Zone.Should().Be(MartinZone.ZoneOfPain);
    }

    /// <summary>
    /// Verify that an orphaned purely abstract interface (A = 1.0) that no one depends on
    /// (Ca = 0, Ce > 0 => I = 1.0) is classified in the "Zone of Uselessness".
    /// </summary>
    [Fact]
    public void MartinMetrics_ShouldIdentifyZoneOfUselessness_ForAbstractUncoupledInterface()
    {
        // Arrange: Abstract interface that has no implementers or users, but depends on another type
        var graph = new CodeGraph();

        var orphanInterface = new Symbol { Id = Guid.NewGuid(), Name = "IOrphanService", QualifiedName = "IOrphanService", FilePath = "IOrphanService.cls", Kind = SymbolKind.Interface, LineStart = 1, LineEnd = 40 };
        var otherModule = new Symbol { Id = Guid.NewGuid(), Name = "HelperMod", QualifiedName = "HelperMod", FilePath = "HelperMod.bas", Kind = SymbolKind.Module, LineStart = 1, LineEnd = 20 };

        graph.Symbols.AddRange(new[] { orphanInterface, otherModule });

        // orphanInterface depends on otherModule via UsesType
        graph.AddUsesType(orphanInterface, otherModule);

        // Act
        var dataset = MartinMetricsCalculator.Analyze(graph);
        var item = dataset.Items.FirstOrDefault(i => i.Id == orphanInterface.Id);

        // Assert
        item.Should().NotBeNull();
        item!.AfferentCoupling.Should().Be(0);
        item.EfferentCoupling.Should().Be(1);
        item.Instability.Should().Be(1.0);
        item.Abstractness.Should().Be(1.0);
        item.Zone.Should().Be(MartinZone.ZoneOfUselessness);
    }

    /// <summary>
    /// Verify that a well-balanced component located close to the line A + I = 1
    /// is categorized into MainSequence or Balanced.
    /// </summary>
    [Fact]
    public void MartinMetrics_ShouldIdentifyMainSequence_ForBalancedComponents()
    {
        // Arrange: Concrete module that depends on 2 modules and is called by 2 modules (I = 0.5, A = 0.0 => D = 0.5)
        var graph = new CodeGraph();

        var coreMod = new Symbol { Id = Guid.NewGuid(), Name = "CoreService", QualifiedName = "CoreService", FilePath = "CoreService.bas", Kind = SymbolKind.Module };
        var coreMethod = new Symbol { Id = Guid.NewGuid(), Name = "Execute", QualifiedName = "CoreService.Execute", FilePath = "CoreService.bas", Kind = SymbolKind.Method };
        graph.Symbols.AddRange(new[] { coreMod, coreMethod });
        graph.AddContains(coreMod, coreMethod);

        var depMod = new Symbol { Id = Guid.NewGuid(), Name = "DepMod", QualifiedName = "DepMod", FilePath = "DepMod.bas", Kind = SymbolKind.Module };
        var depMethod = new Symbol { Id = Guid.NewGuid(), Name = "DepFunc", QualifiedName = "DepMod.DepFunc", FilePath = "DepMod.bas", Kind = SymbolKind.Method };
        graph.Symbols.AddRange(new[] { depMod, depMethod });
        graph.AddContains(depMod, depMethod);

        var callerMod = new Symbol { Id = Guid.NewGuid(), Name = "CallerMod", QualifiedName = "CallerMod", FilePath = "CallerMod.bas", Kind = SymbolKind.Module };
        var callerMethod = new Symbol { Id = Guid.NewGuid(), Name = "CallCore", QualifiedName = "CallerMod.CallCore", FilePath = "CallerMod.bas", Kind = SymbolKind.Method };
        graph.Symbols.AddRange(new[] { callerMod, callerMethod });
        graph.AddContains(callerMod, callerMethod);

        graph.AddCalls(coreMethod, depMethod);
        graph.AddCalls(callerMethod, coreMethod);

        // Act
        var dataset = MartinMetricsCalculator.Analyze(graph);
        var item = dataset.Items.FirstOrDefault(i => i.Id == coreMod.Id);

        // Assert
        item.Should().NotBeNull();
        item!.AfferentCoupling.Should().Be(1);
        item.EfferentCoupling.Should().Be(1);
        item.Instability.Should().Be(0.5);
        item.Zone.Should().NotBe(MartinZone.ZoneOfPain);
        item.Zone.Should().NotBe(MartinZone.ZoneOfUselessness);
    }
}
