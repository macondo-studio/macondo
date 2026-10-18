using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Estrattore semantico di file sorgente Visual Basic .NET (.vb) basato sul compilatore Microsoft Roslyn (<see cref="VisualBasicSyntaxTree"/>).
/// 
/// RESPONSABILITÀ ARCHITETTURALI:
/// 1. Effettua il parsing del codice sorgente VB.NET costruendo l'albero di sintassi formale (Visual Basic SyntaxTree).
/// 2. Attraversa l'albero tramite <see cref="VisualBasicSymbolWalker"/> estraendo tutti i simboli strutturati
///    (Classi, Interfacce, Strutture, Moduli, Enum, Routine Sub/Function, Costruttori, Proprietà, Campi, Eventi).
/// 3. Inserisce i simboli nel <see cref="CodeGraph"/> condiviso collegandoli con relazioni di contenimento (<see cref="RelationshipKind.Contains"/>).
/// 4. Raccoglie i call-site di invocazione, le istanziazioni 'New' e le clausole di ereditarietà/interfaccia ('Inherits' e 'Implements')
///    per consentire al <see cref="Macondo.Semantic.Graph.CallGraphBuilder"/> di generare i collegamenti semantici cross-language.
/// </summary>
public static class VisualBasicNetExtractor
{
    /// <summary>
    /// Esegue il parsing e l'estrazione semantica di un file VB.NET inserendo direttamente i simboli nel <see cref="CodeGraph"/>.
    /// </summary>
    /// <param name="graph">Il grafo semantico condiviso in cui registrare i simboli e le relazioni CONTAINS.</param>
    /// <param name="filePath">Percorso o nome del file VB.NET analizzato.</param>
    /// <param name="sourceText">Il testo del codice sorgente VB.NET completo.</param>
    /// <returns>Il risultato strutturato contenente simboli, call-site, istanziazioni ed ereditarietà estratte.</returns>
    public static VisualBasicNetExtractionResult ExtractVisualBasicNetFile(CodeGraph graph, string filePath, string sourceText)
    {
        // 1. Parsing del testo sorgente VB.NET tramite Roslyn con supporto alla versione più recente del linguaggio
        var syntaxTree = VisualBasicSyntaxTree.ParseText(
            sourceText,
            path: filePath,
            options: new VisualBasicParseOptions(LanguageVersion.Latest));

        var root = syntaxTree.GetRoot();

        // 2. Raccolta di eventuali errori o warning diagnostici sintattici
        var errors = new List<string>();
        foreach (var diag in syntaxTree.GetDiagnostics())
        {
            if (diag.Severity == DiagnosticSeverity.Error)
                errors.Add(diag.ToString());
        }

        // 3. Esecuzione del walker specializzato per l'estrazione gerarchica dei simboli e delle relazioni
        var walker = new VisualBasicSymbolWalker(graph, filePath);
        walker.Visit(root);

        // 4. Confezionamento del risultato finale di estrazione
        return new VisualBasicNetExtractionResult
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
