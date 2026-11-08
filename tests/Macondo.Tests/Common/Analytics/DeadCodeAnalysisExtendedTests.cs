using FluentAssertions;
using Macondo.Semantic.Analytics.DeadCode;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class DeadCodeAnalysisExtendedTests
{
    /// <summary>
    /// Verifica che in presenza di proprietà distinte (Property Get e Property Let),
    /// qualora solo Property Get venga invocata dall'applicazione, la Property Let mai referenziata
    /// venga contrassegnata come codice morto.
    /// </summary>
    [Fact]
    public void DeadCodeAnalysis_ShouldIdentifyUnusedPropertyAccessor_WhenGetterIsUsedButSetterIsNot()
    {
        // Arrange
        var graph = new CodeGraph();

        var mod = new Symbol { Id = Guid.NewGuid(), Name = "AppMod", QualifiedName = "AppMod", FilePath = "AppMod.bas", Kind = SymbolKind.Module };
        var subMain = new Symbol { Id = Guid.NewGuid(), Name = "Main", QualifiedName = "AppMod.Main", FilePath = "AppMod.bas", Kind = SymbolKind.Method };
        graph.Symbols.AddRange(new[] { mod, subMain });
        graph.AddContains(mod, subMain);

        var cls = new Symbol { Id = Guid.NewGuid(), Name = "AppSettings", QualifiedName = "AppSettings", FilePath = "AppSettings.cls", Kind = SymbolKind.Class };
        var propGet = new Symbol { Id = Guid.NewGuid(), Name = "Timeout_Get", QualifiedName = "AppSettings.Timeout_Get", FilePath = "AppSettings.cls", Kind = SymbolKind.Property, Accessibility = Accessibility.Public };
        var propLet = new Symbol { Id = Guid.NewGuid(), Name = "Timeout_Let", QualifiedName = "AppSettings.Timeout_Let", FilePath = "AppSettings.cls", Kind = SymbolKind.Property, Accessibility = Accessibility.Public, LineStart = 15, LineEnd = 25 };

        graph.Symbols.AddRange(new[] { cls, propGet, propLet });
        graph.AddContains(cls, propGet);
        graph.AddContains(cls, propLet);

        // Sub Main invoca solo propGet
        graph.AddCalls(subMain, propGet);

        // Act
        var dataset = DeadCodeAnalyzer.Analyze(graph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Items.Should().NotContain(i => i.Id == propGet.Id, "Il getter è invocato da Sub Main");
        dataset.Items.Should().Contain(i => i.Id == propLet.Id, "Il setter non è mai referenziato");
    }

    /// <summary>
    /// Verifica che l'analizzatore gestisca un CodeGraph vuoto senza errori,
    /// producendo un dataset con rapporto di codice morto pari a 0.
    /// </summary>
    [Fact]
    public void DeadCodeAnalysis_ShouldHandleEmptyGraph_Gracefully()
    {
        // Arrange
        var emptyGraph = new CodeGraph();

        // Act
        var dataset = DeadCodeAnalyzer.Analyze(emptyGraph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.TotalSymbolsAnalyzed.Should().Be(0);
        dataset.ReachableSymbolsCount.Should().Be(0);
        dataset.DeadCodeRatio.Should().Be(0.0);
        dataset.Items.Should().BeEmpty();
    }
}
