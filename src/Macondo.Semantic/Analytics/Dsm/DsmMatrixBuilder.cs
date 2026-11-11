using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.Dsm;

/// <summary>
/// Agnostic structural dependency matrix (DSM) constructor.
/// It works on any CodeGraph regardless of the source language.
/// </summary>
public static class DsmMatrixBuilder
{
    /// <summary>
    /// Constructs a new instance of <see cref="DsmMatrix"/> from the provided semantic code graph.
    /// </summary>
    /// <param name="graph">The semantic <see cref="CodeGraph"/> containing extracted symbols and relations.</param>
    /// <param name="ordering">The desired sorting strategy for the rows and columns of the DSM matrix.</param>
    /// <returns>The DSM matrix populated with dependencies, weights, strongly connected components, and loop categorization.</returns>
    /// <remarks>
    /// The construction procedure operates according to the following phases:
    /// <list type="number">
    /// <item><description>Identifies all top-level modules/classes/containers and maps the <c>CONTAINS</c> hierarchy to trace from the individual members (methods, properties, fields) to their parent container.</description></item>
    /// <item><description>Initializes the two-dimensional grid <c>N x N</c> of DSM cells.</description></item>
    /// <item><description>Projects each semantic relationship (calls, instantiations, type uses, event management) onto the cell corresponding to the source-destination pair, increasing its weight and accumulating details.</description></item>
    /// <item><description>Runs the Tarjan algorithm (<see cref="DetectCyclesAndComponents"/>) to detect strongly connected components (SCC) and flag cells/modules involved in circular dependencies.</description></item>
    /// <item><description>Applies any required geometric reordering (<see cref="SortMatrix"/>) to group the cycles or sort by type/alphabet.</description></item>
    /// </list>
    /// </remarks>
    public static DsmMatrix Build(CodeGraph graph, DsmOrdering ordering = DsmOrdering.TopologicalOrPartitioned)
    {
        var symbolMap = graph.Symbols.ToDictionary(s => s.Id);

        // ---------------------------------------------------------------------
        // PHASE 1: Reconstruction of the parent -> children hierarchy (CONTAINS relationships).
        // Required to map internal members (e.g. Modulo1.CalcolaTotal method)
        // to the respective top-level module or container (Module1).
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
        // PHASE 2: Detection of top-level container nodes (Classes, Forms, Modules, UDTs, UserControls, Interfaces, Structures, Records).
        // If there are no explicit containers, we use all non-project symbols.
        // ---------------------------------------------------------------------
        var containers = graph.Symbols
            .Where(s => s.Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.Module or SymbolKind.UserControl or SymbolKind.Type or SymbolKind.Interface or SymbolKind.Struct or SymbolKind.Record)
            .ToList();

        if (containers.Count == 0)

            containers = graph.Symbols.Where(s => s.Kind != SymbolKind.Project).ToList();

        // O(1) lookup map: any symbol or member ID -> parent container ID
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
        // PHASE 3: Instantiation of the DSM items and allocation of the N x N two-dimensional grid.
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

        // Quick lookup map: Container ID -> Row/Column Index (0..N-1)
        var containerIndexMap = new Dictionary<Guid, int>();
        for (int i = 0; i < items.Count; i++)
        {
            containerIndexMap[items[i].Id] = i;
        }

        // ---------------------------------------------------------------------
        // STEP 4: Projection of all semantic relations onto DSM cells (r, c).
        // We exclude purely syntactic relations (Contains, Declares) and self-relations (same module).
        // ---------------------------------------------------------------------
        foreach (var rel in graph.Relationships)
        {
            if (rel.Kind == RelationshipKind.Contains || rel.Kind == RelationshipKind.Declares)
                continue;

            // We trace from the specific symbols (e.g. Sub Caller -> Sub Call) to the respective modules
            if (!symbolToContainer.TryGetValue(rel.SourceId, out var srcContainerId) ||
                !symbolToContainer.TryGetValue(rel.TargetId, out var tgtContainerId))

                continue;

            // We ignore intra-module dependencies (on the main diagonal)
            if (srcContainerId == tgtContainerId)
                continue;

            if (!containerIndexMap.TryGetValue(srcContainerId, out var rowIdx) ||
                !containerIndexMap.TryGetValue(tgtContainerId, out var colIdx))

                continue;

            // Get cell (row = caller/source, column = callee/destination)
            var cell = matrix.GetCell(rowIdx, colIdx);
            cell.Weight++;
            matrix.TotalDependencies++;

            symbolMap.TryGetValue(rel.SourceId, out var srcSym);
            symbolMap.TryGetValue(rel.TargetId, out var tgtSym);

            // Bitmask flag mapping for dependency types
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
        // PHASE 5: Cycle Detection with Tarjan Algorithm for Strongly Connected Components (SCC).
        // ---------------------------------------------------------------------
        DetectCyclesAndComponents(matrix);

        // ---------------------------------------------------------------------
        // STEP 6: Geometric ordering of the matrix if required.
        // ---------------------------------------------------------------------
        if (ordering != DsmOrdering.Alphabetical)

            SortMatrix(matrix, ordering);

        // ---------------------------------------------------------------------
        // STEP 7: Calculating InDegree and OutDegree for each element of the DSM.
        // OutDegree = number of other modules the module depends on (r row cells with HasDependency).
        // InDegree = number of other modules that depend on the module (r column cells with HasDependency).
        // ---------------------------------------------------------------------
        int n = matrix.Size;
        for (int r = 0; r < n; r++)
        {
            int outDeg = 0;
            int inDeg = 0;
            for (int c = 0; c < n; c++)
            {
                if (r != c && matrix.Cells[r, c].HasDependency)
                    outDeg++;
                if (r != c && matrix.Cells[c, r].HasDependency)
                    inDeg++;
            }
            matrix.Items[r].OutDegree = outDeg;
            matrix.Items[r].InDegree = inDeg;
        }

        return matrix;
    }

    /// <summary>
    /// Detect cycles and mutual/circular dependencies within the DSM by applying the Tarjan algorithm for Strongly Connected Components (SCC).
    /// </summary>
    /// <param name="matrix">The DSM matrix to be analyzed.</param>
    /// <remarks>
    /// Builds an adjacency list based on cells with weight > 0, performs a depth-first visit (DFS)
    /// using <see cref="StrongConnect"/> and isolates module subsets with cyclic interdependencies (<c>SCC.Count > 1</c>).
    /// All cells inside these components are marked with <see cref="DsmCell.IsInCycle"/> = <c>true</c>
    /// and elements with <see cref="DsmItem.HasCircularDependencies"/> = <c>true</c>.
    /// </remarks>
    private static void DetectCyclesAndComponents(DsmMatrix matrix)
    {
        int n = matrix.Size;
        
        // 1. Construction of the adjacency list of the directed graph starting from the cells with active dependency
        var adj = new List<int>[n];
        for (int i = 0; i < n; i++) adj[i] = new List<int>();

        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                if (matrix.GetCell(r, c).HasDependency)

                    adj[r].Add(c);
            }
        }

