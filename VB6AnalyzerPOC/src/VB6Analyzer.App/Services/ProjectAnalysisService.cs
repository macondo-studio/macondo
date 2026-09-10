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
    /// Analizza tutti i file sorgente VB6 (.cls, .bas, .frm, .ctl) all'interno di una cartella.
    /// </summary>
    /// <param name="folderPath">Percorso della cartella contenente i file VB6.</param>
    /// <param name="progress">Oggetto opzionale per la segnalazione dell'avanzamento in tempo reale.</param>
    public static VisualCodeGraph AnalyzeFolder(string folderPath, IProgress<ProjectAnalysisProgress>? progress = null)
    {
        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException($"Cartella non trovata: {folderPath}");

        var files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cls", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".bas", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".frm", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".ctl", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => Path.GetFileName(f))
            .ToList();

        var totalFiles = files.Count;
        var projectName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        var semanticGraph = new Semantic.Model.CodeGraph();
        var extractionResults = new List<ModuleExtractionResult>(totalFiles);

        // Segnalazione iniziale
        progress?.Report(new ProjectAnalysisProgress
        {
            CurrentFileIndex = 0,
            TotalFiles = totalFiles,
            CurrentFileName = totalFiles > 0 ? Path.GetFileName(files[0]) : string.Empty,
            ExtractedNodesCount = 0,
            PhaseMessage = totalFiles > 0
                ? LocalizationService.Instance.Get("Loading_ParsingFile", Path.GetFileName(files[0]))
                : LocalizationService.Instance["Loading_ResolvingTypes"]
        });

        // 1. Estrazione AST e simboli per ciascun file
        for (int i = 0; i < totalFiles; i++)
        {
            var file = files[i];
            var fileName = Path.GetFileName(file);

            var source = File.ReadAllText(file);
            var result = ModuleExtractor.ExtractModule(semanticGraph, fileName, source);
            extractionResults.Add(result);

            // Segnala il completamento del file corrente con il numero aggiornato di nodi/simboli estratti
            progress?.Report(new ProjectAnalysisProgress
            {
                CurrentFileIndex = i + 1,
                TotalFiles = totalFiles,
                CurrentFileName = fileName,
                ExtractedNodesCount = semanticGraph.Symbols.Count,
                PhaseMessage = LocalizationService.Instance.Get("Loading_ParsingFile", fileName)
            });
        }

        // 2. Risoluzione globale dei tipi (TypeResolver)
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

        // 3. Risoluzione simboli e costruzione del Call Graph (CallGraphBuilder)
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

        // 4. Adattamento al modello visuale per Avalonia
        progress?.Report(new ProjectAnalysisProgress
        {
            CurrentFileIndex = totalFiles,
            TotalFiles = totalFiles,
            CurrentFileName = string.Empty,
            ExtractedNodesCount = semanticGraph.Symbols.Count,
            PhaseMessage = LocalizationService.Instance["Loading_GeneratingVisualGraph"]
        });

        return GraphVisualAdapter.FromSemanticGraph(semanticGraph, projectName);
    }

    /// <summary>
    /// Carica e analizza la suite integrata di fixture VB6.
    /// </summary>
    /// <param name="progress">Oggetto opzionale per la segnalazione dell'avanzamento in tempo reale.</param>
    public static VisualCodeGraph LoadDefaultFixtures(IProgress<ProjectAnalysisProgress>? progress = null)
    {
        var baseDir = AppContext.BaseDirectory;
        var fixturesDir = Path.Combine(baseDir, "Fixtures");

        if (!Directory.Exists(fixturesDir))
        {
            // Cerca nella cartella del progetto di spike se in esecuzione diretta da bin
            var fallback = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\..\VB6ParserSpike\src\Macondo.Semantic.Spike\Fixtures"));
            if (Directory.Exists(fallback))
                fixturesDir = fallback;
        }

        if (Directory.Exists(fixturesDir))
        {
            return AnalyzeFolder(fixturesDir, progress);
        }

        // Fallback minimale se nessuna cartella fixture è presente su disco
        var emptySemanticGraph = new Semantic.Model.CodeGraph();
        return GraphVisualAdapter.FromSemanticGraph(emptySemanticGraph, "Empty Project");
    }
}
