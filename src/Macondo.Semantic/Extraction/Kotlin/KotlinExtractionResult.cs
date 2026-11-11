using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Structured result of semantic extraction of a single Kotlin source file (.kt or .kts).
/// </summary>
public sealed class KotlinExtractionResult
{
    /// <summary>Absolute or relative path to the analyzed Kotlin source file.</summary>
    public required string FilePath { get; init; }

    /// <summary>List of root symbols (classes, interfaces, objects, enums or file fronts for top-level elements) declared in the file.</summary>
    public required IReadOnlyList<Symbol> TopLevelSymbols { get; init; }

    /// <summary>List of call call-sites detected in function bodies or properties.</summary>
    public required IReadOnlyList<KotlinCallSite> CallSites { get; init; }

    /// <summary>List of instantiations detected in property methods or initializations.</summary>
    public required IReadOnlyList<KotlinInstantiation> Instantiations { get; init; }

    /// <summary>List of inheritance relations and interface implementations extracted from type declarations.</summary>
    public required IReadOnlyList<KotlinTypeInheritance> Inheritances { get; init; }

    /// <summary>List of diagnostic parsing errors detected on the file.</summary>
    public required IReadOnlyList<string> ParseErrors { get; init; }

    /// <summary>Indicates whether the file was successfully parsed and extracted without blocking errors.</summary>
    public bool Success => ParseErrors.Count == 0;
}
