using VB6Analyzer.Semantic.Extraction;
using VB6Analyzer.Semantic.Model;

namespace VB6Analyzer.Semantic.Spike;

/// <summary>
/// Spike M2: verifica che SymbolExtractor produca Symbol e relazioni CONTAINS
/// corretti a partire dai fixture VB6 (gli stessi di M1, più uno nuovo dedicato a
/// Property/Const/Field, mai testati finora).
/// </summary>
internal static class Program
{
    private static void Main()
    {
        var fixturesDir = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        if (!Directory.Exists(fixturesDir))
        {
            Console.Error.WriteLine($"Cartella Fixtures non trovata in: {fixturesDir}");
            return;
        }

        // Un solo CodeGraph condiviso per tutti i moduli, come previsto dalla
        // sezione 18: il grafo aggrega più file, non ne crea uno per file.
        var graph = new CodeGraph();

        var fixtureFiles = Directory.GetFiles(fixturesDir, "*.cls").OrderBy(f => f).ToList();

        foreach (var file in fixtureFiles)
        {
            RunFixture(graph, Path.GetFileName(file), File.ReadAllText(file));
        }

        PrintAggregateSummary(graph);
    }

    private static void RunFixture(CodeGraph graph, string fileName, string source)
    {
        Console.WriteLine("======================================================");
        Console.WriteLine($"Fixture: {fileName}");
        Console.WriteLine("------------------------------------------------------");

        var result = ModuleExtractor.ExtractModule(graph, fileName, source);

        if (!result.Success)
        {
            Console.WriteLine("PARSE FALLITO. Errori:");
            foreach (var error in result.ParseErrors)
            {
                Console.WriteLine($"  - {error}");
            }
            return;
        }

        Console.WriteLine("Estrazione OK. Gerarchia dei simboli (CONTAINS):");
        PrintSymbolTree(graph, result.ModuleSymbol, depth: 0);

        if (result.LikelyEventHandlerNames.Count > 0)
        {
            Console.WriteLine($"Possibili event handler (euristica sul nome, NON una relazione nel grafo): {string.Join(", ", result.LikelyEventHandlerNames)}");
        }
    }

    private static void PrintSymbolTree(CodeGraph graph, Symbol symbol, int depth)
    {
        var indent = new string(' ', depth * 2);
        Console.WriteLine($"{indent}- [{symbol.Kind}] {symbol.Name}  (righe {symbol.LineStart}-{symbol.LineEnd})");

        foreach (var child in graph.GetChildren(symbol.Id).OrderBy(c => c.LineStart))
        {
            PrintSymbolTree(graph, child, depth + 1);
        }
    }

    private static void PrintAggregateSummary(CodeGraph graph)
    {
        Console.WriteLine();
        Console.WriteLine("=== Riepilogo aggregato su tutti i fixture ===");

        var byKind = graph.Symbols
            .GroupBy(s => s.Kind)
            .OrderBy(g => g.Key.ToString());

        foreach (var group in byKind)
        {
            Console.WriteLine($"{group.Key,-10} {group.Count()}");
        }

        Console.WriteLine($"Totale simboli: {graph.Symbols.Count}");
        Console.WriteLine($"Totale relazioni CONTAINS: {graph.Relationships.Count}");
    }
}
