using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Rappresenta un sito di chiamata a metodo (call-site) rilevato all'interno del corpo di un metodo, costruttore o blocco Java.
/// </summary>
public sealed class JavaCallSite
{
    /// <summary>Simbolo della routine o metodo Java chiamante (es. un metodo 'processOrder', un costruttore o un blocco statico).</summary>
    public required Symbol CallerSymbol { get; init; }

    /// <summary>Nome identificativo del metodo o membro invocato (es. "processOrder", "save", "execute", "super", "this").</summary>
    public required string InvokedMethodName { get; init; }

    /// <summary>Testo dell'espressione ricevitore (receiver) se presente (es. "customerService", "orderRepo", "this", "super").</summary>
    public string? ReceiverExpressionText { get; init; }

    /// <summary>Numero di riga nel file sorgente Java in cui si trova l'invocazione (1-indexed).</summary>
    public int Line { get; init; }
}
