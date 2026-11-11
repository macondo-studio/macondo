using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Represents a call-site found within the body of a C# method or property.
/// </summary>
public sealed class CSharpCallSite
{
    /// <summary>Symbol of the calling C# routine or member (e.g. a getter/setter method or property).</summary>
    public required Symbol CallerSymbol { get; init; }

    /// <summary>Identifying name of the invoked method or member (e.g. "ProcessOrder", "Save").</summary>
    public required string InvokedMethodName { get; init; }

    /// <summary>Text of the receiver expression if present (e.g. "customerService", "orderRepo", "this").</summary>
    public string? ReceiverExpressionText { get; init; }

    /// <summary>Line number in the C# source file where the invocation is located (1-indexed).</summary>
    public int Line { get; init; }
}
