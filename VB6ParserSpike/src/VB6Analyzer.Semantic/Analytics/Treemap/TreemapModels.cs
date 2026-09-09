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

/// <summary>
/// Algoritmo di layout Squarified Treemap (Bruls, Huizing, van Wijk).
/// Suddivide un rettangolo in sottomaree con aspect ratio prossimo a 1.0 (quadrati), garantendo la massima leggibilità.
/// Completamente agnostico da qualsiasi linguaggio o piattaforma UI.
/// </summary>
public static class TreemapLayoutEngine
{
    public static void Layout(TreemapItem root, TreemapRect targetBounds, double padding = 3.0)
    {
        root.Bounds = targetBounds;
        if (root.Children.Count == 0) return;

        LayoutChildren(root, targetBounds, padding);
    }

    private static void LayoutChildren(TreemapItem parent, TreemapRect bounds, double padding)
    {
        if (parent.Children.Count == 0) return;

        double innerX = bounds.X + padding;
        double innerY = bounds.Y + (parent.Depth > 0 ? 18.0 : padding);
        double innerW = Math.Max(1.0, bounds.Width - (padding * 2));
        double innerH = Math.Max(1.0, bounds.Height - (parent.Depth > 0 ? 18.0 + padding : padding * 2));

        var availableRect = new TreemapRect(innerX, innerY, innerW, innerH);
        double totalValue = parent.Children.Sum(c => Math.Max(1.0, c.Value));

        var sortedChildren = parent.Children.OrderByDescending(c => Math.Max(1.0, c.Value)).ToList();

        double totalArea = availableRect.Area;
        var normalizedWeights = sortedChildren.Select(c => (Math.Max(1.0, c.Value) / totalValue) * totalArea).ToList();

        Squarify(sortedChildren, normalizedWeights, 0, availableRect);

        foreach (var child in parent.Children)
        {
            if (child.Children.Count > 0 && child.Bounds.Width > 20 && child.Bounds.Height > 20)
            {
                LayoutChildren(child, child.Bounds, Math.Max(1.5, padding - 0.5));
            }
        }
    }

    private static void Squarify(
        List<TreemapItem> items,
        List<double> areas,
        int startIndex,
        TreemapRect rect)
    {
        if (startIndex >= items.Count || rect.Width <= 0 || rect.Height <= 0) return;

        int rowEnd = startIndex;
        var currentRow = new List<double> { areas[startIndex] };
        double rowSum = areas[startIndex];
        double sideLength = Math.Min(rect.Width, rect.Height);

        double bestWorstRatio = CalculateWorstRatio(currentRow, rowSum, sideLength);

        for (int i = startIndex + 1; i < items.Count; i++)
        {
            double nextArea = areas[i];
            var testRow = new List<double>(currentRow) { nextArea };
            double testSum = rowSum + nextArea;
            double testRatio = CalculateWorstRatio(testRow, testSum, sideLength);

            if (testRatio <= bestWorstRatio)
            {
                currentRow = testRow;
                rowSum = testSum;
                bestWorstRatio = testRatio;
                rowEnd = i;
            }
            else
            {
                break;
            }
        }

        var remainingRect = LayoutRow(items, currentRow, startIndex, rowEnd, rowSum, rect);
        Squarify(items, areas, rowEnd + 1, remainingRect);
    }

    private static double CalculateWorstRatio(List<double> row, double rowSum, double sideLength)
    {
        if (row.Count == 0 || rowSum <= 0 || sideLength <= 0) return double.MaxValue;
        double sideSq = sideLength * sideLength;
        double sumSq = rowSum * rowSum;

        double maxArea = row.Max();
        double minArea = row.Min();

        return Math.Max((sideSq * maxArea) / sumSq, sumSq / (sideSq * minArea));
    }

    private static TreemapRect LayoutRow(
        List<TreemapItem> items,
        List<double> rowAreas,
        int startIdx,
        int endIdx,
        double rowSum,
        TreemapRect rect)
    {
        bool isHorizontal = rect.Width >= rect.Height;
        double rowBreadth = (isHorizontal ? rect.Height : rect.Width) > 0 
            ? rowSum / (isHorizontal ? rect.Height : rect.Width) 
            : 0;

        double offset = 0;
        for (int i = startIdx; i <= endIdx; i++)
        {
            double area = rowAreas[i - startIdx];
            double itemLength = rowBreadth > 0 ? area / rowBreadth : 0;

            if (isHorizontal)
            {
                items[i].Bounds = new TreemapRect(rect.X, rect.Y + offset, rowBreadth, itemLength);
                offset += itemLength;
            }
            else
            {
                items[i].Bounds = new TreemapRect(rect.X + offset, rect.Y, itemLength, rowBreadth);
                offset += itemLength;
            }
        }

        if (isHorizontal)
        {
            return new TreemapRect(rect.X + rowBreadth, rect.Y, Math.Max(0, rect.Width - rowBreadth), rect.Height);
        }
        else
        {
            return new TreemapRect(rect.X, rect.Y + rowBreadth, rect.Width, Math.Max(0, rect.Height - rowBreadth));
        }
    }
}

/// <summary>
/// Costruttore del modello Treemap a partire da qualsiasi CodeGraph semantico.
/// </summary>
public static class TreemapModelBuilder
{
    public static TreemapItem Build(CodeGraph graph)
    {
        var root = new TreemapItem
        {
            Id = Guid.Empty,
            Name = "Root",
            QualifiedName = "Project",
            Kind = SymbolKind.Project,
            Depth = 0
        };

        var containers = graph.Symbols
            .Where(s => s.Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.Module or SymbolKind.UserControl or SymbolKind.Type)
            .OrderBy(c => c.Name)
            .ToList();

        if (containers.Count == 0)
        {
            containers = graph.Symbols.Where(s => s.Kind != SymbolKind.Project).ToList();
        }

        foreach (var container in containers)
        {
            var containerItem = new TreemapItem
            {
                Id = container.Id,
                Name = container.Name,
                QualifiedName = container.QualifiedName,
                Kind = container.Kind,
                Depth = 1,
                Parent = root
            };

            foreach (var childSym in graph.GetChildren(container.Id))
            {
                double loc = Math.Max(5.0, childSym.Parameters.Count * 3.0 + 8.0);
                int outgoingCalls = graph.Relationships.Count(r => r.SourceId == childSym.Id && r.Kind == RelationshipKind.Calls);
                double complexity = 1.0 + outgoingCalls * 1.5;

                var childItem = new TreemapItem
                {
                    Id = childSym.Id,
                    Name = childSym.Name,
                    QualifiedName = childSym.QualifiedName,
                    Kind = childSym.Kind,
                    Depth = 2,
                    Value = loc,
                    MetricScore = complexity,
                    Parent = containerItem
                };
                containerItem.Children.Add(childItem);
            }

            if (containerItem.Children.Count == 0)
            {
                containerItem.Value = 20.0;
                containerItem.MetricScore = 1.0;
            }
            else
            {
                containerItem.Value = containerItem.Children.Sum(c => c.Value);
                containerItem.MetricScore = containerItem.Children.Average(c => c.MetricScore);
            }

            root.Children.Add(containerItem);
        }

        root.Value = root.Children.Sum(c => c.Value);
        root.MetricScore = root.Children.Count > 0 ? root.Children.Average(c => c.MetricScore) : 1.0;

        return root;
    }
}
