using Macondo.App.Models;
using Macondo.Semantic.Model;

namespace Macondo.App.Layout;

/// <summary>
/// Motore di layout a Cluster di Isole (Sottosistemi Funzionali).
/// Identifica i gruppi di moduli/classi fortemente coesi tramite analisi delle componenti connesse e densità di chiamate,
/// e dispone ciascun gruppo su una propria "isola" autonoma sul canvas bidimensionale.
/// </summary>
public sealed class ClusteredIslandsLayoutEngine : IGraphLayoutEngine
{
    public static readonly ClusteredIslandsLayoutEngine Instance = new();

    void IGraphLayoutEngine.Apply(VisualCodeGraph graph) => Apply(graph);

    /// <summary>
    /// Calcola e applica le coordinate (X, Y) a tutti i nodi del grafo visuale raggruppando i moduli in isole funzionali autonome.
    /// </summary>
    /// <param name="graph">Il grafo visuale da posizionare.</param>
    /// <remarks>
    /// L'algoritmo:
    /// <list type="number">
    ///   <item><description>Esegue l'algoritmo Disjoint Set (Union-Find con path compression) per isolare le componenti connesse tra i diversi container.</description></item>
    ///   <item><description>Distribuisce i centroidi delle diverse isole sul macro-piano 2D lungo una circonferenza di raggio proporzionale al numero di cluster.</description></item>
    ///   <item><description>All'interno di ciascuna isola, dispone i container su un anello locale e i rispettivi membri interni su satelliti attorno al container.</description></item>
    /// </list>
    /// </remarks>
    public static void Apply(VisualCodeGraph graph)
    {
        // 1. Recupero di tutti i nodi container (classi, moduli, form, ecc.)
        var containers = graph.Nodes.Where(n => n.IsContainer).ToList();
        if (containers.Count == 0) return;

        var projects = graph.Nodes.Where(n => n.Kind == SymbolKind.Project).ToList();

        // =========================================================================
        // PASSO 1: Mappatura di appartenenza nodo -> container
        // Mappa sia i container stessi sia i relativi nodi membri (metodi, proprietà, campi)
        // all'ID del rispettivo container genitore per poter proiettare gli archi tra membri.
        // =========================================================================
        var nodeToContainerMap = new Dictionary<Guid, Guid>();
        foreach (var c in containers)
        {
            nodeToContainerMap[c.Id] = c.Id;
            foreach (var m in graph.GetChildren(c.Id))
            {
                nodeToContainerMap[m.Id] = c.Id;
            }
        }

        // =========================================================================
        // PASSO 2: Struttura dati Disjoint Set (Union-Find) con Path Compression
        // Identifica le componenti connesse del grafo tra i diversi container:
        // due moduli appartengono alla stessa isola se esiste una relazione semantica
        // diretta o indiretta (chiamata, istanziazione, tipo usato) tra di loro.
        // =========================================================================
        var parent = containers.ToDictionary(c => c.Id, c => c.Id);

        // Funzione ricorsiva di ricerca della radice del set con compressione del cammino
        Guid FindRoot(Guid id)
        {
            if (parent[id] != id)
                parent[id] = FindRoot(parent[id]);
            return parent[id];
        }

        // Unione di due set disgiunti
        void Union(Guid a, Guid b)
        {
            var rootA = FindRoot(a);
            var rootB = FindRoot(b);
            if (rootA != rootB)
                parent[rootB] = rootA;
        }

        // Unisce i container collegati da archi semantici nel grafo
        foreach (var edge in graph.Edges)
        {
            if (nodeToContainerMap.TryGetValue(edge.SourceId, out var c1) &&
                nodeToContainerMap.TryGetValue(edge.TargetId, out var c2) &&
                c1 != c2)
            {
                Union(c1, c2);
            }
        }

        // =========================================================================
        // PASSO 3: Raggruppamento dei container nelle rispettive isole/cluster
        // Ordina le isole in modo decrescente per dimensione (numero di container)
        // in modo da posizionare i cluster più significativi per primi.
        // =========================================================================
        var containerById = containers.ToDictionary(c => c.Id);
        var islands = containers
            .GroupBy(c => FindRoot(c.Id))
            .Select(g => g.ToList())
            .OrderByDescending(list => list.Count)
            .ToList();

        var islandCount = islands.Count;

        // =========================================================================
        // PASSO 4: Posizionamento macro-geometrico dei centroidi delle isole
        // =========================================================================
        var macroRadius = Math.Max(500.0, islandCount * 140.0 + containers.Count * 25.0);

        for (var i = 0; i < islandCount; i++)
        {
            var islandContainers = islands[i];
            double islandCenterX = 0;
            double islandCenterY = 0;

            // Se ci sono più isole, distribuisce i loro centri lungo una macro-circonferenza;
            // se c'è un'unica isola, posiziona il centroide all'origine (0, 0).
            if (islandCount > 1)
            {
                var islandAngle = 2 * Math.PI * i / islandCount;
                islandCenterX = Math.Cos(islandAngle) * macroRadius;
                islandCenterY = Math.Sin(islandAngle) * macroRadius;
            }

            var countInIsland = islandContainers.Count;
            var islandRadius = countInIsland == 1 ? 0 : Math.Max(160.0, countInIsland * 55.0);

            // =====================================================================
            // PASSO 5: Posizionamento dei container all'interno della singola isola
            // =====================================================================
            for (var j = 0; j < countInIsland; j++)
            {
                var container = islandContainers[j];
                if (countInIsland == 1)
                {
                    // Singolo container: posizionato esattamente al centro dell'isola
                    container.X = islandCenterX;
                    container.Y = islandCenterY;
                }
                else
                {
                    // Multi-container: disposti su un anello locale attorno al centroide dell'isola
                    var angleInIsland = 2 * Math.PI * j / countInIsland;
                    container.X = islandCenterX + Math.Cos(angleInIsland) * islandRadius;
                    container.Y = islandCenterY + Math.Sin(angleInIsland) * islandRadius;
                }

                // =================================================================
                // PASSO 6: Posizionamento dei membri satelliti attorno al container
                // =================================================================
                var members = graph.GetChildren(container.Id).ToList();
                var memberCount = Math.Max(members.Count, 1);
                var memberRingRadius = Math.Max(80.0, memberCount * 13.0);

                for (var mIdx = 0; mIdx < members.Count; mIdx++)
                {
                    var memberAngle = 2 * Math.PI * mIdx / memberCount;
                    var member = members[mIdx];
                    member.X = container.X + Math.Cos(memberAngle) * memberRingRadius;
                    member.Y = container.Y + Math.Sin(memberAngle) * memberRingRadius;
                }
            }
        }

        // =========================================================================
        // PASSO 7: Posizionamento dei nodi Progetto al baricentro dei loro moduli
        // =========================================================================
        foreach (var proj in projects)
        {
            var projContainers = graph.GetChildren(proj.Id).ToList();
            if (projContainers.Count > 0)
            {
                proj.X = projContainers.Average(c => c.X);
                proj.Y = projContainers.Average(c => c.Y);
            }
            else
            {
                proj.X = 0;
                proj.Y = 0;
            }
        }
    }
}
