using FluentAssertions;
using Macondo.Semantic.Analytics.Martin;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class MartinMetricsTests
{
    /// <summary>
    /// Verifica il calcolo del dataset delle metriche di Robert C. Martin sull'intero set di fixture di progetto,
    /// accertando che tutti i componenti container vengano analizzati e che le distanze normalizzate siano coerenti [0..1].
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
    /// Verifica che un componente puramente concreto (A = 0.0) verso cui convergono molteplici moduli esterni
    /// (Ca elevato, Ce = 0 => I = 0.0) venga classificato correttamente nella "Zone of Pain" (Zona di Sofferenza).
    /// </summary>
    [Fact]
    public void MartinMetrics_ShouldIdentifyZoneOfPain_ForConcreteHighlyCoupledModule()
    {
        // Arrange: Modulo concreto GodObject con 5 chiamanti esterni e 0 dipendenze in uscita
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
    /// Verifica che un'interfaccia puramente astratta (A = 1.0) orfana da cui nessuno dipende
    /// (Ca = 0, Ce > 0 => I = 1.0) venga classificata nella "Zone of Uselessness" (Zona di Inutilità).
    /// </summary>
    [Fact]
    public void MartinMetrics_ShouldIdentifyZoneOfUselessness_ForAbstractUncoupledInterface()
    {
        // Arrange: Interfaccia astratta che non ha implementatori né utilizzatori, ma dipende da un altro tipo
        var graph = new CodeGraph();

        var orphanInterface = new Symbol { Id = Guid.NewGuid(), Name = "IOrphanService", QualifiedName = "IOrphanService", FilePath = "IOrphanService.cls", Kind = SymbolKind.Interface, LineStart = 1, LineEnd = 40 };
        var otherModule = new Symbol { Id = Guid.NewGuid(), Name = "HelperMod", QualifiedName = "HelperMod", FilePath = "HelperMod.bas", Kind = SymbolKind.Module, LineStart = 1, LineEnd = 20 };

        graph.Symbols.AddRange(new[] { orphanInterface, otherModule });

        // orphanInterface dipende da otherModule tramite UsesType
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
    /// Verifica che un componente ben bilanciato situato vicino alla retta A + I = 1
    /// venga categorizzato in MainSequence o Balanced.
    /// </summary>
    [Fact]
    public void MartinMetrics_ShouldIdentifyMainSequence_ForBalancedComponents()
    {
        // Arrange: Modulo concreto che dipende da 2 moduli e viene chiamato da 2 moduli (I = 0.5, A = 0.0 => D = 0.5)
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
