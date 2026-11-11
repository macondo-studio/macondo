using Macondo.App.Layout;
using Macondo.App.Models;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;
using Macondo.Semantic.Resolution;

namespace Macondo.App.Services;

/// <summary>
/// Analysis and pipeline service to load and analyze real VB6 projects in memory (M5).
/// </summary>
public static class ProjectAnalysisService
{
    /// <summary>
    /// Parse multiple folders, .vbp files or .vbg files within a single semantic workspace.
    /// </summary>
    public static VisualCodeGraph AnalyzePaths(
        IReadOnlyList<string> paths,
        IProgress<ProjectAnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var projects = ProjectDescriptor.DiscoverFromPaths(paths, cancellationToken);
        if (projects.Count == 0)
        {
            var emptySemanticGraph = new Semantic.Model.CodeGraph();
            return GraphVisualAdapter.FromSemanticGraph(emptySemanticGraph, "Empty Workspace");
        }

        return AnalyzeProjects(projects, progress, cancellationToken);
    }

    /// <summary>
    /// Calculate the estimated duration in seconds for scanning, AST parsing, semantic resolution
    /// and CodeGraph generation based on the number of source files and projects in the workspace.
    /// </summary>
    public static double GetEstimatedProjectAnalysisSeconds(int totalFiles, int projectCount = 1)
    {
        if (totalFiles <= 0) return 0.5;

        var cpuCores = Math.Max(1, Environment.ProcessorCount);
        // Real parallelism factor (sub-linear scaling for I/O saturation and thread contention with Antlr)
        var parallelFactor = Math.Max(1.0, Math.Min(cpuCores * 0.7, 8.0));

        // 1. Parallel AST parsing (~220ms CPU time for real VB6/C# source file)
        var parseTime = (totalFiles * 0.22) / parallelFactor;

        // 2. Global semantic type resolution and Call Graph construction (~25ms per file)
        var resolutionTime = 0.5 + (totalFiles * 0.025);

        // 3. Fixed memory allocation and visual adapter overhead for Avalonia
        var totalEstimated = 0.5 + parseTime + resolutionTime;

        return Math.Max(0.5, Math.Round(totalEstimated, 1));
    }

    /// <summary>
    /// Performs complete analysis of a list of VB6 projects creating a single, unified CodeGraph
    /// with cross-project resolution of types, calls and instantiations.
    /// </summary>
    public static VisualCodeGraph AnalyzeProjects(
        IReadOnlyList<ProjectDescriptor> projects,
        IProgress<ProjectAnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var totalFiles = projects.Sum(p => p.SourceFiles.Count);
        var estimatedTotalSeconds = GetEstimatedProjectAnalysisSeconds(totalFiles, projects.Count);
        var analysisStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var semanticGraph = new Semantic.Model.CodeGraph();
        var extractionResults = new List<ModuleExtractionResult>(totalFiles);
        var csharpResults = new List<CSharpExtractionResult>(totalFiles);
        var projectSymbols = new Dictionary<Guid, Symbol>();

        // 1. Record the root symbols of each Project
        foreach (var proj in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isKotlin = proj.SourceFiles.Any(f => f.EndsWith(".kt", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".kts", StringComparison.OrdinalIgnoreCase));
            var isCs = proj.SourceFiles.Any(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) ||
                       (proj.ProjectFilePath != null && (proj.ProjectFilePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || proj.ProjectFilePath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)));
            var isJava = proj.SourceFiles.Any(f => f.EndsWith(".java", StringComparison.OrdinalIgnoreCase)) ||
                         (proj.ProjectFilePath != null && (proj.ProjectFilePath.EndsWith("pom.xml", StringComparison.OrdinalIgnoreCase) || proj.ProjectFilePath.EndsWith(".gradle", StringComparison.OrdinalIgnoreCase) || proj.ProjectFilePath.EndsWith(".gradle.kts", StringComparison.OrdinalIgnoreCase)));
            var projLang = isKotlin ? SourceLanguage.Kotlin : (isJava ? SourceLanguage.Java : (isCs ? SourceLanguage.CSharp : SourceLanguage.VisualBasic6));

            var projSymbol = new Symbol
            {
                Id = proj.Id,
                Kind = SymbolKind.Project,
                Name = proj.Name,
                QualifiedName = proj.Name,
                FilePath = proj.ProjectFilePath ?? proj.FolderPath,
                Language = projLang,
                LineStart = 1,
                LineEnd = 1
            };
            semanticGraph.Symbols.Add(projSymbol);
            projectSymbols[proj.Id] = projSymbol;
        }

