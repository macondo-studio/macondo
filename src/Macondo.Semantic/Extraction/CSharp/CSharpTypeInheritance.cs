using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Represents a base class inheritance clause or interface implementation for a C# type.
/// </summary>
public sealed class CSharpTypeInheritance
{
    /// <summary>Symbol of the type derived from or implementing the interface (Class, Struct or Record).</summary>
    public required Symbol DerivedTypeSymbol { get; init; }

    /// <summary>Name of the base type or inherited/implemented interface (e.g. "BaseEntity", "IDisposable").</summary>
    public required string BaseOrInterfaceTypeName { get; init; }

    /// <summary>Line number in the C# source file in which the base clause (1-indexed) is present.</summary>
    public int Line { get; init; }
}
