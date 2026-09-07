using Antlr4.Runtime;
using Macondo.Parser.Diagnostics;
using Macondo.Parser.Grammar;

namespace Macondo.Parser.Spike;

/// <summary>
/// Spike M1: verifica che la grammar ANTLR4 VisualBasic6 (ProLeap/grammars-v4),
/// rigenerata per C#, parsi correttamente un set minimo di costrutti VB6 rilevanti
/// per il charter (sezione 14, 32), e in particolare verifica concretamente il
/// rischio aperto della sezione 44.4: la distinzione tra chiamata a membro
/// qualificata (Customer.Update) e chiamata implicita in un blocco With (.Update).
///
/// NON fa ancora symbol extraction vera (quello è M2): qui ci limitiamo a parsare
/// e stampare l'albero sintattico, più un mini-visitor di esempio che elenca i Sub
/// trovati, solo per dimostrare come si aggancia un Visitor alla grammar generata.
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

        var fixtureFiles = Directory.GetFiles(fixturesDir, "*.cls").OrderBy(f => f).ToList();

        foreach (var file in fixtureFiles)
        {
            RunFixture(Path.GetFileName(file), File.ReadAllText(file));
        }

        Console.WriteLine();
        Console.WriteLine("=== Verifica mirata: With implicito vs chiamata qualificata ===");
        Console.WriteLine("Confronta a occhio le due righe dell'albero corrispondenti a '.Update' (fixture 02)");
        Console.WriteLine("e 'Customer.Update' (fixture 01). Se la grammar distingue correttamente i due casi,");
        Console.WriteLine("il nodo iCS_S_MembersCall di '.Update' NON deve avere un figlio");
        Console.WriteLine("iCS_S_VariableOrProcedureCall/iCS_S_ProcedureOrArrayCall prima del/dei iCS_S_MemberCall,");
        Console.WriteLine("mentre quello di 'Customer.Update' SI'.");
    }

    private static void RunFixture(string name, string source)
    {
        Console.WriteLine("======================================================");
        Console.WriteLine($"Fixture: {name}");
        Console.WriteLine("------------------------------------------------------");

        var errors = new List<string>();

        var inputStream = new AntlrInputStream(source);
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
            Console.WriteLine("PARSE FALLITO. Errori:");
            foreach (var error in errors)
            {
                Console.WriteLine($"  - {error}");
            }
            return;
        }

        Console.WriteLine("Parse OK.");

        // Elenco dei Sub trovati, tramite il Visitor generato — dimostrazione
        // minima del pattern che useremo in M2 per la symbol extraction vera.
        var subNames = new SubNameCollectorVisitor();
        subNames.Visit(tree);
        if (subNames.Names.Count > 0)
        {
            Console.WriteLine($"Sub trovati: {string.Join(", ", subNames.Names)}");
        }

        Console.WriteLine("Parse tree (formato LISP, per ispezione visiva):");
        Console.WriteLine(tree.ToStringTree(parser));
    }

    /// <summary>
    /// Visitor di esempio: raccoglie i nomi dei Sub incontrati nell'albero.
    /// Anticipazione minima di come sarà strutturato il vero symbol extractor (M2).
    /// </summary>
    private sealed class SubNameCollectorVisitor : VisualBasic6ParserBaseVisitor<object?>
    {
        public List<string> Names { get; } = new();

        public override object? VisitSubStmt(VisualBasic6Parser.SubStmtContext context)
        {
            Names.Add(context.ambiguousIdentifier().GetText());
            return base.VisitSubStmt(context);
        }
    }
}
