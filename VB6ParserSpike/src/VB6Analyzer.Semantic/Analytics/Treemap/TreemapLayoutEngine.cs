namespace Macondo.Semantic.Analytics.Treemap;

/// <summary>
/// Algoritmo di layout Squarified Treemap (Bruls, Huizing, van Wijk).
/// Suddivide un rettangolo in sottomaree con aspect ratio prossimo a 1.0 (quadrati), garantendo la massima leggibilità.
/// Completamente agnostico da qualsiasi linguaggio o piattaforma UI.
/// </summary>
public static class TreemapLayoutEngine
{
    /// <summary>
    /// Esegue il calcolo geometrico del layout Squarified Treemap per l'elemento radice e tutta la sua gerarchia di figli.
    /// </summary>
    /// <param name="root">L'elemento radice della Treemap.</param>
    /// <param name="targetBounds">I confini rettangolari disponibili sul canvas.</param>
    /// <param name="padding">Margine di separazione in pixel tra i nodi e i relativi container.</param>
    public static void Layout(TreemapItem root, TreemapRect targetBounds, double padding = 3.0)
    {
        root.Bounds = targetBounds;
        if (root.Children.Count == 0) return;

        LayoutChildren(root, targetBounds, padding);
    }

    /// <summary>
    /// Suddivide l'area del nodo contenitore tra i suoi elementi figli diretti.
    /// </summary>
    /// <param name="parent">Il nodo genitore i cui figli devono essere disposti.</param>
    /// <param name="bounds">I confini rettangolari assegnati al genitore.</param>
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
            {
                LayoutChildren(child, child.Bounds, Math.Max(1.5, padding - 0.5));
            }
        }
    }

    /// <summary>
    /// Funzione ricorsiva di tessellazione Squarified (Bruls, Huizing, van Wijk).
    /// </summary>
    /// <param name="items">Lista ordinata per peso decrescente degli elementi da posizionare.</param>
    /// <param name="areas">Superfici proporzionali normalizzate corrispondenti a ciascun elemento.</param>
    /// <param name="startIndex">Indice di partenza per la riga corrente.</param>
    /// <param name="rect">Rettangolo rimanente da suddividere.</param>
    /// <remarks>
    /// L'algoritmo accumula elementi nella riga corrente fintanto che l'aspect ratio peggiore (<see cref="CalculateWorstRatio"/>)
    /// migliora (si avvicina a 1.0). Quando l'aggiunta di un ulteriore elemento peggiorerebbe l'aspect ratio,
    /// la riga viene finalizzata con <see cref="LayoutRow"/> e l'algoritmo procede ricorsivamente sul rettangolo residuo.
    /// </remarks>
    private static void Squarify(
        List<TreemapItem> items,
        List<double> areas,
        int startIndex,
        TreemapRect rect)
    {
        if (startIndex >= items.Count || rect.Width <= 0 || rect.Height <= 0) return;

        // Inizializza la riga corrente con il primo elemento disponibile
        int rowEnd = startIndex;
        var currentRow = new List<double> { areas[startIndex] };
        double rowSum = areas[startIndex];
        // Determina il lato corto del rettangolo contenitore lungo cui impilare
        double sideLength = Math.Min(rect.Width, rect.Height);

        // Calcola il peggior aspect ratio della riga iniziale
        double bestWorstRatio = CalculateWorstRatio(currentRow, rowSum, sideLength);

        // Aggiunge elementi finché l'aspect ratio medio migliora o si avvicina a 1.0 (quadrato)
        for (int i = startIndex + 1; i < items.Count; i++)
        {
            double nextArea = areas[i];
            var testRow = new List<double>(currentRow) { nextArea };
            double testSum = rowSum + nextArea;
            double testRatio = CalculateWorstRatio(testRow, testSum, sideLength);

            // Se l'aggiunta del nuovo elemento riduce o mantiene l'allungamento dei rettangoli, accettalo nella riga
            if (testRatio <= bestWorstRatio)
            {
                currentRow = testRow;
                rowSum = testSum;
                bestWorstRatio = testRatio;
                rowEnd = i;
            }
            else
            {
                // L'aspect ratio comincia a degradare: chiudi la riga corrente
                break;
            }
        }

        // Posiziona geometricamente i rettangoli della riga consolidata e ricava lo spazio rimanente
        var remainingRect = LayoutRow(items, currentRow, startIndex, rowEnd, rowSum, rect);
        // Ricorsione per impacchettare i restanti elementi nello spazio residuo
        Squarify(items, areas, rowEnd + 1, remainingRect);
    }

    /// <summary>
    /// Calcola il peggior aspect ratio (rapporto base/altezza o altezza/base) tra tutti i rettangoli di una riga di prova.
    /// </summary>
    /// <param name="row">Superfici dei rettangoli inclusi nella riga.</param>
    /// <param name="rowSum">Somma totale delle superfici della riga.</param>
    /// <param name="sideLength">Lunghezza del lato corto del rettangolo contenitore.</param>
    /// <returns>Il massimo valore di aspect ratio (valori vicini a 1.0 indicano forme quadrate ottimali).</returns>
    private static double CalculateWorstRatio(List<double> row, double rowSum, double sideLength)
    {
        if (row.Count == 0 || rowSum <= 0 || sideLength <= 0) return double.MaxValue;
        double sideSq = sideLength * sideLength;
        double sumSq = rowSum * rowSum;

        double maxArea = row.Max();
        double minArea = row.Min();

        // Formula standard di Bruls-Huizing-van Wijk: max( (s^2 * max_area) / sum^2, sum^2 / (s^2 * min_area) )
        return Math.Max((sideSq * maxArea) / sumSq, sumSq / (sideSq * minArea));
    }

    /// <summary>
    /// Posiziona geometricamente i rettangoli della riga consolidata lungo il lato corto e restituisce l'area rimanente.
    /// </summary>
    /// <param name="items">Lista degli elementi.</param>
    /// <param name="rowAreas">Aree degli elementi della riga.</param>
    /// <param name="startIdx">Indice iniziale.</param>
    /// <param name="endIdx">Indice finale.</param>
    /// <param name="rowSum">Somma delle aree della riga.</param>
    /// <param name="rect">Rettangolo di riferimento prima dell'inserimento della riga.</param>
    /// <returns>Il sotto-rettangolo rimanente per le righe successive.</returns>
    private static TreemapRect LayoutRow(
        List<TreemapItem> items,
        List<double> rowAreas,
        int startIdx,
        int endIdx,
        double rowSum,
        TreemapRect rect)
    {
        bool isHorizontal = rect.Width >= rect.Height;
        // Calcola lo spessore della riga rispetto al lato lungo del rettangolo disponibile
        double rowBreadth = (isHorizontal ? rect.Height : rect.Width) > 0 
            ? rowSum / (isHorizontal ? rect.Height : rect.Width) 
            : 0;

        double offset = 0;
        // Assegna le coordinate di ciascun rettangolo all'interno della riga
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

        // Restituisce l'area rettangolare residua tagliando via la riga appena posizionata
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
