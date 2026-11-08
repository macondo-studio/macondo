using FluentAssertions;
using Macondo.Semantic.Analytics.SharedState;
using Macondo.Semantic.Impact;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class SharedStateAnalysisTests
{
    /// <summary>
    /// Verifica che l'analizzatore individui correttamente variabili con visibilità Public o Global
    /// dichiarate all'interno di moduli standard (.bas) o classi.
    /// </summary>
    [Fact]
    public void SharedStateAnalysis_ShouldDetectGlobalVariables_InStandardModules()
    {
        // Arrange
        var graph = new CodeGraph();

        var globMod = new Symbol { Id = Guid.NewGuid(), Name = "Globals", QualifiedName = "Globals", FilePath = "Globals.bas", Kind = SymbolKind.Module };
        var pubVar = new Symbol { Id = Guid.NewGuid(), Name = "g_CurrentUser", QualifiedName = "Globals.g_CurrentUser", FilePath = "Globals.bas", Kind = SymbolKind.Field, Accessibility = Accessibility.Public };
        var privVar = new Symbol { Id = Guid.NewGuid(), Name = "m_InternalCounter", QualifiedName = "Globals.m_InternalCounter", FilePath = "Globals.bas", Kind = SymbolKind.Field, Accessibility = Accessibility.Private };

        graph.Symbols.AddRange(new[] { globMod, pubVar, privVar });
        graph.AddContains(globMod, pubVar);
        graph.AddContains(globMod, privVar);

        // Act
        var dataset = SharedStateAnalyzer.Analyze(graph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Items.Should().HaveCount(1);
        dataset.Items[0].VariableName.Should().Be("g_CurrentUser");
        dataset.Items[0].ModuleName.Should().Be("Globals");
    }

    /// <summary>
    /// Verifica che l'analizzatore tracci e distingua lettori (Reads) e scrittori (Writes)
    /// identificando se l'accesso avviene da un modulo differente (IsCrossModule).
    /// </summary>
    [Fact]
    public void SharedStateAnalysis_ShouldTrackReadersAndWriters_AndDistinguishCrossModuleAccess()
    {
        // Arrange
        var graph = new CodeGraph();

        var stateMod = new Symbol { Id = Guid.NewGuid(), Name = "StateMod", QualifiedName = "StateMod", FilePath = "StateMod.bas", Kind = SymbolKind.Module };
        var globalState = new Symbol { Id = Guid.NewGuid(), Name = "g_SystemStatus", QualifiedName = "StateMod.g_SystemStatus", FilePath = "StateMod.bas", Kind = SymbolKind.Field, Accessibility = Accessibility.Global };
        var localInit = new Symbol { Id = Guid.NewGuid(), Name = "InitStatus", QualifiedName = "StateMod.InitStatus", FilePath = "StateMod.bas", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { stateMod, globalState, localInit });
        graph.AddContains(stateMod, globalState);
        graph.AddContains(stateMod, localInit);

        // localInit scrive su g_SystemStatus nello stesso modulo
        graph.Relationships.Add(new Relationship
        {
            SourceId = localInit.Id,
            TargetId = globalState.Id,
            Kind = RelationshipKind.Writes,
            Confidence = ResolutionConfidence.Exact
        });

        // Modulo esterno che legge g_SystemStatus
        var readerMod = new Symbol { Id = Guid.NewGuid(), Name = "ReaderMod", QualifiedName = "ReaderMod", FilePath = "ReaderMod.bas", Kind = SymbolKind.Module };
        var readerRoutine = new Symbol { Id = Guid.NewGuid(), Name = "CheckStatus", QualifiedName = "ReaderMod.CheckStatus", FilePath = "ReaderMod.bas", Kind = SymbolKind.Method };
        graph.Symbols.AddRange(new[] { readerMod, readerRoutine });
        graph.AddContains(readerMod, readerRoutine);

        graph.Relationships.Add(new Relationship
        {
            SourceId = readerRoutine.Id,
            TargetId = globalState.Id,
            Kind = RelationshipKind.Reads,
            Confidence = ResolutionConfidence.Exact
        });

        // Act
        var dataset = SharedStateAnalyzer.Analyze(graph);

        // Assert
        var item = dataset.Items.FirstOrDefault(i => i.VariableId == globalState.Id);
        item.Should().NotBeNull();
        item!.TotalAccessorsCount.Should().Be(2);
        item.WritersCount.Should().Be(1);
        item.ReadersCount.Should().Be(1);
        item.CrossModuleWritersCount.Should().Be(0);

        var readerAccess = item.Accessors.First(a => a.RoutineId == readerRoutine.Id);
        readerAccess.IsCrossModule.Should().BeTrue();
        readerAccess.AccessKind.Should().Be(SharedStateAccessKind.Read);
    }

    /// <summary>
    /// Verifica che una variabile globale modificata da molteplici moduli esterni differenti (>= 4)
    /// venga categorizzata con livello di rischio di mutazione Critical.
    /// </summary>
    [Fact]
    public void SharedStateAnalysis_ShouldAssignCriticalRisk_WhenMultipleCrossModulesWriteToVariable()
    {
        // Arrange
        var graph = new CodeGraph();

        var stateMod = new Symbol { Id = Guid.NewGuid(), Name = "StateMod", QualifiedName = "StateMod", FilePath = "StateMod.bas", Kind = SymbolKind.Module };
        var sharedVar = new Symbol { Id = Guid.NewGuid(), Name = "g_AppState", QualifiedName = "StateMod.g_AppState", FilePath = "StateMod.bas", Kind = SymbolKind.Field, Accessibility = Accessibility.Public };
        graph.Symbols.AddRange(new[] { stateMod, sharedVar });
        graph.AddContains(stateMod, sharedVar);

        for (int i = 1; i <= 4; i++)
        {
            var writerMod = new Symbol { Id = Guid.NewGuid(), Name = $"WriterMod{i}", QualifiedName = $"WriterMod{i}", FilePath = $"WriterMod{i}.bas", Kind = SymbolKind.Module };
            var writerMethod = new Symbol { Id = Guid.NewGuid(), Name = $"Mutate{i}", QualifiedName = $"WriterMod{i}.Mutate{i}", FilePath = $"WriterMod{i}.bas", Kind = SymbolKind.Method };
            graph.Symbols.AddRange(new[] { writerMod, writerMethod });
            graph.AddContains(writerMod, writerMethod);

            graph.Relationships.Add(new Relationship
            {
                SourceId = writerMethod.Id,
                TargetId = sharedVar.Id,
                Kind = RelationshipKind.Writes,
                Confidence = ResolutionConfidence.Exact
            });
        }

        // Act
        var dataset = SharedStateAnalyzer.Analyze(graph);
        var item = dataset.Items.First(i => i.VariableId == sharedVar.Id);

        // Assert
        item.CrossModuleWritersCount.Should().Be(4);
        item.MutationRisk.Should().Be(ImpactRiskLevel.Critical);
        dataset.CriticalVariablesCount.Should().Be(1);
    }

    /// <summary>
    /// Verifica che una variabile globale che non subisce alcuna scrittura
    /// mantenga un livello di rischio Low.
    /// </summary>
    [Fact]
    public void SharedStateAnalysis_ShouldAssignLowRisk_WhenVariableIsOnlyRead()
    {
        // Arrange
        var graph = new CodeGraph();

        var stateMod = new Symbol { Id = Guid.NewGuid(), Name = "ConfigMod", QualifiedName = "ConfigMod", FilePath = "ConfigMod.bas", Kind = SymbolKind.Module };
        var readOnlyGlobal = new Symbol { Id = Guid.NewGuid(), Name = "g_AppVersion", QualifiedName = "ConfigMod.g_AppVersion", FilePath = "ConfigMod.bas", Kind = SymbolKind.Field, Accessibility = Accessibility.Public };
        graph.Symbols.AddRange(new[] { stateMod, readOnlyGlobal });
        graph.AddContains(stateMod, readOnlyGlobal);

        // Act
        var dataset = SharedStateAnalyzer.Analyze(graph);

        // Assert
        var item = dataset.Items.First(i => i.VariableId == readOnlyGlobal.Id);
        item.WritersCount.Should().Be(0);
        item.MutationRisk.Should().Be(ImpactRiskLevel.Low);
    }
}
