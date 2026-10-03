using Macondo.App.Layout;
using Macondo.App.Models;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;
using Macondo.Semantic.Resolution;

namespace Macondo.App.Services;

/// <summary>
/// Servizio di analisi e pipeline per caricare e analizzare progetti VB6 reali in memoria (M5).
/// </summary>
public static class ProjectAnalysisService
{
    /// <summary>
    /// Analizza molteplici cartelle, file .vbp o file .vbg all'interno di un unico workspace semantico.
    /// </summary>
    public static VisualCodeGraph AnalyzePaths(
        IReadOnlyList<string> paths,
        IProgress<ProjectAnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
#if !DEBUG
        cancellationToken.ThrowIfCancellationRequested();
#endif
        var projects = ProjectDescriptor.DiscoverFromPaths(paths);
        if (projects.Count == 0)
        {
            var emptySemanticGraph = new Semantic.Model.CodeGraph();
            return GraphVisualAdapter.FromSemanticGraph(emptySemanticGraph, "Empty Workspace");
        }

        return AnalyzeProjects(projects, progress, cancellationToken);
    }

    /// <summary>
    /// Calcola la durata stimata in secondi per la scansione, il parsing AST, la risoluzione semantica
    /// e la generazione del CodeGraph in base al numero di file sorgente e progetti del workspace.
    /// </summary>
    public static double GetEstimatedProjectAnalysisSeconds(int totalFiles, int projectCount = 1)
    {
        if (totalFiles <= 0) return 0.5;

        var cpuCores = Math.Max(1, Environment.ProcessorCount);
        // Fattore di parallelismo reale (scaling sub-lineare per saturazione I/O e thread contention con Antlr)
        var parallelFactor = Math.Max(1.0, Math.Min(cpuCores * 0.7, 8.0));

        // 1. Parsing AST parallelo (~220ms di CPU time per file sorgente VB6/C# reale)
        var parseTime = (totalFiles * 0.22) / parallelFactor;

        // 2. Risoluzione semantica globale dei tipi e costruzione del Call Graph (~25ms per file)
        var resolutionTime = 0.5 + (totalFiles * 0.025);

        // 3. Overhead fisso di allocazione memoria e adattatore visuale per Avalonia
        var totalEstimated = 0.5 + parseTime + resolutionTime;

        return Math.Max(0.5, Math.Round(totalEstimated, 1));
    }

    /// <summary>
    /// Esegue l'analisi completa di una lista di progetti VB6 creando un unico CodeGraph unificato
    /// con risoluzione cross-project di tipi, chiamate e istanziazioni.
    /// </summary>
    public static VisualCodeGraph AnalyzeProjects(
        IReadOnlyList<ProjectDescriptor> projects,
        IProgress<ProjectAnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
#if !DEBUG
        cancellationToken.ThrowIfCancellationRequested();
#endif
        var totalFiles = projects.Sum(p => p.SourceFiles.Count);
        var estimatedTotalSeconds = GetEstimatedProjectAnalysisSeconds(totalFiles, projects.Count);
        var analysisStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var semanticGraph = new Semantic.Model.CodeGraph();
        var extractionResults = new List<ModuleExtractionResult>(totalFiles);
        var csharpResults = new List<CSharpExtractionResult>(totalFiles);
        var projectSymbols = new Dictionary<Guid, Symbol>();

        // 1. Registra i simboli radice di ciascun Progetto
        foreach (var proj in projects)
        {
#if !DEBUG
            cancellationToken.ThrowIfCancellationRequested();
#endif
            var isCs = proj.SourceFiles.Any(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) ||
                       (proj.ProjectFilePath != null && (proj.ProjectFilePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || proj.ProjectFilePath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)));
            var projLang = isCs ? SourceLanguage.CSharp : SourceLanguage.VisualBasic6;

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

        // Segnalazione iniziale
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

        // 2. Estrazione AST e simboli multi-core (Parallel.ForEach)
        // Raccoglie tutti i file con il rispettivo descrittore di progetto
        var fileWorkItems = projects
            .SelectMany(p => p.SourceFiles.Select(f => (Project: p, ProjSymbol: projectSymbols[p.Id], FilePath: f)))
            .ToList();

        // Strutture concorrenti per raccogliere i risultati dai vari worker thread
        var concurrentVbResults = new System.Collections.Concurrent.ConcurrentBag<(ModuleExtractionResult Result, Semantic.Model.CodeGraph LocalGraph, ProjectDescriptor Proj, Symbol ProjSymbol)>();
        var concurrentCsResults = new System.Collections.Concurrent.ConcurrentBag<(CSharpExtractionResult Result, Semantic.Model.CodeGraph LocalGraph, ProjectDescriptor Proj, Symbol ProjSymbol)>();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount),
            CancellationToken = cancellationToken
        };

        var lastReportTicks = Environment.TickCount64;
        int processedFiles = 0;

        Parallel.ForEach(fileWorkItems, parallelOptions, item =>
        {
#if !DEBUG
            cancellationToken.ThrowIfCancellationRequested();
#endif
            var file = item.FilePath;
            var fileName = Path.GetFileName(file);
            var fileExtension = Path.GetExtension(file);

            if (File.Exists(file))
            {
                var source = File.ReadAllText(file);
                var localGraph = new Semantic.Model.CodeGraph();

                if (string.Equals(fileExtension, ".cs", StringComparison.OrdinalIgnoreCase))
                {
                    var csResult = CSharpExtractor.ExtractCSharpFile(localGraph, file, source);
                    concurrentCsResults.Add((csResult, localGraph, item.Project, item.ProjSymbol));
                }
                else
                {
                    var vbResult = ModuleExtractor.ExtractModule(localGraph, file, source);
                    concurrentVbResults.Add((vbResult, localGraph, item.Project, item.ProjSymbol));
                }
            }

            var currentDone = Interlocked.Increment(ref processedFiles);

            // Throttling del progresso per non saturare il dispatcher UI (ogni ~80ms o sull'ultimo file)
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
                    // Il post-processing (TypeResolver, CallGraph, VisualGraph) richiede circa il 25% del tempo di parsing dell'intero workspace
                    var estimatedPostProcessSec = Math.Max(0.4, (totalFiles * measuredRatePerFile) * 0.25);

                    var dynamicRemaining = remainingParseSec + estimatedPostProcessSec;

                    // Per i primi file, esegue un blending morbido con la stima euristica iniziale per evitare picchi
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
                    ExtractedNodesCount = concurrentVbResults.Count + concurrentCsResults.Count,
                    EstimatedTotalSeconds = estimatedTotalSeconds,
                    EstimatedRemainingSeconds = remainingSec,
                    PhaseMessage = LocalizationService.Instance.Get("Loading_ParsingFile", fileName)
                });
            }
        });

        // 2b. Unione deterministica e atomica dei grafi locali nel CodeGraph globale del Workspace
        foreach (var (vbResult, localGraph, proj, projSymbol) in concurrentVbResults)
        {
            extractionResults.Add(vbResult);
            semanticGraph.Symbols.AddRange(localGraph.Symbols);
            semanticGraph.Relationships.AddRange(localGraph.Relationships);

            // Collega il modulo al proprio progetto tramite relazione CONTAINS
            semanticGraph.AddContains(projSymbol, vbResult.ModuleSymbol);

            // Imposta il QualifiedName con il prefisso del progetto se workspace multi-progetto
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
                semanticGraph.AddContains(projSymbol, topSym);
                if (projects.Count > 1 && !topSym.QualifiedName.StartsWith(proj.Name + ".", StringComparison.OrdinalIgnoreCase))

                    topSym.QualifiedName = $"{proj.Name}.{topSym.QualifiedName}";
            }
        }

        // 3. Risoluzione globale dei tipi (TypeResolver) su tutto il Workspace
