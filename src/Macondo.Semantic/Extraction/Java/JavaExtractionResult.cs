using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Structured result of semantic extraction of a single Java source file (.java).
/// </summary>
public sealed class JavaExtractionResult
{
    /// <summary>Absolute or relative path to the analyzed Java source file.</summary>
    public required string FilePath { get; init; }

    /// <summary>List of top-level symbols (classes, interfaces, records, enums, annotations) declared in the file.</summary>
    public required IReadOnlyList<Symbol> TopLevelSymbols { get; init; }

    /// <summary>List of invocation call-sites detected in Java method bodies.</summary>
    public required IReadOnlyList<JavaCallSite> CallSites { get; init; }

    /// <summary>List of 'new' instantiations detected in methods or field initializations.</summary>
    public required IReadOnlyList<JavaInstantiation> Instantiations { get; init; }

    /// <summary>List of inheritance relations and interface implementations extracted from type declarations.</summary>
    public required IReadOnlyList<JavaTypeInheritance> Inheritances { get; init; }

    /// <summary>List of diagnostic parsing errors detected on the file.</summary>
    public required IReadOnlyList<string> ParseErrors { get; init; }

    /// <summary>Indicates whether the file was successfully parsed and extracted without blocking errors.</summary>
    public bool Success => ParseErrors.Count == 0;
}
