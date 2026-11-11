using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Represents a call-site found within the body of a Java method, constructor, or block.
/// </summary>
public sealed class JavaCallSite
{
    /// <summary>Symbol of the calling Java procedure or method (e.g. a 'processOrder' method, a constructor or a static block).</summary>
    public required Symbol CallerSymbol { get; init; }

    /// <summary>Identifying name of the invoked method or member (e.g. "processOrder", "save", "execute", "super", "this").</summary>
    public required string InvokedMethodName { get; init; }

    /// <summary>Text of the receiver expression if present (e.g. "customerService", "orderRepo", "this", "super").</summary>
    public string? ReceiverExpressionText { get; init; }

    /// <summary>Line number in the Java source file where the invocation is located (1-indexed).</summary>
    public int Line { get; init; }
}
