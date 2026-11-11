using System.Text.Json;
using System.Text.Json.Serialization;
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
/// Full audit report DTO for export in structured JSON format.
/// </summary>
public sealed class AuditReportDto
{
    public int Version { get; set; } = 1;
    public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;
    public AuditSummaryDto Summary { get; set; } = new();
    public DeadCodeDataset? DeadCode { get; set; }
    public MaintainabilityDataset? Maintainability { get; set; }
    public ArchitecturalRulesReport? ArchitecturalRules { get; set; }
    public CodeCloneReport? CodeClones { get; set; }
    public CohesionDataset? Cohesion { get; set; }
    public MartinMetricsDataset? MartinMetrics { get; set; }
    public SharedStateDataset? SharedState { get; set; }
    public CrudMatrixDataset? CrudMatrix { get; set; }
    public DataFlowDataset? DataFlow { get; set; }
    public ScreenFlowGraph? ScreenFlow { get; set; }
    public DsmMatrixDto? Dsm { get; set; }
}

public sealed class AuditSummaryDto
{
    public int TotalSymbols { get; set; }
    public int TotalRelationships { get; set; }
    public double AverageMaintainabilityIndex { get; set; }
    public double DeadCodeRatio { get; set; }
    public int ArchitecturalViolations { get; set; }
    public int DuplicateCloneGroups { get; set; }
    public int RefactoringCandidatesCount { get; set; }
    public int GodObjectsCount { get; set; }
    public int HighRiskSharedVariablesCount { get; set; }
    public int TaintPathsCount { get; set; }
}

public sealed class DsmCellDto
{
    public int RowIndex { get; set; }
    public int ColumnIndex { get; set; }
    public Guid RowContainerId { get; set; }
    public Guid ColumnContainerId { get; set; }
    public int Weight { get; set; }
    public bool IsInCycle { get; set; }
    public string DependencyKinds { get; set; } = string.Empty;
}

public sealed class DsmMatrixDto
{
    public int Size { get; set; }
    public int TotalDependencies { get; set; }
    public int TotalCyclicDependencies { get; set; }
    public List<DsmItem> Items { get; set; } = new();
    public List<List<Guid>> StronglyConnectedComponents { get; set; } = new();
    public List<DsmCellDto> NonEmptyCells { get; set; } = new();

    public static DsmMatrixDto FromMatrix(DsmMatrix dsm)
    {
        var dto = new DsmMatrixDto
        {
            Size = dsm.Size,
            TotalDependencies = dsm.TotalDependencies,
            TotalCyclicDependencies = dsm.TotalCyclicDependencies,
            Items = dsm.Items,
            StronglyConnectedComponents = dsm.StronglyConnectedComponents
        };

        int n = dsm.Size;
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                var cell = dsm.GetCell(r, c);
                if (cell != null && (cell.Weight > 0 || cell.IsInCycle))

                    dto.NonEmptyCells.Add(new DsmCellDto
                    {
                        RowIndex = cell.RowIndex,
                        ColumnIndex = cell.ColumnIndex,
                        RowContainerId = cell.RowContainerId,
                        ColumnContainerId = cell.ColumnContainerId,
                        Weight = cell.Weight,
                        IsInCycle = cell.IsInCycle,
                        DependencyKinds = cell.DependencyKinds.ToString()
                    });
            }
        }

        return dto;
    }
}

/// <summary>
/// JSON exporter for analytics datasets and aggregate audit report.
/// </summary>
public static class AnalysisJsonExporter
{
    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions CompactOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Serialize any object or analytics dataset into JSON string.
    /// </summary>
    public static string ExportToJson<T>(T data, bool indented = true)
    {
        if (data is DsmMatrix dsm)

            return ExportDsmToJson(dsm, indented);

        return JsonSerializer.Serialize(data, indented ? DefaultOptions : CompactOptions);
    }

    /// <summary>
    /// Serializes the DSM array by converting it to a DTO without unsupported multidimensional arrays.
    /// </summary>
    public static string ExportDsmToJson(DsmMatrix dsm, bool indented = true)
    {
        var dto = DsmMatrixDto.FromMatrix(dsm);
        return JsonSerializer.Serialize(dto, indented ? DefaultOptions : CompactOptions);
    }

    /// <summary>
    /// Builds and serializes the aggregated full audit report into JSON.
    /// </summary>
    public static string ExportAuditReportToJson(
        CodeGraph graph,
        DeadCodeDataset deadCode,
        MaintainabilityDataset maintainability,
        ArchitecturalRulesReport rules,
        CodeCloneReport clones,
        CohesionDataset cohesion,
        MartinMetricsDataset martin,
        SharedStateDataset sharedState,
        CrudMatrixDataset crud,
        DataFlowDataset dataFlow,
        DsmMatrix dsm,
        ScreenFlowGraph screenFlow,
        bool indented = true)
    {
        var dto = new AuditReportDto
        {
            Summary = new AuditSummaryDto
            {
                TotalSymbols = graph.Symbols.Count,
                TotalRelationships = graph.Relationships.Count,
                AverageMaintainabilityIndex = maintainability.AverageMaintainabilityIndex,
                DeadCodeRatio = deadCode.DeadCodeRatio,
                ArchitecturalViolations = rules.TotalViolations,
                DuplicateCloneGroups = clones.TotalDuplicateGroups,
                RefactoringCandidatesCount = cohesion.RefactoringCandidateCount,
                GodObjectsCount = martin.PainZoneCount,
                HighRiskSharedVariablesCount = sharedState.CriticalVariablesCount + sharedState.HighRiskVariablesCount,
                TaintPathsCount = dataFlow.TotalTaintPaths
            },
            DeadCode = deadCode,
            Maintainability = maintainability,
            ArchitecturalRules = rules,
            CodeClones = clones,
            Cohesion = cohesion,
            MartinMetrics = martin,
            SharedState = sharedState,
            CrudMatrix = crud,
            DataFlow = dataFlow,
            ScreenFlow = screenFlow,
            Dsm = DsmMatrixDto.FromMatrix(dsm)
        };

        return JsonSerializer.Serialize(dto, indented ? DefaultOptions : CompactOptions);
    }
}
