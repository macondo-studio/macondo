namespace Macondo.Semantic.Analytics.Treemap;

/// <summary>
/// Squarified Treemap layout algorithm (Bruls, Huizing, van Wijk).
/// Subdivides a rectangle into sub-tides with an aspect ratio close to 1.0 (squares), ensuring maximum readability.
/// Completely agnostic to any UI language or platform.
/// </summary>
public static class TreemapLayoutEngine
{
    /// <summary>
    /// Performs geometric calculation of the Squarified Treemap layout for the root element and all of its child hierarchy.
    /// </summary>
    /// <param name="root">The root element of the Treemap.</param>
    /// <param name="targetBounds">The rectangular borders available on the canvas.</param>
    /// <param name="padding">Separation margin in pixels between nodes and their containers.</param>
    public static void Layout(TreemapItem root, TreemapRect targetBounds, double padding = 3.0)
    {
        root.Bounds = targetBounds;
        if (root.Children.Count == 0) return;

        LayoutChildren(root, targetBounds, padding);
    }

    /// <summary>
    /// Divides the area of the containing node among its direct children.
    /// </summary>
    /// <param name="parent">The parent node whose children are to be arranged.</param>
    /// <param name="bounds">The rectangular boundaries assigned to the parent.</param>
    /// <param name="padding">Margine interno.</param>
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

                LayoutChildren(child, child.Bounds, Math.Max(1.5, padding - 0.5));
        }
    }

    /// <summary>
    /// Recursive tessellation function Squarified (Bruls, Huizing, van Wijk).
    /// </summary>
    /// <param name="items">List ordered by decreasing weight of the elements to be positioned.</param>
    /// <param name="areas">Normalized proportional surfaces corresponding to each element.</param>
    /// <param name="startIndex">Starting index for the current row.</param>
    /// <param name="rect">Remaining rectangle to subdivide.</param>
    /// <remarks>
    /// The algorithm accumulates elements in the current row until the worst aspect ratio (<see cref="CalculateWorstRatio"/>)
    /// improves (approaches 1.0). When adding an additional element would worsen the aspect ratio,
    /// the row is finalized with <see cref="LayoutRow"/> and the algorithm proceeds recursively on the residual rectangle.
    /// </remarks>
    private static void Squarify(
        List<TreemapItem> items,
        List<double> areas,
        int startIndex,
        TreemapRect rect)
    {
        if (startIndex >= items.Count || rect.Width <= 0 || rect.Height <= 0) return;

        // Initialize the current row with the first available element
        int rowEnd = startIndex;
        var currentRow = new List<double> { areas[startIndex] };
        double rowSum = areas[startIndex];
        // Determines the short side of the container rectangle to stack along
        double sideLength = Math.Min(rect.Width, rect.Height);

        // Calculate the worst aspect ratio of the initial row
        double bestWorstRatio = CalculateWorstRatio(currentRow, rowSum, sideLength);

        // Adds elements until the average aspect ratio improves or approaches 1.0 (squared)
        for (int i = startIndex + 1; i < items.Count; i++)
        {
            double nextArea = areas[i];
            var testRow = new List<double>(currentRow) { nextArea };
            double testSum = rowSum + nextArea;
            double testRatio = CalculateWorstRatio(testRow, testSum, sideLength);

            // If adding the new element reduces or maintains the elongation of the rectangles, accept it in the row
            if (testRatio <= bestWorstRatio)
            {
                currentRow = testRow;
                rowSum = testSum;
                bestWorstRatio = testRatio;
                rowEnd = i;
            }
            else
            {
                // The aspect ratio begins to degrade: close the current line
                break;
            }
        }

        // Geometrically position the rectangles of the consolidated row and obtain the remaining space
        var remainingRect = LayoutRow(items, currentRow, startIndex, rowEnd, rowSum, rect);
        // Recursion to pack the remaining elements into the remaining space
        Squarify(items, areas, rowEnd + 1, remainingRect);
    }

    /// <summary>
    /// Calculates the worst aspect ratio (base/height ratio or height/base ratio) among all the rectangles in a test row.
    /// </summary>
    /// <param name="row">Surfaces of the rectangles included in the row.</param>
    /// <param name="rowSum">Total sum of the surfaces of the row.</param>
    /// <param name="sideLength">Length of the short side of the container rectangle.</param>
    /// <returns>The maximum aspect ratio value (values close to 1.0 indicate optimal square shapes).</returns>
    private static double CalculateWorstRatio(List<double> row, double rowSum, double sideLength)
    {
        if (row.Count == 0 || rowSum <= 0 || sideLength <= 0) return double.MaxValue;
        double sideSq = sideLength * sideLength;
        double sumSq = rowSum * rowSum;

        double maxArea = row.Max();
        double minArea = row.Min();

        // Standard Bruls-Huizing-van Wijk formula: max( (s^2 * max_area) / sum^2, sum^2 / (s^2 * min_area) )
        return Math.Max((sideSq * maxArea) / sumSq, sumSq / (sideSq * minArea));
    }

    /// <summary>
    /// Geometrically places the rectangles of the consolidated row along the short side and returns the remaining area.
    /// </summary>
    /// <param name="items">List of elements.</param>
    /// <param name="rowAreas">Row element areas.</param>
    /// <param name="startIdx">Indice iniziale.</param>
    /// <param name="endIdx">Indice finale.</param>
    /// <param name="rowSum">Sum of the areas of the row.</param>
    /// <param name="rect">Reference rectangle before inserting the line.</param>
    /// <returns>The remaining sub-rectangle for the following rows.</returns>
    private static TreemapRect LayoutRow(
        List<TreemapItem> items,
        List<double> rowAreas,
        int startIdx,
        int endIdx,
        double rowSum,
        TreemapRect rect)
    {
        bool isHorizontal = rect.Width >= rect.Height;
        // Calculate the thickness of the line relative to the long side of the available rectangle
        double rowBreadth = (isHorizontal ? rect.Height : rect.Width) > 0 
            ? rowSum / (isHorizontal ? rect.Height : rect.Width) 
            : 0;

        double offset = 0;
        // Assign the coordinates of each rectangle within the row
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

        // Returns the remaining rectangular area by cutting away the newly placed line
        if (isHorizontal)

            return new TreemapRect(rect.X + rowBreadth, rect.Y, Math.Max(0, rect.Width - rowBreadth), rect.Height);

        else
        {
            return new TreemapRect(rect.X, rect.Y + rowBreadth, rect.Width, Math.Max(0, rect.Height - rowBreadth));
        }
    }
}
