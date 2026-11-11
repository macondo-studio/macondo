namespace Macondo.Semantic.Extraction;

/// <summary>
/// Single lexical token with positional metadata (1-indexed row and column).
/// </summary>
internal sealed class JavaToken
{
    public required JavaTokenType Type { get; init; }
    public required string Value { get; init; }
    public required int Line { get; init; }
    public required int Column { get; init; }

    public override string ToString() => $"[{Type}] '{Value}' (L:{Line})";
}
