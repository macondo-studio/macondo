namespace Macondo.Semantic.Extraction;

/// <summary>
/// Type of lexical token identified in the Kotlin source code (.kt / .kts).
/// </summary>
internal enum KotlinTokenType
{
    /// <summary>Type, variable, function or package identifier (e.g. 'OrderService', 'count', `` `when` ``).</summary>
    Identifier,

    /// <summary>Reserved keyword of the Kotlin language (e.g. 'class', 'fun', 'val', 'var', 'object').</summary>
    Keyword,

    /// <summary>Integer or floating point numeric constant (e.g. '42', '3.14f', '0x1F', '1_000_000L').</summary>
    Number,

    /// <summary>Costante letterale stringa standard o raw multiriga (es. "hello", """multiline""").</summary>
    StringLiteral,

    /// <summary>Character literal (e.g. 'a', '\n').</summary>
    CharLiteral,

    /// <summary>Symbol or punctuation operator (e.g. '{', '}', ';', '?.', '?:', '::', '->', '===').</summary>
    Symbol,

    /// <summary>End of token stream (End of File).</summary>
    EndOfFile
}
