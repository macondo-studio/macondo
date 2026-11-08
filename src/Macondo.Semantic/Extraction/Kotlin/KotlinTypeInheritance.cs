using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Rappresenta una clausola di supertipo (ereditarietà di classe o implementazione di interfaccia) in Kotlin (es. ': BaseClass(), Interface1, Interface2').
/// </summary>
public sealed class KotlinTypeInheritance
{
    /// <summary>Simbolo del tipo derivato o che implementa l'interfaccia (Classe, Data Class, Interfaccia o Object).</summary>
    public required Symbol DerivedTypeSymbol { get; init; }

    /// <summary>Nome del tipo base o interfaccia (es. "BaseEntity", "Comparable", "Closeable").</summary>
    public required string BaseOrInterfaceTypeName { get; init; }

    /// <summary>Numero di riga nel file sorgente Kotlin in cui è dichiarata la clausola (1-indexed).</summary>
    public int Line { get; init; }

    /// <summary>Indica se la relazione è con certezza un'interfaccia o una classe base derivata.</summary>
    public bool IsInterface { get; init; }
}
