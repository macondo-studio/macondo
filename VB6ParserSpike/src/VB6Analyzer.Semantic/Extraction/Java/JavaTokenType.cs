namespace Macondo.Semantic.Extraction;

/// <summary>
/// Tipologia di token lessicale individuato nel codice sorgente Java.
/// </summary>
internal enum JavaTokenType
{
    /// <summary>Identificatore di tipo, variabile, metodo o package (es. 'OrderService', 'count').</summary>
    Identifier,

    /// <summary>Parola chiave riservata del linguaggio Java (es. 'class', 'public', 'void').</summary>
    Keyword,

    /// <summary>Costante numerica intera o floating point (es. '42', '3.14f', '0x1F').</summary>
    Number,

    /// <summary>Costante letterale stringa o text block (es. "hello", """multiline""").</summary>
    StringLiteral,

    /// <summary>Costante letterale carattere (es. 'a', '\n').</summary>
    CharLiteral,

    /// <summary>Simbolo o operatore punteggiatura (es. '{', '}', ';', '::', '->').</summary>
    Symbol,

    /// <summary>Fine del flusso di token (End of File).</summary>
    EndOfFile
}
