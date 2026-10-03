using System.Text;
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
/// Esportatore Markdown unificato per tutte le analisi semantiche e architetturali di Macondo.
/// Genera report chiari, leggibili e formattati con tabelle ed elenchi compatibili con GitHub, Markdown viewer e LLM.
/// </summary>
public static class AnalysisMarkdownExporter
{
    #region Dead Code & Reachability

    public static string ExportDeadCode(DeadCodeDataset dataset)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Dead Code & Reachability Analysis Report");
        sb.AppendLine();
        sb.AppendLine($"*Generated at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC*");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"| Metric | Value |");
        sb.AppendLine($"| :--- | :--- |");
        sb.AppendLine($"| **Total Symbols Analyzed** | {dataset.TotalSymbolsAnalyzed} |");
        sb.AppendLine($"| **Reachable Symbols** | {dataset.ReachableSymbolsCount} |");
        sb.AppendLine($"| **Dead / Orphan Symbols** | {dataset.DeadSymbolsCount} |");
        sb.AppendLine($"| **Dead Code Ratio** | {dataset.DeadCodeRatio:F1}% |");
        sb.AppendLine($"| **Estimated Dead LOC** | ~{dataset.TotalDeadLoc:N0} lines |");
        sb.AppendLine($"| **Unreachable Routines** | {dataset.UnreachableRoutinesCount} |");
        sb.AppendLine($"| **Unused Fields / Constants** | {dataset.UnusedFieldsCount} |");
        sb.AppendLine($"| **Orphan Containers** | {dataset.OrphanContainersCount} |");
        sb.AppendLine();

        if (dataset.Items.Count == 0)
        {
            sb.AppendLine("> [!NOTE]");
            sb.AppendLine("> No dead code or orphaned symbols were detected. All components are reachable from application entry points.");
            return sb.ToString();
        }

        sb.AppendLine("## Dead Code Items");
        sb.AppendLine();
        sb.AppendLine("| Name | Category | Container | File | Lines | Reason |");
        sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- |");

        foreach (var item in dataset.Items.OrderBy(i => i.Category).ThenBy(i => i.FilePath).ThenBy(i => i.Name))
        {
            var fileName = Path.GetFileName(item.FilePath);
            var lineRange = item.LineStart > 0 ? $"{item.LineStart}-{item.LineEnd}" : "-";
            var categoryName = item.Category switch
            {
                DeadCodeCategory.UnreachableRoutine => "Unreachable Routine",
                DeadCodeCategory.UnusedFieldOrConstant => "Unused Field/Const",
                DeadCodeCategory.OrphanContainer => "Orphan Container",
                DeadCodeCategory.UnusedType => "Unused Type",
                _ => item.Category.ToString()
            };
            sb.AppendLine($"| `{item.Name}` | **{categoryName}** | `{item.ContainerName}` | `{fileName}` | {lineRange} | {item.Reason} |");
        }

        return sb.ToString();
    }

    #endregion

    #region Maintainability & Halstead

    public static string ExportMaintainability(MaintainabilityDataset dataset)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Maintainability Index & Halstead Metrics Report");
        sb.AppendLine();
        sb.AppendLine($"*Generated at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC*");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"| Metric | Value |");
        sb.AppendLine($"| :--- | :--- |");
        sb.AppendLine($"| **Analyzed Containers** | {dataset.Items.Count} |");
        sb.AppendLine($"| **Average Maintainability Index (MI)** | **{dataset.AverageMaintainabilityIndex:F1} / 100** |");
        sb.AppendLine($"| **High Maintainability (MI >= 80)** | {dataset.HighCount} |");
        sb.AppendLine($"| **Moderate Maintainability (20 <= MI < 80)** | {dataset.ModerateCount} |");
        sb.AppendLine($"| **Critical / Low Maintainability (MI < 20)** | {dataset.LowCount} |");
        sb.AppendLine($"| **Total Estimated Latent Bugs (Halstead)** | {dataset.TotalEstimatedBugs:F2} |");
        sb.AppendLine();

        sb.AppendLine("## Container Metrics Breakdown");
        sb.AppendLine();
        sb.AppendLine("| Container | Kind | File | LOC | CC (McCabe) | Halstead Vol | MI (0-100) | Rating | Est. Bugs |");
        sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |");

        foreach (var item in dataset.Items.OrderBy(i => i.MaintainabilityIndex))
        {
            var fileName = Path.GetFileName(item.FilePath);
            var ratingStr = item.Rating switch
            {
                MaintainabilityRating.High => "High",
                MaintainabilityRating.Moderate => "Moderate",
                MaintainabilityRating.Low => "Critical",
                _ => item.Rating.ToString()
            };

            sb.AppendLine($"| `{item.Name}` | {item.Kind} | `{fileName}` | {item.LinesOfCode} | {item.CyclomaticComplexity} | {item.Halstead.Volume:F0} | **{item.MaintainabilityIndex:F1}** | {ratingStr} | {item.Halstead.EstimatedBugs:F2} |");
        }

        return sb.ToString();
    }

    #endregion

    #region Architectural Rules & Linter

    public static string ExportArchitecturalRules(ArchitecturalRulesReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Architectural Rules & Linter Violations Report");
        sb.AppendLine();
        sb.AppendLine($"*Generated at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC*");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"| Metric | Value |");
        sb.AppendLine($"| :--- | :--- |");
        sb.AppendLine($"| **Rules Evaluated** | {report.TotalRulesEvaluated} |");
        sb.AppendLine($"| **Total Violations** | {report.TotalViolations} |");
        sb.AppendLine($"| **Critical Violations** | {report.CriticalViolations} |");
        sb.AppendLine($"| **Error Violations** | {report.ErrorViolations} |");
        sb.AppendLine($"| **Warning Violations** | {report.WarningViolations} |");
        sb.AppendLine();

        if (report.Violations.Count == 0)
        {
            sb.AppendLine("> [!NOTE]");
            sb.AppendLine("> No architectural rule violations were detected. Architecture conforms to configured rulesets.");
            return sb.ToString();
        }

        sb.AppendLine("## Violations Detail");
        sb.AppendLine();
        sb.AppendLine("| Severity | Rule Name | Source Symbol | Target Symbol | Relationship | Description |");
        sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- |");

        foreach (var v in report.Violations.OrderByDescending(v => v.Severity).ThenBy(v => v.Rule.Name))
        {
            var relKind = v.Relationship?.Kind.ToString() ?? "-";
            sb.AppendLine($"| **{v.Severity}** | {v.Rule.Name} | `{v.SourceSymbol?.QualifiedName ?? "Unknown"}` | `{v.TargetSymbol?.QualifiedName ?? "Unknown"}` | {relKind} | {v.Message} |");
        }

        return sb.ToString();
    }

    #endregion

    #region Code Clones & Duplications

    public static string ExportCodeClones(CodeCloneReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Code Clone Detection (CPD) Report");
        sb.AppendLine();
        sb.AppendLine($"*Generated at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC*");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"| Metric | Value |");
        sb.AppendLine($"| :--- | :--- |");
        sb.AppendLine($"| **Duplicate Clone Groups** | {report.TotalDuplicateGroups} |");
        sb.AppendLine($"| **Total Duplicate Occurrences** | {report.TotalDuplicateOccurrences} |");
        sb.AppendLine($"| **Total Redundant Lines** | ~{report.TotalRedundantLines} lines |");
        sb.AppendLine($"| **Exact (Type-1) Clones** | {report.ExactClonesCount} |");
        sb.AppendLine($"| **Parameterized (Type-2) Clones** | {report.ParameterizedClonesCount} |");
        sb.AppendLine();

        if (report.CloneGroups.Count == 0)
        {
            sb.AppendLine("> [!NOTE]");
            sb.AppendLine("> No duplicate code blocks or clones were detected matching the configured threshold.");
            return sb.ToString();
        }

        int groupIndex = 1;
        foreach (var group in report.CloneGroups.OrderByDescending(g => g.RedundantLines))
        {
            sb.AppendLine($"### Clone Group #{groupIndex++} ({group.Type}, {group.LineCount} lines per occurrence, {group.Occurrences.Count} duplicates, ~{group.RedundantLines} redundant lines)");
            sb.AppendLine();
            sb.AppendLine("| File | Module | Lines |");
            sb.AppendLine("| :--- | :--- | :--- |");
            foreach (var occ in group.Occurrences)
            {
                var fileName = Path.GetFileName(occ.FilePath);
                sb.AppendLine($"| `{fileName}` | `{occ.ModuleName}` | {occ.LineStart}-{occ.LineEnd} |");
            }
            sb.AppendLine();
            if (group.Occurrences.Count > 0 && !string.IsNullOrWhiteSpace(group.Occurrences[0].CodeSnippet))
            {
                sb.AppendLine("```vb");
                sb.AppendLine(group.Occurrences[0].CodeSnippet.Trim());
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    #endregion

    #region Cohesion (LCOM4 & C&K)

    public static string ExportCohesion(CohesionDataset dataset)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Cohesion Analysis (LCOM4 & C&K) Report");
        sb.AppendLine();
        sb.AppendLine($"*Generated at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC*");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"| Metric | Value |");
        sb.AppendLine($"| :--- | :--- |");
        sb.AppendLine($"| **Total Containers Analyzed** | {dataset.Items.Count} |");
        sb.AppendLine($"| **Average LCOM4 (Methods >= 2)** | {dataset.AverageLcom4:F2} |");
        sb.AppendLine($"| **Average TCC (Tight Class Cohesion)** | {dataset.AverageTcc:P1} |");
        sb.AppendLine($"| **Refactoring Candidates (LCOM4 >= 2)** | {dataset.RefactoringCandidateCount} |");
        sb.AppendLine();

        sb.AppendLine("## Container Cohesion Breakdown");
        sb.AppendLine();
        sb.AppendLine("| Container | Kind | File | Methods | Fields | LCOM4 | TCC | CBO | RFC | Refactor Candidate |");
        sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |");

        foreach (var item in dataset.Items.OrderByDescending(i => i.Lcom4).ThenByDescending(i => i.MethodCount))
        {
            var fileName = Path.GetFileName(item.FilePath);
            var isCandidateStr = item.IsRefactoringCandidate ? "**YES**" : "No";
            sb.AppendLine($"| `{item.Name}` | {item.Kind} | `{fileName}` | {item.MethodCount} | {item.FieldCount} | **{item.Lcom4}** | {item.TightClassCohesion:P0} | {item.CouplingBetweenObjects} | {item.ResponseForClass} | {isCandidateStr} |");
        }

        return sb.ToString();
    }

    #endregion

    #region Martin Metrics (Package Coupling & Architecture)

    public static string ExportMartinMetrics(MartinMetricsDataset dataset)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Robert C. Martin Metrics Report");
        sb.AppendLine();
        sb.AppendLine($"*Generated at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC*");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"| Metric | Value |");
        sb.AppendLine($"| :--- | :--- |");
        sb.AppendLine($"| **Analyzed Components** | {dataset.Items.Count} |");
        sb.AppendLine($"| **Average Distance (D) from Main Sequence** | {dataset.AverageDistance:F3} |");
        sb.AppendLine($"| **Components in Zone of Pain (God Objects)** | {dataset.PainZoneCount} |");
        sb.AppendLine($"| **Components in Zone of Uselessness** | {dataset.UselessnessZoneCount} |");
        sb.AppendLine($"| **Components on Main Sequence** | {dataset.MainSequenceCount} |");
        sb.AppendLine();

        sb.AppendLine("## Components Breakdown");
        sb.AppendLine();
        sb.AppendLine("| Component | Kind | Ca (Afferent) | Ce (Efferent) | Instability (I) | Abstractness (A) | Distance (D) | Zone |");
        sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |");

        foreach (var item in dataset.Items.OrderByDescending(i => i.Zone == MartinZone.ZoneOfPain ? 1 : 0).ThenByDescending(i => i.NormalizedDistance))
        {
            var zoneStr = item.Zone switch
            {
                MartinZone.ZoneOfPain => "**Zone of Pain**",
                MartinZone.ZoneOfUselessness => "**Zone of Uselessness**",
                MartinZone.MainSequence => "Main Sequence",
                _ => "Balanced"
            };

            sb.AppendLine($"| `{item.Name}` | {item.Kind} | {item.AfferentCoupling} | {item.EfferentCoupling} | {item.Instability:F2} | {item.Abstractness:F2} | {item.NormalizedDistance:F3} | {zoneStr} |");
        }

        return sb.ToString();
    }

    #endregion

    #region Shared State & Global Mutations

    public static string ExportSharedState(SharedStateDataset dataset)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Shared Global State & Mutation Risk Report");
        sb.AppendLine();
        sb.AppendLine($"*Generated at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC*");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"| Metric | Value |");
        sb.AppendLine($"| :--- | :--- |");
        sb.AppendLine($"| **Total Shared Variables / Fields** | {dataset.TotalSharedVariablesCount} |");
        sb.AppendLine($"| **Total Writers Accesses** | {dataset.TotalWritersCount} |");
        sb.AppendLine($"| **Total Cross-Module Writers** | {dataset.TotalCrossModuleWritersCount} |");
        sb.AppendLine($"| **Critical Mutation Risk Variables** | {dataset.CriticalVariablesCount} |");
        sb.AppendLine($"| **High Mutation Risk Variables** | {dataset.HighRiskVariablesCount} |");
        sb.AppendLine();

        if (dataset.Items.Count == 0)
        {
            sb.AppendLine("> [!NOTE]");
            sb.AppendLine("> No shared global mutable variables or cross-module mutations detected.");
            return sb.ToString();
        }

        sb.AppendLine("## Shared Variables Breakdown");
        sb.AppendLine();
        sb.AppendLine("| Variable | Module | File | Readers | Writers | Cross-Module Writers | Mutation Risk |");
        sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- | :--- |");

        foreach (var item in dataset.Items.OrderByDescending(i => i.MutationRisk).ThenByDescending(i => i.CrossModuleWritersCount))
        {
            var fileName = Path.GetFileName(item.FilePath);
            sb.AppendLine($"| `{item.VariableName}` | `{item.ModuleName}` | `{fileName}` | {item.ReadersCount} | {item.WritersCount} | {item.CrossModuleWritersCount} | **{item.MutationRisk}** |");
        }

        return sb.ToString();
    }

    #endregion

    #region CRUD Operations Matrix

    public static string ExportCrudMatrix(CrudMatrixDataset dataset)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# CRUD Operations & Data Entities Matrix Report");
        sb.AppendLine();
        sb.AppendLine($"*Generated at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC*");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"| Metric | Value |");
        sb.AppendLine($"| :--- | :--- |");
        sb.AppendLine($"| **Identified Data Entities** | {dataset.TotalEntities} |");
        sb.AppendLine($"| **Total CRUD Interactions** | {dataset.TotalCrudOperations} |");
        sb.AppendLine();

        if (dataset.EntitySummaries.Count == 0)
        {
            sb.AppendLine("> [!NOTE]");
            sb.AppendLine("> No data entities or CRUD operations detected in the analyzed codebase.");
            return sb.ToString();
        }

        sb.AppendLine("## Entity CRUD Summary");
        sb.AppendLine();
        sb.AppendLine("| Entity / Table | Create (C) | Read (R) | Update (U) | Delete (D) | Total Accessors |");
        sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- |");

        foreach (var ent in dataset.EntitySummaries.OrderByDescending(e => e.TotalAccessorsCount))
        {
            sb.AppendLine($"| **{ent.EntityName}** | {ent.CreateCount} | {ent.ReadCount} | {ent.UpdateCount} | {ent.DeleteCount} | {ent.TotalAccessorsCount} |");
        }

        return sb.ToString();
    }

    #endregion

    #region Data Flow & Taint Analysis

    public static string ExportDataFlow(DataFlowDataset dataset)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Data Flow & Taint Tracking Report");
        sb.AppendLine();
        sb.AppendLine($"*Generated at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC*");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"| Metric | Value |");
        sb.AppendLine($"| :--- | :--- |");
        sb.AppendLine($"| **Total Source-to-Sink Taint Paths** | {dataset.TotalTaintPaths} |");
        sb.AppendLine($"| **SQL Injection Risks** | {dataset.SqlInjectionRisks} |");
        sb.AppendLine($"| **Shell / Command Execution Risks** | {dataset.CommandExecutionRisks} |");
        sb.AppendLine($"| **Dead Stores / Def-Use Anomalies** | {dataset.TotalDeadStores} |");
        sb.AppendLine();

        if (dataset.TaintPaths.Count == 0 && dataset.Anomalies.Count == 0)
        {
            sb.AppendLine("> [!NOTE]");
            sb.AppendLine("> No insecure data flow paths or def-use anomalies detected.");
            return sb.ToString();
        }

        if (dataset.TaintPaths.Count > 0)
        {
            sb.AppendLine("## Taint Paths (Source-to-Sink)");
            sb.AppendLine();
            sb.AppendLine("| Source Kind | Source Symbol | Sink Kind | Sink Symbol | Propagation Chain | Hops |");
            sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- |");

            foreach (var path in dataset.TaintPaths)
            {
                var chain = string.Join(" -> ", path.PropagationChain.Select(s => s.Name));
                sb.AppendLine($"| `{path.SourceKind}` | `{path.SourceSymbol?.Name ?? "Unknown"}` | `{path.SinkKind}` | `{path.SinkSymbol?.Name ?? "Unknown"}` | `{chain}` | {path.HopCount} |");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    #endregion

    #region Screen Flow (UI Navigation)

    public static string ExportScreenFlow(ScreenFlowGraph graph)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Screen Flow & UI Navigation Report");
        sb.AppendLine();
        sb.AppendLine($"*Generated at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC*");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"| Metric | Value |");
        sb.AppendLine($"| :--- | :--- |");
        sb.AppendLine($"| **Total Screens / Forms** | {graph.Screens.Count} |");
        sb.AppendLine($"| **Total UI Transitions** | {graph.Transitions.Count} |");
        sb.AppendLine();

        sb.AppendLine("## Screens");
        sb.AppendLine();
        sb.AppendLine("| Screen Name | Title | Startup | Controls | Event Handlers | Inbound | Outbound |");
        sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- | :--- |");

        foreach (var screen in graph.Screens.OrderByDescending(s => s.IsStartupScreen).ThenBy(s => s.Name))
        {
            var startupStr = screen.IsStartupScreen ? "**YES**" : "No";
            sb.AppendLine($"| `{screen.Name}` | {screen.Title} | {startupStr} | {screen.ControlsCount} | {screen.EventHandlersCount} | {screen.IncomingTransitionsCount} | {screen.OutgoingTransitionsCount} |");
        }
        sb.AppendLine();

        if (graph.Transitions.Count > 0)
        {
            sb.AppendLine("## Navigation Transitions");
            sb.AppendLine();
            sb.AppendLine("| Source Screen | Target Screen | Transition Kind | Trigger Routine | Trigger Event |");
            sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");

            foreach (var t in graph.Transitions)
            {
                var source = graph.FindScreen(t.SourceScreenId)?.Name ?? "Unknown";
                var target = graph.FindScreen(t.TargetScreenId)?.Name ?? "Unknown";
                sb.AppendLine($"| `{source}` | `{target}` | **{t.TransitionKind}** | `{t.TriggerRoutineName}` | `{t.TriggerEventName}` |");
            }
        }

        return sb.ToString();
    }

    #endregion

    #region Design Structure Matrix (DSM)

    public static string ExportDsm(DsmMatrix dsm)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Design Structure Matrix (DSM) Report");
        sb.AppendLine();
        sb.AppendLine($"*Generated at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC*");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"| Metric | Value |");
        sb.AppendLine($"| :--- | :--- |");
        sb.AppendLine($"| **Matrix Size (Modules/Containers)** | {dsm.Size} |");
        sb.AppendLine($"| **Total Dependencies** | {dsm.TotalDependencies} |");
        sb.AppendLine($"| **Total Cyclic Dependencies** | {dsm.TotalCyclicDependencies} |");
        sb.AppendLine($"| **Strongly Connected Components (Cycles)** | {dsm.StronglyConnectedComponents.Count} |");
        sb.AppendLine();

        if (dsm.StronglyConnectedComponents.Count > 0)
        {
            sb.AppendLine("## Circular Dependency Clusters");
            sb.AppendLine();
            var itemLookup = dsm.Items.ToDictionary(i => i.Id);
            int cycleIdx = 1;
            foreach (var comp in dsm.StronglyConnectedComponents.Where(c => c.Count > 1))
            {
                var names = comp.Select(id => itemLookup.GetValueOrDefault(id)?.Name ?? id.ToString()).ToList();
                sb.AppendLine($"### Cycle #{cycleIdx++} ({comp.Count} modules)");
                sb.AppendLine($"- Modules: {string.Join(" <-> ", names.Select(n => $"`{n}`"))}");
                sb.AppendLine();
            }
        }

        sb.AppendLine("## Module Coupling Table");
        sb.AppendLine();
        sb.AppendLine("| # | Module | Kind | In-Degree (Ca) | Out-Degree (Ce) | Cyclic |");
        sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- |");

        foreach (var item in dsm.Items.OrderByDescending(i => i.HasCircularDependencies ? 1 : 0).ThenByDescending(i => i.InDegree + i.OutDegree))
        {
            var cyclicStr = item.HasCircularDependencies ? "**YES**" : "No";
            sb.AppendLine($"| {item.Index + 1} | `{item.Name}` | {item.Kind} | {item.InDegree} | {item.OutDegree} | {cyclicStr} |");
        }

        return sb.ToString();
    }

    #endregion

    #region Impact & Blast Radius

    public static string ExportImpact(ImpactReport impact)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Blast Radius & Impact Analysis Report");
        sb.AppendLine();
        sb.AppendLine($"*Generated at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC*");
        sb.AppendLine();
        sb.AppendLine($"## Target Symbol: `{impact.TargetSymbol.QualifiedName}` ({impact.TargetSymbol.Kind})");
        sb.AppendLine();
        sb.AppendLine($"| Metric | Value |");
        sb.AppendLine($"| :--- | :--- |");
        sb.AppendLine($"| **Assessed Risk Level** | **{impact.RiskLevel}** |");
        sb.AppendLine($"| **Total Impacted Symbols** | {impact.TotalImpactedSymbolsCount} |");
        sb.AppendLine($"| **Direct Callers** | {impact.DirectCallers.Count} |");
        sb.AppendLine($"| **Indirect Callers** | {impact.IndirectCallers.Count} |");
        sb.AppendLine($"| **Max Dependency Depth** | {impact.MaxDependencyDepth} |");
        sb.AppendLine($"| **Affected Forms (UI Contact Points)** | {impact.AffectedForms.Count} |");
        sb.AppendLine($"| **Affected Classes (.cls)** | {impact.AffectedClasses.Count} |");
        sb.AppendLine($"| **Affected Modules (.bas)** | {impact.AffectedModules.Count} |");
        sb.AppendLine();

        if (impact.DirectCallers.Count > 0)
        {
            sb.AppendLine("### Direct Callers (Depth = 1)");
            sb.AppendLine();
            sb.AppendLine("| Caller | Kind | File | Line |");
            sb.AppendLine("| :--- | :--- | :--- | :--- |");
            foreach (var c in impact.DirectCallers)
            {
                var fileName = Path.GetFileName(c.FilePath);
                sb.AppendLine($"| `{c.QualifiedName}` | {c.Kind} | `{fileName}` | {c.LineStart} |");
            }
            sb.AppendLine();
        }

        if (impact.IndirectCallers.Count > 0)
        {
            sb.AppendLine("### Indirect Callers (Depth >= 2)");
            sb.AppendLine();
            sb.AppendLine("| Depth | Caller | Enclosing Container | File |");
            sb.AppendLine("| :--- | :--- | :--- | :--- |");
            foreach (var ic in impact.IndirectCallers.OrderBy(i => i.Depth))
            {
                var fileName = Path.GetFileName(ic.Symbol.FilePath);
                sb.AppendLine($"| Level {ic.Depth} | `{ic.Symbol.QualifiedName}` | `{ic.EnclosingContainer?.Name ?? "N/A"}` | `{fileName}` |");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    #endregion

    #region Full Audit Report (All-in-One)

    public static string ExportAuditReport(
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
        ScreenFlowGraph screenFlow)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Architecture & Code Quality Audit Report");
        sb.AppendLine();
        sb.AppendLine($"*Generated by Macondo Architecture & Code Analyzer on {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC*");
        sb.AppendLine();

        // 1. Executive Summary
        sb.AppendLine("## 1. Executive Summary & Health Dashboard");
        sb.AppendLine();
        sb.AppendLine("| Category | Key Indicator | Value | Status |");
        sb.AppendLine("| :--- | :--- | :--- | :--- |");
        sb.AppendLine($"| **Codebase Scope** | Total Symbols / Relationships | {graph.Symbols.Count} symbols, {graph.Relationships.Count} rels | Information |");
        sb.AppendLine($"| **Maintainability** | Average Maintainability Index (MI) | **{maintainability.AverageMaintainabilityIndex:F1} / 100** | {(maintainability.AverageMaintainabilityIndex >= 80 ? "Pass (High)" : maintainability.AverageMaintainabilityIndex >= 60 ? "Warning (Moderate)" : "Critical (Low)")} |");
        sb.AppendLine($"| **Dead Code** | Dead Code Ratio | **{deadCode.DeadCodeRatio:F1}%** ({deadCode.DeadSymbolsCount} symbols, ~{deadCode.TotalDeadLoc:N0} dead LOC) | {(deadCode.DeadCodeRatio < 10 ? "Pass" : deadCode.DeadCodeRatio < 25 ? "Warning" : "Critical")} |");
        sb.AppendLine($"| **Architecture Rules** | Critical/Error Violations | **{rules.CriticalViolations + rules.ErrorViolations}** violations | {(rules.CriticalViolations + rules.ErrorViolations == 0 ? "Pass" : "Failed")} |");
        sb.AppendLine($"| **Duplication (CPD)** | Redundant Duplicate Lines | **~{clones.TotalRedundantLines} lines** ({clones.TotalDuplicateGroups} groups) | {(clones.TotalRedundantLines < 200 ? "Pass" : "Warning")} |");
        sb.AppendLine($"| **Cohesion (LCOM4)** | Refactoring Candidates | **{cohesion.RefactoringCandidateCount}** containers | {(cohesion.RefactoringCandidateCount == 0 ? "Pass" : "Warning")} |");
        sb.AppendLine($"| **Martin Architecture** | God Objects in Zone of Pain | **{martin.PainZoneCount}** components | {(martin.PainZoneCount == 0 ? "Pass" : "Warning")} |");
        sb.AppendLine($"| **Global State** | High/Critical Mutation Risks | **{sharedState.CriticalVariablesCount + sharedState.HighRiskVariablesCount}** variables | {(sharedState.CriticalVariablesCount + sharedState.HighRiskVariablesCount == 0 ? "Pass" : "Warning")} |");
        sb.AppendLine($"| **Security / DataFlow** | Taint Paths & Insecure Flows | **{dataFlow.TotalTaintPaths}** paths | {(dataFlow.TotalTaintPaths == 0 ? "Pass" : "Warning")} |");
        sb.AppendLine($"| **Coupling / DSM** | Cyclic Dependency Groups | **{dsm.StronglyConnectedComponents.Count(c => c.Count > 1)}** cycles | {(dsm.TotalCyclicDependencies == 0 ? "Pass" : "Warning")} |");
        sb.AppendLine();

        // 2. Architectural Rules & Violations
        sb.AppendLine("## 2. Architectural Rules & Governance");
        sb.AppendLine();
        if (rules.Violations.Count == 0)

            sb.AppendLine("No architectural rule violations detected.");

        else
        {
            sb.AppendLine($"Detected **{rules.TotalViolations}** violation(s) across {rules.TotalRulesEvaluated} evaluated rules.");
            sb.AppendLine();
            sb.AppendLine("| Severity | Rule | Source | Target | Detail |");
            sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");
            foreach (var v in rules.Violations.Take(15))
            {
                sb.AppendLine($"| **{v.Severity}** | {v.Rule.Name} | `{v.SourceSymbol?.Name ?? "-"}` | `{v.TargetSymbol?.Name ?? "-"}` | {v.Message} |");
            }
            if (rules.Violations.Count > 15)

                sb.AppendLine($"*... and {rules.Violations.Count - 15} more violations.*");
        }
        sb.AppendLine();

        // 3. Maintainability & Top Critical Containers
        sb.AppendLine("## 3. Maintainability & Technical Debt Candidates");
        sb.AppendLine();
        sb.AppendLine($"Average MI: **{maintainability.AverageMaintainabilityIndex:F1} / 100**. Total estimated latent bugs: **{maintainability.TotalEstimatedBugs:F2}**.");
        sb.AppendLine();
        sb.AppendLine("| Container | Kind | LOC | CC (McCabe) | Halstead Vol | MI (0-100) | Rating |");
        sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- | :--- |");
        foreach (var item in maintainability.Items.OrderBy(i => i.MaintainabilityIndex).Take(10))
        {
            sb.AppendLine($"| `{item.Name}` | {item.Kind} | {item.LinesOfCode} | {item.CyclomaticComplexity} | {item.Halstead.Volume:F0} | **{item.MaintainabilityIndex:F1}** | {item.Rating} |");
        }
        sb.AppendLine();

        // 4. Dead Code Summary
        sb.AppendLine("## 4. Dead Code & Orphaned Components");
        sb.AppendLine();
        sb.AppendLine($"Dead code ratio is **{deadCode.DeadCodeRatio:F1}%** (~{deadCode.TotalDeadLoc:N0} dead lines across {deadCode.DeadSymbolsCount} symbols).");
        sb.AppendLine();
        if (deadCode.Items.Count > 0)
        {
            sb.AppendLine("| Name | Category | Container | File | Reason |");
            sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");
            foreach (var item in deadCode.Items.Take(15))
            {
                var f = Path.GetFileName(item.FilePath);
                sb.AppendLine($"| `{item.Name}` | {item.Category} | `{item.ContainerName}` | `{f}` | {item.Reason} |");
            }
            if (deadCode.Items.Count > 15)

                sb.AppendLine($"*... and {deadCode.Items.Count - 15} more dead code items.*");
        }
        sb.AppendLine();

        // 5. Code Duplications & Clones
        sb.AppendLine("## 5. Code Duplications (CPD)");
        sb.AppendLine();
        sb.AppendLine($"Found **{clones.TotalDuplicateGroups}** duplicate groups affecting **{clones.TotalDuplicateOccurrences}** locations with **~{clones.TotalRedundantLines}** redundant lines.");
        sb.AppendLine();

        // 6. Martin Architectural Zones & God Objects
        sb.AppendLine("## 6. Robert C. Martin Coupling & Stability Zones");
        sb.AppendLine();
        sb.AppendLine($"Main sequence average distance: **{martin.AverageDistance:F3}**. Components in Zone of Pain: **{martin.PainZoneCount}**.");
        sb.AppendLine();
        if (martin.PainZoneCount > 0)
        {
            sb.AppendLine("### High-Risk Monoliths (Zone of Pain)");
            sb.AppendLine("| Component | Kind | Ca (Afferent) | Ce (Efferent) | Instability | Abstractness | Distance |");
            sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- | :--- |");
            foreach (var p in martin.Items.Where(i => i.Zone == MartinZone.ZoneOfPain).OrderByDescending(i => i.AfferentCoupling))
            {
                sb.AppendLine($"| `{p.Name}` | {p.Kind} | {p.AfferentCoupling} | {p.EfferentCoupling} | {p.Instability:F2} | {p.Abstractness:F2} | {p.NormalizedDistance:F3} |");
            }
            sb.AppendLine();
        }

        // 7. Shared State & Global Variable Mutations
        sb.AppendLine("## 7. Shared Global State & Mutation Risks");
        sb.AppendLine();
        sb.AppendLine($"Identified **{sharedState.TotalSharedVariablesCount}** shared variables. Critical mutation risks: **{sharedState.CriticalVariablesCount}**, High risks: **{sharedState.HighRiskVariablesCount}**.");
        sb.AppendLine();

        // 8. Data Flow & Security
        sb.AppendLine("## 8. Data Flow & Taint Paths");
        sb.AppendLine();
        sb.AppendLine($"Identified **{dataFlow.TotalTaintPaths}** unsanitized source-to-sink data paths (SQL injection risks: {dataFlow.SqlInjectionRisks}, Shell risks: {dataFlow.CommandExecutionRisks}).");
        sb.AppendLine();

        // 9. UI Navigation & Screens
        sb.AppendLine("## 9. Screen Flow & UI Navigation Summary");
        sb.AppendLine();
        sb.AppendLine($"Identified **{screenFlow.Screens.Count}** UI screens with **{screenFlow.Transitions.Count}** navigation transitions.");
        sb.AppendLine();

        return sb.ToString();
    }

    #endregion
}
