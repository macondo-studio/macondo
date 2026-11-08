namespace Macondo.Semantic.Extraction;

/// <summary>
/// Tipologia di token lessicale individuato nel codice sorgente Kotlin (.kt / .kts).
/// </summary>
internal enum KotlinTokenType
{
    /// <summary>Identificatore di tipo, variabile, funzione o package (es. 'OrderService', 'count', `` `when` ``).</summary>
    Identifier,

    /// <summary>Parola chiave riservata del linguaggio Kotlin (es. 'class', 'fun', 'val', 'var', 'object').</summary>
    Keyword,

    /// <summary>Costante numerica intera o a virgola mobile (es. '42', '3.14f', '0x1F', '1_000_000L').</summary>
    Number,

    /// <summary>Costante letterale stringa standard o raw multiriga (es. "hello", """multiline""").</summary>
    StringLiteral,

    /// <summary>Costante letterale carattere (es. 'a', '\n').</summary>
    CharLiteral,

    /// <summary>Simbolo o operatore punteggiatura (es. '{', '}', ';', '?.', '?:', '::', '->', '===').</summary>
    Symbol,

    /// <summary>Fine del flusso di token (End of File).</summary>
    EndOfFile
}
