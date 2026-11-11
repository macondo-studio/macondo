using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Represents an object-type instantiation (e.g. 'new Foo(...)', 'new()') found in C# code.
/// </summary>
public sealed class CSharpInstantiation
{
    /// <summary>Source symbol in which the instantiation occurs (method, property, or field with inline initializer).</summary>
    public required Symbol SourceSymbol { get; init; }

    /// <summary>Simple or qualified name of the instantiated type (e.g. "CustomerService", "List", "var").</summary>
    public required string TargetTypeName { get; init; }

    /// <summary>Line number in the C# source file where the instantiation occurs (1-indexed).</summary>
    public int Line { get; init; }
}
