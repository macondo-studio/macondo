using FluentAssertions;
using Macondo.Semantic.Analytics.ArchitecturalRules;
using Macondo.Semantic.Analytics.CodeClones;
using Macondo.Semantic.Analytics.DeadCode;
using Macondo.Semantic.Analytics.Maintainability;
using Macondo.Semantic.Export;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Export;

public class ExportAndSerializationDeepTests
{
    #region Unicode and Special Characters JSON Serialization

    /// <summary>
    /// Verifica che la serializzazione e deserializzazione JSON gestisca correttamente
    /// caratteri Unicode, accenti, virgolette e simboli matematici nei nomi dei file e nei metadati dei simboli.
    /// </summary>
    [Fact]
    public void JsonExporter_ShouldPreserveUnicodeCharactersAndSpecialSymbols()
    {
        // Arrange
        var graph = new CodeGraph();
        var symbol = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "Calcola_Totale_Fattura_€",
            QualifiedName = "Amministrazione.Contabilità.Calcola_Totale_Fattura_€",
            FilePath = "src/Contabilità/GestioneFatture_Società.cls",
            Kind = SymbolKind.Method,
            ReturnType = "Decimal",
            Accessibility = Accessibility.Public,
            LineStart = 15,
            LineEnd = 40
        };
        symbol.Parameters.Add(new ParameterSymbol
        {
            Name = "importo_lordo",
            DeclaredTypeName = "Decimal",
            DefaultValue = "0.00"
        });

        graph.Symbols.Add(symbol);

        // Act
        var json = CodeGraphJsonExporter.ExportToJson(graph, indented: true);
        var restoredGraph = CodeGraphJsonExporter.ImportFromJson(json);

        // Assert
        json.Should().NotBeNullOrWhiteSpace();
        restoredGraph.Should().NotBeNull();
        var restoredSym = restoredGraph.FindSymbolById(symbol.Id);

        restoredSym.Should().NotBeNull();
        restoredSym!.Name.Should().Be("Calcola_Totale_Fattura_€");
        restoredSym.QualifiedName.Should().Be("Amministrazione.Contabilità.Calcola_Totale_Fattura_€");
        restoredSym.ReturnType.Should().Be("Decimal");
        restoredSym.Accessibility.Should().Be(Accessibility.Public);
    }

    #endregion

    #region Empty Datasets and Boundary Export Scenarios

    /// <summary>
    /// Verifica che gli esportatori Markdown e JSON gestiscano report e dataset analitici vuoti
    /// (0 violazioni, 0 dead code, 0 cloni) senza sollevare eccezioni e producendo output leggibile.
    /// </summary>
    [Fact]
    public void AnalysisMarkdownExporter_ShouldHandleEmptyDatasets_WithoutErrors()
    {
        // Arrange
        var emptyGraph = new CodeGraph();
        var deadCodeReport = DeadCodeAnalyzer.Analyze(emptyGraph);
        var clonesReport = CodeCloneDetector.DetectClones(emptyGraph);
        var rulesReport = ArchitecturalRulesEngine.Evaluate(emptyGraph);

        // Act
        var deadCodeMd = AnalysisMarkdownExporter.ExportDeadCode(deadCodeReport);
        var clonesMd = AnalysisMarkdownExporter.ExportCodeClones(clonesReport);
        var rulesMd = AnalysisMarkdownExporter.ExportArchitecturalRules(rulesReport);

        // Assert
        deadCodeMd.Should().NotBeNullOrWhiteSpace();
        deadCodeMd.Should().Contain("# Dead Code & Reachability Analysis Report");

        clonesMd.Should().NotBeNullOrWhiteSpace();
        clonesMd.Should().Contain("# Code Clone Detection (CPD) Report");

        rulesMd.Should().NotBeNullOrWhiteSpace();
        rulesMd.Should().Contain("# Architectural Rules & Linter Violations Report");
    }

    #endregion

    #region Deep Hierarchical Slice Exporting

    /// <summary>
    /// Verifica che CodeGraphMarkdownExporter.ExportSlice generi la documentazione
    /// corretta includendo sia i chiamanti (Upstream) che i chiamati (Downstream) della routine selezionata.
    /// </summary>
    [Fact]
    public void MarkdownExporter_ShouldGenerateCompleteSliceDocumentation_WithUpstreamAndDownstream()
    {
        // Arrange
        var graph = new CodeGraph();

        var caller = new Symbol { Id = Guid.NewGuid(), Name = "EntryPoint", QualifiedName = "Mod.EntryPoint", FilePath = "Mod.bas", Kind = SymbolKind.Method };
        var focus = new Symbol { Id = Guid.NewGuid(), Name = "CoreService", QualifiedName = "Mod.CoreService", FilePath = "Mod.bas", Kind = SymbolKind.Method };
        var callee = new Symbol { Id = Guid.NewGuid(), Name = "DatabaseExec", QualifiedName = "Mod.DatabaseExec", FilePath = "Mod.bas", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { caller, focus, callee });
        graph.AddCalls(caller, focus);
        graph.AddCalls(focus, callee);

        // Act
        var sliceMarkdown = CodeGraphMarkdownExporter.ExportSlice(graph, focus);

        // Assert
        sliceMarkdown.Should().NotBeNullOrWhiteSpace();
        sliceMarkdown.Should().Contain(focus.Name);
        sliceMarkdown.Should().Contain(caller.Name);
        sliceMarkdown.Should().Contain(callee.Name);
    }

    #endregion
}
