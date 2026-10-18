using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Rappresenta una clausola di ereditarietà di classe base ('Inherits') o implementazione di interfaccia ('Implements') per un tipo VB.NET.
/// </summary>
public sealed class VisualBasicNetTypeInheritance
{
    /// <summary>Simbolo del tipo derivato o che implementa l'interfaccia (Classe o Struttura).</summary>
    public required Symbol DerivedTypeSymbol { get; init; }

    /// <summary>Nome del tipo base o dell'interfaccia ereditata/implementata (es. "BaseEntity", "IDisposable").</summary>
    public required string BaseOrInterfaceTypeName { get; init; }

    /// <summary>Indica se si tratta di una clausola 'Inherits' (true) o 'Implements' (false).</summary>
    public bool IsInheritance { get; init; } = true;

    /// <summary>Numero di riga nel file sorgente VB.NET in cui è presente la clausola (1-indexed).</summary>
    public int Line { get; init; }
}
