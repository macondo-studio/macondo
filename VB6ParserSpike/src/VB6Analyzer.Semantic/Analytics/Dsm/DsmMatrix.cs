using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.Dsm;

/// <summary>
/// Rappresenta il tipo o natura di una dipendenza tra due contenitori o moduli in una DSM.
/// </summary>
[Flags]
public enum DsmDependencyKind
{
    None = 0,
    Call = 1 << 0,
    Instantiation = 1 << 1,
    TypeUsage = 1 << 2,
    InheritanceOrImplements = 1 << 3,
    FieldAccess = 1 << 4,
    EventHandling = 1 << 5
}

/// <summary>
/// Singolo dettaglio di dipendenza tra due simboli (chiamante -> chiamato o utilizzatore -> tipo).
/// </summary>
public sealed record DsmDependencyDetail(
    Guid SourceSymbolId,
    string SourceSymbolName,
    Guid TargetSymbolId,
    string TargetSymbolName,
    RelationshipKind RelationshipKind,
    string? Description = null
);

/// <summary>
/// Cella della matrice DSM all'incrocio tra una riga (Source) e una colonna (Target).
/// </summary>
public sealed class DsmCell
{
    public int RowIndex { get; init; }
    public int ColumnIndex { get; init; }
    public Guid RowContainerId { get; init; }
    public Guid ColumnContainerId { get; init; }
    
    /// <summary>Numero totale di collegamenti/dipendenze tra il modulo riga e il modulo colonna.</summary>
    public int Weight { get; set; }

    /// <summary>Tipi di dipendenze presenti.</summary>
    public DsmDependencyKind DependencyKinds { get; set; } = DsmDependencyKind.None;

    /// <summary>Indica se questa cella fa parte di un ciclo di dipendenza reciproca / circolare.</summary>
    public bool IsInCycle { get; set; }

    /// <summary>Lista dei collegamenti specifici (per l'ispezione al click).</summary>
    public List<DsmDependencyDetail> Details { get; } = new();

    public bool HasDependency => Weight > 0;
}

/// <summary>
/// Riga o colonna della matrice DSM che rappresenta un modulo/classe/file.
/// </summary>
public sealed class DsmItem
{
    public int Index { get; set; }
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string QualifiedName { get; init; } = string.Empty;
    public SymbolKind Kind { get; init; }
    public int ClusterId { get; set; }
    public int InDegree { get; set; }
    public int OutDegree { get; set; }
    public bool HasCircularDependencies { get; set; }
}

/// <summary>
/// Modello completo della matrice DSM generata da un CodeGraph.
/// </summary>
public sealed class DsmMatrix
{
    public List<DsmItem> Items { get; init; } = new();
    public DsmCell[,] Cells { get; internal set; } = new DsmCell[0, 0];
    public List<List<Guid>> StronglyConnectedComponents { get; init; } = new();
    public int TotalDependencies { get; set; }
    public int TotalCyclicDependencies { get; set; }

    public int Size => Items.Count;

    public DsmCell GetCell(int row, int col) => Cells[row, col];

    public void InitializeCells()
    {
        int n = Items.Count;
        Cells = new DsmCell[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                Cells[r, c] = new DsmCell
                {
                    RowIndex = r,
                    ColumnIndex = c,
                    RowContainerId = Items[r].Id,
                    ColumnContainerId = Items[c].Id
                };
            }
        }
    }
}

/// <summary>
/// Modalità di ordinamento della matrice DSM.
/// </summary>
public enum DsmOrdering
{
    Alphabetical,
    ByKindAndName,
    TopologicalOrPartitioned
}

