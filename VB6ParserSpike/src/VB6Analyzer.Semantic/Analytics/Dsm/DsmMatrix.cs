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
    /// <summary>
    /// Costruisce una nuova istanza di <see cref="DsmMatrix"/> a partire dal grafo di codice semantico fornito.
    /// </summary>
    /// <param name="graph">Il <see cref="CodeGraph"/> semantico contenente simboli e relazioni estratti.</param>
    /// <param name="ordering">La strategia di ordinamento desiderata per le righe e colonne della matrice DSM.</param>
    /// <returns>La matrice DSM popolata con dipendenze, pesi, componenti fortemente connesse e categorizzazione dei cicli.</returns>
    /// <remarks>
    /// La procedura di costruzione opera secondo le seguenti fasi:
    /// <list type="number">
    ///   <item><description>Identifica tutti i moduli/classi/container di primo livello e mappa la gerarchia <c>CONTAINS</c> per risalire dai singoli membri (metodi, proprietà, campi) al loro container padre.</description></item>
    ///   <item><description>Inizializza la griglia bidimensionale <c>N x N</c> di celle DSM.</description></item>
    ///   <item><description>Proietta ogni relazione semantica (chiamate, istanziazioni, usi di tipo, gestione eventi) sulla cella corrispondente alla coppia sorgente-destinazione, incrementandone il peso e accumulando i dettagli.</description></item>
    ///   <item><description>Esegue l'algoritmo di Tarjan (<see cref="DetectCyclesAndComponents"/>) per rilevare le componenti fortemente connesse (SCC) e contrassegnare le celle/moduli coinvolti in dipendenze circolari.</description></item>
    ///   <item><description>Applica l'eventuale riordinamento geometrico richiesto (<see cref="SortMatrix"/>) per raggruppare i cicli o ordinare per tipologia/alfabeto.</description></item>
    /// </list>
    /// </remarks>
    public static DsmMatrix Build(CodeGraph graph, DsmOrdering ordering = DsmOrdering.TopologicalOrPartitioned)
    {
        var symbolMap = graph.Symbols.ToDictionary(s => s.Id);

        // ---------------------------------------------------------------------
        // FASE 1: Ricostruzione della gerarchia padre -> figli (relazioni CONTAINS).
        // Necessaria per mappare i membri interni (es. metodo Modulo1.CalcolaTotale)
        // al rispettivo modulo o container di primo livello (Modulo1).
        // ---------------------------------------------------------------------
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

        // ---------------------------------------------------------------------
        // FASE 2: Rilevamento dei nodi container di primo livello (Classi, Form, Moduli, UDT, UserControl).
        // Se non vi sono container espliciti, usiamo tutti i simboli non di progetto.
        // ---------------------------------------------------------------------
        var containers = graph.Symbols
            .Where(s => s.Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.Module or SymbolKind.UserControl or SymbolKind.Type)
            .ToList();

        if (containers.Count == 0)
        {
            containers = graph.Symbols.Where(s => s.Kind != SymbolKind.Project).ToList();
        }

        // Mappa di lookup O(1): qualsiasi ID di simbolo o membro -> ID del container di appartenenza
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

        // ---------------------------------------------------------------------
        // FASE 3: Istanziazione degli item DSM e allocazione della griglia bidimensionale N x N.
        // ---------------------------------------------------------------------
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

        // Mappa di lookup veloce: Container ID -> Indice riga/colonna (0..N-1)
        var containerIndexMap = new Dictionary<Guid, int>();
        for (int i = 0; i < items.Count; i++)
        {
            containerIndexMap[items[i].Id] = i;
        }

        // ---------------------------------------------------------------------
        // FASE 4: Proiezione di tutte le relazioni semantiche sulle celle DSM (r, c).
        // Escludiamo relazioni puramente sintattiche (Contains, Declares) e le autorelazioni (stesso modulo).
        // ---------------------------------------------------------------------
        foreach (var rel in graph.Relationships)
        {
            if (rel.Kind == RelationshipKind.Contains || rel.Kind == RelationshipKind.Declares)
                continue;

            // Risaliamo dai simboli specifici (es. Sub Chiamante -> Sub Chiamata) ai rispettivi moduli
            if (!symbolToContainer.TryGetValue(rel.SourceId, out var srcContainerId) ||
                !symbolToContainer.TryGetValue(rel.TargetId, out var tgtContainerId))
            {
                continue;
            }

            // Ignoriamo le dipendenze intra-modulo (sulla diagonale principale)
            if (srcContainerId == tgtContainerId)
                continue;

            if (!containerIndexMap.TryGetValue(srcContainerId, out var rowIdx) ||
                !containerIndexMap.TryGetValue(tgtContainerId, out var colIdx))
            {
                continue;
            }

            // Recupera la cella (riga = chiamante/sorgente, colonna = chiamato/destinazione)
            var cell = matrix.GetCell(rowIdx, colIdx);
            cell.Weight++;
            matrix.TotalDependencies++;

            symbolMap.TryGetValue(rel.SourceId, out var srcSym);
            symbolMap.TryGetValue(rel.TargetId, out var tgtSym);

            // Mappatura flag bitmask per le tipologie di dipendenza
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

        // ---------------------------------------------------------------------
        // FASE 5: Rilevamento Cicli con Algoritmo di Tarjan per Componenti Fortemente Connesse (SCC).
        // ---------------------------------------------------------------------
        DetectCyclesAndComponents(matrix);

        // ---------------------------------------------------------------------
        // FASE 6: Ordinamento geometrico della matrice se richiesto.
        // ---------------------------------------------------------------------
        if (ordering != DsmOrdering.Alphabetical)
        {
            SortMatrix(matrix, ordering);
        }

        return matrix;
    }

    /// <summary>
    /// Rileva i cicli e le dipendenze mutue/circolari all'interno della DSM applicando l'algoritmo di Tarjan per le Componenti Fortemente Connesse (SCC).
    /// </summary>
    /// <param name="matrix">La matrice DSM da analizzare.</param>
    /// <remarks>
    /// Costruisce una lista di adiacenza basata sulle celle con peso &gt; 0, esegue una visita in profondità (DFS)
    /// mediante <see cref="StrongConnect"/> e isola i sottoinsiemi di moduli con interdipendenze cicliche (<c>SCC.Count &gt; 1</c>).
    /// Tutte le celle interne a tali componenti vengono contrassegnate con <see cref="DsmCell.IsInCycle"/> = <c>true</c>
    /// e gli elementi con <see cref="DsmItem.HasCircularDependencies"/> = <c>true</c>.
    /// </remarks>
    private static void DetectCyclesAndComponents(DsmMatrix matrix)
    {
        int n = matrix.Size;
        
        // 1. Costruzione della lista di adiacenza del grafo orientato a partire dalle celle con dipendenza attiva
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

        // 2. Strutture dati per l'algoritmo di Tarjan (DFS O(V+E))
        int index = 0;
        var indices = new int[n];
        Array.Fill(indices, -1); // -1 indica nodo non ancora visitato
        var lowlink = new int[n];
        var onStack = new bool[n];
        var stack = new Stack<int>();
        var sccs = new List<List<int>>();

        // 3. Esecuzione DFS su tutti i nodi non ancora esplorati
        for (int i = 0; i < n; i++)
        {
            if (indices[i] == -1)
            {
                StrongConnect(i, ref index, indices, lowlink, onStack, stack, adj, sccs);
            }
        }

        // 4. Analisi delle SCC rilevate: una SCC con Count > 1 rappresenta un ciclo di dipendenze circolari
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

    /// <summary>
    /// Procedura ricorsiva principale dell'algoritmo di Tarjan per l'individuazione di componenti fortemente connesse (SCC).
    /// </summary>
    /// <param name="u">Indice del nodo/modulo corrente in esame.</param>
    /// <param name="index">Contatore progressivo dei nodi visitati (assegnato come ID temporaneo di scoperta).</param>
    /// <param name="indices">Vettore degli indici di scoperta (<c>-1</c> se non ancora visitato).</param>
    /// <param name="lowlink">Vettore del valore di low-link (il più piccolo indice di scoperta raggiungibile da <paramref name="u"/> attraverso lo stack).</param>
    /// <param name="onStack">Flag booleano che indica se un nodo si trova attualmente nello stack di ricorsione.</param>
    /// <param name="stack">Lo stack dei nodi del cammino DFS corrente.</param>
    /// <param name="adj">Lista di adiacenza del grafo delle dipendenze.</param>
    /// <param name="sccs">Lista di output in cui vengono accumulate le componenti fortemente connesse individuate.</param>
    /// <remarks>
    /// Quando <c>lowlink[u] == indices[u]</c>, il nodo <paramref name="u"/> rappresenta la radice di una SCC:
    /// tutti i nodi sopra di esso nello stack vengono estratti per formare la nuova componente connessa.
    /// L'algoritmo ha complessità temporale lineare <c>O(V + E)</c>, ottimale per grafi di grandi dimensioni.
    /// </remarks>
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
        // Assegna indice progressivo di visita al nodo u e inseriscilo nello stack
        indices[u] = index;
        lowlink[u] = index;
        index++;
        stack.Push(u);
        onStack[u] = true;

        // Esplora tutti gli archi uscenti (u -> v)
        foreach (var v in adj[u])
        {
            if (indices[v] == -1)
            {
                // Il successore v non è ancora stato visitato: visita ricorsiva
                StrongConnect(v, ref index, indices, lowlink, onStack, stack, adj, sccs);
                // Aggiorna il lowlink di u con il minimo raggiungibile da v
                lowlink[u] = Math.Min(lowlink[u], lowlink[v]);
            }
            else if (onStack[v])
            {
                // Il successore v è attualmente nello stack di chiamata: back-edge verso un antenato della stessa SCC
                lowlink[u] = Math.Min(lowlink[u], indices[v]);
            }
        }

        // Se u è la radice della componente fortemente connessa (nessun nodo sopra di esso può risalire oltre u)
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

    /// <summary>
    /// Riordina gli elementi e riallinea la matrice DSM secondo il criterio specificato.
    /// </summary>
    /// <param name="matrix">L'istanza di <see cref="DsmMatrix"/> da ordinare in-place.</param>
    /// <param name="ordering">La modalità di ordinamento richiesta (per tipo e nome o raggruppando le dipendenze circolari in cima/diagonale).</param>
    /// <remarks>
    /// La procedura:
    /// <list type="number">
    ///   <item><description>Ordina la lista degli elementi (<see cref="DsmItem"/>) e riassegna i loro indici ordinali.</description></item>
    ///   <item><description>Crea una nuova matrice <c>N x N</c> di celle, riallocando ciascuna cella <c>(r, c)</c> in base alla nuova posizione degli elementi associati preservando pesi, tipologie e dettagli.</description></item>
    ///   <item><description>Sostituisce la vecchia griglia di celle con la nuova matrice riallineata.</description></item>
    /// </list>
    /// </remarks>
    private static void SortMatrix(DsmMatrix matrix, DsmOrdering ordering)
    {
        var oldItems = matrix.Items.ToList();
        List<DsmItem> sortedItems;

        // 1. Ordina la lista degli elementi secondo la strategia prescelta
        if (ordering == DsmOrdering.ByKindAndName)
        {
            sortedItems = oldItems.OrderBy(x => x.Kind).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        else
        {
            // TopologicalOrPartitioned: raggruppa prima i moduli con dipendenze circolari, poi per tipo e nome
            sortedItems = oldItems
                .OrderByDescending(x => x.HasCircularDependencies)
                .ThenBy(x => x.Kind)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Riassegna gli indici progressivi (0..N-1)
        for (int i = 0; i < sortedItems.Count; i++)
        {
            sortedItems[i].Index = i;
        }

        // Mappa di lookup per trovare la posizione precedente di ciascun modulo
        var oldMap = new Dictionary<Guid, int>();
        for (int i = 0; i < oldItems.Count; i++) oldMap[oldItems[i].Id] = i;

        // 2. Crea la nuova griglia di celle N x N riallineata secondo le nuove coordinate riga/colonna
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

        // 3. Sostituisce la lista e la matrice di celle in-place
        matrix.Items.Clear();
        matrix.Items.AddRange(sortedItems);
        matrix.Cells = newCells;
    }
}
