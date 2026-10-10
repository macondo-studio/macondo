using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Rappresenta una clausola di ereditarietà di classe base ('extends') o implementazione di interfaccia ('implements') per un tipo Java.
/// </summary>
public sealed class JavaTypeInheritance
{
    /// <summary>Simbolo del tipo derivato o che implementa l'interfaccia (Classe, Record o Interfaccia).</summary>
    public required Symbol DerivedTypeSymbol { get; init; }

    /// <summary>Nome del tipo base o dell'interfaccia ereditata/implementata (es. "BaseEntity", "Comparable", "Serializable").</summary>
    public required string BaseOrInterfaceTypeName { get; init; }

    /// <summary>Numero di riga nel file sorgente Java in cui è presente la clausola (1-indexed).</summary>
    public int Line { get; init; }

    /// <summary>Indica se la relazione è di implementazione interfaccia ('implements') o ereditarietà ('extends').</summary>
    public bool IsInterface { get; init; }
}
