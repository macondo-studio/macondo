using Macondo.App.Models;

namespace Macondo.App.Layout;

/// <summary>
/// Interfaccia comune per gli algoritmi di disposizione geometrica e fisica del grafo visuale.
/// </summary>
public interface IGraphLayoutEngine
{
    /// <summary>
    /// Calcola e applica le coordinate (X, Y) a tutti i nodi del <see cref="VisualCodeGraph"/>.
    /// </summary>
    void Apply(VisualCodeGraph graph);
}
