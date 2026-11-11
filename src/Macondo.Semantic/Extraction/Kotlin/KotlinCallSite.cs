using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Represents a method or function invocation site (call-site) identified in the Kotlin source code.
/// </summary>
public sealed class KotlinCallSite
{
    /// <summary>Symbol of the calling Kotlin function, method, or initializer.</summary>
    public required Symbol CallerSymbol { get; init; }

    /// <summary>Name of the function or method invoked (e.g. "processOrder", "save", "println", "super", "this").</summary>
    public required string InvokedMethodName { get; init; }

    /// <summary>Receiver expression if present (e.g. "orderService", "repo", "this", "super").</summary>
    public string? ReceiverExpressionText { get; init; }

    /// <summary>Line number in the Kotlin source file where the invocation is located (1-indexed).</summary>
    public int Line { get; init; }
}