        // 2. Data structures for the Tarjan algorithm (DFS O(V+E))
        int index = 0;
        var indices = new int[n];
        Array.Fill(indices, -1); // -1 indica nodo non ancora visitato
        var lowlink = new int[n];
        var onStack = new bool[n];
        var stack = new Stack<int>();
        var sccs = new List<List<int>>();

        // 3. Running DFS on all nodes not yet explored
        for (int i = 0; i < n; i++)
        {
            if (indices[i] == -1)

                StrongConnect(i, ref index, indices, lowlink, onStack, stack, adj, sccs);
        }

        // 4. Analysis of detected SCCs: An SCC with Count > 1 represents a loop of circular dependencies
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
    /// Main recursive procedure of the Tarjan algorithm for identifying strongly connected components (SCC).
    /// </summary>
    /// <param name="u">Index of the current node/module under examination.</param>
    /// <param name="index">Progressive counter of visited nodes (assigned as temporary discovery ID).</param>
    /// <param name="indices">Vector of discovery indices (<c>-1</c> if not yet visited).</param>
    /// <param name="lowlink">Low-link value vector (the smallest discovery index reachable by <paramref name="u"/> across the stack).</param>
    /// <param name="onStack">Boolean flag indicating whether a node is currently on the recursion stack.</param>
    /// <param name="stack">The node stack of the current DFS path.</param>
    /// <param name="adj">Dependency graph adjacency list.</param>
    /// <param name="sccs">Output list in which the identified strongly connected components are accumulated.</param>
    /// <remarks>
    /// When <c>lowlink[u] == indices[u]</c>, the node <paramref name="u"/> represents the root of an SCC:
    /// all nodes above it in the stack are popped to form the new connected component.
    /// The algorithm has linear time complexity <c>O(V + E)</c>, optimal for large graphs.
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
        // Assign progressive visit index to node u and push it onto the stack
        indices[u] = index;
        lowlink[u] = index;
        index++;
        stack.Push(u);
        onStack[u] = true;

