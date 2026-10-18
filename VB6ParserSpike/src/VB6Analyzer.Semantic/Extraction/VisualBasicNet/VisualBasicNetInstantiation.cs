using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Rappresenta un'istanziazione di tipo oggetto (es. 'New OrderService(...)') rilevata nel codice VB.NET.
/// </summary>
public sealed class VisualBasicNetInstantiation
{
    /// <summary>Simbolo sorgente in cui avviene l'istanziazione (metodo, proprietà, oppure campo con inizializzatore inline).</summary>
    public required Symbol SourceSymbol { get; init; }

    /// <summary>Nome semplice o qualificato del tipo istanziato (es. "CustomerService", "List(Of Order)").</summary>
    public required string TargetTypeName { get; init; }

    /// <summary>Numero di riga nel file sorgente VB.NET in cui avviene l'istanziazione (1-indexed).</summary>
    public int Line { get; init; }
}
