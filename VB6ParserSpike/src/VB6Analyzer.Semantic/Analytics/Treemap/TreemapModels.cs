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

/// <summary>
/// Costruttore del modello Treemap a partire da qualsiasi CodeGraph semantico.
/// </summary>
public static class TreemapModelBuilder
{
    /// <summary>
    /// Costruisce l'albero gerarchico <see cref="TreemapItem"/> a due livelli (Progetto -&gt; Moduli -&gt; Membri) aggregando LOC e complessità.
    /// </summary>
    /// <param name="graph">Il CodeGraph da cui estrarre i simboli.</param>
    /// <returns>Il nodo radice della Treemap pronto per il calcolo del layout.</returns>
    public static TreemapItem Build(CodeGraph graph)
    {
        // 1. Nodo radice globale dell'intero progetto
        var root = new TreemapItem
        {
            Id = Guid.Empty,
            Name = "Root",
            QualifiedName = "Project",
            Kind = SymbolKind.Project,
            Depth = 0
        };

        // 2. Raccoglie tutti i container primari (Classi, Form, Moduli, UserControl, UDT, Interfacce, Strutture, Record)
        var containers = graph.Symbols
            .Where(s => s.Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.Module or SymbolKind.UserControl or SymbolKind.Type or SymbolKind.Interface or SymbolKind.Struct or SymbolKind.Record)
            .OrderBy(c => c.Name)
            .ToList();

        if (containers.Count == 0)
        {
            containers = graph.Symbols.Where(s => s.Kind != SymbolKind.Project).ToList();
        }

        // Indicizzazione O(1) delle chiamate uscenti per ogni simbolo sorgente per evitare scansioni O(R) nel loop
        var outgoingCallsCount = new Dictionary<Guid, int>();
        foreach (var r in graph.Relationships)
        {
            if (r.Kind == RelationshipKind.Calls)
            {
                outgoingCallsCount[r.SourceId] = outgoingCallsCount.GetValueOrDefault(r.SourceId) + 1;
            }
        }

        // 3. Costruisce l'albero dei figli e calcola le metriche di peso (LOC stimate) e complessità
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

            var children = graph.GetChildren(container.Id)
                .Where(c => c.Kind is SymbolKind.Method or SymbolKind.Property or SymbolKind.Field)
                .ToList();

            if (children.Count > 0)
            {
                foreach (var child in children)
                {
                    // Calcolo LOC o peso basato sulle righe di codice o complessità delle chiamate in O(1)
                    int lineCount = Math.Max(1, child.LineEnd - child.LineStart + 1);
                    outgoingCallsCount.TryGetValue(child.Id, out int callees);
                    double complexityScore = 1.0 + (callees * 1.5) + (child.Parameters.Count * 0.8);

                    var childItem = new TreemapItem
                    {
                        Id = child.Id,
                        Name = child.Name,
                        QualifiedName = child.QualifiedName,
                        Kind = child.Kind,
                        Value = lineCount,
                        MetricScore = complexityScore,
                        Depth = 2,
                        Parent = containerItem
                    };

                    containerItem.Children.Add(childItem);
                }

                containerItem.Value = containerItem.Children.Sum(c => c.Value);
                containerItem.MetricScore = containerItem.Children.Average(c => c.MetricScore);
            }
            else
            {
                int lineCount = Math.Max(1, container.LineEnd - container.LineStart + 1);
                containerItem.Value = lineCount;
                containerItem.MetricScore = 1.0;
            }

            root.Children.Add(containerItem);
        }

        // Aggrega i totali sulla radice
        root.Value = root.Children.Sum(c => c.Value);
        root.MetricScore = root.Children.Count > 0 ? root.Children.Average(c => c.MetricScore) : 1.0;

        return root;
    }
}
