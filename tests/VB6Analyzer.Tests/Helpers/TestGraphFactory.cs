using Macondo.Semantic.Extraction;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;
using Macondo.Semantic.Resolution;

namespace Macondo.Tests.Helpers;

public static class TestGraphFactory
{
    public static string FixturesDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public static (CodeGraph Graph, List<ModuleExtractionResult> Results) CreateAndAnalyzeAllFixtures()
    {
        var graph = new CodeGraph();
        var extractionResults = new List<ModuleExtractionResult>();

        if (!Directory.Exists(FixturesDirectory))
        {
            throw new DirectoryNotFoundException($"Fixtures directory not found at: {FixturesDirectory}");
        }

        var fixtureFiles = Directory.GetFiles(FixturesDirectory, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cls", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".bas", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".frm", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".ctl", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => Path.GetFileName(f))
            .ToList();

        foreach (var file in fixtureFiles)
        {
            var fileName = Path.GetFileName(file);
            var source = File.ReadAllText(file);
            var result = ModuleExtractor.ExtractModule(graph, fileName, source);
            extractionResults.Add(result);
        }

        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);

        var builder = new CallGraphBuilder(graph, symbolResolver);
        builder.BuildGraph(extractionResults);

        return (graph, extractionResults);
    }

    public static (CodeGraph Graph, ModuleExtractionResult Result) CreateAndAnalyzeSingleModule(string fileName, string sourceText)
    {
        var graph = new CodeGraph();
        var result = ModuleExtractor.ExtractModule(graph, fileName, sourceText);

        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);

        var builder = new CallGraphBuilder(graph, symbolResolver);
        builder.BuildGraph(new[] { result });

        return (graph, result);
    }
}
