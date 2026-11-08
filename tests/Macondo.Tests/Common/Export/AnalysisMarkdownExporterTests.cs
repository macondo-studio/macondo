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
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Common.Export;

public class AnalysisMarkdownExporterTests
{
    [Fact]
    public void ExportDeadCode_ShouldGenerateFormattedMarkdownTable()
    {
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();
        var dataset = DeadCodeAnalyzer.Analyze(graph);

        var markdown = AnalysisMarkdownExporter.ExportDeadCode(dataset);

        markdown.Should().NotBeNullOrWhiteSpace();
        markdown.Should().Contain("# Dead Code & Reachability Analysis Report");
        markdown.Should().Contain("Total Symbols Analyzed");
        markdown.Should().Contain("Dead Code Ratio");
    }

    [Fact]
    public void ExportMaintainability_ShouldGenerateFormattedMarkdownTable()
    {
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();
        var dataset = MaintainabilityCalculator.Analyze(graph);

        var markdown = AnalysisMarkdownExporter.ExportMaintainability(dataset);

        markdown.Should().NotBeNullOrWhiteSpace();
        markdown.Should().Contain("# Maintainability Index & Halstead Metrics Report");
        markdown.Should().Contain("Average Maintainability Index (MI)");
        markdown.Should().Contain("Container Metrics Breakdown");
    }

    [Fact]
    public void ExportArchitecturalRules_ShouldGenerateSummaryAndViolationsTable()
    {
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();
        var report = ArchitecturalRulesEngine.Evaluate(graph);

        var markdown = AnalysisMarkdownExporter.ExportArchitecturalRules(report);

        markdown.Should().NotBeNullOrWhiteSpace();
        markdown.Should().Contain("# Architectural Rules & Linter Violations Report");
        markdown.Should().Contain("Rules Evaluated");
    }

    [Fact]
    public void ExportCodeClones_ShouldGenerateClonesReport()
    {
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();
        var report = CodeCloneDetector.DetectClones(graph);

        var markdown = AnalysisMarkdownExporter.ExportCodeClones(report);

        markdown.Should().NotBeNullOrWhiteSpace();
        markdown.Should().Contain("# Code Clone Detection (CPD) Report");
        markdown.Should().Contain("Duplicate Clone Groups");
    }

    [Fact]
    public void ExportCohesion_ShouldGenerateLcom4AndMetricsTable()
    {
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();
        var dataset = CohesionCalculator.Analyze(graph);

        var markdown = AnalysisMarkdownExporter.ExportCohesion(dataset);

        markdown.Should().NotBeNullOrWhiteSpace();
        markdown.Should().Contain("# Cohesion Analysis (LCOM4 & C&K) Report");
        markdown.Should().Contain("Average LCOM4");
        markdown.Should().Contain("Container Cohesion Breakdown");
    }

    [Fact]
    public void ExportMartinMetrics_ShouldGenerateCouplingAndZonesReport()
    {
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();
        var dataset = MartinMetricsCalculator.Analyze(graph);

        var markdown = AnalysisMarkdownExporter.ExportMartinMetrics(dataset);

        markdown.Should().NotBeNullOrWhiteSpace();
        markdown.Should().Contain("# Robert C. Martin Metrics Report");
        markdown.Should().Contain("Distance (D) from Main Sequence");
        markdown.Should().Contain("Zone of Pain");
    }

    [Fact]
    public void ExportSharedState_ShouldGenerateGlobalVariablesReport()
    {
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();
        var dataset = SharedStateAnalyzer.Analyze(graph);

        var markdown = AnalysisMarkdownExporter.ExportSharedState(dataset);

        markdown.Should().NotBeNullOrWhiteSpace();
        markdown.Should().Contain("# Shared Global State & Mutation Risk Report");
        markdown.Should().Contain("Total Shared Variables / Fields");
    }

    [Fact]
    public void ExportCrudMatrix_ShouldGenerateEntitiesMatrix()
    {
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();
        var dataset = CrudMatrixAnalyzer.Analyze(graph);

        var markdown = AnalysisMarkdownExporter.ExportCrudMatrix(dataset);

        markdown.Should().NotBeNullOrWhiteSpace();
        markdown.Should().Contain("# CRUD Operations & Data Entities Matrix Report");
        markdown.Should().Contain("Identified Data Entities");
    }

    [Fact]
    public void ExportDataFlow_ShouldGenerateTaintReport()
    {
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();
        var dataset = TaintAnalyzer.Analyze(graph);

        var markdown = AnalysisMarkdownExporter.ExportDataFlow(dataset);

        markdown.Should().NotBeNullOrWhiteSpace();
        markdown.Should().Contain("# Data Flow & Taint Tracking Report");
        markdown.Should().Contain("Total Source-to-Sink Taint Paths");
    }

    [Fact]
    public void ExportScreenFlow_ShouldGenerateNavigationReport()
    {
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();
        var screenFlow = ScreenFlowBuilder.Build(graph);

        var markdown = AnalysisMarkdownExporter.ExportScreenFlow(screenFlow);

        markdown.Should().NotBeNullOrWhiteSpace();
        markdown.Should().Contain("# Screen Flow & UI Navigation Report");
        markdown.Should().Contain("Total Screens / Forms");
    }

    [Fact]
    public void ExportDsm_ShouldGenerateCouplingAndCyclesTable()
    {
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();
        var dsm = DsmMatrixBuilder.Build(graph);

        var markdown = AnalysisMarkdownExporter.ExportDsm(dsm);

        markdown.Should().NotBeNullOrWhiteSpace();
        markdown.Should().Contain("# Design Structure Matrix (DSM) Report");
        markdown.Should().Contain("Matrix Size (Modules/Containers)");
    }

    [Fact]
    public void ExportImpact_ShouldGenerateBlastRadiusTable()
    {
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();
        var symbol = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method) ?? graph.Symbols.First();
        var impactAnalyzer = new ImpactAnalyzer(graph);
        var impactReport = impactAnalyzer.AnalyzeSymbolImpact(symbol);

        var markdown = AnalysisMarkdownExporter.ExportImpact(impactReport);

        markdown.Should().NotBeNullOrWhiteSpace();
        markdown.Should().Contain("# Blast Radius & Impact Analysis Report");
        markdown.Should().Contain("Target Symbol:");
        markdown.Should().Contain("Assessed Risk Level");
    }

    [Fact]
    public void ExportAuditReport_ShouldCombineAllAnalysesIntoComprehensiveDocument()
    {
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();
        var audit = AuditReportBuilder.RunAll(graph);

        var markdown = audit.ToMarkdown();

        markdown.Should().NotBeNullOrWhiteSpace();
        markdown.Should().Contain("# Architecture & Code Quality Audit Report");
        markdown.Should().Contain("1. Executive Summary & Health Dashboard");
        markdown.Should().Contain("2. Architectural Rules & Governance");
        markdown.Should().Contain("3. Maintainability & Technical Debt Candidates");
        markdown.Should().Contain("4. Dead Code & Orphaned Components");
        markdown.Should().Contain("6. Robert C. Martin Coupling & Stability Zones");
    }
}