        // Initial reporting
        var firstFile = projects.SelectMany(p => p.SourceFiles).FirstOrDefault();
        progress?.Report(new ProjectAnalysisProgress
        {
            CurrentFileIndex = 0,
            TotalFiles = totalFiles,
            CurrentFileName = firstFile != null ? Path.GetFileName(firstFile) : string.Empty,
            ExtractedNodesCount = semanticGraph.Symbols.Count,
            EstimatedTotalSeconds = estimatedTotalSeconds,
            EstimatedRemainingSeconds = estimatedTotalSeconds,
            PhaseMessage = firstFile != null
                ? LocalizationService.Instance.Get("Loading_ParsingFile", Path.GetFileName(firstFile))
                : LocalizationService.Instance["Loading_ResolvingTypes"]
        });

        // 2. AST extraction and multi-core symbols (Parallel.ForEach)
        // Collects all files with their respective project descriptor
        var fileWorkItems = projects
            .SelectMany(p => p.SourceFiles.Select(f => (Project: p, ProjSymbol: projectSymbols[p.Id], FilePath: f)))
            .ToList();

        // Concurrent structures to collect results from various worker threads
        var concurrentVbResults = new System.Collections.Concurrent.ConcurrentBag<(ModuleExtractionResult Result, Semantic.Model.CodeGraph LocalGraph, ProjectDescriptor Proj, Symbol ProjSymbol)>();
        var concurrentCsResults = new System.Collections.Concurrent.ConcurrentBag<(CSharpExtractionResult Result, Semantic.Model.CodeGraph LocalGraph, ProjectDescriptor Proj, Symbol ProjSymbol)>();
        var concurrentJavaResults = new System.Collections.Concurrent.ConcurrentBag<(JavaExtractionResult Result, Semantic.Model.CodeGraph LocalGraph, ProjectDescriptor Proj, Symbol ProjSymbol)>();
        var concurrentKotlinResults = new System.Collections.Concurrent.ConcurrentBag<(KotlinExtractionResult Result, Semantic.Model.CodeGraph LocalGraph, ProjectDescriptor Proj, Symbol ProjSymbol)>();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount),
            CancellationToken = cancellationToken
        };

        var lastReportTicks = Environment.TickCount64;
        int processedFiles = 0;

        Parallel.ForEach(fileWorkItems, parallelOptions, item =>
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            var file = item.FilePath;
            var fileName = Path.GetFileName(file);
            var fileExtension = Path.GetExtension(file);

            if (File.Exists(file))
            {
                var source = File.ReadAllText(file);
                var localGraph = new Semantic.Model.CodeGraph();

                if (cancellationToken.IsCancellationRequested)
                    return;

                if (string.Equals(fileExtension, ".cs", StringComparison.OrdinalIgnoreCase))
                {
                    var csResult = CSharpExtractor.ExtractCSharpFile(localGraph, file, source);
                    if (!cancellationToken.IsCancellationRequested)
                        concurrentCsResults.Add((csResult, localGraph, item.Project, item.ProjSymbol));
                }
                else if (string.Equals(fileExtension, ".java", StringComparison.OrdinalIgnoreCase))
                {
                    var javaResult = JavaExtractor.ExtractJavaFile(localGraph, file, source);
                    if (!cancellationToken.IsCancellationRequested)
                        concurrentJavaResults.Add((javaResult, localGraph, item.Project, item.ProjSymbol));
                }
                else if (string.Equals(fileExtension, ".kt", StringComparison.OrdinalIgnoreCase) || string.Equals(fileExtension, ".kts", StringComparison.OrdinalIgnoreCase))
                {
                    var kotlinResult = KotlinExtractor.ExtractKotlinFile(localGraph, file, source);
                    if (!cancellationToken.IsCancellationRequested)
                        concurrentKotlinResults.Add((kotlinResult, localGraph, item.Project, item.ProjSymbol));
                }
                else
                {
                    var vbResult = ModuleExtractor.ExtractModule(localGraph, file, source);
                    if (!cancellationToken.IsCancellationRequested)
                        concurrentVbResults.Add((vbResult, localGraph, item.Project, item.ProjSymbol));
                }
            }

            if (cancellationToken.IsCancellationRequested)
                return;

            var currentDone = Interlocked.Increment(ref processedFiles);

            // Progress throttling to not saturate the UI dispatcher (every ~80ms or on the last file)
            var nowTicks = Environment.TickCount64;
            if (currentDone == totalFiles || (nowTicks - Interlocked.Read(ref lastReportTicks)) > 80)
            {
                Interlocked.Exchange(ref lastReportTicks, nowTicks);

                var elapsedSec = analysisStopwatch.Elapsed.TotalSeconds;
                double remainingSec;

                if (currentDone >= 2 && elapsedSec > 0.15)
                {
                    var measuredRatePerFile = elapsedSec / currentDone;
                    var remainingFiles = totalFiles - currentDone;
                    var remainingParseSec = remainingFiles * measuredRatePerFile;
                    // Post-processing (TypeResolver, CallGraph, VisualGraph) takes approximately 25% of the parsing time of the entire workspace
                    var estimatedPostProcessSec = Math.Max(0.4, (totalFiles * measuredRatePerFile) * 0.25);

                    var dynamicRemaining = remainingParseSec + estimatedPostProcessSec;

                    // For the first few files, it performs soft blending with the initial heuristic estimate to avoid spikes
                    if (currentDone < 6)
                    {
                        var weight = currentDone / 6.0;
                        var initialRemaining = Math.Max(0.5, estimatedTotalSeconds - elapsedSec);
                        remainingSec = (initialRemaining * (1.0 - weight)) + (dynamicRemaining * weight);
                    }
                    else
                    {
                        remainingSec = dynamicRemaining;
                    }
                }
                else
                {
                    remainingSec = Math.Max(0.5, estimatedTotalSeconds - elapsedSec);
                }

                remainingSec = Math.Max(0.1, Math.Round(remainingSec, 1));

                progress?.Report(new ProjectAnalysisProgress
                {
                    CurrentFileIndex = currentDone,
                    TotalFiles = totalFiles,
                    CurrentFileName = fileName,
                    ExtractedNodesCount = concurrentVbResults.Count + concurrentCsResults.Count + concurrentJavaResults.Count + concurrentKotlinResults.Count,
                    EstimatedTotalSeconds = estimatedTotalSeconds,
                    EstimatedRemainingSeconds = remainingSec,
                    PhaseMessage = LocalizationService.Instance.Get("Loading_ParsingFile", fileName)
                });
            }
        });

        cancellationToken.ThrowIfCancellationRequested();

        // 2b. Ultra-high-performance batch merging of local graphs into the Workspace's global CodeGraph
        // To avoid O(N^2) containment index recalculations (which caused 8GB RAM spikes and GC block minutes),
        // we first aggregate all the symbols and relations without touching RegisterRelationship,
        // we add project-module containment relations in bulk to the relations list,
        // and we perform a single atomic synchronization of the indexes at the end of the merge.
        foreach (var (vbResult, localGraph, proj, projSymbol) in concurrentVbResults)
        {
            extractionResults.Add(vbResult);
            semanticGraph.Symbols.AddRange(localGraph.Symbols);
            semanticGraph.Relationships.AddRange(localGraph.Relationships);

            // Containment relationship between the Project symbol and the top-level module
            semanticGraph.Relationships.Add(new Relationship
            {
                SourceId = projSymbol.Id,
                TargetId = vbResult.ModuleSymbol.Id,
                Kind = RelationshipKind.Contains,
                Confidence = ResolutionConfidence.Exact
            });

            // Set the QualifiedName with the project prefix if multi-project workspace
            if (projects.Count > 1)
                vbResult.ModuleSymbol.QualifiedName = $"{proj.Name}.{vbResult.ModuleSymbol.Name}";
        }

        foreach (var (csResult, localGraph, proj, projSymbol) in concurrentCsResults)
        {
            csharpResults.Add(csResult);
            semanticGraph.Symbols.AddRange(localGraph.Symbols);
            semanticGraph.Relationships.AddRange(localGraph.Relationships);

            foreach (var topSym in csResult.TopLevelSymbols)
            {
                semanticGraph.Relationships.Add(new Relationship
                {
                    SourceId = projSymbol.Id,
                    TargetId = topSym.Id,
                    Kind = RelationshipKind.Contains,
                    Confidence = ResolutionConfidence.Exact
                });

                if (projects.Count > 1 && !topSym.QualifiedName.StartsWith(proj.Name + ".", StringComparison.OrdinalIgnoreCase))
                    topSym.QualifiedName = $"{proj.Name}.{topSym.QualifiedName}";
            }
        }

        var javaResults = new List<JavaExtractionResult>(concurrentJavaResults.Count);
        foreach (var (javaResult, localGraph, proj, projSymbol) in concurrentJavaResults)
        {
            javaResults.Add(javaResult);
            semanticGraph.Symbols.AddRange(localGraph.Symbols);
            semanticGraph.Relationships.AddRange(localGraph.Relationships);

            foreach (var topSym in javaResult.TopLevelSymbols)
            {
                semanticGraph.Relationships.Add(new Relationship
                {
                    SourceId = projSymbol.Id,
                    TargetId = topSym.Id,
                    Kind = RelationshipKind.Contains,
                    Confidence = ResolutionConfidence.Exact
                });

                if (projects.Count > 1 && !topSym.QualifiedName.StartsWith(proj.Name + ".", StringComparison.OrdinalIgnoreCase))
                    topSym.QualifiedName = $"{proj.Name}.{topSym.QualifiedName}";
            }
        }

        var kotlinResults = new List<KotlinExtractionResult>(concurrentKotlinResults.Count);
        foreach (var (kotlinResult, localGraph, proj, projSymbol) in concurrentKotlinResults)
        {
            kotlinResults.Add(kotlinResult);
            semanticGraph.Symbols.AddRange(localGraph.Symbols);
            semanticGraph.Relationships.AddRange(localGraph.Relationships);

            foreach (var topSym in kotlinResult.TopLevelSymbols)
            {
                semanticGraph.Relationships.Add(new Relationship
                {
                    SourceId = projSymbol.Id,
                    TargetId = topSym.Id,
                    Kind = RelationshipKind.Contains,
                    Confidence = ResolutionConfidence.Exact
                });

                if (projects.Count > 1 && !topSym.QualifiedName.StartsWith(proj.Name + ".", StringComparison.OrdinalIgnoreCase))
                    topSym.QualifiedName = $"{proj.Name}.{topSym.QualifiedName}";
            }
        }

        // Dereference and empty parsing temporary collections to immediately free RAM
        concurrentVbResults.Clear();
        concurrentCsResults.Clear();
        concurrentJavaResults.Clear();
        concurrentKotlinResults.Clear();

        // Rebuilds the CodeGraph indices only once in a global and atomic way
        semanticGraph.BuildIndices();

        // Invokes explicit Generation 2 garbage collection to immediately free
        // tens of thousands of ephemeral objects created during the parallel parsing phase
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true);
        GC.WaitForPendingFinalizers();

        // 3. Global type resolution (TypeResolver) across the entire Workspace
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new ProjectAnalysisProgress
        {
            CurrentFileIndex = totalFiles,
            TotalFiles = totalFiles,
            CurrentFileName = string.Empty,
            ExtractedNodesCount = semanticGraph.Symbols.Count,
            EstimatedTotalSeconds = estimatedTotalSeconds,
            EstimatedRemainingSeconds = Math.Max(0.1, Math.Round(estimatedTotalSeconds * 0.15, 1)),
            PhaseMessage = LocalizationService.Instance["Loading_ResolvingTypes"]
        });

        var typeResolver = new TypeResolver(semanticGraph, cancellationToken);
        typeResolver.ResolveAllTypes(cancellationToken);

        // 4. Symbol resolution and construction of the cross-project Call Graph
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new ProjectAnalysisProgress
        {
            CurrentFileIndex = totalFiles,
            TotalFiles = totalFiles,
            CurrentFileName = string.Empty,
            ExtractedNodesCount = semanticGraph.Symbols.Count,
            EstimatedTotalSeconds = estimatedTotalSeconds,
            EstimatedRemainingSeconds = Math.Max(0.1, Math.Round(estimatedTotalSeconds * 0.08, 1)),
            PhaseMessage = LocalizationService.Instance["Loading_BuildingCallGraph"]
        });

        var symbolResolver = new SymbolResolver(semanticGraph, typeResolver);
        var callGraphBuilder = new CallGraphBuilder(semanticGraph, symbolResolver);
        callGraphBuilder.BuildGraph(extractionResults, csharpResults, javaResults, kotlinResults, cancellationToken);

        // Dereference and empty extraction collections as soon as the Call Graph is generated
        // to free up memory before proceeding with the visual graph and its layout
        extractionResults.Clear();
        csharpResults.Clear();
        javaResults.Clear();
        kotlinResults.Clear();

        // 5. Adaptation to the visual model for Avalonia
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new ProjectAnalysisProgress
        {
            CurrentFileIndex = totalFiles,
            TotalFiles = totalFiles,
            CurrentFileName = string.Empty,
            ExtractedNodesCount = semanticGraph.Symbols.Count,
            EstimatedTotalSeconds = estimatedTotalSeconds,
            EstimatedRemainingSeconds = 0.1,
            PhaseMessage = LocalizationService.Instance["Loading_GeneratingVisualGraph"]
        });

        // Garbage collection before visual rendering/layout
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true);
        GC.WaitForPendingFinalizers();

        var workspaceTitle = projects.Count == 1 ? projects[0].Name : $"Workspace ({projects.Count} Projects)";
        var preferredLayout = SettingsService.Instance.Settings.PreferredLayout;
        return GraphVisualAdapter.FromSemanticGraph(semanticGraph, workspaceTitle, preferredLayout, cancellationToken);
    }

    /// <summary>
    /// Loads and analyzes the integrated suite of VB6 fixtures.
    /// </summary>
    /// <param name="progress">Optional object for reporting real-time progress.</param>
    /// <param name="cancellationToken">Optional delete token to stop ongoing analysis.</param>
    public static VisualCodeGraph LoadDefaultFixtures(IProgress<ProjectAnalysisProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "Fixtures"),
            Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\..\tests\Fixtures")),
            Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\src\Macondo.App\bin\Debug\net8.0\Fixtures"))

        };

        foreach (var dir in candidates)
        {
            if (Directory.Exists(dir))
            {
                var projs = ProjectDescriptor.DiscoverFromPaths(new[] { dir }, cancellationToken);
                if (projs.Count > 0 && projs.Any(p => p.SourceFiles.Count > 0))
                    return AnalyzeProjects(projs, progress, cancellationToken);
            }
        }

        // Minimal fallback if no fixture folder is present on disk
        var emptySemanticGraph = new Semantic.Model.CodeGraph();
        return GraphVisualAdapter.FromSemanticGraph(emptySemanticGraph, "Empty Project");
    }
}