        // Explore all outgoing arcs (u -> v)
        foreach (var v in adj[u])
        {
            if (indices[v] == -1)
            {
                // The successor v has not yet been visited: recursive visit
                StrongConnect(v, ref index, indices, lowlink, onStack, stack, adj, sccs);
                // Update u's lowlink with the minimum reachable from v
                lowlink[u] = Math.Min(lowlink[u], lowlink[v]);
            }
            else if (onStack[v])

    lowlink[u] = Math.Min(lowlink[u], indices[v]);
        }

        // If u is the root of the strongly connected component (no node above it can trace beyond u)
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
    /// Reorders the elements and realigns the DSM matrix according to the specified criterion.
    /// </summary>
    /// <param name="matrix">The instance of <see cref="DsmMatrix"/> to order in-place.</param>
    /// <param name="ordering">The required sorting mode (by type and name, or grouping circular dependencies on top/diagonal).</param>
    /// <remarks>
    /// The procedure:
    /// <list type="number">
    /// <item><description>Sorts the list of elements (<see cref="DsmItem"/>) and reassigns their ordinal indices.</description></item>
    /// <item><description>Creates a new <c>N x N</c> matrix of cells, reallocating each cell <c>(r, c)</c> based on the new position of the associated elements while preserving weights, types and details.</description></item>
    /// <item><description>Replaces the old grid of cells with the new realigned matrix.</description></item>
    /// </list>
    /// </remarks>
    private static void SortMatrix(DsmMatrix matrix, DsmOrdering ordering)
    {
        var oldItems = matrix.Items.ToList();
        List<DsmItem> sortedItems;

        // 1. Sort the list of elements according to the chosen strategy
        if (ordering == DsmOrdering.ByKindAndName)

            sortedItems = oldItems.OrderBy(x => x.Kind).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();

        else
        {
            // TopologicalOrPartitioned: Group modules with circular dependencies first, then by type and name
            sortedItems = oldItems
                .OrderByDescending(x => x.HasCircularDependencies)
                .ThenBy(x => x.Kind)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Reassign progressive indices (0..N-1)
        for (int i = 0; i < sortedItems.Count; i++)
        {
            sortedItems[i].Index = i;
        }

        // Lookup map to find the previous position of each module
        var oldMap = new Dictionary<Guid, int>();
        for (int i = 0; i < oldItems.Count; i++) oldMap[oldItems[i].Id] = i;

        // 2. Create the new grid of N x N cells realigned according to the new row/column coordinates
        // We reassign references to existing cells without allocating new objects or copying lists
        var oldCells = matrix.Cells;
        var newCells = new DsmCell[sortedItems.Count, sortedItems.Count];
        for (int r = 0; r < sortedItems.Count; r++)
        {
            int oldR = oldMap[sortedItems[r].Id];
            for (int c = 0; c < sortedItems.Count; c++)
            {
                int oldC = oldMap[sortedItems[c].Id];
                var cell = oldCells[oldR, oldC];
                cell.RowIndex = r;
                cell.ColumnIndex = c;
                newCells[r, c] = cell;
            }
        }

        // 3. Replaces list and array cells in-place
        matrix.Items.Clear();
        matrix.Items.AddRange(sortedItems);
        matrix.Cells = newCells;
    }
}
