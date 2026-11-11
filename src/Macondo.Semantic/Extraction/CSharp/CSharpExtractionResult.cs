using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Structured result of semantic extraction of a single C# source file (.cs).
/// </summary>
public sealed class CSharpExtractionResult
{
    /// <summary>Absolute or relative path to the analyzed C# source file.</summary>
    public required string FilePath { get; init; }

    /// <summary>List of top-level symbols (classes, interfaces, records, struct, enums) declared in the file.</summary>
    public required IReadOnlyList<Symbol> TopLevelSymbols { get; init; }

    /// <summary>List of invocation call-sites detected in the bodies of C# routines.</summary>
    public required IReadOnlyList<CSharpCallSite> CallSites { get; init; }

    /// <summary>List of 'new' instantiations detected in methods or field initializations.</summary>
    public required IReadOnlyList<CSharpInstantiation> Instantiations { get; init; }

    /// <summary>List of inheritance relations and interface implementations extracted from type declarations.</summary>
    public required IReadOnlyList<CSharpTypeInheritance> Inheritances { get; init; }

    /// <summary>List of compilation or parsing diagnostic errors detected by Roslyn on the file.</summary>
    public required IReadOnlyList<string> ParseErrors { get; init; }

    /// <summary>Indicates whether the file was successfully parsed and extracted without critical syntax errors.</summary>
    public bool Success => ParseErrors.Count == 0;
}
