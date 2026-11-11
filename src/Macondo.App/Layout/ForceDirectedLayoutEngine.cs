using Macondo.App.Models;
using Macondo.Semantic.Model;

namespace Macondo.App.Layout;

/// <summary>
/// Force-Directed layout engine based on physical model (Fruchterman-Reingold).
/// Apply repulsive forces between all containers to avoid overlapping and elastic attractive forces
/// along semantic calls and relationships, organically arranging nodes with rapid convergence.
/// </summary>
public sealed class ForceDirectedLayoutEngine : IGraphLayoutEngine
{
    public static readonly ForceDirectedLayoutEngine Instance = new();

    void IGraphLayoutEngine.Apply(VisualCodeGraph graph) => Apply(graph);

    /// <summary>
    /// Calculate and apply the coordinates (X, Y) to all the nodes of the visual graph using Force-Directed (Fruchterman-Reingold) physical simulation.
    /// </summary>
    /// <param name="graph">The visual graph to position.</param>
    /// <param name="cancellationToken">Optional clear token to stop physics simulation.</param>
    public static void Apply(VisualCodeGraph graph, CancellationToken cancellationToken = default)
    {
        // 1. Retrieve all container nodes (classes, modules, forms, etc.)
        var containers = graph.Nodes.Where(n => n.IsContainer).ToList();
        if (containers.Count == 0) return;

        var projects = graph.Nodes.Where(n => n.Kind == SymbolKind.Project).ToList();
        var n = containers.Count;

        // =========================================================================
        // STEP 1: Node membership map -> container
        // Map each member node (method, property, field) to its parent container.
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
        // STEP 2: Initial circular placement (Stable Seed)
        // Arranging the containers on a preliminary ring avoids singularities or local minima
        // chaotic before starting the Fruchterman-Reingold physics simulation.
        // =========================================================================
        var initialRadius = Math.Max(350.0, n * 30.0);
        for (var i = 0; i < n; i++)
        {
            var angle = 2 * Math.PI * i / n;
            containers[i].X = Math.Cos(angle) * initialRadius;
            containers[i].Y = Math.Sin(angle) * initialRadius;
        }

        // =========================================================================
        // STEP 3: Initialization of physical model parameters (Fruchterman-Reingold)
        // - k: ideal equilibrium distance between two nodes (scale with the virtual area of the canvas)
        // - iterations: adaptive number of iterations (reduced for massive graphs to ensure fast convergence)
        // - temp: initial temperature (maximum excursion allowed per step)
        // - coolingFactor: geometric cooling rate (Simulated Annealing)
        // =========================================================================
        var optimalDistance = Math.Max(220.0, Math.Sqrt((1200.0 * 1200.0) / Math.Max(n, 1)));
        var k = optimalDistance;
        var kSquared = k * k;

        // On very large graphs (>800 nodes) thermal convergence occurs in the first 30-45 iterations;
        // reducing the maximum limit avoids redundant calculations at low temperatures guaranteeing times less than a few seconds.
        var iterations = n > 2000 ? 30 : (n > 800 ? 45 : Math.Min(80, Math.Max(35, n * 2)));
        var initialTemp = initialRadius * 0.4;
        var temp = initialTemp;
        var coolingFactor = 0.95;

        // Reusable buffers for the net displacements calculated at each iteration
        var dispX = new double[n];
        var dispY = new double[n];

        // =========================================================================
        // STEP 4: Mapping semantic arcs between containers with differentiated weights
        // Projects member relationships onto equivalent arcs between parent containers:
        // - Calls (3.0): strong attraction for modules that call each other
        // - Instantiates (2.5): Attraction for factory/creator module
        // - UsesType (1.5): type dependency attraction
        // =========================================================================
        var containerEdges = new List<(int SrcIdx, int TgtIdx, double Weight)>();
        var containerIndexById = containers.Select((c, idx) => (c.Id, idx)).ToDictionary(x => x.Id, x => x.idx);

        foreach (var edge in graph.Edges)
        {
            if (nodeToContainerMap.TryGetValue(edge.SourceId, out var c1) &&
                nodeToContainerMap.TryGetValue(edge.TargetId, out var c2) &&
                c1 != c2 &&
                containerIndexById.TryGetValue(c1, out var idx1) &&
                containerIndexById.TryGetValue(c2, out var idx2))
            {
                var w = edge.Kind switch
                {
                    RelationshipKind.Calls => 3.0,
                    RelationshipKind.Instantiates => 2.5,
                    RelationshipKind.UsesType => 1.5,
                    _ => 1.0
                };
                containerEdges.Add((idx1, idx2, w));
            }
        }

        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount)
        };

        // =========================================================================
        // STEP 5: Main dynamic simulation loop (Physics Loop)
        // =========================================================================
        for (var iter = 0; iter < iterations; iter++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Reset of accumulated displacement vectors
            Array.Clear(dispX, 0, n);
            Array.Clear(dispY, 0, n);

            // 5a. Electrostatic repulsive forces between all pairs of nodes (O(N^2) parallelized on multi-core)
            // Each worker thread independently and thread-safely calculates the net resultant for index 'i'
            // Coulomb formula: F_rep = k^2 / dist
            Parallel.For(0, n, parallelOptions, i =>
            {
                var c1 = containers[i];
                double netFx = 0.0;
                double netFy = 0.0;

                for (var j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    var c2 = containers[j];
                    var dx = c1.X - c2.X;
                    var dy = c1.Y - c2.Y;
                    var dist = Math.Sqrt(dx * dx + dy * dy);

                    // Singularity protection (exact overlap of two nodes)
                    if (dist < 0.001)
                    {
                        dx = (i + 1) * 0.1;
                        dy = (j + 1) * 0.1;
                        dist = Math.Sqrt(dx * dx + dy * dy);
                    }

                    var repForce = kSquared / dist;
                    netFx += (dx / dist) * repForce;
                    netFy += (dy / dist) * repForce;
                }

                dispX[i] = netFx;
                dispY[i] = netFy;
            });

            // 5b. Elastic attractive forces along active semantic arcs (Hooke's Law)
            // Hooke's formula: F_att = (dist^2 / k) * weight
            foreach (var (srcIdx, tgtIdx, weight) in containerEdges)
            {
                var c1 = containers[srcIdx];
                var c2 = containers[tgtIdx];
                var dx = c1.X - c2.X;
                var dy = c1.Y - c2.Y;
                var dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist < 0.001) continue;

                var attForce = (dist * dist / k) * weight;
                var fx = (dx / dist) * attForce;
                var fy = (dy / dist) * attForce;

                // Connected nodes attract each other
                dispX[srcIdx] -= fx;
                dispY[srcIdx] -= fy;
                dispX[tgtIdx] += fx;
                dispY[tgtIdx] += fy;
            }

            // 5c. Centripetal gravitational force towards the origin (0, 0)
            // Prevents disconnected nodes or edge clusters from drifting away indefinitely
            for (var i = 0; i < n; i++)
            {
                var c = containers[i];
                var dist = Math.Sqrt(c.X * c.X + c.Y * c.Y);
                if (dist > 1.0)
                {
                    dispX[i] -= (c.X / dist) * (dist * 0.05);
                    dispY[i] -= (c.Y / dist) * (dist * 0.05);
                }
            }

            // 5d. Application of movements limited by the temperature threshold (Annealing)
            for (var i = 0; i < n; i++)
            {
                var dLen = Math.Sqrt(dispX[i] * dispX[i] + dispY[i] * dispY[i]);
                if (dLen > 0.001)
                {
                    var step = Math.Min(dLen, temp);
                    containers[i].X += (dispX[i] / dLen) * step;
                    containers[i].Y += (dispY[i] / dLen) * step;
                }
            }

            // 5e. Progressive cooling of the system for smooth convergence
            temp *= coolingFactor;
        }

        // =========================================================================
        // STEP 6: Positioning satellite members around their respective containers
        // =========================================================================
        foreach (var container in containers)
        {
            var members = graph.GetChildren(container.Id).ToList();
            if (members.Count == 0) continue;
            var memberCount = members.Count;
            var memberRingRadius = Math.Max(80.0, memberCount * 13.0);

            for (var j = 0; j < memberCount; j++)
            {
                var memberAngle = 2 * Math.PI * j / memberCount;
                var member = members[j];
                member.X = container.X + Math.Cos(memberAngle) * memberRingRadius;
                member.Y = container.Y + Math.Sin(memberAngle) * memberRingRadius;
            }
        }

        // =========================================================================
        // STEP 7: Positioning the Project nodes at the geometric centroid of the modules
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
