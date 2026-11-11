using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.Martin;

/// <summary>
/// Agnostic analyzer for calculating metrics by Robert C. Martin (Abstractness, Instability, Distance from Main Sequence).
/// It natively supports both VB6 and C# constructs.
/// </summary>
public static class MartinMetricsCalculator
{
    private static readonly HashSet<SymbolKind> ContainerKinds = new()
    {
        SymbolKind.Class,
        SymbolKind.Module,
        SymbolKind.Form,
        SymbolKind.UserControl,
        SymbolKind.Interface,
        SymbolKind.Record,
        SymbolKind.Struct
    };

    /// <summary>
    /// Performs Martin metrics calculation on all containers (modules/classes/interfaces) of the CodeGraph.
    /// </summary>
    public static MartinMetricsDataset Analyze(CodeGraph graph)
    {
        var dataset = new MartinMetricsDataset();
        if (graph.Symbols.Count == 0)
            return dataset;

        // 1. Isolate all symbols of type Container (File/Class/Module/Interface)
        var containers = graph.Symbols
            .Where(s => ContainerKinds.Contains(s.Kind))
            .ToList();

        if (containers.Count == 0)
            return dataset;

        var containerById = containers.ToDictionary(c => c.Id);

        // Map each member symbol to its parent container
        var memberToContainerMap = new Dictionary<Guid, Guid>();
        foreach (var c in containers)
        {
            memberToContainerMap[c.Id] = c.Id;
            var children = graph.GetChildren(c.Id);
            foreach (var child in children)
            {
                memberToContainerMap[child.Id] = c.Id;
            }
        }

        // 2. Calculate Afferent (Ca) and Efferent (Ce) Couplings between containers
        var afferentSets = containers.ToDictionary(c => c.Id, _ => new HashSet<Guid>());
        var efferentSets = containers.ToDictionary(c => c.Id, _ => new HashSet<Guid>());

        foreach (var rel in graph.Relationships)
        {
            // Consider any semantic dependencies (calls, instantiations, types, event handling, constant/field reads)
            if (rel.Kind != RelationshipKind.Contains && rel.Kind != RelationshipKind.Declares)
            {
                if (memberToContainerMap.TryGetValue(rel.SourceId, out var srcContId) &&
                    memberToContainerMap.TryGetValue(rel.TargetId, out var tgtContId))
                {
                    if (srcContId != tgtContId)
                    {
                        efferentSets[srcContId].Add(tgtContId);
                        afferentSets[tgtContId].Add(srcContId);
                    }
                }
            }
        }

        // 3. Calculate Abstractness (A), Instability (I) and Distance (D) for each container
        foreach (var c in containers)
        {
            var ce = efferentSets[c.Id].Count;
            var ca = afferentSets[c.Id].Count;
            var totalCoupling = ce + ca;

            var instability = totalCoupling > 0 ? (double)ce / totalCoupling : 0.0;

            var children = graph.GetChildren(c.Id).ToList();
            int totalMembers = children.Count;
            int abstractCount = 0;
            int concreteCount = 0;

            if (c.Kind == SymbolKind.Interface)

                abstractCount = Math.Max(1, totalMembers);

            else
            {
                foreach (var m in children)
                {
                    if (m.Kind is SymbolKind.Method or SymbolKind.Property)
                    {
                        if (m.Kind == SymbolKind.Interface)

                            abstractCount++;

                        else
                        {
                            concreteCount++;
                        }
                    }
                    else if (m.Kind is SymbolKind.Field or SymbolKind.Constant)

    concreteCount++;
                }

                if (totalMembers == 0)

                    concreteCount = 1;
            }

            var totalSymbolsForAbs = abstractCount + concreteCount;
            var abstractness = totalSymbolsForAbs > 0 ? (double)abstractCount / totalSymbolsForAbs : 0.0;

            var distanceRaw = Math.Abs(abstractness + instability - 1.0);
            var normalizedDistance = Math.Clamp(distanceRaw, 0.0, 1.0);

            // Assign the zone
            MartinZone zone;
            if (abstractness < 0.35 && instability < 0.35)

                zone = MartinZone.ZoneOfPain;

            else if (abstractness > 0.65 && instability > 0.65)

    zone = MartinZone.ZoneOfUselessness;

else if (distanceRaw <= 0.3)

    zone = MartinZone.MainSequence;

else
            {
                zone = MartinZone.Balanced;
            }

            int estimatedLoc = Math.Max(1, c.LineEnd - c.LineStart + 1);

            dataset.Items.Add(new MartinMetricItem
            {
                Id = c.Id,
                Name = c.Name,
                QualifiedName = c.QualifiedName,
                FilePath = c.FilePath,
                Kind = c.Kind,
                AfferentCoupling = ca,
                EfferentCoupling = ce,
                Instability = instability,
                AbstractSymbolsCount = abstractCount,
                ConcreteSymbolsCount = concreteCount,
                Abstractness = abstractness,
                NormalizedDistance = normalizedDistance,
                Zone = zone,
                LocOrSize = estimatedLoc
            });
        }

        dataset.Items.Sort((a, b) => b.NormalizedDistance.CompareTo(a.NormalizedDistance));
        return dataset;
    }
}
