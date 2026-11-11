using Macondo.App.Models;

namespace Macondo.App.Layout;

/// <summary>
/// Common interface for visual graph geometric and physical layout algorithms.
/// </summary>
public interface IGraphLayoutEngine
{
    /// <summary>
    /// Calculate and apply the (X, Y) coordinates to all nodes of the <see cref="VisualCodeGraph"/>.
    /// </summary>
    void Apply(VisualCodeGraph graph);
}
