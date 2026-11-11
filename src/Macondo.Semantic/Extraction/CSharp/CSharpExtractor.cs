using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// C# source file semantic extractor based on the Microsoft Roslyn compiler (<see cref="CSharpSyntaxTree"/>).
/// 
/// RESPONSABILITÀ:
/// 1. Parse the C# source code by building a syntax tree (Roslyn SyntaxTree).
/// 2. Traverse the tree via <see cref="CSharpSymbolWalker"/> extracting all structured symbols
/// (Classes, Interfaces, Records, Structs, Enums, Methods, Properties, Fields, Events, Constants).
/// 3. Inserts symbols into the shared <see cref="CodeGraph"/> by connecting them with containment relations (<see cref="RelationshipKind.Contains"/>).
/// 4. Collects invocation call-sites, 'new' instantiations and inheritance clauses to allow
/// to <see cref="Macondo.Semantic.Graph.CallGraphBuilder"/> to generate cross-language semantic links.
/// </summary>
public static class CSharpExtractor
{
    /// <summary>
    /// Performs parsing and semantic extraction of a C# file by directly inserting symbols into the <see cref="CodeGraph"/>.
    /// </summary>
    /// <param name="graph">The shared semantic graph of in-memory analysis.</param>
    /// <param name="filePath">Path or name of the analyzed C# file.</param>
    /// <param name="sourceText">The complete C# source code of the file.</param>
    /// <returns>The structured result containing extracted symbols, call sites, instantiations and inheritances.</returns>
    public static CSharpExtractionResult ExtractCSharpFile(CodeGraph graph, string filePath, string sourceText)
    {
        // 1. Parsing C# source text using Roslyn with support for the latest version of the language
        var syntaxTree = CSharpSyntaxTree.ParseText(
            sourceText,
            path: filePath,
            options: new CSharpParseOptions(LanguageVersion.Latest));

        var root = syntaxTree.GetRoot();

        // 2. Collection of any syntactic diagnostic errors
        var errors = new List<string>();
        foreach (var diag in syntaxTree.GetDiagnostics())
        {
            if (diag.Severity == DiagnosticSeverity.Error)
                errors.Add(diag.ToString());
        }

        // 3. Execution of the specialized walker for the hierarchical extraction of symbols and relations
        var walker = new CSharpSymbolWalker(graph, filePath);
        walker.Visit(root);

        // 4. Packaging of the final extraction result
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
