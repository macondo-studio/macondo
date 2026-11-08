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
    public int RowIndex { get; set; }
    public int ColumnIndex { get; set; }
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
