using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Represents a base class inheritance clause ('extends') or interface implementation ('implements') for a Java type.
/// </summary>
public sealed class JavaTypeInheritance
{
    /// <summary>Symbol of the type derived from or implementing the interface (Class, Record or Interface).</summary>
    public required Symbol DerivedTypeSymbol { get; init; }

    /// <summary>Name of the base type or inherited/implemented interface (e.g. "BaseEntity", "Comparable", "Serializable").</summary>
    public required string BaseOrInterfaceTypeName { get; init; }

    /// <summary>Line number in the Java source file in which the (1-indexed) clause is present.</summary>
    public int Line { get; init; }

    /// <summary>Indicates whether the relationship is interface implementation ('implements') or inheritance ('extends').</summary>
    public bool IsInterface { get; init; }
}
