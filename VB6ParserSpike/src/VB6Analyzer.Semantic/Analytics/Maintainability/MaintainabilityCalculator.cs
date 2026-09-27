using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.Maintainability;

/// <summary>
/// Calcolatore poliglotto del Maintainability Index (MI) e delle metriche di Halstead Software Science.
/// Conforme allo standard di riferimento Microsoft Code Metrics e Carnegie Mellon Software Engineering Institute (SEI).
/// 
/// FORMULA DEL MAINTAINABILITY INDEX (MI):
/// MI_raw = 171 - 5.2 * ln(Halstead_Volume) - 0.23 * Cyclomatic_Complexity - 16.2 * ln(Lines_of_Code)
/// MI_norm = Clamp((MI_raw * 100) / 171, 0, 100)
/// 
/// SOGLIE DI VALUTAZIONE:
/// - 80 - 100: Alta manutenibilità (Verde).
/// - 60 - 79:  Media manutenibilità / Rischio moderato (Giallo).
/// - 0  - 59:  Bassa manutenibilità / Alto debito tecnico e candidato urgente al refactoring (Rosso).
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
    /// Combina complessità ciclomatica di McCabe, volume di Halstead e linee di codice sorgente (LOC).
    /// </summary>
    /// <param name="graph">Il CodeGraph contenente simboli e relazioni del progetto.</param>
    /// <returns>Il dataset <see cref="MaintainabilityDataset"/> con tutti gli indici calcolati e ordinati per criticità crescente.</returns>
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

            // -----------------------------------------------------------------
            // FASE 1: Stima delle chiamate effettuate dai metodi interni del container
            // -----------------------------------------------------------------
            int callsCount = graph.Relationships.Count(r => r.Kind == RelationshipKind.Calls && (r.SourceId == container.Id || children.Any(c => c.Id == r.SourceId)));

            // -----------------------------------------------------------------
            // FASE 2: Stima della complessità ciclomatica aggregata (McCabe)
            // -----------------------------------------------------------------
            int cyclomaticComplexity = Math.Max(1, methodCount + (callsCount / 2));

            // -----------------------------------------------------------------
            // FASE 3: Stima dei parametri di Halstead Software Science:
            // -----------------------------------------------------------------
            // - Operatori distinti (n1) e totali (N1): parole chiave, assegnazioni, chiamate, ramificazioni
            // - Operandi distinti (n2) e totali (N2): identificatori, variabili, costanti, parametri
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

            // -----------------------------------------------------------------
            // FASE 4: Calcolo del Volume di Halstead e applicazione formula SEI / Microsoft
            // -----------------------------------------------------------------
            // MI_raw = 171 - 5.2 * ln(V) - 0.23 * CC - 16.2 * ln(LOC)
            // MI_norm = Clamp((MI_raw * 100) / 171, 0, 100)
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

        // ---------------------------------------------------------------------
        // FASE 5: Ordinamento per Maintainability Index crescente (più problematici in cima)
        // ---------------------------------------------------------------------
        dataset.Items.Sort((a, b) => a.MaintainabilityIndex.CompareTo(b.MaintainabilityIndex));
        return dataset;
    }
}

