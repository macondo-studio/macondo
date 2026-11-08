namespace Macondo.Semantic.Extraction;

/// <summary>
/// Singolo token lessicale Kotlin con coordinate di riga e colonna (1-indexed).
/// </summary>
internal sealed class KotlinToken
{
    public required KotlinTokenType Type { get; init; }
    public required string Value { get; init; }
    public required int Line { get; init; }
    public required int Column { get; init; }

    public override string ToString() => $"[{Type}] '{Value}' (L:{Line})";
}
