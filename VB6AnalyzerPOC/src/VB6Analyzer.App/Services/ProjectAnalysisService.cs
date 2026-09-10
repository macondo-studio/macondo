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
    /// Analizza tutti i file sorgente VB6 all'interno di una cartella.
    /// </summary>
    /// <param name="folderPath">Percorso della cartella contenente i file VB6.</param>
    /// <param name="progress">Oggetto opzionale per la segnalazione dell'avanzamento in tempo reale.</param>
    public static VisualCodeGraph AnalyzeFolder(string folderPath, IProgress<ProjectAnalysisProgress>? progress = null)
    {
        return AnalyzePaths(new[] { folderPath }, progress);
    }

    /// <summary>
    /// Analizza molteplici cartelle, file .vbp o file .vbg all'interno di un unico workspace semantico.
    /// </summary>
    public static VisualCodeGraph AnalyzePaths(IReadOnlyList<string> paths, IProgress<ProjectAnalysisProgress>? progress = null)
    {
        var projects = ProjectDescriptor.DiscoverFromPaths(paths);
        if (projects.Count == 0)
        {
            var emptySemanticGraph = new Semantic.Model.CodeGraph();
            return GraphVisualAdapter.FromSemanticGraph(emptySemanticGraph, "Empty Workspace");
        }

        return AnalyzeProjects(projects, progress);
    }

    /// <summary>
    /// Esegue l'analisi completa di una lista di progetti VB6 creando un unico CodeGraph unificato
    /// con risoluzione cross-project di tipi, chiamate e istanziazioni.
    /// </summary>
    public static VisualCodeGraph AnalyzeProjects(IReadOnlyList<ProjectDescriptor> projects, IProgress<ProjectAnalysisProgress>? progress = null)
    {
        var totalFiles = projects.Sum(p => p.SourceFiles.Count);
        var semanticGraph = new Semantic.Model.CodeGraph();
        var extractionResults = new List<ModuleExtractionResult>(totalFiles);
        var projectSymbols = new Dictionary<Guid, Symbol>();

        // 1. Registra i simboli radice di ciascun Progetto
        foreach (var proj in projects)
        {
            var projSymbol = new Symbol
            {
                Id = proj.Id,
                Kind = SymbolKind.Project,
                Name = proj.Name,
                QualifiedName = proj.Name,
                FilePath = proj.VbpPath ?? proj.FolderPath,
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
            PhaseMessage = firstFile != null
                ? LocalizationService.Instance.Get("Loading_ParsingFile", Path.GetFileName(firstFile))
                : LocalizationService.Instance["Loading_ResolvingTypes"]
        });

        int processedFiles = 0;

        // 2. Estrazione AST e simboli per ciascun file di ciascun progetto
        foreach (var proj in projects)
        {
            var projSymbol = projectSymbols[proj.Id];

            foreach (var file in proj.SourceFiles)
            {
                var fileName = Path.GetFileName(file);
                if (File.Exists(file))
                {
                    var source = File.ReadAllText(file);
                    var result = ModuleExtractor.ExtractModule(semanticGraph, file, source);
                    extractionResults.Add(result);

                    // Collega il modulo al proprio progetto tramite relazione CONTAINS
                    semanticGraph.AddContains(projSymbol, result.ModuleSymbol);

                    // Imposta il QualifiedName con il prefisso del progetto se non già presente
                    if (projects.Count > 1)
                    {
                        result.ModuleSymbol.QualifiedName = $"{proj.Name}.{result.ModuleSymbol.Name}";
                    }
                }

                processedFiles++;

                progress?.Report(new ProjectAnalysisProgress
                {
                    CurrentFileIndex = processedFiles,
                    TotalFiles = totalFiles,
                    CurrentFileName = fileName,
                    ExtractedNodesCount = semanticGraph.Symbols.Count,
                    PhaseMessage = LocalizationService.Instance.Get("Loading_ParsingFile", fileName)
                });
            }
        }

        // 3. Risoluzione globale dei tipi (TypeResolver) su tutto il Workspace
        progress?.Report(new ProjectAnalysisProgress
        {
            CurrentFileIndex = totalFiles,
            TotalFiles = totalFiles,
            CurrentFileName = string.Empty,
            ExtractedNodesCount = semanticGraph.Symbols.Count,
            PhaseMessage = LocalizationService.Instance["Loading_ResolvingTypes"]
        });

        var typeResolver = new TypeResolver(semanticGraph);
        typeResolver.ResolveAllTypes();

        // 4. Risoluzione simboli e costruzione del Call Graph cross-project
        progress?.Report(new ProjectAnalysisProgress
        {
            CurrentFileIndex = totalFiles,
            TotalFiles = totalFiles,
            CurrentFileName = string.Empty,
            ExtractedNodesCount = semanticGraph.Symbols.Count,
            PhaseMessage = LocalizationService.Instance["Loading_BuildingCallGraph"]
        });

        var symbolResolver = new SymbolResolver(semanticGraph, typeResolver);
        var callGraphBuilder = new CallGraphBuilder(semanticGraph, symbolResolver);
        callGraphBuilder.BuildGraph(extractionResults);

        // 5. Adattamento al modello visuale per Avalonia
        progress?.Report(new ProjectAnalysisProgress
        {
            CurrentFileIndex = totalFiles,
            TotalFiles = totalFiles,
            CurrentFileName = string.Empty,
            ExtractedNodesCount = semanticGraph.Symbols.Count,
            PhaseMessage = LocalizationService.Instance["Loading_GeneratingVisualGraph"]
        });

        var workspaceTitle = projects.Count == 1 ? projects[0].Name : $"Workspace ({projects.Count} Projects)";
        return GraphVisualAdapter.FromSemanticGraph(semanticGraph, workspaceTitle);
    }

    /// <summary>
    /// Carica e analizza la suite integrata di fixture VB6.
    /// </summary>
    /// <param name="progress">Oggetto opzionale per la segnalazione dell'avanzamento in tempo reale.</param>
    public static VisualCodeGraph LoadDefaultFixtures(IProgress<ProjectAnalysisProgress>? progress = null)
    {
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
                {
                    return AnalyzeProjects(projs, progress);
                }
            }
        }

        // Fallback minimale se nessuna cartella fixture è presente su disco
        var emptySemanticGraph = new Semantic.Model.CodeGraph();
        return GraphVisualAdapter.FromSemanticGraph(emptySemanticGraph, "Empty Project");
    }
}
