using FluentAssertions;
using Macondo.Semantic.Analytics.ArchitecturalRules;
using Macondo.Semantic.Analytics.CodeClones;
using Macondo.Semantic.Analytics.Cohesion;
using Macondo.Semantic.Analytics.DataFlow;
using Macondo.Semantic.Analytics.DataMatrix;
using Macondo.Semantic.Analytics.DeadCode;
using Macondo.Semantic.Analytics.Dsm;
using Macondo.Semantic.Analytics.Maintainability;
using Macondo.Semantic.Analytics.Martin;
using Macondo.Semantic.Analytics.ScreenFlow;
using Macondo.Semantic.Analytics.SharedState;
using Macondo.Semantic.Export;
using Macondo.Semantic.Impact;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Export;

public class AnalysisExportersComprehensiveEdgeCasesTests
{
    /// <summary>
    /// Verifica che AnalysisMarkdownExporter esporti formattazioni Markdown coerenti
    /// e valide per tutti i dataset analitici quando generati a partire da un CodeGraph vuoto.
    /// </summary>
    [Fact]
    public void MarkdownExporter_ShouldExportAllAnalyzersSafely_ForEmptyGraphDatasets()
    {
        // Arrange
        var emptyGraph = new CodeGraph();

        var deadCode = DeadCodeAnalyzer.Analyze(emptyGraph);
        var maintainability = MaintainabilityCalculator.Analyze(emptyGraph);
        var rules = ArchitecturalRulesEngine.Evaluate(emptyGraph);
        var clones = CodeCloneDetector.DetectClones(emptyGraph);
        var cohesion = CohesionCalculator.Analyze(emptyGraph);
        var martin = MartinMetricsCalculator.Analyze(emptyGraph);
        var sharedState = SharedStateAnalyzer.Analyze(emptyGraph);
        var crud = CrudMatrixAnalyzer.Analyze(emptyGraph);
        var dataFlow = TaintAnalyzer.Analyze(emptyGraph);
        var screenFlow = ScreenFlowBuilder.Build(emptyGraph);
        var dsm = DsmMatrixBuilder.Build(emptyGraph);

        // Act & Assert
        var mdDeadCode = AnalysisMarkdownExporter.ExportDeadCode(deadCode);
        mdDeadCode.Should().NotBeNullOrWhiteSpace();

        var mdMaintainability = AnalysisMarkdownExporter.ExportMaintainability(maintainability);
        mdMaintainability.Should().NotBeNullOrWhiteSpace();

        var mdRules = AnalysisMarkdownExporter.ExportArchitecturalRules(rules);
        mdRules.Should().NotBeNullOrWhiteSpace();

        var mdClones = AnalysisMarkdownExporter.ExportCodeClones(clones);
        mdClones.Should().NotBeNullOrWhiteSpace();

        var mdCohesion = AnalysisMarkdownExporter.ExportCohesion(cohesion);
        mdCohesion.Should().NotBeNullOrWhiteSpace();

        var mdMartin = AnalysisMarkdownExporter.ExportMartinMetrics(martin);
        mdMartin.Should().NotBeNullOrWhiteSpace();

        var mdSharedState = AnalysisMarkdownExporter.ExportSharedState(sharedState);
        mdSharedState.Should().NotBeNullOrWhiteSpace();

        var mdCrud = AnalysisMarkdownExporter.ExportCrudMatrix(crud);
        mdCrud.Should().NotBeNullOrWhiteSpace();

        var mdDataFlow = AnalysisMarkdownExporter.ExportDataFlow(dataFlow);
        mdDataFlow.Should().NotBeNullOrWhiteSpace();

        var mdScreenFlow = AnalysisMarkdownExporter.ExportScreenFlow(screenFlow);
        mdScreenFlow.Should().NotBeNullOrWhiteSpace();

        var mdDsm = AnalysisMarkdownExporter.ExportDsm(dsm);
        mdDsm.Should().NotBeNullOrWhiteSpace();

        var audit = AuditReportBuilder.RunAll(emptyGraph);
        var mdAudit = audit.ToMarkdown();
        mdAudit.Should().NotBeNullOrWhiteSpace();
    }
}
