using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.Treemap;

/// <summary>
/// Costruttore del modello Treemap a partire da qualsiasi CodeGraph semantico.
/// </summary>
public static class TreemapModelBuilder
{
    /// <summary>
    /// Costruisce l'albero gerarchico <see cref="TreemapItem"/> a due livelli (Progetto -&gt; Moduli -&gt; Membri) aggregando LOC e complessità.
    /// </summary>
    /// <param name="graph">Il CodeGraph da cui estrarre i simboli.</param>
    /// <returns>Il nodo radice della Treemap pronto per il calcolo del layout.</returns>
    public static TreemapItem Build(CodeGraph graph)
    {
        // 1. Nodo radice globale dell'intero progetto
        var root = new TreemapItem
        {
            Id = Guid.Empty,
            Name = "Root",
            QualifiedName = "Project",
            Kind = SymbolKind.Project,
            Depth = 0
        };

        // 2. Raccoglie tutti i container primari (Classi, Form, Moduli, UserControl, UDT, Interfacce, Strutture, Record)
        var containers = graph.Symbols
            .Where(s => s.Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.Module or SymbolKind.UserControl or SymbolKind.Type or SymbolKind.Interface or SymbolKind.Struct or SymbolKind.Record)
            .OrderBy(c => c.Name)
            .ToList();

        if (containers.Count == 0)

            containers = graph.Symbols.Where(s => s.Kind != SymbolKind.Project).ToList();

        // Indicizzazione O(1) delle chiamate uscenti per ogni simbolo sorgente per evitare scansioni O(R) nel loop
        var outgoingCallsCount = new Dictionary<Guid, int>();
        foreach (var r in graph.Relationships)
        {
            if (r.Kind == RelationshipKind.Calls)

                outgoingCallsCount[r.SourceId] = outgoingCallsCount.GetValueOrDefault(r.SourceId) + 1;
        }

        // 3. Costruisce l'albero dei figli e calcola le metriche di peso (LOC stimate) e complessità
        foreach (var container in containers)
        {
            var containerItem = new TreemapItem
            {
                Id = container.Id,
                Name = container.Name,
                QualifiedName = container.QualifiedName,
                Kind = container.Kind,
                Depth = 1,
                Parent = root
            };

            var children = graph.GetChildren(container.Id)
                .Where(c => c.Kind is SymbolKind.Method or SymbolKind.Property or SymbolKind.Field)
                .ToList();

            if (children.Count > 0)
            {
                foreach (var child in children)
                {
                    // Calcolo LOC o peso basato sulle righe di codice o complessità delle chiamate in O(1)
                    int lineCount = Math.Max(1, child.LineEnd - child.LineStart + 1);
                    outgoingCallsCount.TryGetValue(child.Id, out int callees);
                    double complexityScore = 1.0 + (callees * 1.5) + (child.Parameters.Count * 0.8);

                    var childItem = new TreemapItem
                    {
                        Id = child.Id,
                        Name = child.Name,
                        QualifiedName = child.QualifiedName,
                        Kind = child.Kind,
                        Value = lineCount,
                        MetricScore = complexityScore,
                        Depth = 2,
                        Parent = containerItem
                    };

                    containerItem.Children.Add(childItem);
                }

                containerItem.Value = containerItem.Children.Sum(c => c.Value);
                containerItem.MetricScore = containerItem.Children.Average(c => c.MetricScore);
            }
            else
            {
                int lineCount = Math.Max(1, container.LineEnd - container.LineStart + 1);
                containerItem.Value = lineCount;
                containerItem.MetricScore = 1.0;
            }

            root.Children.Add(containerItem);
        }

        // Aggrega i totali sulla radice
        root.Value = root.Children.Sum(c => c.Value);
        root.MetricScore = root.Children.Count > 0 ? root.Children.Average(c => c.MetricScore) : 1.0;

        return root;
    }
}
