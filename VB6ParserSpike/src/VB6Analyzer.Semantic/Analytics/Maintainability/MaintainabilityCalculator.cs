using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.Maintainability;

/// <summary>
/// Calcolatore poliglotto del Maintainability Index (MI) e delle metriche di Halstead.
/// Conforme allo standard Microsoft Code Metrics e Carnegie Mellon SEI.
/// </summary>
public static class MaintainabilityCalculator
{
    private static readonly HashSet<SymbolKind> ContainerKinds = new()
    {
        SymbolKind.Class,
        SymbolKind.Module,
        SymbolKind.Form,
        SymbolKind.UserControl,
        SymbolKind.Struct,
        SymbolKind.Record
    };

    /// <summary>
    /// Esegue il calcolo dell'indice di manutenibilità e delle metriche di Halstead su tutti i contenitori del CodeGraph.
    /// </summary>
    public static MaintainabilityDataset Analyze(CodeGraph graph)
    {
        var dataset = new MaintainabilityDataset();
        if (graph.Symbols.Count == 0)
            return dataset;

        var containers = graph.Symbols.Where(s => ContainerKinds.Contains(s.Kind)).ToList();
        if (containers.Count == 0)
            return dataset;

        foreach (var container in containers)
        {
            var children = graph.GetChildren(container.Id).ToList();
            int loc = Math.Max(1, container.LineEnd - container.LineStart + 1);

            int methodCount = children.Count(c => c.Kind == SymbolKind.Method || c.Kind == SymbolKind.Property);
            int fieldCount = children.Count(c => c.Kind == SymbolKind.Field || c.Kind == SymbolKind.Constant);

            // Stima delle chiamate effettuate o ricevute
            int callsCount = graph.Relationships.Count(r => r.Kind == RelationshipKind.Calls && (r.SourceId == container.Id || children.Any(c => c.Id == r.SourceId)));

            // Stima della complessità ciclomatica aggregata
            int cyclomaticComplexity = Math.Max(1, methodCount + (callsCount / 2));

            // Stima o calcolo dei parametri di Halstead
            // Operatori tipici: assegnazioni, chiamate, if/select, cicli, return
            // Operandi tipici: identificatori, variabili, costanti, parametri
            int distinctOperators = Math.Max(4, 5 + (methodCount > 0 ? (int)Math.Log2(methodCount + 1) * 2 : 1));
            int distinctOperands = Math.Max(4, fieldCount + methodCount + 2);
            int totalOperators = Math.Max(distinctOperators, (loc * 3) / 2 + callsCount * 2);
            int totalOperands = Math.Max(distinctOperands, loc + (fieldCount * 3));

            var halstead = new HalsteadMetrics
            {
                DistinctOperators = distinctOperators,
                DistinctOperands = distinctOperands,
                TotalOperators = totalOperators,
                TotalOperands = totalOperands
            };

            double volume = halstead.Volume;
            double rawMi = 171.0 - (5.2 * Math.Log(Math.Max(1.0, volume))) - (0.23 * cyclomaticComplexity) - (16.2 * Math.Log(Math.Max(1.0, loc)));
            double normalizedMi = Math.Clamp((rawMi * 100.0) / 171.0, 0.0, 100.0);

            var item = new MaintainabilityItem
            {
                Id = container.Id,
                Name = container.Name,
                QualifiedName = container.QualifiedName,
                FilePath = container.FilePath,
                Kind = container.Kind,
                Language = container.Language,
                LinesOfCode = loc,
                CyclomaticComplexity = cyclomaticComplexity,
                Halstead = halstead,
                MaintainabilityIndex = Math.Round(normalizedMi, 1)
            };

            dataset.Items.Add(item);
        }

        dataset.Items.Sort((a, b) => a.MaintainabilityIndex.CompareTo(b.MaintainabilityIndex));
        return dataset;
    }
}