/// <summary>
/// Costruttore agnostico della matrice di dipendenza strutturale (DSM).
/// Funziona su qualsiasi CodeGraph a prescindere dal linguaggio sorgente.
/// </summary>
public static class DsmMatrixBuilder
{
    public static DsmMatrix Build(CodeGraph graph, DsmOrdering ordering = DsmOrdering.TopologicalOrPartitioned)
    {
        var symbolMap = graph.Symbols.ToDictionary(s => s.Id);

        // Mappa delle relazioni Contains: parent -> children
        var parentToChildren = new Dictionary<Guid, List<Guid>>();
        foreach (var rel in graph.Relationships.Where(r => r.Kind == RelationshipKind.Contains))
        {
            if (!parentToChildren.TryGetValue(rel.SourceId, out var list))
            {
                list = new List<Guid>();
                parentToChildren[rel.SourceId] = list;
            }
            list.Add(rel.TargetId);
        }

        // 1. Raccoglie tutti i container di primo livello (Classi, Form, Moduli, Componenti, ecc.)
        var containers = graph.Symbols
            .Where(s => s.Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.Module or SymbolKind.UserControl or SymbolKind.Type)
            .ToList();

        if (containers.Count == 0)
        {
            containers = graph.Symbols.Where(s => s.Kind != SymbolKind.Project).ToList();
        }

        // Mappa da simbolo (anche metodo/campo figlio) al relativo container di primo livello
        var symbolToContainer = new Dictionary<Guid, Guid>();
        foreach (var container in containers)
        {
            symbolToContainer[container.Id] = container.Id;
            if (parentToChildren.TryGetValue(container.Id, out var children))
            {
                foreach (var childId in children)
                {
                    symbolToContainer[childId] = container.Id;
                }
            }
        }

        // 2. Costruisce gli item della DSM
        var items = containers.Select((c, idx) => new DsmItem
        {
            Index = idx,
            Id = c.Id,
            Name = c.Name,
            QualifiedName = c.QualifiedName,
            Kind = c.Kind
        }).ToList();

        var matrix = new DsmMatrix { Items = items };
        matrix.InitializeCells();

        var containerIndexMap = new Dictionary<Guid, int>();
        for (int i = 0; i < items.Count; i++)
        {
            containerIndexMap[items[i].Id] = i;
        }

        // 3. Mappa tutte le relazioni semantiche nelle celle
        foreach (var rel in graph.Relationships)
        {
            if (rel.Kind == RelationshipKind.Contains || rel.Kind == RelationshipKind.Declares)
                continue;

            if (!symbolToContainer.TryGetValue(rel.SourceId, out var srcContainerId) ||
                !symbolToContainer.TryGetValue(rel.TargetId, out var tgtContainerId))
            {
                continue;
            }

            if (srcContainerId == tgtContainerId)
                continue;

            if (!containerIndexMap.TryGetValue(srcContainerId, out var rowIdx) ||
                !containerIndexMap.TryGetValue(tgtContainerId, out var colIdx))
            {
                continue;
            }

            var cell = matrix.GetCell(rowIdx, colIdx);
            cell.Weight++;
            matrix.TotalDependencies++;

            symbolMap.TryGetValue(rel.SourceId, out var srcSym);
            symbolMap.TryGetValue(rel.TargetId, out var tgtSym);

            var depKind = rel.Kind switch
            {
                RelationshipKind.Calls => DsmDependencyKind.Call,
                RelationshipKind.Instantiates => DsmDependencyKind.Instantiation,
                RelationshipKind.UsesType => DsmDependencyKind.TypeUsage,
                RelationshipKind.Inherits or RelationshipKind.Implements => DsmDependencyKind.InheritanceOrImplements,
                RelationshipKind.Reads or RelationshipKind.Writes => DsmDependencyKind.FieldAccess,
                RelationshipKind.HandlesEvent => DsmDependencyKind.EventHandling,
                _ => DsmDependencyKind.Call
            };

            cell.DependencyKinds |= depKind;
            cell.Details.Add(new DsmDependencyDetail(
                rel.SourceId,
                srcSym?.Name ?? "Symbol",
                rel.TargetId,
                tgtSym?.Name ?? "Symbol",
                rel.Kind,
                rel.Kind.ToString()
            ));
        }

        // 4. Rilevamento Cicli con Algoritmo di Tarjan per Componenti Fortemente Connesse (SCC)
        DetectCyclesAndComponents(matrix);

        // 5. Ordinamento della matrice se richiesto
        if (ordering != DsmOrdering.Alphabetical)
        {
            SortMatrix(matrix, ordering);
        }

        return matrix;
    }

