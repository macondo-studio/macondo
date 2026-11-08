namespace Macondo.Semantic.Extraction;

/// <summary>
/// Singolo token lessicale con metadati di posizione (riga e colonna 1-indexed).
/// </summary>
internal sealed class JavaToken
{
    public required JavaTokenType Type { get; init; }
    public required string Value { get; init; }
    public required int Line { get; init; }
    public required int Column { get; init; }

    public override string ToString() => $"[{Type}] '{Value}' (L:{Line})";
}
