using VB6Analyzer.App.Layout;
using VB6Analyzer.App.Models;
using VB6Analyzer.Semantic.Extraction;
using VB6Analyzer.Semantic.Graph;
using VB6Analyzer.Semantic.Model;
using VB6Analyzer.Semantic.Resolution;

namespace VB6Analyzer.App.Services;

/// <summary>
/// Servizio di analisi e pipeline per caricare e analizzare progetti VB6 reali in memoria (M5).
/// </summary>
public static class ProjectAnalysisService
{
    /// <summary>
    /// Analizza tutti i file sorgente VB6 (.cls, .bas, .frm, .ctl) all'interno di una cartella.
    /// </summary>
    /// <param name="folderPath">Percorso della cartella contenente i file VB6.</param>
    public static VisualCodeGraph AnalyzeFolder(string folderPath)
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

        var projectName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        var semanticGraph = new Semantic.Model.CodeGraph();
        var extractionResults = new List<ModuleExtractionResult>();

        // 1. Estrazione AST e simboli per ciascun file
        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var source = File.ReadAllText(file);
            var result = ModuleExtractor.ExtractModule(semanticGraph, fileName, source);
            extractionResults.Add(result);
        }

        // 2. Risoluzione globale dei tipi (TypeResolver)
        var typeResolver = new TypeResolver(semanticGraph);
        typeResolver.ResolveAllTypes();

        // 3. Risoluzione simboli e costruzione del Call Graph (CallGraphBuilder)
        var symbolResolver = new SymbolResolver(semanticGraph, typeResolver);
        var callGraphBuilder = new CallGraphBuilder(semanticGraph, symbolResolver);
        callGraphBuilder.BuildGraph(extractionResults);

        // 4. Adattamento al modello visuale per Avalonia
        return GraphVisualAdapter.FromSemanticGraph(semanticGraph, projectName);
    }

    /// <summary>
    /// Carica e analizza la suite integrata di fixture VB6.
    /// </summary>
    public static VisualCodeGraph LoadDefaultFixtures()
    {
        var baseDir = AppContext.BaseDirectory;
        var fixturesDir = Path.Combine(baseDir, "Fixtures");

        if (!Directory.Exists(fixturesDir))
        {
            // Cerca nella cartella del progetto di spike se in esecuzione diretta da bin
            var fallback = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\..\VB6ParserSpike\src\VB6Analyzer.Semantic.Spike\Fixtures"));
            if (Directory.Exists(fallback))
                fixturesDir = fallback;
        }

        if (Directory.Exists(fixturesDir))
        {
            return AnalyzeFolder(fixturesDir);
        }

        // Fallback minimale se nessuna cartella fixture è presente su disco
        var emptySemanticGraph = new Semantic.Model.CodeGraph();
        return GraphVisualAdapter.FromSemanticGraph(emptySemanticGraph, "Empty Project");
    }
}
