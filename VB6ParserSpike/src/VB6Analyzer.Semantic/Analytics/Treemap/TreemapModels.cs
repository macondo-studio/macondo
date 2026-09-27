using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.Treemap;

/// <summary>
/// Rettangolo bidimensionale per il layout della Treemap.
/// </summary>
public readonly record struct TreemapRect(double X, double Y, double Width, double Height)
{
    public double Area => Width * Height;
    public double AspectRatio => Height > 0 && Width > 0 ? Math.Max(Width / Height, Height / Width) : double.MaxValue;
    public bool Contains(double px, double py) => px >= X && px <= X + Width && py >= Y && py <= Y + Height;
}

/// <summary>
/// Nodo gerarchico della Treemap (Modulo, Classe, Metodo o Proprietà).
/// </summary>
public sealed class TreemapItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string QualifiedName { get; init; } = string.Empty;
    public SymbolKind Kind { get; init; }
    public int Depth { get; set; }
    
    /// <summary>Valore dimensionale principale (es. Righe di Codice / LOC o numero di istruzioni).</summary>
    public double Value { get; set; }

    /// <summary>Punteggio della metrica secondaria per il colore (es. Complessità Ciclomatica o Rischio da 1 a 100).</summary>
    public double MetricScore { get; set; }

    /// <summary>Coordinate calcolate dal layout Squarified.</summary>
    public TreemapRect Bounds { get; set; }

    /// <summary>Elementi figli racchiusi.</summary>
    public List<TreemapItem> Children { get; init; } = new();

    /// <summary>Riferimento al genitore per la navigazione e drill-down.</summary>
    public TreemapItem? Parent { get; set; }

    public bool IsLeaf => Children.Count == 0;
}
