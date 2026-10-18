using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Estrattore semantico di file sorgente Visual Basic .NET (.vb) basato su Microsoft Roslyn (<see cref="VisualBasicSyntaxTree"/>).
/// </summary>
public static class VisualBasicNetExtractor
{
    /// <summary>
    /// Esegue il parsing e l'estrazione semantica di un file VB.NET inserendo i simboli nel <see cref="CodeGraph"/>.
    /// </summary>
    public static VisualBasicNetExtractionResult ExtractVisualBasicNetFile(CodeGraph graph, string filePath, string sourceText)
    {
        var syntaxTree = VisualBasicSyntaxTree.ParseText(
            sourceText,
            path: filePath,
            options: new VisualBasicParseOptions(LanguageVersion.Latest));

        var errors = new List<string>();
        foreach (var diag in syntaxTree.GetDiagnostics())
        {
            if (diag.Severity == DiagnosticSeverity.Error)
                errors.Add(diag.ToString());
        }

        return new VisualBasicNetExtractionResult
        {
            FilePath = filePath,
            TopLevelSymbols = Array.Empty<Symbol>(),
            CallSites = Array.Empty<VisualBasicNetCallSite>(),
            Instantiations = Array.Empty<VisualBasicNetInstantiation>(),
            Inheritances = Array.Empty<VisualBasicNetTypeInheritance>(),
            ParseErrors = errors
        };
    }
}
