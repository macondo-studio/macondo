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
/// Motore di esecuzione CLI (Command-Line Interface) per Macondo.
/// Gestisce il parsing degli argomenti, l'esecuzione delle analisi semantiche,
/// la generazione dei report (Markdown e JSON) e la valutazione dei Quality Gate CI/CD.
/// </summary>
public static class CliRunner
{
    /// <summary>
    /// Codice di uscita per esecuzione completata con successo.
    /// </summary>
    public const int ExitCodeSuccess = 0;

    /// <summary>
    /// Codice di uscita per errori di parsing, argomenti mancanti/non validi o eccezioni impreviste.
    /// </summary>
    public const int ExitCodeError = 1;

    /// <summary>
    /// Codice di uscita quando un Quality Gate CI/CD fallisce (violazioni architetturali, MI o Dead Code).
    /// </summary>
    public const int ExitCodeQualityGateFailed = 2;

    /// <summary>
    /// Determina se gli argomenti della riga di comando corrispondono a una modalità di esecuzione CLI non interattiva.
    /// </summary>
    /// <param name="args">Array degli argomenti passati al processo.</param>
    /// <returns><c>true</c> se è richiesta l'esecuzione CLI o la guida; altrimenti <c>false</c>.</returns>
    public static bool IsCliInvocation(string[]? args)
    {
        if (args == null || args.Length == 0)
            return false;

        return args.Any(a => a is "analyze" or "--export-md" or "-m" or "--export-json" or "-j"
            or "--report" or "-r" or "--out-dir" or "-o" or "--analysis" or "-a"
            or "--dead-code" or "--maintainability" or "--rules" or "--clones" or "--cohesion"
            or "--martin" or "--shared-state" or "--crud" or "--data-flow" or "--screen-flow"
            or "--dsm" or "--impact" or "--fail-on-violations" or "--fail-on-rules"
            or "--fail-on-mi" or "--fail-on-dead-code" or "--help" or "-h" or "help" or "/?");
    }

    /// <summary>
    /// Esegue il comando CLI analizzando i parametri forniti ed emettendo i risultati sui flussi di output specificati.
    /// </summary>
    /// <param name="args">Argomenti da riga di comando.</param>
    /// <param name="outWriter">Flusso per i messaggi informativi e di progresso (se null, usa <see cref="Console.Out"/>).</param>
    /// <param name="errorWriter">Flusso per i messaggi di errore (se null, usa <see cref="Console.Error"/>).</param>
    /// <returns>
    /// Codice di uscita:
    /// <list type="bullet">
    ///   <item><description><see cref="ExitCodeSuccess"/> (0): Analisi ed export completati con successo.</description></item>
    ///   <item><description><see cref="ExitCodeError"/> (1): Parametri non validi o errore durante l'esecuzione.</description></item>
    ///   <item><description><see cref="ExitCodeQualityGateFailed"/> (2): Quality Gate CI/CD non superato.</description></item>
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

        // 1. Controllo richiesta Help
        if (args.Any(a => a is "-h" or "--help" or "help" or "/?"))
        {
            PrintHelp(outWriter);
            return ExitCodeSuccess;
        }

        // Variabili di configurazione input & output generale
        var inputPaths = new List<string>();
        string? exportMdPath = null;
        string? exportJsonPath = null;
        string? reportPath = null;
        string? outDir = null;
        string? requestedAnalyses = null;
        string format = "md"; // Formati supportati: "md", "json", "all"
        string? rulesConfigPath = null;

        // Output specifici per singole analisi (granular flags)
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

        // Opzioni per CI/CD Quality Gates
        bool failOnViolations = false;
        double? failOnMiThreshold = null;
        double? failOnDeadCodeThreshold = null;

        // 2. Parsing degli argomenti da riga di comando
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
            // Batch export su directory
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
            // File di configurazione regole architetturali custom
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
            // Esportazioni singole granulari (il file di output è opzionale: se omesso, viene usato un nome predefinito)
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
            else if (!arg.StartsWith("-"))
                inputPaths.Add(arg);
        }

        // 3. Validazione percorsi sorgente
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

        // Se non è stato specificato alcun output né alcun quality gate, il default è generare un audit report completo
        if (!hasSpecificOutput && !failOnViolations && !failOnMiThreshold.HasValue && !failOnDeadCodeThreshold.HasValue)
            reportPath = "audit-report.md";

        try
        {
            // 4. Discovery e analisi semantica dei progetti
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
                    // Ignora scritture se il writer è stato rilasciato
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

            // 5. Caricamento regole architetturali custom (opzionale)
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

            // 6. Esportazioni Legacy (Repo Map & Raw JSON)
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

            // 7. Generazione Audit Report (--report)
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
                        // Ignora se il writer è stato rilasciato
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

            // 8. Esportazione batch su cartella di destinazione (--out-dir)
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

            // 9. Esportazioni granulari per flag individuali
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

            // 10. Impact Analysis (Blast Radius) su singolo simbolo
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

            // 11. Valutazione dei Quality Gate CI/CD
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
            return ExitCodeError;
        }
    }

    /// <summary>
    /// Salva il contenuto specificato nel percorso indicato, creando le cartelle padre se non esistono.
    /// </summary>
    /// <param name="path">Percorso del file di destinazione.</param>
    /// <param name="content">Contenuto testuale da scrivere in codifica UTF-8.</param>
    public static void SaveFile(string path, string content)
    {
        var fullPath = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(fullPath, content, Encoding.UTF8);
    }

    /// <summary>
    /// Stampa la guida completa all'utilizzo della riga di comando sul writer specificato.
    /// </summary>
    /// <param name="outWriter">Flusso di destinazione (se null, usa <see cref="Console.Out"/>).</param>
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
    /// Implementazione sincrona e thread-safe di <see cref="IProgress{T}"/> per ambienti CLI/batch.
    /// Evita l'accodamento asincrono su ThreadPool/SynchronizationContext che potrebbe tentare scritture su flussi già rilasciati.
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
                // Ignora scritture se lo stream di output sottostante è stato chiuso
            }
        }
    }
}