#if !DEBUG
        cancellationToken.ThrowIfCancellationRequested();
#endif
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

        var typeResolver = new TypeResolver(semanticGraph);
        typeResolver.ResolveAllTypes();

        // 4. Risoluzione simboli e costruzione del Call Graph cross-project
#if !DEBUG
        cancellationToken.ThrowIfCancellationRequested();
#endif
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
        callGraphBuilder.BuildGraph(extractionResults, csharpResults);

        // 5. Adattamento al modello visuale per Avalonia
#if !DEBUG
        cancellationToken.ThrowIfCancellationRequested();
#endif
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

        var workspaceTitle = projects.Count == 1 ? projects[0].Name : $"Workspace ({projects.Count} Projects)";
        return GraphVisualAdapter.FromSemanticGraph(semanticGraph, workspaceTitle);
    }

    /// <summary>
    /// Carica e analizza la suite integrata di fixture VB6.
    /// </summary>
    /// <param name="progress">Oggetto opzionale per la segnalazione dell'avanzamento in tempo reale.</param>
    /// <param name="cancellationToken">Token opzionale di cancellazione per interrompere l'analisi in corso.</param>
    public static VisualCodeGraph LoadDefaultFixtures(IProgress<ProjectAnalysisProgress>? progress = null, CancellationToken cancellationToken = default)
    {
#if !DEBUG
        cancellationToken.ThrowIfCancellationRequested();
#endif
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "Fixtures"),
            Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\..\VB6ParserSpike\src\VB6Analyzer.Semantic.Spike\Fixtures")),
            Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\VB6AnalyzerPOC\src\VB6Analyzer.App\bin\Debug\net8.0\Fixtures")),
            @".\VB6ParserSpike\src\VB6Analyzer.Semantic.Spike\Fixtures"
        };

        foreach (var dir in candidates)
        {
            if (Directory.Exists(dir))
            {
                var projs = ProjectDescriptor.DiscoverFromPaths(new[] { dir });
                if (projs.Count > 0 && projs.Any(p => p.SourceFiles.Count > 0))

                    return AnalyzeProjects(projs, progress, cancellationToken);
            }
        }

        // Fallback minimale se nessuna cartella fixture è presente su disco
        var emptySemanticGraph = new Semantic.Model.CodeGraph();
        return GraphVisualAdapter.FromSemanticGraph(emptySemanticGraph, "Empty Project");
    }
}
