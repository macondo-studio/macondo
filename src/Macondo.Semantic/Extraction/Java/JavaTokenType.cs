namespace Macondo.Semantic.Extraction;

/// <summary>
/// Type of lexical token identified in the Java source code.
/// </summary>
internal enum JavaTokenType
{
    /// <summary>Type, variable, method or package identifier (e.g. 'OrderService', 'count').</summary>
    Identifier,

    /// <summary>Reserved keyword of the Java language (e.g. 'class', 'public', 'void').</summary>
    Keyword,

    /// <summary>Costante numerica intera o floating point (es. '42', '3.14f', '0x1F').</summary>
    Number,

    /// <summary>Costante letterale stringa o text block (es. "hello", """multiline""").</summary>
    StringLiteral,

    /// <summary>Character literal (e.g. 'a', '\n').</summary>
    CharLiteral,

    /// <summary>Symbol or punctuation operator (e.g. '{', '}', ';', '::', '->').</summary>
    Symbol,

    /// <summary>End of token stream (End of File).</summary>
    EndOfFile
}
