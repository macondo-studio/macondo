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
    public static void ApplyLayout(VisualCodeGraph graph, GraphLayoutMode mode)
    {
        switch (mode)
        {
            case GraphLayoutMode.StandardRadial:
                RadialLayoutEngine.Apply(graph);
                break;
            case GraphLayoutMode.AffinityRadial:
                AffinityRadialLayoutEngine.Apply(graph);
                break;
            case GraphLayoutMode.ClusteredIslands:
                ClusteredIslandsLayoutEngine.Apply(graph);
                break;
            case GraphLayoutMode.ForceDirected:
                ForceDirectedLayoutEngine.Apply(graph);
                break;
            default:
                RadialLayoutEngine.Apply(graph);
                break;
        }
    }
}
