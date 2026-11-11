using System.Text.Json;
using Macondo.Semantic.Analytics.ArchitecturalFlow;
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
using Macondo.Semantic.Impact;
using Macondo.Semantic.Model;

namespace Macondo.Semantic.Export;

/// <summary>
/// Container of the results of all semantic analyzes performed on a <see cref="CodeGraph"/>.
/// </summary>
public sealed class AuditReportResult
{
    public required CodeGraph Graph { get; init; }
    public required DeadCodeDataset DeadCode { get; init; }
    public required MaintainabilityDataset Maintainability { get; init; }
    public required ArchitecturalRulesReport Rules { get; init; }
    public required CodeCloneReport Clones { get; init; }
    public required CohesionDataset Cohesion { get; init; }
    public required MartinMetricsDataset Martin { get; init; }
    public required SharedStateDataset SharedState { get; init; }
    public required CrudMatrixDataset CrudMatrix { get; init; }
    public required DataFlowDataset DataFlow { get; init; }
    public required DsmMatrix Dsm { get; init; }
    public required ScreenFlowGraph ScreenFlow { get; init; }

    public string ToMarkdown()
    {
        return AnalysisMarkdownExporter.ExportAuditReport(
            Graph, DeadCode, Maintainability, Rules, Clones, Cohesion, Martin, SharedState, CrudMatrix, DataFlow, Dsm, ScreenFlow);
    }

    public string ToJson(bool indented = true)
    {
        return AnalysisJsonExporter.ExportAuditReportToJson(
            Graph, DeadCode, Maintainability, Rules, Clones, Cohesion, Martin, SharedState, CrudMatrix, DataFlow, Dsm, ScreenFlow, indented);
    }
}

/// <summary>
/// Orchestrator engine for batch execution of all semantic analyzes and generation of comprehensive reports.
/// </summary>
public static class AuditReportBuilder
{
    /// <summary>
    /// Performs all analysis on the passed CodeGraph and produces a <see cref="AuditReportResult"/>.
    /// </summary>
    public static AuditReportResult RunAll(CodeGraph graph, IEnumerable<ArchitecturalRule>? customRules = null, Action<string>? progressCallback = null)
    {
        progressCallback?.Invoke("Executing Dead Code Analysis...");
        var deadCode = DeadCodeAnalyzer.Analyze(graph);

        progressCallback?.Invoke("Executing Maintainability & Halstead Metrics...");
        var maintainability = MaintainabilityCalculator.Analyze(graph);

        progressCallback?.Invoke("Evaluating Architectural Rules & Constraints...");
        var rules = ArchitecturalRulesEngine.Evaluate(graph, customRules);

        progressCallback?.Invoke("Detecting Duplicate Code Blocks & Clones (CPD)...");
        var clones = CodeCloneDetector.DetectClones(graph);

        progressCallback?.Invoke("Calculating Class Cohesion (LCOM4 & C&K)...");
        var cohesion = CohesionCalculator.Analyze(graph);

        progressCallback?.Invoke("Calculating Martin Coupling & Instability Metrics...");
        var martin = MartinMetricsCalculator.Analyze(graph);

        progressCallback?.Invoke("Analyzing Shared Global State & Mutation Risk...");
        var sharedState = SharedStateAnalyzer.Analyze(graph);

        progressCallback?.Invoke("Building CRUD Operations & Data Entities Matrix...");
        var crud = CrudMatrixAnalyzer.Analyze(graph);

        progressCallback?.Invoke("Analyzing Data Flow & Taint Source-to-Sink Paths...");
        var dataFlow = TaintAnalyzer.Analyze(graph);

        progressCallback?.Invoke("Constructing Design Structure Matrix (DSM)...");
        var dsm = DsmMatrixBuilder.Build(graph);

        progressCallback?.Invoke("Extracting UI Navigation & Screen Flow...");
        var screenFlow = ScreenFlowBuilder.Build(graph);

        return new AuditReportResult
        {
            Graph = graph,
            DeadCode = deadCode,
            Maintainability = maintainability,
            Rules = rules,
            Clones = clones,
            Cohesion = cohesion,
            Martin = martin,
            SharedState = sharedState,
            CrudMatrix = crud,
            DataFlow = dataFlow,
            Dsm = dsm,
            ScreenFlow = screenFlow
        };
    }
}
