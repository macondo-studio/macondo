using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.Treemap;

/// <summary>
/// Two-dimensional rectangle for Treemap layout.
/// </summary>
public readonly record struct TreemapRect(double X, double Y, double Width, double Height)
{
    public double Area => Width * Height;
    public double AspectRatio => Height > 0 && Width > 0 ? Math.Max(Width / Height, Height / Width) : double.MaxValue;
    public bool Contains(double px, double py) => px >= X && px <= X + Width && py >= Y && py <= Y + Height;
}

/// <summary>
/// Hierarchical node of the Treemap (Module, Class, Method or Property).
/// </summary>
public sealed class TreemapItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string QualifiedName { get; init; } = string.Empty;
    public SymbolKind Kind { get; init; }
    public int Depth { get; set; }
    
    /// <summary>Main dimensional value (e.g. Lines of Code / LOC or number of instructions).</summary>
    public double Value { get; set; }

    /// <summary>Secondary metric score for color (e.g. Cyclomatic Complexity or Risk from 1 to 100).</summary>
    public double MetricScore { get; set; }

    /// <summary>Coordinates calculated from the Squarified layout.</summary>
    public TreemapRect Bounds { get; set; }

    /// <summary>Enclosed child elements.</summary>
    public List<TreemapItem> Children { get; init; } = new();

    /// <summary>Reference to parent for navigation and drill-down.</summary>
    public TreemapItem? Parent { get; set; }

    public bool IsLeaf => Children.Count == 0;
}
