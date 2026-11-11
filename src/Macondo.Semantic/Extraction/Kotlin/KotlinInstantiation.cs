using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Represents a class or constructor instantiation found in Kotlin code (e.g. 'User("Alice")', 'ArrayList<String>()'.
/// </summary>
public sealed class KotlinInstantiation
{
    /// <summary>Calling symbol where the instantiation occurs (function, constructor, or container type for inline properties).</summary>
    public required Symbol SourceSymbol { get; init; }

    /// <summary>Simple or qualified name of the instantiated type (e.g. "CustomerService", "ArrayList", "Order").</summary>
    public required string TargetTypeName { get; init; }

    /// <summary>Line number in the Kotlin source file where instantiation occurs (1-indexed).</summary>
    public int Line { get; init; }
}
