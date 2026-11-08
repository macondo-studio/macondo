using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Estrattore semantico di file sorgente C# basato sul compilatore Microsoft Roslyn (<see cref="CSharpSyntaxTree"/>).
/// 
/// RESPONSABILITÀ:
/// 1. Effettua il parsing del codice sorgente C# costruendo un albero sintattico (Roslyn SyntaxTree).
/// 2. Attraversa l'albero tramite <see cref="CSharpSymbolWalker"/> estraendo tutti i simboli strutturati
///    (Classi, Interfacce, Record, Struct, Enum, Metodi, Proprietà, Campi, Eventi, Costanti).
/// 3. Inserisce i simboli nel <see cref="CodeGraph"/> condiviso collegandoli con relazioni di contenimento (<see cref="RelationshipKind.Contains"/>).
/// 4. Raccoglie i call-site di invocazione, le istanziazioni 'new' e le clausole di ereditarietà per consentire
///    al <see cref="Macondo.Semantic.Graph.CallGraphBuilder"/> di generare i collegamenti semantici cross-language.
/// </summary>
public static class CSharpExtractor
{
    /// <summary>
    /// Esegue il parsing e l'estrazione semantica di un file C# inserendo direttamente i simboli nel <see cref="CodeGraph"/>.
    /// </summary>
    /// <param name="graph">Il grafo semantico condiviso dell'analisi in memoria.</param>
    /// <param name="filePath">Percorso o nome del file C# analizzato.</param>
    /// <param name="sourceText">Il codice sorgente C# completo del file.</param>
    /// <returns>Il risultato strutturato contenente simboli, call site, istanziazioni ed ereditarietà estratte.</returns>
    public static CSharpExtractionResult ExtractCSharpFile(CodeGraph graph, string filePath, string sourceText)
    {
        // 1. Parsing del testo sorgente C# tramite Roslyn con supporto alla versione più recente del linguaggio
        var syntaxTree = CSharpSyntaxTree.ParseText(
            sourceText,
            path: filePath,
            options: new CSharpParseOptions(LanguageVersion.Latest));

        var root = syntaxTree.GetRoot();

        // 2. Raccolta di eventuali errori di diagnostica sintattica
        var errors = new List<string>();
        foreach (var diag in syntaxTree.GetDiagnostics())
        {
            if (diag.Severity == DiagnosticSeverity.Error)
                errors.Add(diag.ToString());
        }

        // 3. Esecuzione del walker specializzato per l'estrazione gerarchica dei simboli e delle relazioni
        var walker = new CSharpSymbolWalker(graph, filePath);
        walker.Visit(root);

        // 4. Confezionamento del risultato finale di estrazione
        return new CSharpExtractionResult
        {
            FilePath = filePath,
            TopLevelSymbols = walker.TopLevelSymbols,
            CallSites = walker.CallSites,
            Instantiations = walker.Instantiations,
            Inheritances = walker.Inheritances,
            ParseErrors = errors
        };
    }
}
