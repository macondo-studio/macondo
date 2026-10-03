using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Macondo.App.Models;
using Macondo.App.Services;
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

namespace Macondo.App;

internal static class Program
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);
    private const uint ATTACH_PARENT_PROCESS = unchecked((uint)-1);

    [STAThread]
    public static int Main(string[] args)
    {
        if (IsCliInvocation(args))
        {
            if (OperatingSystem.IsWindows())

                AttachConsole(ATTACH_PARENT_PROCESS);
            return RunCli(args);
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();

    private static bool IsCliInvocation(string[] args)
    {
        if (args == null || args.Length == 0) return false;
        return args.Any(a => a is "analyze" or "--export-md" or "-m" or "--export-json" or "-j"
            or "--report" or "-r" or "--out-dir" or "-o" or "--analysis" or "-a"
            or "--dead-code" or "--maintainability" or "--rules" or "--clones" or "--cohesion"
            or "--martin" or "--shared-state" or "--crud" or "--data-flow" or "--screen-flow"
            or "--dsm" or "--impact" or "--fail-on-violations" or "--fail-on-rules"
            or "--fail-on-mi" or "--fail-on-dead-code" or "--help" or "-h" or "help" or "/?");
    }

    private static int RunCli(string[] args)
    {
        if (args.Any(a => a is "-h" or "--help" or "help" or "/?"))
        {
            PrintCliHelp();
            return 0;
        }

        // Input & Output options
        var inputPaths = new List<string>();
        string? exportMdPath = null;
        string? exportJsonPath = null;
        string? reportPath = null;
        string? outDir = null;
        string? requestedAnalyses = null;
        string format = "md"; // "md", "json", "all"
        string? rulesConfigPath = null;

        // Granular file outputs
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

        // CI/CD Quality Gate options
        bool failOnViolations = false;
        double? failOnMiThreshold = null;
        double? failOnDeadCodeThreshold = null;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("analyze", StringComparison.OrdinalIgnoreCase))

                continue;

            else if (arg.Equals("--export-md", StringComparison.OrdinalIgnoreCase) || arg.Equals("-m", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length) exportMdPath = args[++i];
                else { Console.Error.WriteLine("[Error] Missing file path for --export-md"); return 1; }
            }
            else if (arg.Equals("--export-json", StringComparison.OrdinalIgnoreCase) || arg.Equals("-j", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length) exportJsonPath = args[++i];
                else { Console.Error.WriteLine("[Error] Missing file path for --export-json"); return 1; }
            }
            else if (arg.Equals("--report", StringComparison.OrdinalIgnoreCase) || arg.Equals("-r", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length) reportPath = args[++i];
                else { Console.Error.WriteLine("[Error] Missing file path for --report"); return 1; }
            }
            else if (arg.Equals("--out-dir", StringComparison.OrdinalIgnoreCase) || arg.Equals("-o", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length) outDir = args[++i];
                else { Console.Error.WriteLine("[Error] Missing directory path for --out-dir"); return 1; }
            }
            else if (arg.Equals("--analysis", StringComparison.OrdinalIgnoreCase) || arg.Equals("-a", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length) requestedAnalyses = args[++i];
                else { Console.Error.WriteLine("[Error] Missing value for --analysis"); return 1; }
            }
            else if (arg.Equals("--format", StringComparison.OrdinalIgnoreCase) || arg.Equals("-f", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length) format = args[++i].ToLowerInvariant();
                else { Console.Error.WriteLine("[Error] Missing value for --format"); return 1; }
            }
            else if (arg.Equals("--rules-config", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length) rulesConfigPath = args[++i];
                else { Console.Error.WriteLine("[Error] Missing file path for --rules-config"); return 1; }
            }
            else if (arg.Equals("--dead-code", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) deadCodePath = args[++i];
                else deadCodePath = "dead-code.md";
            }
            else if (arg.Equals("--maintainability", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) maintainabilityPath = args[++i];
                else maintainabilityPath = "maintainability.md";
            }
            else if (arg.Equals("--rules", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) rulesPath = args[++i];
                else rulesPath = "architectural-rules.md";
            }
            else if (arg.Equals("--clones", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) clonesPath = args[++i];
                else clonesPath = "code-clones.md";
            }
            else if (arg.Equals("--cohesion", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) cohesionPath = args[++i];
                else cohesionPath = "cohesion.md";
            }
            else if (arg.Equals("--martin", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) martinPath = args[++i];
                else martinPath = "martin-metrics.md";
            }
            else if (arg.Equals("--shared-state", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) sharedStatePath = args[++i];
                else sharedStatePath = "shared-state.md";
            }
            else if (arg.Equals("--crud", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) crudPath = args[++i];
                else crudPath = "crud-matrix.md";
            }
            else if (arg.Equals("--data-flow", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) dataFlowPath = args[++i];
                else dataFlowPath = "data-flow.md";
            }
            else if (arg.Equals("--screen-flow", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) screenFlowPath = args[++i];
                else screenFlowPath = "screen-flow.md";
            }
            else if (arg.Equals("--dsm", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) dsmPath = args[++i];
                else dsmPath = "dsm-matrix.md";
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
                    Console.Error.WriteLine("[Error] Missing symbol name for --impact <symbol> [output_file]");
                    return 1;
                }
            }
            else if (arg.Equals("--fail-on-violations", StringComparison.OrdinalIgnoreCase) || arg.Equals("--fail-on-rules", StringComparison.OrdinalIgnoreCase))

    failOnViolations = true;

else if (arg.Equals("--fail-on-mi", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && double.TryParse(args[++i], System.Globalization.CultureInfo.InvariantCulture, out var threshold))

                    failOnMiThreshold = threshold;

                else
                {
                    Console.Error.WriteLine("[Error] Missing or invalid numeric threshold for --fail-on-mi <number>");
                    return 1;
                }
            }
            else if (arg.Equals("--fail-on-dead-code", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && double.TryParse(args[++i].Replace("%", ""), System.Globalization.CultureInfo.InvariantCulture, out var pct))

                    failOnDeadCodeThreshold = pct;

                else
                {
                    Console.Error.WriteLine("[Error] Missing or invalid numeric threshold for --fail-on-dead-code <percentage>");
                    return 1;
                }
            }
            else if (!arg.StartsWith("-"))

    inputPaths.Add(arg);
        }

        if (inputPaths.Count == 0)
        {
            Console.Error.WriteLine("[Error] No target project, group or folder path specified for analysis.");
            PrintCliHelp();
            return 1;
        }

        bool hasSpecificOutput = exportMdPath != null || exportJsonPath != null || reportPath != null
            || outDir != null || deadCodePath != null || maintainabilityPath != null || rulesPath != null
            || clonesPath != null || cohesionPath != null || martinPath != null || sharedStatePath != null
            || crudPath != null || dataFlowPath != null || screenFlowPath != null || dsmPath != null || impactArg != null;

        // If no specific export or out-dir is given, default to generating a comprehensive audit report
        if (!hasSpecificOutput && !failOnViolations && !failOnMiThreshold.HasValue && !failOnDeadCodeThreshold.HasValue)

            reportPath = "audit-report.md";

        try
        {
            Console.WriteLine($"[Macondo] Discovering projects from {inputPaths.Count} path(s)...");
            var projects = ProjectDescriptor.DiscoverFromPaths(inputPaths);
            if (projects.Count == 0)
            {
                Console.Error.WriteLine($"[Error] No supported project files (.vbp, .vbg, .csproj, .sln) or source files found in: {string.Join(", ", inputPaths)}");
                return 1;
            }

            var totalFiles = projects.Sum(p => p.SourceFiles.Count);
            Console.WriteLine($"[Macondo] Discovered {projects.Count} project(s) with {totalFiles} source file(s).");

            var progress = new Progress<ProjectAnalysisProgress>(p =>
            {
                if (!string.IsNullOrEmpty(p.CurrentFileName))

                    Console.WriteLine($" [{p.CurrentFileIndex}/{p.TotalFiles}] {p.CurrentFileName} ({p.ExtractedNodesCount} symbols)");

                else if (!string.IsNullOrEmpty(p.PhaseMessage))

    Console.WriteLine($" -> {p.PhaseMessage}");
            });

            var visualGraph = ProjectAnalysisService.AnalyzeProjects(projects, progress);
            var semanticGraph = visualGraph.SemanticGraph;
            if (semanticGraph == null || semanticGraph.Symbols.Count == 0)
            {
                Console.Error.WriteLine("[Error] Semantic analysis produced an empty CodeGraph.");
                return 1;
            }

            Console.WriteLine($"[Macondo] Extraction complete: {semanticGraph.Symbols.Count} symbols, {semanticGraph.Relationships.Count} relationships.");

            // Load custom architectural rules if specified
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
                    Console.WriteLine($"[Macondo] Loaded {customRules?.Count ?? 0} custom architectural rule(s) from: {rulesConfigPath}");
                }
                else
                {
                    Console.Error.WriteLine($"[Warning] Rules config file not found: {rulesConfigPath}");
                }
            }

            // 1. Legacy exports (Repo Map & Raw JSON)
            if (!string.IsNullOrEmpty(exportMdPath))
            {
                var md = CodeGraphMarkdownExporter.Export(semanticGraph);
                SaveFile(exportMdPath, md);
                Console.WriteLine($"[OK] Markdown Repo Map exported to: {Path.GetFullPath(exportMdPath)}");
            }

            if (!string.IsNullOrEmpty(exportJsonPath))
            {
                var json = CodeGraphJsonExporter.ExportToJson(semanticGraph, indented: true);
                SaveFile(exportJsonPath, json);
                Console.WriteLine($"[OK] CodeGraph JSON exported to: {Path.GetFullPath(exportJsonPath)}");
            }

            // 2. Full Audit Report (--report)
            AuditReportResult? auditResult = null;
            AuditReportResult GetOrBuildAuditResult()
            {
                return auditResult ??= AuditReportBuilder.RunAll(semanticGraph, customRules, msg => Console.WriteLine($" [Analytics] {msg}"));
            }

            if (!string.IsNullOrEmpty(reportPath))
            {
                var audit = GetOrBuildAuditResult();
                var fullReportPath = Path.GetFullPath(reportPath);
                var isJson = reportPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

                var content = isJson ? audit.ToJson(indented: true) : audit.ToMarkdown();
                SaveFile(fullReportPath, content);
                Console.WriteLine($"[OK] Audit Report ({ (isJson ? "JSON" : "Markdown") }) exported to: {fullReportPath}");
            }

            // 3. Batch export to Output Directory (--out-dir)
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
                    if (wantMd) SaveFile(Path.Combine(fullOutDir, "repo-map.md"), CodeGraphMarkdownExporter.Export(semanticGraph));
                    if (wantJson) SaveFile(Path.Combine(fullOutDir, "graph.json"), CodeGraphJsonExporter.ExportToJson(semanticGraph));
                }

                Console.WriteLine($"[OK] All requested analyses exported to folder: {fullOutDir}");
            }

            // 4. Granular CLI flags
            void ExportGranular<T>(string? path, Func<T> datasetFactory, Func<T, string> mdExporter, Func<T, string> jsonExporter, string label)
            {
                if (string.IsNullOrEmpty(path)) return;
                var data = datasetFactory();
                var isJson = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
                var content = isJson ? jsonExporter(data) : mdExporter(data);
                SaveFile(path, content);
                Console.WriteLine($"[OK] {label} exported to: {Path.GetFullPath(path)}");
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

            if (impactArg.HasValue)
            {
                var targetSymName = impactArg.Value.SymbolName;
                var targetSym = semanticGraph.Symbols.FirstOrDefault(s =>
                    s.Name.Equals(targetSymName, StringComparison.OrdinalIgnoreCase) ||
                    s.QualifiedName.Equals(targetSymName, StringComparison.OrdinalIgnoreCase));

                if (targetSym == null)

                    Console.Error.WriteLine($"[Warning] Symbol not found for Impact Analysis: {targetSymName}");

                else
                {
                    var impactAnalyzer = new ImpactAnalyzer(semanticGraph);
                    var impactReport = impactAnalyzer.AnalyzeSymbolImpact(targetSym);
                    var isJson = impactArg.Value.OutPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
                    var content = isJson ? AnalysisJsonExporter.ExportToJson(impactReport) : AnalysisMarkdownExporter.ExportImpact(impactReport);
                    SaveFile(impactArg.Value.OutPath, content);
                    Console.WriteLine($"[OK] Impact Analysis for '{targetSym.QualifiedName}' exported to: {Path.GetFullPath(impactArg.Value.OutPath)}");
                }
            }

            // 5. CI/CD Quality Gates evaluation
            bool qualityGateFailed = false;

            if (failOnViolations)
            {
                var rules = auditResult?.Rules ?? ArchitecturalRulesEngine.Evaluate(semanticGraph, customRules);
                var criticalAndErrorCount = rules.CriticalViolations + rules.ErrorViolations;
                if (criticalAndErrorCount > 0)
                {
                    Console.Error.WriteLine($"[Quality Gate FAILED] Found {criticalAndErrorCount} architectural rule violation(s) ({rules.CriticalViolations} critical, {rules.ErrorViolations} error).");
                    qualityGateFailed = true;
                }
                else
                {
                    Console.WriteLine($"[Quality Gate PASSED] Zero critical/error architectural violations.");
                }
            }

            if (failOnMiThreshold.HasValue)
            {
                var miData = auditResult?.Maintainability ?? MaintainabilityCalculator.Analyze(semanticGraph);
                if (miData.AverageMaintainabilityIndex < failOnMiThreshold.Value)
                {
                    Console.Error.WriteLine($"[Quality Gate FAILED] Average Maintainability Index ({miData.AverageMaintainabilityIndex:F1}) is below threshold ({failOnMiThreshold.Value:F1}).");
                    qualityGateFailed = true;
                }
                else
                {
                    Console.WriteLine($"[Quality Gate PASSED] Average Maintainability Index ({miData.AverageMaintainabilityIndex:F1}) meets threshold ({failOnMiThreshold.Value:F1}).");
                }
            }

            if (failOnDeadCodeThreshold.HasValue)
            {
                var deadData = auditResult?.DeadCode ?? DeadCodeAnalyzer.Analyze(semanticGraph);
                if (deadData.DeadCodeRatio > failOnDeadCodeThreshold.Value)
                {
                    Console.Error.WriteLine($"[Quality Gate FAILED] Dead code ratio ({deadData.DeadCodeRatio:F1}%) exceeds allowed maximum ({failOnDeadCodeThreshold.Value:F1}%).");
                    qualityGateFailed = true;
                }
                else
                {
                    Console.WriteLine($"[Quality Gate PASSED] Dead code ratio ({deadData.DeadCodeRatio:F1}%) is within allowed limit ({failOnDeadCodeThreshold.Value:F1}%).");
                }
            }

            if (qualityGateFailed)

                return 2; // Exit code 2 indicates CI/CD Quality Gate failure

            Console.WriteLine("[Macondo] Execution completed successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Fatal Error] Analysis failed: {ex.Message}");
            return 1;
        }
    }

    private static void SaveFile(string path, string content)
    {
        var fullPath = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(fullPath, content, Encoding.UTF8);
    }

    private static void PrintCliHelp()
    {
        Console.WriteLine(@"
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
}
