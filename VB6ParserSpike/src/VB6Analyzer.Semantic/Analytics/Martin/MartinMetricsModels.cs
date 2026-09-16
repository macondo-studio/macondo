using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.Martin;

/// <summary>
/// Zona concettuale del diagramma di Martin (Robert C. Martin / Uncle Bob).
/// </summary>
public enum MartinZone
{
    /// <summary>
    /// Componenti bilanciati, posizionati lungo la Sequenza Principale (A + I ≈ 1).
    /// </summary>
    MainSequence,

    /// <summary>
    /// Zona di Sofferenza (Zone of Pain): Bassa astrattezza (A basso) e alta stabilità (I basso, molti dipendenti).
    /// Tipico dei God Object / Moduli monolitici concreti difficilissimi da modificare.
    /// </summary>
    ZoneOfPain,

    /// <summary>
    /// Zona di Inutilità (Zone of Uselessness): Alta astrattezza (A alto) e alta instabilità (I alto, nessuno ne dipende).
    /// Astrazioni sovra-ingegnerizzate o interfacce orfane.
    /// </summary>
    ZoneOfUselessness,

    /// <summary>
    /// Zona intermedia bilanciata non critica.
    /// </summary>
    Balanced
}

/// <summary>
/// Singolo componente analizzato nel Diagramma di Martin (Modulo, Classe, Form, Interfaccia, Controllo).
/// </summary>
public sealed class MartinMetricItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string QualifiedName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public SymbolKind Kind { get; init; }

    /// <summary>Accoppiamento Afferente (Ca): Numero di componenti esterni che dipendono da questo modulo.</summary>
    public int AfferentCoupling { get; set; }

    /// <summary>Accoppiamento Efferente (Ce): Numero di componenti esterni da cui questo modulo dipende.</summary>
    public int EfferentCoupling { get; set; }

    /// <summary>Instabilità (I = Ce / (Ca + Ce)): Varia da 0.0 (massima stabilità) a 1.0 (massima instabilità).</summary>
    public double Instability { get; set; }

    /// <summary>Numero di elementi astratti (interfacce, metodi astratti, firme vuote/pure).</summary>
    public int AbstractSymbolsCount { get; set; }

    /// <summary>Numero di elementi concreti (metodi implementati, campi, proprietà concrete).</summary>
    public int ConcreteSymbolsCount { get; set; }

    /// <summary>Astrattezza (A = Na / (Na + Nc)): Varia da 0.0 (puramente concreto) a 1.0 (puramente astratto).</summary>
    public double Abstractness { get; set; }

    /// <summary>Distanza normalizzata dalla Sequenza Principale D = |A + I - 1| / sqrt(2) in [0..1].</summary>
    public double NormalizedDistance { get; set; }

    /// <summary>Distanza assoluta D = |A + I - 1|.</summary>
    public double Distance => Math.Abs(Abstractness + Instability - 1.0);

    /// <summary>Zona architettonica assegnata.</summary>
    public MartinZone Zone { get; set; }

    /// <summary>Linee di codice stimate o numero membri totali.</summary>
    public int LocOrSize { get; set; }
}

/// <summary>
/// Dataset completo delle Metriche di Martin per un progetto o workspace.
/// </summary>
public sealed class MartinMetricsDataset
{
    public List<MartinMetricItem> Items { get; init; } = new();

    public double AverageDistance => Items.Count > 0 ? Items.Average(i => i.NormalizedDistance) : 0.0;
    public int PainZoneCount => Items.Count(i => i.Zone == MartinZone.ZoneOfPain);
    public int UselessnessZoneCount => Items.Count(i => i.Zone == MartinZone.ZoneOfUselessness);
    public int MainSequenceCount => Items.Count(i => i.Zone == MartinZone.MainSequence);
}

/// <summary>
/// Analizzatore agnostico per il calcolo delle metriche di Robert C. Martin (Abstractness, Instability, Distance from Main Sequence).
/// Supporta nativamente sia costrutti VB6 che C#.
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
    /// Esegue il calcolo delle metriche di Martin su tutti i container (moduli/classi/interfacce) del CodeGraph.
    /// </summary>
    public static MartinMetricsDataset Analyze(CodeGraph graph)
    {
        var dataset = new MartinMetricsDataset();
        if (graph.Symbols.Count == 0)
            return dataset;

        // 1. Isola tutti i simboli di tipo Contenitore (File/Classe/Modulo/Interfaccia)
        var containers = graph.Symbols
            .Where(s => ContainerKinds.Contains(s.Kind))
            .ToList();

        if (containers.Count == 0)
            return dataset;

        var containerById = containers.ToDictionary(c => c.Id);

        // Mappa ogni simbolo membro al suo container di appartenenza
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

        // 2. Calcola Afferent (Ca) ed Efferent (Ce) Couplings tra i container
        var afferentSets = containers.ToDictionary(c => c.Id, _ => new HashSet<Guid>());
        var efferentSets = containers.ToDictionary(c => c.Id, _ => new HashSet<Guid>());

        foreach (var rel in graph.Relationships)
        {
            if (rel.Kind is RelationshipKind.Calls or RelationshipKind.Instantiates or RelationshipKind.UsesType or RelationshipKind.Inherits or RelationshipKind.Implements)
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

        // 3. Calcola Abstractness (A), Instability (I) e Distance (D) per ciascun container
        foreach (var c in containers)
        {
            var ce = efferentSets[c.Id].Count;
            var ca = afferentSets[c.Id].Count;
            var totalCoupling = ce + ca;

            var instability = totalCoupling > 0 ? (double)ce / totalCoupling : 0.5;

            var children = graph.GetChildren(c.Id).ToList();
            int totalMembers = children.Count;
            int abstractCount = 0;
            int concreteCount = 0;

            if (c.Kind == SymbolKind.Interface)
            {
                abstractCount = Math.Max(1, totalMembers);
            }
            else
            {
                foreach (var m in children)
                {
                    if (m.Kind is SymbolKind.Method or SymbolKind.Property)
                    {
                        if (m.Kind == SymbolKind.Interface)
                        {
                            abstractCount++;
                        }
                        else
                        {
                            concreteCount++;
                        }
                    }
                    else if (m.Kind is SymbolKind.Field or SymbolKind.Constant)
                    {
                        concreteCount++;
                    }
                }

                if (totalMembers == 0)
                {
                    concreteCount = 1;
                }
            }

            var totalSymbolsForAbs = abstractCount + concreteCount;
            var abstractness = totalSymbolsForAbs > 0 ? (double)abstractCount / totalSymbolsForAbs : 0.0;

            var distanceRaw = Math.Abs(abstractness + instability - 1.0);
            var normalizedDistance = Math.Clamp(distanceRaw / Math.Sqrt(2.0), 0.0, 1.0);

            // Assegna la zona
            MartinZone zone;
            if (abstractness < 0.35 && instability < 0.35)
            {
                zone = MartinZone.ZoneOfPain;
            }
            else if (abstractness > 0.65 && instability > 0.65)
            {
                zone = MartinZone.ZoneOfUselessness;
            }
            else if (distanceRaw <= 0.3)
            {
                zone = MartinZone.MainSequence;
            }
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
