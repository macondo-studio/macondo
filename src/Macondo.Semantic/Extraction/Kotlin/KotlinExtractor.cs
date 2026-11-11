using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Semantic extractor for Kotlin source files (.kt / .kts) with dedicated lexer and fault-tolerant recursive AST parser.
/// </summary>
public static class KotlinExtractor
{
    /// <summary>
    /// Performs parsing and semantic extraction of a Kotlin file by directly inserting symbols into the <see cref="CodeGraph"/>.
    /// </summary>
    /// <param name="graph">The shared CodeGraph in which to record symbols and relationships CONTAINS.</param>
    /// <param name="filePath">Path or name of the analyzed Kotlin source file.</param>
    /// <param name="sourceText">The complete Kotlin source code text of the file.</param>
    /// <returns>The structured result containing symbols, call-sites, instantiations and inheritance.</returns>
    public static KotlinExtractionResult ExtractKotlinFile(CodeGraph graph, string filePath, string sourceText)
    {
        var parser = new KotlinFileParser(graph, filePath, sourceText);
        return parser.Parse();
    }
}
