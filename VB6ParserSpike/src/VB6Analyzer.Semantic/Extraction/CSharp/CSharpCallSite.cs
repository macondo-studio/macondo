using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Rappresenta un sito di chiamata a metodo (call-site) rilevato all'interno del corpo di un metodo o proprietà C#.
/// </summary>
public sealed class CSharpCallSite
{
    /// <summary>Simbolo della routine o membro C# chiamante (es. un metodo o property getter/setter).</summary>
    public required Symbol CallerSymbol { get; init; }

    /// <summary>Nome identificativo del metodo o membro invocato (es. "ProcessOrder", "Save").</summary>
    public required string InvokedMethodName { get; init; }

    /// <summary>Testo dell'espressione ricevitore (receiver) se presente (es. "customerService", "orderRepo", "this").</summary>
    public string? ReceiverExpressionText { get; init; }

    /// <summary>Numero di riga nel file sorgente C# in cui si trova l'invocazione (1-indexed).</summary>
    public int Line { get; init; }
}
