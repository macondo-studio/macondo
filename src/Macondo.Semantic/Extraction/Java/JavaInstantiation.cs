using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Rappresenta un'istanziazione di tipo oggetto (es. 'new Customer(...)', 'new ArrayList<>()') rilevata nel codice Java.
/// </summary>
public sealed class JavaInstantiation
{
    /// <summary>Simbolo sorgente in cui avviene l'istanziazione (metodo, costruttore, oppure tipo contenitore per inizializzatori inline di campo).</summary>
    public required Symbol SourceSymbol { get; init; }

    /// <summary>Nome semplice o qualificato del tipo istanziato (es. "CustomerService", "ArrayList", "HashMap").</summary>
    public required string TargetTypeName { get; init; }

    /// <summary>Numero di riga nel file sorgente Java in cui avviene l'istanziazione (1-indexed).</summary>
    public int Line { get; init; }
}
