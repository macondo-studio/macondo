using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Macondo.App.Models;
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

namespace Macondo.App.Services;

/// <summary>
/// Command-Line Interface (CLI) execution engine for Macondo.
/// It manages the parsing of arguments, the execution of semantic analyses,
/// report generation (Markdown and JSON) and CI/CD Quality Gate evaluation.
/// </summary>
public static class CliRunner
{
    /// <summary>
    /// Exit code for successful execution.
    /// </summary>
    public const int ExitCodeSuccess = 0;

    /// <summary>
    /// Exit code for parsing errors, missing/invalid arguments, or unexpected exceptions.
    /// </summary>
    public const int ExitCodeError = 1;

    /// <summary>
    /// Exit code when a CI/CD Quality Gate fails (architectural violations, MI or Dead Code).
    /// </summary>
    public const int ExitCodeQualityGateFailed = 2;

    /// <summary>
    /// Determines whether command line arguments correspond to a non-interactive CLI execution mode.
    /// </summary>
    /// <param name="args">Array of arguments passed to the process.</param>
    /// <returns><c>true</c> if CLI execution or help is required; otherwise <c>false</c>.</returns>
    public static bool IsCliInvocation(string[]? args)
    {
        if (args == null || args.Length == 0)
            return false;

        return args.Any(a => a is "analyze" or "--export-md" or "-m" or "--export-json" or "-j"
            or "--report" or "-r" or "--out-dir" or "-o" or "--analysis" or "-a"
            or "--dead-code" or "--maintainability" or "--rules" or "--clones" or "--cohesion"
            or "--martin" or "--shared-state" or "--crud" or "--data-flow" or "--screen-flow"
            or "--dsm" or "--impact" or "--fail-on-violations" or "--fail-on-rules"
            or "--fail-on-mi" or "--fail-on-dead-code" or "--verbose" or "-v" or "--help" or "-h" or "help" or "/?");
    }

    /// <summary>
    /// Executes the CLI command by parsing the supplied parameters and outputting the results to the specified output streams.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <param name="outWriter">Flow for informational and progress messages (if null, use <see cref="Console.Out"/>).</param>
    /// <param name="errorWriter">Error message flow (if null, use <see cref="Console.Error"/>).</param>
    /// <returns>
    /// Exit code:
    /// <list type="bullet">
    /// <item><description><see cref="ExitCodeSuccess"/> (0): Analysis and export completed successfully.</description></item>
    /// <item><description><see cref="ExitCodeError"/> (1): Invalid parameters or error during execution.</description></item>
    /// <item><description><see cref="ExitCodeQualityGateFailed"/> (2): CI/CD Quality Gate failed.</description></item>
    /// </list>
    /// </returns>
    public static int Run(string[] args, TextWriter? outWriter = null, TextWriter? errorWriter = null)
    {
        outWriter ??= Console.Out;
        errorWriter ??= Console.Error;

        if (args == null || args.Length == 0)
        {
            errorWriter.WriteLine("[Error] No arguments provided.");
            PrintHelp(outWriter);
            return ExitCodeError;
        }

        // 1. Check Help request
        if (args.Any(a => a is "-h" or "--help" or "help" or "/?"))
        {
            PrintHelp(outWriter);
            return ExitCodeSuccess;
        }

        // General input & output configuration variables
        var inputPaths = new List<string>();
        string? exportMdPath = null;
        string? exportJsonPath = null;
        string? reportPath = null;
        string? outDir = null;
        string? requestedAnalyses = null;
        string format = "md"; // Formati supportati: "md", "json", "all"
        string? rulesConfigPath = null;

        // Specific outputs for individual analyzes (granular flags)
        string? deadCodePath = null;
        string? maintainabilityPath = null;
        string? rulesPath = null;
        string? clonesPath = null;
        string? cohesionPath = null;
        string? martinPath = null;
        string? sharedStatePath = null;
        string? crudPath = null;
        string? dataFlowPath = null;
        string? screenFlowPath = null;
        string? dsmPath = null;
        (string SymbolName, string OutPath)? impactArg = null;

        // Options for CI/CD Quality Gates
        bool failOnViolations = false;
        double? failOnMiThreshold = null;
        double? failOnDeadCodeThreshold = null;
        bool verbose = false;

        // 2. Parsing command line arguments
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.Equals("analyze", StringComparison.OrdinalIgnoreCase))
                continue;

