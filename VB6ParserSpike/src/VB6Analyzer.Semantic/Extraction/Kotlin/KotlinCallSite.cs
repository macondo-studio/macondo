using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Rappresenta un sito di invocazione di metodo o funzione (call-site) individuato nel codice sorgente Kotlin.
/// </summary>
public sealed class KotlinCallSite
{
    /// <summary>Simbolo della funzione, metodo o inizializzatore Kotlin chiamante.</summary>
    public required Symbol CallerSymbol { get; init; }

    /// <summary>Nome della funzione o metodo invocato (es. "processOrder", "save", "println", "super", "this").</summary>
    public required string InvokedMethodName { get; init; }

    /// <summary>Espressione del ricevitore (receiver) se presente (es. "orderService", "repo", "this", "super").</summary>
    public string? ReceiverExpressionText { get; init; }

    /// <summary>Numero di riga nel file sorgente Kotlin in cui si trova l'invocazione (1-indexed).</summary>
    public int Line { get; init; }
}
