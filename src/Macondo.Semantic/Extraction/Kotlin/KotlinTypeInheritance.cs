using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Represents a supertype clause (class inheritance or interface implementation) in Kotlin (e.g. ': BaseClass(), Interface1, Interface2').
/// </summary>
public sealed class KotlinTypeInheritance
{
    /// <summary>Symbol of the type derived from or implementing the interface (Class, Data Class, Interface or Object).</summary>
    public required Symbol DerivedTypeSymbol { get; init; }

    /// <summary>Name of the base type or interface (e.g. "BaseEntity", "Comparable", "Closeable").</summary>
    public required string BaseOrInterfaceTypeName { get; init; }

    /// <summary>Line number in the Kotlin source file where the (1-indexed) clause is declared.</summary>
    public int Line { get; init; }

    /// <summary>Indicates whether the relationship is definitely an interface or a derived base class.</summary>
    public bool IsInterface { get; init; }
}
