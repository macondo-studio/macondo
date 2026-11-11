using Macondo.App.Models;
using Macondo.Semantic.Model;

namespace Macondo.App.Layout;

/// <summary>
/// Standard radial/hierarchical layout engine for visual CodeGraph.
/// Posiziona:
/// - The Project node in the center (0, 0).
/// - Modules, classes, forms, UDTs and enums on a concentric main ring.
/// - The methods, properties, fields and constants on satellite orbits around each module they belong to.
/// </summary>
public sealed class RadialLayoutEngine : IGraphLayoutEngine
{
    public static readonly RadialLayoutEngine Instance = new();

    void IGraphLayoutEngine.Apply(VisualCodeGraph graph) => Apply(graph);

    /// <summary>
    /// Calculate and apply (X, Y) coordinates to all nodes of the visual graph in standard concentric radial mode.
    /// </summary>
    /// <param name="graph">The visual graph to be arranged in 2D space.</param>
    public static void Apply(VisualCodeGraph graph)
    {
        // 1. Retrieve all nodes of type Project present in the graph
        var projects = graph.Nodes.Where(n => n.Kind == SymbolKind.Project).ToList();
        if (projects.Count == 0) return;

        var isMultiProject = projects.Count > 1;
        // Calculate the macro radius to adequately space multiple projects if any
        var projectMacroRadius = Math.Max(1200.0, projects.Count * 700.0);

        // 2. Iterate over each project in the workspace
        for (var pIdx = 0; pIdx < projects.Count; pIdx++)
        {
            var project = projects[pIdx];

            // In case of multi-project, it arranges the projects on a macro-circle;
            // for single project, place the root directly at the origin (0, 0).
            if (isMultiProject)
            {
                var projAngle = 2 * Math.PI * pIdx / projects.Count;
                project.X = Math.Cos(projAngle) * projectMacroRadius;
                project.Y = Math.Sin(projAngle) * projectMacroRadius;
            }
            else
            {
                project.X = 0;
                project.Y = 0;
            }

            // 3. Recover all top-level containers (modules, classes, forms, structs, enums) of the project
            var containers = graph.GetChildren(project.Id).ToList();
            var containerCount = Math.Max(containers.Count, 1);

            // Calculate the radius of the main ring proportionally to the number of containers
            // to ensure adequate perimeter spacing and avoid visual collisions
            var containerRingRadius = Math.Max(380.0, containerCount * 35.0);

            // 4. Distributes containers evenly along the main circumference
            for (var i = 0; i < containers.Count; i++)
            {
                var angle = 2 * Math.PI * i / containerCount;
                var container = containers[i];
                container.X = project.X + Math.Cos(angle) * containerRingRadius;
                container.Y = project.Y + Math.Sin(angle) * containerRingRadius;

                // 5. Retrieve the container's child members (methods, properties, constructors, fields, constants)
                var members = graph.GetChildren(container.Id).ToList();
                var memberCount = Math.Max(members.Count, 1);
                
                // Satellite orbit radius dynamically scaled to the number of members
                var memberRingRadius = Math.Max(85.0, memberCount * 14.0);

                // Places each child member on a satellite orbit around its parent container
                for (var j = 0; j < members.Count; j++)
                {
                    var memberAngle = 2 * Math.PI * j / memberCount;
                    var member = members[j];
                    member.X = container.X + Math.Cos(memberAngle) * memberRingRadius;
                    member.Y = container.Y + Math.Sin(memberAngle) * memberRingRadius;
                }
            }
        }
    }
}
