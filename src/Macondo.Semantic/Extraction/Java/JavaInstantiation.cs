using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Represents an object-type instantiation (e.g. 'new Customer(...)', 'new ArrayList<>()') found in Java code.
/// </summary>
public sealed class JavaInstantiation
{
    /// <summary>Source symbol where the instantiation occurs (method, constructor, or container type for inline field initializers).</summary>
    public required Symbol SourceSymbol { get; init; }

    /// <summary>Simple or qualified name of the instantiated type (e.g. "CustomerService", "ArrayList", "HashMap").</summary>
    public required string TargetTypeName { get; init; }

    /// <summary>Line number in the Java source file where the instantiation occurs (1-indexed).</summary>
    public int Line { get; init; }
}
