using Macondo.App.Models;

namespace Macondo.App.Layout;

/// <summary>
/// Gestore e dispatcher centrale dei motori di layout per il CodeGraph visuale.
/// </summary>
public static class GraphLayoutManager
{
    /// <summary>
    /// Applica l'algoritmo di disposizione spaziale specificato al grafo visuale.
    /// </summary>
    /// <param name="graph">Il grafo visuale su cui calcolare e assegnare le posizioni dei nodi.</param>
    /// <param name="mode">La modalità o algoritmo di layout selezionato dall'utente.</param>
    /// <param name="cancellationToken">Token opzionale di cancellazione per interrompere il calcolo del layout.</param>
    public static void ApplyLayout(VisualCodeGraph graph, GraphLayoutMode mode, CancellationToken cancellationToken = default)
    {
        // Smista l'esecuzione verso l'engine di layout specifico in base alla modalità selezionata
        switch (mode)
        {
            case GraphLayoutMode.StandardRadial:
                // Layout radiale concentrico standard (moduli sull'anello principale, membri su satelliti)
                RadialLayoutEngine.Apply(graph);
                break;

            case GraphLayoutMode.AffinityRadial:
                // Layout radiale con ottimizzazione angolare per affinità semantica (TSP / Nearest Neighbor)
                AffinityRadialLayoutEngine.Apply(graph, cancellationToken);
                break;

            case GraphLayoutMode.ClusteredIslands:
                // Layout a isole funzionali basato su componenti connesse (Union-Find)
                ClusteredIslandsLayoutEngine.Apply(graph, cancellationToken);
                break;

            case GraphLayoutMode.ForceDirected:
                // Layout fisico a forze repulsive ed elastiche (Fruchterman-Reingold con Simulated Annealing)
                ForceDirectedLayoutEngine.Apply(graph, cancellationToken);
                break;

            default:
                // Fallback di sicurezza su modalità radiale standard
                RadialLayoutEngine.Apply(graph);
                break;
        }
    }
}
