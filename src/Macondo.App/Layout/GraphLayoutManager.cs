using Macondo.App.Models;

namespace Macondo.App.Layout;

/// <summary>
/// Central layout engine manager and dispatcher for visual CodeGraph.
/// </summary>
public static class GraphLayoutManager
{
    /// <summary>
    /// Applies the specified spatial arrangement algorithm to the visual graph.
    /// </summary>
    /// <param name="graph">The visual graph on which to calculate and assign the positions of the nodes.</param>
    /// <param name="mode">The layout mode or algorithm selected by the user.</param>
    /// <param name="cancellationToken">Optional clear token to stop layout calculation.</param>
    public static void ApplyLayout(VisualCodeGraph graph, GraphLayoutMode mode, CancellationToken cancellationToken = default)
    {
        // Routes execution to the specific layout engine based on the selected mode
        switch (mode)
        {
            case GraphLayoutMode.StandardRadial:
                // Standard concentric radial layout (modules on main ring, members on satellites)
                RadialLayoutEngine.Apply(graph);
                break;

            case GraphLayoutMode.AffinityRadial:
                // Radial layout with angular optimization for semantic affinity (TSP / Nearest Neighbor)
                AffinityRadialLayoutEngine.Apply(graph, cancellationToken);
                break;

            case GraphLayoutMode.ClusteredIslands:
                // Functional island layout based on connected components (Union-Find)
                ClusteredIslandsLayoutEngine.Apply(graph, cancellationToken);
                break;

            case GraphLayoutMode.ForceDirected:
                // Physical layout with repulsive and elastic forces (Fruchterman-Reingold with Simulated Annealing)
                ForceDirectedLayoutEngine.Apply(graph, cancellationToken);
                break;

            default:
                // Safety fallback to standard radial mode
                RadialLayoutEngine.Apply(graph);
                break;
        }
    }
}
