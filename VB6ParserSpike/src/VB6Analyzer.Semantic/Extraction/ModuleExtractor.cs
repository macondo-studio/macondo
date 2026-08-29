using Antlr4.Runtime;
using VB6Analyzer.Parser.Diagnostics;
using VB6Analyzer.Parser.Grammar;
using VB6Analyzer.Semantic.Model;

namespace VB6Analyzer.Semantic.Extraction;

/// <summary>Esito dell'estrazione di un singolo modulo VB6.</summary>
public sealed class ModuleExtractionResult
{
    public required Symbol ModuleSymbol { get; init; }
    public required IReadOnlyList<string> ParseErrors { get; init; }
    public required IReadOnlyList<string> LikelyEventHandlerNames { get; init; }
    public VisualBasic6Parser.StartRuleContext? ParseTree { get; init; }

    public bool Success => ParseErrors.Count == 0;
}

/// <summary>
/// Collega parsing (M1, VisualBasic6Parser) e symbol extraction (M2/M3, SymbolExtractor)
/// in un'unica chiamata: testo sorgente di un modulo VB6 dentro, CodeGraph popolato
/// (in-place, sezione 18: struttura condivisa, non ricreata per ogni modulo) fuori.
/// </summary>
public static class ModuleExtractor
{
    public static ModuleExtractionResult ExtractModule(CodeGraph graph, string filePath, string sourceText)
    {
        var moduleKind = ModuleFileKind.FromExtension(filePath);
        var moduleName = Path.GetFileNameWithoutExtension(filePath);

        var moduleSymbol = new Symbol
        {
            Id = Guid.NewGuid(),
            Kind = moduleKind,
            Name = moduleName,
            QualifiedName = moduleName,
            FilePath = filePath,
            LineStart = 1,
            LineEnd = 1
        };
        graph.Symbols.Add(moduleSymbol);

        var errors = new List<string>();

        var inputStream = new AntlrInputStream(sourceText);
        var lexer = new VisualBasic6Lexer(inputStream);
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(new CollectingErrorListener(errors));

        var tokenStream = new CommonTokenStream(lexer);
        var parser = new VisualBasic6Parser(tokenStream);
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new CollectingErrorListener(errors));

        var tree = parser.startRule();

        if (errors.Count > 0)
        {
            return new ModuleExtractionResult
            {
                ModuleSymbol = moduleSymbol,
                ParseErrors = errors,
                LikelyEventHandlerNames = Array.Empty<string>(),
                ParseTree = null
            };
        }

        moduleSymbol.LineEnd = tree.Stop?.Line ?? 1;

        var extractor = new SymbolExtractor(graph, moduleSymbol, filePath);
        extractor.Visit(tree);

        return new ModuleExtractionResult
        {
            ModuleSymbol = moduleSymbol,
            ParseErrors = errors,
            LikelyEventHandlerNames = extractor.LikelyEventHandlerNames,
            ParseTree = tree
        };
    }
}
