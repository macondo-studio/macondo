using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.Maintainability;

/// <summary>
/// Multilingual Maintainability Index (MI) and metrics calculator from Halstead Software Science.
/// Complies with the Microsoft Code Metrics and Carnegie Mellon Software Engineering Institute (SEI) reference standard.
/// 
/// MAINTAINABILITY INDEX FORMULA (MI):
/// MI_raw = 171 - 5.2 * ln(Halstead_Volume) - 0.23 * Cyclomatic_Complexity - 16.2 * ln(Lines_of_Code)
/// MI_norm = Clamp((MI_raw * 100) / 171, 0, 100)
/// 
/// ASSESSMENT THRESHOLDS:
/// - 80 - 100: Alta manutenibilità (Verde).
/// - 60 - 79:  Media manutenibilità / Rischio moderato (Giallo).
/// - 0 - 59: Low maintainability / High technical debt and urgent candidate for refactoring (Red).
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
    /// Performs the calculation of the maintainability index and Halstead metrics on all containers in the CodeGraph.
    /// It combines McCabe cyclomatic complexity, Halstead volume, and lines of source code (LOC).
    /// </summary>
    /// <param name="graph">The CodeGraph containing project symbols and relationships.</param>
    /// <returns>The <see cref="MaintainabilityDataset"/> dataset with all the indices calculated and ordered by increasing criticality.</returns>
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
            // PHASE 1: Estimate the calls made by the container's internal methods
            // -----------------------------------------------------------------
            int callsCount = graph.Relationships.Count(r => r.Kind == RelationshipKind.Calls && (r.SourceId == container.Id || children.Any(c => c.Id == r.SourceId)));

            // -----------------------------------------------------------------
            // PHASE 2: Estimation of aggregate cyclomatic complexity (McCabe)
            // -----------------------------------------------------------------
            int cyclomaticComplexity = Math.Max(1, methodCount + (callsCount / 2));

            // -----------------------------------------------------------------
            // STEP 3: Halstead Software Science Parameter Estimation:
            // -----------------------------------------------------------------
            // - Distinct (n1) and total (N1) operators: keywords, assignments, calls, branches
            // - Distinct (n2) and total (N2) operands: identifiers, variables, constants, parameters
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
            // PHASE 4: Calculation of the Halstead Volume and application of the SEI / Microsoft formula
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
        // STEP 5: Sort by Maintainability Index ascending (most problematic at the top)
        // ---------------------------------------------------------------------
        dataset.Items.Sort((a, b) => a.MaintainabilityIndex.CompareTo(b.MaintainabilityIndex));
        return dataset;
    }
}

