using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.ApiDiff;

/// <summary>
/// Analizzatore poliglotto di API Surface Diff e Breaking Change Detection (SemVer Compliance).
/// Confronta due stati del CodeGraph (es. baseline vs current o prima/dopo modifiche).
/// </summary>
public static class ApiDiffAnalyzer
{
    private static readonly HashSet<Accessibility> PublicAccessibilities = new()
    {
        Accessibility.Public,
        Accessibility.Global,
        Accessibility.Protected,
        Accessibility.ProtectedInternal,
        Accessibility.Default // in VB6 .bas i metodi non marcati sono public
    };

    /// <summary>
    /// Confronta la superficie pubblica esportata tra due snapshot di CodeGraph e produce il report SemVer.
    /// </summary>
    public static ApiDiffReport Compare(CodeGraph baselineGraph, CodeGraph currentGraph)
    {
        var report = new ApiDiffReport();

        // 1. Isola i simboli pubblici di ciascun grafo
        var oldPublic = baselineGraph.Symbols
            .Where(s => IsPublicApi(s))
            .ToDictionary(s => s.QualifiedName, StringComparer.OrdinalIgnoreCase);

        var newPublic = currentGraph.Symbols
            .Where(s => IsPublicApi(s))
            .ToDictionary(s => s.QualifiedName, StringComparer.OrdinalIgnoreCase);

        // 2. Rileva simboli rimossi dall'API pubblica (BREAKING CHANGE)
        foreach (var (qName, oldSym) in oldPublic)
        {
            if (!newPublic.TryGetValue(qName, out var newSym))
            {
                // Verifica se è stato reso privato o rimosso completamente
                var stillExistsPrivate = currentGraph.Symbols.FirstOrDefault(s => s.QualifiedName.Equals(qName, StringComparison.OrdinalIgnoreCase));
                if (stillExistsPrivate != null)
                {
                    report.Differences.Add(new ApiDifferenceItem
                    {
                        SymbolQualifiedName = qName,
                        SymbolKind = oldSym.Kind,
                        ChangeKind = ApiChangeKind.ChangedAccessibility,
                        Impact = ApiChangeImpact.MajorBreaking,
                        Description = $"Rottura di compatibilità API: il simbolo pubblico '{qName}' ha ridotto la sua visibilità a '{stillExistsPrivate.Accessibility}'.",
                        OldSymbol = oldSym,
                        NewSymbol = stillExistsPrivate
                    });
                }
                else
                {
                    report.Differences.Add(new ApiDifferenceItem
                    {
                        SymbolQualifiedName = qName,
                        SymbolKind = oldSym.Kind,
                        ChangeKind = ApiChangeKind.RemovedSymbol,
                        Impact = ApiChangeImpact.MajorBreaking,
                        Description = $"Rottura di compatibilità API: il simbolo pubblico '{qName}' è stato eliminato dall'interfaccia.",
                        OldSymbol = oldSym
                    });
                }
            }
            else
            {
                // Confronta le firme dei parametri
                CompareSignatures(oldSym, newSym, report);
            }
        }

        // 3. Rileva nuovi simboli pubblici aggiunti (MINOR FEATURE)
        foreach (var (qName, newSym) in newPublic)
        {
            if (!oldPublic.ContainsKey(qName))
            {
                report.Differences.Add(new ApiDifferenceItem
                {
                    SymbolQualifiedName = qName,
                    SymbolKind = newSym.Kind,
                    ChangeKind = ApiChangeKind.AddedSymbol,
                    Impact = ApiChangeImpact.Minor,
                    Description = $"Nuova API pubblica retrocompatibile aggiunta: '{qName}' ({newSym.Kind}).",
                    NewSymbol = newSym
                });
            }
        }

        // Ordina mettendo in cima i Breaking Changes
        report.Differences.Sort((a, b) => b.Impact.CompareTo(a.Impact));
        return report;
    }

    private static void CompareSignatures(Symbol oldSym, Symbol newSym, ApiDiffReport report)
    {
        if (oldSym.Kind is not (SymbolKind.Method or SymbolKind.Property))
            return;

        int oldParamCount = oldSym.Parameters.Count;
        int newParamCount = newSym.Parameters.Count;

        if (oldParamCount != newParamCount)
        {
            // Se sono stati aggiunti parametri senza default/optional è un breaking change
            bool isBreaking = newParamCount < oldParamCount || newSym.Parameters.Skip(oldParamCount).Any(p => !p.IsOptional);

            report.Differences.Add(new ApiDifferenceItem
            {
                SymbolQualifiedName = oldSym.QualifiedName,
                SymbolKind = oldSym.Kind,
                ChangeKind = ApiChangeKind.ChangedParameters,
                Impact = isBreaking ? ApiChangeImpact.MajorBreaking : ApiChangeImpact.Minor,
                Description = isBreaking
                    ? $"Rottura firma: il metodo '{oldSym.QualifiedName}' ha modificato il numero di parametri obbligatori da {oldParamCount} a {newParamCount}."
                    : $"Modifica retrocompatibile: aggiunti parametri opzionali al metodo '{oldSym.QualifiedName}'.",
                OldSymbol = oldSym,
                NewSymbol = newSym
            });
        }
    }

    private static bool IsPublicApi(Symbol symbol)
    {
        if (symbol.Kind is SymbolKind.Project or SymbolKind.Namespace)
            return false;

        return PublicAccessibilities.Contains(symbol.Accessibility);
    }
}