            // Legacy exports (Repo Map & Raw CodeGraph JSON)
            if (arg.Equals("--export-md", StringComparison.OrdinalIgnoreCase) || arg.Equals("-m", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                    exportMdPath = args[++i];
                else
                {
                    errorWriter.WriteLine("[Error] Missing file path for --export-md");
                    return ExitCodeError;
                }
            }
            else if (arg.Equals("--export-json", StringComparison.OrdinalIgnoreCase) || arg.Equals("-j", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                    exportJsonPath = args[++i];
                else
                {
                    errorWriter.WriteLine("[Error] Missing file path for --export-json");
                    return ExitCodeError;
                }
            }
            // Audit Report completo
            else if (arg.Equals("--report", StringComparison.OrdinalIgnoreCase) || arg.Equals("-r", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                    reportPath = args[++i];
                else
                {
                    errorWriter.WriteLine("[Error] Missing file path for --report");
                    return ExitCodeError;
                }
            }
            // Batch export to directory
            else if (arg.Equals("--out-dir", StringComparison.OrdinalIgnoreCase) || arg.Equals("-o", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                    outDir = args[++i];
                else
                {
                    errorWriter.WriteLine("[Error] Missing directory path for --out-dir");
                    return ExitCodeError;
                }
            }
            else if (arg.Equals("--analysis", StringComparison.OrdinalIgnoreCase) || arg.Equals("-a", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                    requestedAnalyses = args[++i];
                else
                {
                    errorWriter.WriteLine("[Error] Missing value for --analysis");
                    return ExitCodeError;
                }
            }
            else if (arg.Equals("--format", StringComparison.OrdinalIgnoreCase) || arg.Equals("-f", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                    format = args[++i].ToLowerInvariant();
                else
                {
                    errorWriter.WriteLine("[Error] Missing value for --format");
                    return ExitCodeError;
                }
            }
            // Custom architectural rules configuration file
            else if (arg.Equals("--rules-config", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                    rulesConfigPath = args[++i];
                else
                {
                    errorWriter.WriteLine("[Error] Missing file path for --rules-config");
                    return ExitCodeError;
                }
            }
            // Granular single exports (output file is optional; if omitted, a default name is used)
            else if (arg.Equals("--dead-code", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    deadCodePath = args[++i];
                else
                    deadCodePath = "dead-code.md";
            }
            else if (arg.Equals("--maintainability", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    maintainabilityPath = args[++i];
                else
                    maintainabilityPath = "maintainability.md";
            }
            else if (arg.Equals("--rules", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    rulesPath = args[++i];
                else
                    rulesPath = "architectural-rules.md";
            }
            else if (arg.Equals("--clones", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    clonesPath = args[++i];
                else
                    clonesPath = "code-clones.md";
            }
            else if (arg.Equals("--cohesion", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    cohesionPath = args[++i];
                else
                    cohesionPath = "cohesion.md";
            }
            else if (arg.Equals("--martin", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    martinPath = args[++i];
                else
                    martinPath = "martin-metrics.md";
            }
            else if (arg.Equals("--shared-state", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    sharedStatePath = args[++i];
                else
                    sharedStatePath = "shared-state.md";
            }
            else if (arg.Equals("--crud", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    crudPath = args[++i];
                else
                    crudPath = "crud-matrix.md";
            }
            else if (arg.Equals("--data-flow", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    dataFlowPath = args[++i];
                else
                    dataFlowPath = "data-flow.md";
            }
            else if (arg.Equals("--screen-flow", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    screenFlowPath = args[++i];
                else
                    screenFlowPath = "screen-flow.md";
            }
            else if (arg.Equals("--dsm", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    dsmPath = args[++i];
                else
                    dsmPath = "dsm-matrix.md";
            }
            else if (arg.Equals("--impact", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 2 < args.Length && !args[i + 1].StartsWith("-") && !args[i + 2].StartsWith("-"))
                {
                    impactArg = (args[i + 1], args[i + 2]);
                    i += 2;
                }
                else if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                {
                    impactArg = (args[i + 1], "blast-radius.md");
                    i += 1;
                }
                else
                {
                    errorWriter.WriteLine("[Error] Missing symbol name for --impact <symbol> [output_file]");
                    return ExitCodeError;
                }
            }
            // Quality Gate flags
            else if (arg.Equals("--fail-on-violations", StringComparison.OrdinalIgnoreCase) || arg.Equals("--fail-on-rules", StringComparison.OrdinalIgnoreCase))
                failOnViolations = true;
            else if (arg.Equals("--fail-on-mi", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && double.TryParse(args[++i], System.Globalization.CultureInfo.InvariantCulture, out var threshold))
                    failOnMiThreshold = threshold;
                else
                {
                    errorWriter.WriteLine("[Error] Missing or invalid numeric threshold for --fail-on-mi <number>");
                    return ExitCodeError;
                }
            }
            else if (arg.Equals("--fail-on-dead-code", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && double.TryParse(args[++i].Replace("%", ""), System.Globalization.CultureInfo.InvariantCulture, out var pct))
                    failOnDeadCodeThreshold = pct;
                else
                {
                    errorWriter.WriteLine("[Error] Missing or invalid numeric threshold for --fail-on-dead-code <percentage>");
                    return ExitCodeError;
                }
            }
            else if (arg.Equals("--verbose", StringComparison.OrdinalIgnoreCase) || arg.Equals("-v", StringComparison.OrdinalIgnoreCase))
                verbose = true;
            else if (!arg.StartsWith("-"))
                inputPaths.Add(arg);
        }

        // 3. Source path validation
        if (inputPaths.Count == 0)
        {
            errorWriter.WriteLine("[Error] No target project, group or folder path specified for analysis.");
            PrintHelp(outWriter);
            return ExitCodeError;
        }

        bool hasSpecificOutput = exportMdPath != null || exportJsonPath != null || reportPath != null
            || outDir != null || deadCodePath != null || maintainabilityPath != null || rulesPath != null
            || clonesPath != null || cohesionPath != null || martinPath != null || sharedStatePath != null
            || crudPath != null || dataFlowPath != null || screenFlowPath != null || dsmPath != null || impactArg != null;

        // If no output or quality gate has been specified, the default is to generate a full audit report
        if (!hasSpecificOutput && !failOnViolations && !failOnMiThreshold.HasValue && !failOnDeadCodeThreshold.HasValue)
            reportPath = "audit-report.md";

        try
        {
            // 4. Discovery and semantic analysis of projects
            outWriter.WriteLine($"[Macondo] Discovering projects from {inputPaths.Count} path(s)...");
            var projects = ProjectDescriptor.DiscoverFromPaths(inputPaths);
            if (projects.Count == 0)
            {
                errorWriter.WriteLine($"[Error] No supported project files (.vbp, .vbg, .csproj, .sln) or source files found in: {string.Join(", ", inputPaths)}");
                return ExitCodeError;
            }

            var totalFiles = projects.Sum(p => p.SourceFiles.Count);
            outWriter.WriteLine($"[Macondo] Discovered {projects.Count} project(s) with {totalFiles} source file(s).");

            var progress = new SynchronousProgress<ProjectAnalysisProgress>(p =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(p.CurrentFileName))
                        outWriter.WriteLine($" [{p.CurrentFileIndex}/{p.TotalFiles}] {p.CurrentFileName} ({p.ExtractedNodesCount} symbols)");
                    else if (!string.IsNullOrEmpty(p.PhaseMessage))
                        outWriter.WriteLine($" -> {p.PhaseMessage}");
                }
                catch (ObjectDisposedException)
                {
                    // Ignore writes if the writer has been released
                }
            });

            var visualGraph = ProjectAnalysisService.AnalyzeProjects(projects, progress);
            var semanticGraph = visualGraph.SemanticGraph;
            if (semanticGraph == null || semanticGraph.Symbols.Count == 0)
            {
                errorWriter.WriteLine("[Error] Semantic analysis produced an empty CodeGraph.");
                return ExitCodeError;
            }

            outWriter.WriteLine($"[Macondo] Extraction complete: {semanticGraph.Symbols.Count} symbols, {semanticGraph.Relationships.Count} relationships.");

            // 5. Loading custom architectural rules (optional)
            List<ArchitecturalRule>? customRules = null;
            if (!string.IsNullOrEmpty(rulesConfigPath))
            {
                if (File.Exists(rulesConfigPath))
                {
                    var json = File.ReadAllText(rulesConfigPath);
                    customRules = JsonSerializer.Deserialize<List<ArchitecturalRule>>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        Converters = { new JsonStringEnumConverter() }
                    });
                    outWriter.WriteLine($"[Macondo] Loaded {customRules?.Count ?? 0} custom architectural rule(s) from: {rulesConfigPath}");
                }
                else
                {
                    errorWriter.WriteLine($"[Warning] Rules config file not found: {rulesConfigPath}");
                }
            }

            // 6. Legacy Exports (Repo Map & Raw JSON)
            if (!string.IsNullOrEmpty(exportMdPath))
            {
                var md = CodeGraphMarkdownExporter.Export(semanticGraph);
                SaveFile(exportMdPath, md);
                outWriter.WriteLine($"[OK] Markdown Repo Map exported to: {Path.GetFullPath(exportMdPath)}");
            }

            if (!string.IsNullOrEmpty(exportJsonPath))
            {
                var json = CodeGraphJsonExporter.ExportToJson(semanticGraph, indented: true);
                SaveFile(exportJsonPath, json);
                outWriter.WriteLine($"[OK] CodeGraph JSON exported to: {Path.GetFullPath(exportJsonPath)}");
            }

            // 7. Audit Report Generation (--report)
            AuditReportResult? auditResult = null;
            AuditReportResult GetOrBuildAuditResult()
            {
                return auditResult ??= AuditReportBuilder.RunAll(semanticGraph, customRules, msg =>
                {
                    try
                    {
                        outWriter.WriteLine($" [Analytics] {msg}");
                    }
                    catch (ObjectDisposedException)
                    {
                        // Ignore whether the writer has been released
                    }
                });
            }

            if (!string.IsNullOrEmpty(reportPath))
            {
                var audit = GetOrBuildAuditResult();
                var fullReportPath = Path.GetFullPath(reportPath);
                var isJson = reportPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

                var content = isJson ? audit.ToJson(indented: true) : audit.ToMarkdown();
                SaveFile(fullReportPath, content);
                outWriter.WriteLine($"[OK] Audit Report ({ (isJson ? "JSON" : "Markdown") }) exported to: {fullReportPath}");
            }

            // 8. Batch export to destination folder (--out-dir)
            if (!string.IsNullOrEmpty(outDir))
            {
                var audit = GetOrBuildAuditResult();
                var fullOutDir = Path.GetFullPath(outDir);
                if (!Directory.Exists(fullOutDir))
                    Directory.CreateDirectory(fullOutDir);

                var selected = (requestedAnalyses ?? "all").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(s => s.ToLowerInvariant()).ToHashSet();

                bool wantAll = selected.Contains("all");
                bool wantMd = format is "md" or "all";
                bool wantJson = format is "json" or "all";

                void ExportItem<T>(string baseName, T dataset, Func<T, string> mdExporter, Func<T, string> jsonExporter)
                {
                    if (wantMd)
                    {
                        var filePath = Path.Combine(fullOutDir, $"{baseName}.md");
                        SaveFile(filePath, mdExporter(dataset));
                    }
                    if (wantJson)
                    {
                        var filePath = Path.Combine(fullOutDir, $"{baseName}.json");
                        SaveFile(filePath, jsonExporter(dataset));
                    }
                }

                if (wantAll || selected.Contains("dead-code"))
                    ExportItem("dead-code", audit.DeadCode, AnalysisMarkdownExporter.ExportDeadCode, d => AnalysisJsonExporter.ExportToJson(d));

                if (wantAll || selected.Contains("maintainability"))
                    ExportItem("maintainability", audit.Maintainability, AnalysisMarkdownExporter.ExportMaintainability, d => AnalysisJsonExporter.ExportToJson(d));

                if (wantAll || selected.Contains("rules") || selected.Contains("architectural-rules"))
                    ExportItem("architectural-rules", audit.Rules, AnalysisMarkdownExporter.ExportArchitecturalRules, d => AnalysisJsonExporter.ExportToJson(d));

                if (wantAll || selected.Contains("clones") || selected.Contains("code-clones"))
                    ExportItem("code-clones", audit.Clones, AnalysisMarkdownExporter.ExportCodeClones, d => AnalysisJsonExporter.ExportToJson(d));

                if (wantAll || selected.Contains("cohesion"))
                    ExportItem("cohesion", audit.Cohesion, AnalysisMarkdownExporter.ExportCohesion, d => AnalysisJsonExporter.ExportToJson(d));

                if (wantAll || selected.Contains("martin") || selected.Contains("martin-metrics"))
                    ExportItem("martin-metrics", audit.Martin, AnalysisMarkdownExporter.ExportMartinMetrics, d => AnalysisJsonExporter.ExportToJson(d));

                if (wantAll || selected.Contains("shared-state"))
                    ExportItem("shared-state", audit.SharedState, AnalysisMarkdownExporter.ExportSharedState, d => AnalysisJsonExporter.ExportToJson(d));

                if (wantAll || selected.Contains("crud") || selected.Contains("crud-matrix"))
                    ExportItem("crud-matrix", audit.CrudMatrix, AnalysisMarkdownExporter.ExportCrudMatrix, d => AnalysisJsonExporter.ExportToJson(d));

                if (wantAll || selected.Contains("data-flow") || selected.Contains("taint"))
                    ExportItem("data-flow", audit.DataFlow, AnalysisMarkdownExporter.ExportDataFlow, d => AnalysisJsonExporter.ExportToJson(d));

                if (wantAll || selected.Contains("screen-flow"))
                    ExportItem("screen-flow", audit.ScreenFlow, AnalysisMarkdownExporter.ExportScreenFlow, d => AnalysisJsonExporter.ExportToJson(d));

                if (wantAll || selected.Contains("dsm"))
                    ExportItem("dsm-matrix", audit.Dsm, AnalysisMarkdownExporter.ExportDsm, d => AnalysisJsonExporter.ExportToJson(d));

                if (wantAll || selected.Contains("repo-map"))
                {
                    if (wantMd)
                        SaveFile(Path.Combine(fullOutDir, "repo-map.md"), CodeGraphMarkdownExporter.Export(semanticGraph));
                    if (wantJson)
                        SaveFile(Path.Combine(fullOutDir, "graph.json"), CodeGraphJsonExporter.ExportToJson(semanticGraph));
                }

                outWriter.WriteLine($"[OK] All requested analyses exported to folder: {fullOutDir}");
            }

            // 9. Granular exports for individual flags
            void ExportGranular<T>(string? path, Func<T> datasetFactory, Func<T, string> mdExporter, Func<T, string> jsonExporter, string label)
            {
                if (string.IsNullOrEmpty(path))
                    return;
                var data = datasetFactory();
                var isJson = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
                var content = isJson ? jsonExporter(data) : mdExporter(data);
                SaveFile(path, content);
                outWriter.WriteLine($"[OK] {label} exported to: {Path.GetFullPath(path)}");
            }

            ExportGranular(deadCodePath, () => DeadCodeAnalyzer.Analyze(semanticGraph), AnalysisMarkdownExporter.ExportDeadCode, d => AnalysisJsonExporter.ExportToJson(d), "Dead Code Report");
            ExportGranular(maintainabilityPath, () => MaintainabilityCalculator.Analyze(semanticGraph), AnalysisMarkdownExporter.ExportMaintainability, d => AnalysisJsonExporter.ExportToJson(d), "Maintainability Report");
            ExportGranular(rulesPath, () => ArchitecturalRulesEngine.Evaluate(semanticGraph, customRules), AnalysisMarkdownExporter.ExportArchitecturalRules, d => AnalysisJsonExporter.ExportToJson(d), "Architectural Rules Report");
            ExportGranular(clonesPath, () => CodeCloneDetector.DetectClones(semanticGraph), AnalysisMarkdownExporter.ExportCodeClones, d => AnalysisJsonExporter.ExportToJson(d), "Code Clones Report");
            ExportGranular(cohesionPath, () => CohesionCalculator.Analyze(semanticGraph), AnalysisMarkdownExporter.ExportCohesion, d => AnalysisJsonExporter.ExportToJson(d), "Cohesion Report");
            ExportGranular(martinPath, () => MartinMetricsCalculator.Analyze(semanticGraph), AnalysisMarkdownExporter.ExportMartinMetrics, d => AnalysisJsonExporter.ExportToJson(d), "Martin Metrics Report");
            ExportGranular(sharedStatePath, () => SharedStateAnalyzer.Analyze(semanticGraph), AnalysisMarkdownExporter.ExportSharedState, d => AnalysisJsonExporter.ExportToJson(d), "Shared State Report");
            ExportGranular(crudPath, () => CrudMatrixAnalyzer.Analyze(semanticGraph), AnalysisMarkdownExporter.ExportCrudMatrix, d => AnalysisJsonExporter.ExportToJson(d), "CRUD Matrix Report");
            ExportGranular(dataFlowPath, () => TaintAnalyzer.Analyze(semanticGraph), AnalysisMarkdownExporter.ExportDataFlow, d => AnalysisJsonExporter.ExportToJson(d), "Data Flow Report");
            ExportGranular(screenFlowPath, () => ScreenFlowBuilder.Build(semanticGraph), AnalysisMarkdownExporter.ExportScreenFlow, d => AnalysisJsonExporter.ExportToJson(d), "Screen Flow Report");
            ExportGranular(dsmPath, () => DsmMatrixBuilder.Build(semanticGraph), AnalysisMarkdownExporter.ExportDsm, d => AnalysisJsonExporter.ExportToJson(d), "DSM Report");

            // 10. Impact Analysis (Blast Radius) on a single symbol
            if (impactArg.HasValue)
            {
                var targetSymName = impactArg.Value.SymbolName;
                var targetSym = semanticGraph.Symbols.FirstOrDefault(s =>
                    s.Name.Equals(targetSymName, StringComparison.OrdinalIgnoreCase) ||
                    s.QualifiedName.Equals(targetSymName, StringComparison.OrdinalIgnoreCase));

                if (targetSym == null)
                    errorWriter.WriteLine($"[Warning] Symbol not found for Impact Analysis: {targetSymName}");
                else
                {
                    var impactAnalyzer = new ImpactAnalyzer(semanticGraph);
                    var impactReport = impactAnalyzer.AnalyzeSymbolImpact(targetSym);
                    var isJson = impactArg.Value.OutPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
                    var content = isJson ? AnalysisJsonExporter.ExportToJson(impactReport) : AnalysisMarkdownExporter.ExportImpact(impactReport);
                    SaveFile(impactArg.Value.OutPath, content);
                    outWriter.WriteLine($"[OK] Impact Analysis for '{targetSym.QualifiedName}' exported to: {Path.GetFullPath(impactArg.Value.OutPath)}");
                }
            }

            // 11. Evaluation of CI/CD Quality Gates
            bool qualityGateFailed = false;

            if (failOnViolations)
            {
                var rules = auditResult?.Rules ?? ArchitecturalRulesEngine.Evaluate(semanticGraph, customRules);
                var criticalAndErrorCount = rules.CriticalViolations + rules.ErrorViolations;
                if (criticalAndErrorCount > 0)
                {
                    errorWriter.WriteLine($"[Quality Gate FAILED] Found {criticalAndErrorCount} architectural rule violation(s) ({rules.CriticalViolations} critical, {rules.ErrorViolations} error).");
                    qualityGateFailed = true;
                }
                else
                    outWriter.WriteLine($"[Quality Gate PASSED] Zero critical/error architectural violations.");
            }

            if (failOnMiThreshold.HasValue)
            {
                var miData = auditResult?.Maintainability ?? MaintainabilityCalculator.Analyze(semanticGraph);
                if (miData.AverageMaintainabilityIndex < failOnMiThreshold.Value)
                {
                    errorWriter.WriteLine($"[Quality Gate FAILED] Average Maintainability Index ({miData.AverageMaintainabilityIndex:F1}) is below threshold ({failOnMiThreshold.Value:F1}).");
                    qualityGateFailed = true;
                }
                else
                    outWriter.WriteLine($"[Quality Gate PASSED] Average Maintainability Index ({miData.AverageMaintainabilityIndex:F1}) meets threshold ({failOnMiThreshold.Value:F1}).");
            }

            if (failOnDeadCodeThreshold.HasValue)
            {
                var deadData = auditResult?.DeadCode ?? DeadCodeAnalyzer.Analyze(semanticGraph);
                if (deadData.DeadCodeRatio > failOnDeadCodeThreshold.Value)
                {
                    errorWriter.WriteLine($"[Quality Gate FAILED] Dead code ratio ({deadData.DeadCodeRatio:F1}%) exceeds allowed maximum ({failOnDeadCodeThreshold.Value:F1}%).");
                    qualityGateFailed = true;
                }
                else
                    outWriter.WriteLine($"[Quality Gate PASSED] Dead code ratio ({deadData.DeadCodeRatio:F1}%) is within allowed limit ({failOnDeadCodeThreshold.Value:F1}%).");
            }

            if (qualityGateFailed)
                return ExitCodeQualityGateFailed;

            outWriter.WriteLine("[Macondo] Execution completed successfully.");
            return ExitCodeSuccess;
        }
        catch (Exception ex)
        {
            errorWriter.WriteLine($"[Fatal Error] Analysis failed: {ex.Message}");
            if (verbose)
                errorWriter.WriteLine($"\n[Exception Details & Stack Trace]\n{ex}");
            else
                errorWriter.WriteLine($"[Tip] Run with --verbose to display the complete stack trace and diagnostics.");
            return ExitCodeError;
        }
    }

    /// <summary>
    /// Saves the specified content to the specified location, creating parent folders if they do not exist.
    /// The file is written in UTF-8 encoding without BOM for maximum compatibility with standard tools and scripts.
    /// </summary>
    /// <param name="path">Path to the destination file.</param>
    /// <param name="content">Text content to be written in UTF-8 encoding without BOM.</param>
    public static void SaveFile(string path, string content)
    {
        var fullPath = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(fullPath, content, new UTF8Encoding(false));
    }

    /// <summary>
    /// Prints the complete guide to using the command line on the specified writer.
    /// </summary>
    /// <param name="outWriter">Target stream (if null, use <see cref="Console.Out"/>).</param>
    public static void PrintHelp(TextWriter? outWriter = null)
    {
        outWriter ??= Console.Out;
        outWriter.WriteLine(@"
Macondo — Architecture & Code Analyzer (CLI Mode)

Usage:
  Macondo.exe analyze <path_to_vbp_or_folder> [options]

Audit & Comprehensive Reports:
  --report <file.md|file.json>, -r <file>    Export complete Architecture & Quality Audit Report
  --out-dir <folder>, -o <folder>            Export all analysis reports into a directory
  --analysis <list>, -a <list>               Comma-separated list of analyses to export:
                                             (all, dead-code, maintainability, rules, clones,
                                              cohesion, martin, shared-state, crud, data-flow,
                                              screen-flow, dsm, repo-map)
  --format <md|json|all>, -f <format>        Output format for --out-dir (default: md)

Individual Analysis Exports:
  --dead-code [file.md|json]                 Dead Code & Reachability analysis
  --maintainability [file.md|json]           Maintainability Index (MI) & Halstead metrics
  --rules [file.md|json]                     Architectural rules & linter violations
  --clones [file.md|json]                    Code clone detection (CPD) & duplications
  --cohesion [file.md|json]                  Class cohesion (LCOM4, TCC, C&K suite)
  --martin [file.md|json]                    Robert C. Martin coupling & stability metrics
  --shared-state [file.md|json]              Shared global state & mutation risk analysis
  --crud [file.md|json]                      CRUD operations matrix & data entities
  --data-flow [file.md|json]                 Data flow & taint analysis (source-to-sink)
  --screen-flow [file.md|json]               UI navigation graph & screen transitions
  --dsm [file.md|json]                       Design Structure Matrix (DSM) & cycle clusters
  --impact <symbol> [file.md|json]           Blast radius & impact analysis for a specific symbol

Legacy Formats:
  --export-md <file.md>, -m <file.md>        Export hierarchical Repo Map in Markdown format
  --export-json <file.json>, -j <file.json>  Export complete CodeGraph in JSON format

Configuration & Rulesets:
  --rules-config <file.json>                 Load custom architectural rules from a JSON file

CI/CD Quality Gates (Returns Exit Code 2 on failure):
  --fail-on-violations, --fail-on-rules      Fail if any Critical or Error architectural violations exist
  --fail-on-mi <threshold>                   Fail if average Maintainability Index is below threshold (e.g. 50)
  --fail-on-dead-code <percentage>           Fail if dead code ratio exceeds percentage (e.g. 20)

General:
  --verbose, -v                              Enable detailed/verbose logging with full exception stack traces
  --help, -h                                 Show this help message

Examples:
  # 1. Full Audit Report in Markdown
  Macondo.exe analyze ""C:\Progetti\AppVB6"" --report ""audit-report.md""

  # 2. Export all analyses to a reports folder in both MD and JSON
  Macondo.exe analyze ""C:\Progetti\AppVB6"" --out-dir ""./reports"" --format all

  # 3. CI/CD Quality Gate in GitHub Actions / Azure DevOps
  Macondo.exe analyze ""C:\Progetti\AppVB6"" --fail-on-violations --fail-on-mi 40

  # 4. Blast Radius Analysis on a specific routine
  Macondo.exe analyze ""C:\Progetti\AppVB6"" --impact ""frmMain.Form_Load"" ""blast-radius.md""
");
    }

    /// <summary>
    /// Synchronous and thread-safe implementation of <see cref="IProgress{T}"/> for CLI/batch environments.
    /// Avoid asynchronous queuing on ThreadPool/SynchronizationContext which may attempt writes to already released streams.
    /// </summary>
    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value)
        {
            try
            {
                handler(value);
            }
            catch (ObjectDisposedException)
            {
                // Ignore writes if the underlying output stream has been closed
            }
        }
    }
}
