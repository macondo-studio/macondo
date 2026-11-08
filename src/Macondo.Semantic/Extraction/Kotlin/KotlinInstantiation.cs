using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Rappresenta un'istanziazione di classe o costruttore individuata nel codice Kotlin (es. 'User("Alice")', 'ArrayList<String>()').
/// </summary>
public sealed class KotlinInstantiation
{
    /// <summary>Simbolo chiamante in cui avviene l'istanziazione (funzione, costruttore o tipo contenitore per proprietà inline).</summary>
    public required Symbol SourceSymbol { get; init; }

    /// <summary>Nome semplice o qualificato del tipo istanziato (es. "CustomerService", "ArrayList", "Order").</summary>
    public required string TargetTypeName { get; init; }

    /// <summary>Numero di riga nel file sorgente Kotlin in cui si verifica l'istanziazione (1-indexed).</summary>
    public int Line { get; init; }
}
