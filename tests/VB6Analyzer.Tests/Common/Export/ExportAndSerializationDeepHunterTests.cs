using System.Text.Json;
using FluentAssertions;
using Macondo.Semantic.Analytics.ArchitecturalRules;
using Macondo.Semantic.Analytics.Cohesion;
using Macondo.Semantic.Analytics.DeadCode;
using Macondo.Semantic.Analytics.Dsm;
using Macondo.Semantic.Analytics.Maintainability;
using Macondo.Semantic.Analytics.Martin;
using Macondo.Semantic.Export;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Export;

public class ExportAndSerializationDeepHunterTests
{
    #region Special Characters and Unicode Escaping in Exporters

    /// <summary>
    /// Verifica che AnalysisJsonExporter e AnalysisMarkdownExporter gestiscano correttamente simboli con caratteri speciali
    /// (simboli matematici, caratteri XML/HTML come &lt;, &gt;, &amp;, virgolette, Unicode e percorsi complessi).
    /// </summary>
    [Fact]
    public void Exporters_ShouldHandleUnicodeAndSpecialCharacters_WithoutErrors()
    {
        // Arrange
        var graph = new CodeGraph();
        var specialMod = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "Special_Mod<T>&Values'\"_äöü_日本語",
            QualifiedName = "MyApp.Special_Mod<T>&Values'\"_äöü_日本語",
            FilePath = "src/Sub & Folder/Special_Mod<T>.cs",
            Kind = SymbolKind.Class,
            LineStart = 1,
            LineEnd = 100
        };

        var specialMethod = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "Process<K, V> & Return(True)",
            QualifiedName = "MyApp.Special_Mod<T>&Values'\"_äöü_日本語.Process<K, V>",
            FilePath = "src/Sub & Folder/Special_Mod<T>.cs",
            Kind = SymbolKind.Method,
            LineStart = 10,
            LineEnd = 50
        };

        graph.Symbols.AddRange(new[] { specialMod, specialMethod });
        graph.AddContains(specialMod, specialMethod);

        // Act
        var deadCode = DeadCodeAnalyzer.Analyze(graph);
        var jsonDeadCode = AnalysisJsonExporter.ExportToJson(deadCode);
        var mdDeadCode = AnalysisMarkdownExporter.ExportDeadCode(deadCode);

        var maintainability = MaintainabilityCalculator.Analyze(graph);
        var jsonMi = AnalysisJsonExporter.ExportToJson(maintainability);
        var mdMi = AnalysisMarkdownExporter.ExportMaintainability(maintainability);

        // Assert
        jsonDeadCode.Should().NotBeNullOrWhiteSpace();
        mdDeadCode.Should().NotBeNullOrWhiteSpace();
        jsonMi.Should().NotBeNullOrWhiteSpace();
        mdMi.Should().NotBeNullOrWhiteSpace();

        var parsedJson = JsonDocument.Parse(jsonDeadCode);
        parsedJson.Should().NotBeNull();
    }

    #endregion

    #region Empty CodeGraph Export Robustness

    /// <summary>
    /// Verifica che tutti i metodi di esportazione JSON e Markdown funzionino correttamente su un CodeGraph completamente vuoto.
    /// </summary>
    [Fact]
    public void Exporters_ShouldHandleEmptyGraph_Gracefully()
    {
        // Arrange
        var emptyGraph = new CodeGraph();

        // Act
        var deadCode = DeadCodeAnalyzer.Analyze(emptyGraph);
        var jsonDeadCode = AnalysisJsonExporter.ExportToJson(deadCode);
        var mdDeadCode = AnalysisMarkdownExporter.ExportDeadCode(deadCode);

        var maintainability = MaintainabilityCalculator.Analyze(emptyGraph);
        var jsonMi = AnalysisJsonExporter.ExportToJson(maintainability);
        var mdMi = AnalysisMarkdownExporter.ExportMaintainability(maintainability);

        var cohesion = CohesionCalculator.Analyze(emptyGraph);
        var mdCohesion = AnalysisMarkdownExporter.ExportCohesion(cohesion);

        var martin = MartinMetricsCalculator.Analyze(emptyGraph);
        var mdMartin = AnalysisMarkdownExporter.ExportMartinMetrics(martin);

        var dsm = DsmMatrixBuilder.Build(emptyGraph);
        var jsonDsm = AnalysisJsonExporter.ExportDsmToJson(dsm);

        // Assert
        jsonDeadCode.Should().NotBeNullOrWhiteSpace();
        mdDeadCode.Should().NotBeNullOrWhiteSpace();
        jsonMi.Should().NotBeNullOrWhiteSpace();
        mdMi.Should().NotBeNullOrWhiteSpace();
        mdCohesion.Should().NotBeNullOrWhiteSpace();
        mdMartin.Should().NotBeNullOrWhiteSpace();
        jsonDsm.Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region Audit Report Full Roundtrip and Structure Integrity

    /// <summary>
    /// Verifica che AuditReportBuilder generi sezioni complete e serializzabili
    /// e che il parsing del JSON risultante contenga tutte le proprietà chiave attese.
    /// </summary>
    [Fact]
    public void AuditReportBuilder_ShouldProduceCompleteStructuredOutput_ForMultiComponentGraph()
    {
        // Arrange
        var graph = new CodeGraph();
        var modA = new Symbol { Id = Guid.NewGuid(), Name = "ModA", QualifiedName = "ModA", FilePath = "ModA.bas", Kind = SymbolKind.Module, LineStart = 1, LineEnd = 50 };
        var mthA = new Symbol { Id = Guid.NewGuid(), Name = "DoA", QualifiedName = "ModA.DoA", FilePath = "ModA.bas", Kind = SymbolKind.Method, Accessibility = Accessibility.Public, LineStart = 10, LineEnd = 40 };

        var modB = new Symbol { Id = Guid.NewGuid(), Name = "ModB", QualifiedName = "ModB", FilePath = "ModB.cls", Kind = SymbolKind.Class, LineStart = 1, LineEnd = 80 };
        var mthB = new Symbol { Id = Guid.NewGuid(), Name = "DoB", QualifiedName = "ModB.DoB", FilePath = "ModB.cls", Kind = SymbolKind.Method, Accessibility = Accessibility.Public, LineStart = 15, LineEnd = 60 };

        graph.Symbols.AddRange(new[] { modA, mthA, modB, mthB });
        graph.AddContains(modA, mthA);
        graph.AddContains(modB, mthB);
        graph.AddCalls(mthA, mthB);

        // Act
        var audit = AuditReportBuilder.RunAll(graph);
        var json = audit.ToJson(indented: true);
        var md = audit.ToMarkdown();

        // Assert
        json.Should().NotBeNullOrWhiteSpace();
        md.Should().NotBeNullOrWhiteSpace();

        var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("summary", out var summary).Should().BeTrue();
        summary.GetProperty("totalSymbols").GetInt32().Should().Be(4);
    }

    #endregion
}