    private static void DetectCyclesAndComponents(DsmMatrix matrix)
    {
        int n = matrix.Size;
        var adj = new List<int>[n];
        for (int i = 0; i < n; i++) adj[i] = new List<int>();

        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                if (matrix.GetCell(r, c).HasDependency)
                {
                    adj[r].Add(c);
                }
            }
        }

        int index = 0;
        var indices = new int[n];
        Array.Fill(indices, -1);
        var lowlink = new int[n];
        var onStack = new bool[n];
        var stack = new Stack<int>();
        var sccs = new List<List<int>>();

        for (int i = 0; i < n; i++)
        {
            if (indices[i] == -1)
            {
                StrongConnect(i, ref index, indices, lowlink, onStack, stack, adj, sccs);
            }
        }

        int cyclicDepsCount = 0;
        foreach (var scc in sccs.Where(c => c.Count > 1))
        {
            var componentGuids = scc.Select(idx => matrix.Items[idx].Id).ToList();
            matrix.StronglyConnectedComponents.Add(componentGuids);

            foreach (var u in scc)
            {
                matrix.Items[u].HasCircularDependencies = true;
                foreach (var v in scc)
                {
                    var cell = matrix.GetCell(u, v);
                    if (cell.HasDependency)
                    {
                        cell.IsInCycle = true;
                        cyclicDepsCount++;
                    }
                }
            }
        }

        matrix.TotalCyclicDependencies = cyclicDepsCount;
    }

    private static void StrongConnect(
        int u,
        ref int index,
        int[] indices,
        int[] lowlink,
        bool[] onStack,
        Stack<int> stack,
        List<int>[] adj,
        List<List<int>> sccs)
    {
        indices[u] = index;
        lowlink[u] = index;
        index++;
        stack.Push(u);
        onStack[u] = true;

        foreach (var v in adj[u])
        {
            if (indices[v] == -1)
            {
                StrongConnect(v, ref index, indices, lowlink, onStack, stack, adj, sccs);
                lowlink[u] = Math.Min(lowlink[u], lowlink[v]);
            }
            else if (onStack[v])
            {
                lowlink[u] = Math.Min(lowlink[u], indices[v]);
            }
        }

        if (lowlink[u] == indices[u])
        {
            var scc = new List<int>();
            int w;
            do
            {
                w = stack.Pop();
                onStack[w] = false;
                scc.Add(w);
            } while (w != u);
            sccs.Add(scc);
        }
    }

    private static void SortMatrix(DsmMatrix matrix, DsmOrdering ordering)
    {
        var oldItems = matrix.Items.ToList();
        List<DsmItem> sortedItems;

        if (ordering == DsmOrdering.ByKindAndName)
        {
            sortedItems = oldItems.OrderBy(x => x.Kind).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        else
        {
            sortedItems = oldItems
                .OrderByDescending(x => x.HasCircularDependencies)
                .ThenBy(x => x.Kind)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        for (int i = 0; i < sortedItems.Count; i++)
        {
            sortedItems[i].Index = i;
        }

        var oldMap = new Dictionary<Guid, int>();
        for (int i = 0; i < oldItems.Count; i++) oldMap[oldItems[i].Id] = i;

        var newCells = new DsmCell[sortedItems.Count, sortedItems.Count];
        for (int r = 0; r < sortedItems.Count; r++)
        {
            for (int c = 0; c < sortedItems.Count; c++)
            {
                int oldR = oldMap[sortedItems[r].Id];
                int oldC = oldMap[sortedItems[c].Id];
                var oldCell = matrix.GetCell(oldR, oldC);
                newCells[r, c] = new DsmCell
                {
                    RowIndex = r,
                    ColumnIndex = c,
                    RowContainerId = sortedItems[r].Id,
                    ColumnContainerId = sortedItems[c].Id,
                    Weight = oldCell.Weight,
                    DependencyKinds = oldCell.DependencyKinds,
                    IsInCycle = oldCell.IsInCycle
                };
                newCells[r, c].Details.AddRange(oldCell.Details);
            }
        }

        matrix.Items.Clear();
        matrix.Items.AddRange(sortedItems);
        matrix.Cells = newCells;
    }
}
