using System.Text.Json;
using FluentAssertions;
using Macondo.Semantic.Analytics.DeadCode;
using Macondo.Semantic.Analytics.Dsm;
using Macondo.Semantic.Analytics.Maintainability;
using Macondo.Semantic.Export;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Common.Export;

public class AnalysisJsonExporterTests
{
    [Fact]
    public void ExportToJson_DeadCode_ShouldProduceValidJson()
    {
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();
        var dataset = DeadCodeAnalyzer.Analyze(graph);

        var json = AnalysisJsonExporter.ExportToJson(dataset);

        json.Should().NotBeNullOrWhiteSpace();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("totalSymbolsAnalyzed", out _).Should().BeTrue();
    }

    [Fact]
    public void ExportToJson_Maintainability_ShouldProduceValidJson()
    {
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();
        var dataset = MaintainabilityCalculator.Analyze(graph);

        var json = AnalysisJsonExporter.ExportToJson(dataset);

        json.Should().NotBeNullOrWhiteSpace();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("averageMaintainabilityIndex", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("items", out var items).Should().BeTrue();
        items.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public void ExportDsmToJson_ShouldNotThrowOnMultiDimensionalArray()
    {
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();
        var dsm = DsmMatrixBuilder.Build(graph);

        // Act & Assert
        var json = AnalysisJsonExporter.ExportDsmToJson(dsm);
        json.Should().NotBeNullOrWhiteSpace();

        var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("size", out var sizeProp).Should().BeTrue();
        sizeProp.GetInt32().Should().Be(dsm.Size);
        doc.RootElement.TryGetProperty("nonEmptyCells", out var cellsProp).Should().BeTrue();
        cellsProp.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public void ExportAuditReportToJson_ShouldSerializeCompleteAuditDto()
    {
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();
        var audit = AuditReportBuilder.RunAll(graph);

        var json = audit.ToJson(indented: true);

        json.Should().NotBeNullOrWhiteSpace();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("version", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("summary", out var summary).Should().BeTrue();
        summary.TryGetProperty("totalSymbols", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("deadCode", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("maintainability", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("architecturalRules", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("codeClones", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("dsm", out _).Should().BeTrue();
    }
}
