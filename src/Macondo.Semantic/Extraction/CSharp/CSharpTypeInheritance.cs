using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Rappresenta una clausola di ereditarietà di classe base o implementazione di interfaccia per un tipo C#.
/// </summary>
public sealed class CSharpTypeInheritance
{
    /// <summary>Simbolo del tipo derivato o che implementa l'interfaccia (Classe, Struct o Record).</summary>
    public required Symbol DerivedTypeSymbol { get; init; }

    /// <summary>Nome del tipo base o dell'interfaccia ereditata/implementata (es. "BaseEntity", "IDisposable").</summary>
    public required string BaseOrInterfaceTypeName { get; init; }

    /// <summary>Numero di riga nel file sorgente C# in cui è presente la clausola base (1-indexed).</summary>
    public int Line { get; init; }
}
