using Antlr4.Runtime;
using VB6Analyzer.Parser.Diagnostics;
using VB6Analyzer.Parser.Grammar;
using VB6Analyzer.Semantic.Model;

namespace VB6Analyzer.Semantic.Extraction;

/// <summary>
/// Rappresenta l'esito dell'estrazione semantica di un singolo modulo o file VB6.
/// Contiene il simbolo radice creato, eventuali errori sintattici rilevati da ANTLR,
/// la lista di Sub che sembrano event handler e l'albero di parsing per passaggi successivi.
/// </summary>
public sealed class ModuleExtractionResult
{
    /// <summary>Simbolo del modulo generato (di tipo Class, Form, Module o UserControl).</summary>
    public required Symbol ModuleSymbol { get; init; }

    /// <summary>Lista degli errori di sintassi rilevati da lexer e parser ANTLR.</summary>
    public required IReadOnlyList<string> ParseErrors { get; init; }

    /// <summary>Nomi delle procedure Sub che corrispondono alla convenzione di event handler.</summary>
    public required IReadOnlyList<string> LikelyEventHandlerNames { get; init; }

    /// <summary>
    /// Parse tree generato da ANTLR (<see cref="VisualBasic6Parser.StartRuleContext"/>).
    /// Mantenuto in memoria per consentire a successivi analizzatori (es. <see cref="VB6Analyzer.Semantic.Resolution.RoutineInvocationAnalyzer"/>)
    /// di attraversare i corpi dei metodi senza dover riparsare il file sorgente da zero.
    /// </summary>
    public VisualBasic6Parser.StartRuleContext? ParseTree { get; init; }

    /// <summary>Indica se il modulo è stato parsato ed estratto senza errori di sintassi.</summary>
    public bool Success => ParseErrors.Count == 0;
}

/// <summary>
/// Facade e punto di ingresso per il parsing e l'estrazione semantica di un singolo file sorgente VB6.
/// 
/// FLUSSO DI ESECUZIONE:
/// 1. Determina il tipo di modulo dall'estensione (.cls -> Class, .frm -> Form, .bas -> Module, .ctl -> UserControl).
/// 2. Crea il <see cref="Symbol"/> radice del modulo e lo registra nel <see cref="CodeGraph"/> condiviso.
/// 3. Esegue lexing e parsing con ANTLR4 (<see cref="VisualBasic6Lexer"/>, <see cref="VisualBasic6Parser"/>)
///    catturando eventuali errori tramite <see cref="CollectingErrorListener"/>.
/// 4. Esegue <see cref="SymbolExtractor"/> sull'albero sintattico per estrarre tutti i simboli interni
///    e costruire le relazioni CONTAINS.
/// 5. Restituisce il risultato strutturato in <see cref="ModuleExtractionResult"/>.
/// </summary>
public static class ModuleExtractor
{
    /// <summary>
    /// Esegue il parsing e l'estrazione dei simboli per un file sorgente VB6, inserendoli nel grafo in-place.
    /// </summary>
    /// <param name="graph">Il grafo in memoria in cui aggregare i simboli.</param>
    /// <param name="filePath">Percorso o nome del file (usato per estensione e nome modulo).</param>
    /// <param name="sourceText">Il codice sorgente VB6 completo del modulo.</param>
    /// <returns>Il risultato dell'estrazione con i simboli e l'albero di parsing.</returns>
    public static ModuleExtractionResult ExtractModule(CodeGraph graph, string filePath, string sourceText)
    {
        // 1. Identifica il tipo di modulo dall'estensione del file (.cls, .frm, .bas, .ctl)
        var moduleKind = ModuleFileKind.FromExtension(filePath);
        var moduleName = Path.GetFileNameWithoutExtension(filePath);

        // 2. Crea il simbolo contenitore radice per il modulo
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

        // 3. Configura Lexer ANTLR
        var inputStream = new AntlrInputStream(sourceText);
        var lexer = new VisualBasic6Lexer(inputStream);
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(new CollectingErrorListener(errors));

        // 4. Configura TokenStream e Parser ANTLR
        var tokenStream = new CommonTokenStream(lexer);
        var parser = new VisualBasic6Parser(tokenStream);
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new CollectingErrorListener(errors));

        // 5. Avvia il parsing dalla regola principale 'startRule'
        var tree = parser.startRule();

        // Se ci sono errori sintattici bloccanti, interrompe l'estrazione e segnala gli errori
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

        // Aggiorna la riga finale del modulo in base all'ultimo token dell'albero
        moduleSymbol.LineEnd = tree.Stop?.Line ?? 1;

        // 6. Esegue il SymbolExtractor sul parse tree per estrarre routine, proprietà, variabili, costanti, ecc.
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
