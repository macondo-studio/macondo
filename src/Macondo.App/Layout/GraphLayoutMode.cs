namespace Macondo.App.Layout;

/// <summary>
/// How to visually arrange and spatially position nodes in the CodeGraph.
/// </summary>
public enum GraphLayoutMode
{
    /// <summary>
    /// Standard geometric concentric arrangement (Project in the center, modules on a uniform ring).
    /// </summary>
    StandardRadial,

    /// <summary>
    /// Circular layout with angular sorting of modules based on call affinity
    /// to minimize arc crossings through the center of the canvas.
    /// </summary>
    AffinityRadial,

    /// <summary>
    /// Detects tightly coupled business subsystems/modules and groups them into separate islands/clusters on the plane.
    /// </summary>
    ClusteredIslands,

    /// <summary>
    /// Physical simulation with forces (Fruchterman-Reingold): electrostatic repulsion between nodes and spring attraction along the calls.
    /// </summary>
    ForceDirected
}
