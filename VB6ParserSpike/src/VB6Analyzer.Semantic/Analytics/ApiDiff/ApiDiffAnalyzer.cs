using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.ApiDiff;

/// <summary>
/// Analizzatore poliglotto di API Surface Diff e Breaking Change Detection (conforme alle specifiche SemVer 2.0.0).
/// Confronta due stati del <see cref="CodeGraph"/> (es. snapshot baseline/release precedente vs snapshot corrente)
/// per identificare modifiche retrocompatibili e rotture di compatibilità binarie/sorgenti.
/// 
/// CLASSIFICAZIONE DELLE MODIFICHE:
/// 1. Major Breaking Change: rimozione di simboli pubblici, riduzione dell'accessibilità (es. Public -> Private/Internal),
///    modifica del numero di parametri obbligatori in metodi o costruttori.
/// 2. Minor Feature: aggiunta di nuovi tipi, metodi o costruttori pubblici, oppure aggiunta di parametri opzionali.
/// 3. Patch / Neutral: modifiche interne che non impattano la superficie API pubblica esportata.
/// </summary>
public static class ApiDiffAnalyzer
{
    /// <summary>
    /// Insieme dei livelli di visibilità che costituiscono l'API pubblica esportata dal modulo o assembly.
    /// In VB6, nei file .bas le routine senza modificatore esplicito sono Public per default.
    /// </summary>
    private static readonly HashSet<Accessibility> PublicAccessibilities = new()
    {
        Accessibility.Public,
        Accessibility.Global,
        Accessibility.Protected,
        Accessibility.ProtectedInternal,
        Accessibility.Default
    };

    /// <summary>
    /// Confronta la superficie pubblica esportata tra due snapshot di CodeGraph e produce il report dettagliato SemVer.
    /// </summary>
    /// <param name="baselineGraph">Grafo dello snapshot di riferimento (es. versione rilasciata precedente).</param>
    /// <param name="currentGraph">Grafo dello snapshot corrente (es. codice modificato o branch di sviluppo).</param>
    /// <returns>Il report strutturato <see cref="ApiDiffReport"/> con tutte le differenze ordinate per gravità d'impatto.</returns>
    public static ApiDiffReport Compare(CodeGraph baselineGraph, CodeGraph currentGraph)
    {
        var report = new ApiDiffReport();

        // 1. Isola i simboli pubblici di ciascun grafo indicizzandoli per nome qualificato univoco
        var oldPublic = baselineGraph.Symbols
            .Where(s => IsPublicApi(s))
            .ToDictionary(s => s.QualifiedName, StringComparer.OrdinalIgnoreCase);

        var newPublic = currentGraph.Symbols
            .Where(s => IsPublicApi(s))
            .ToDictionary(s => s.QualifiedName, StringComparer.OrdinalIgnoreCase);

        // 2. Rileva simboli rimossi dall'API pubblica o con visibilità ridotta (BREAKING CHANGE)
        foreach (var (qName, oldSym) in oldPublic)
        {
            if (!newPublic.TryGetValue(qName, out var newSym))
            {
                // Verifica se il simbolo esiste ancora nel grafo attuale ma con accessibilità declassata (es. Private/Internal)
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
                    // Il simbolo è stato eliminato completamente dal codice sorgente
                    report.Differences.Add(new ApiDifferenceItem
                    {
                        SymbolQualifiedName = qName,
                        SymbolKind = oldSym.Kind,
                        ChangeKind = ApiChangeKind.RemovedSymbol,
                        Impact = ApiChangeImpact.MajorBreaking,
                        Description = $"Rottura di compatibilità API: il simbolo pubblico '{qName}' è stato eliminato dall'interfaccia esportata.",
                        OldSymbol = oldSym
                    });
                }
            }
            else
            {
                // Il simbolo è presente in entrambe le versioni: confronta le firme dei parametri per rilevare modifiche
                CompareSignatures(oldSym, newSym, report);
            }
        }

        // 3. Rileva nuovi simboli pubblici aggiunti nel grafo corrente (MINOR FEATURE retrocompatibile)
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

        // 4. Ordina le discrepanze ponendo in cima i Breaking Changes critici seguiti dalle modifiche minori
        report.Differences.Sort((a, b) => b.Impact.CompareTo(a.Impact));
        return report;
    }

    /// <summary>
    /// Confronta la lista dei parametri di una routine tra vecchia e nuova versione per determinare se la variazione è breaking.
    /// Se il numero di parametri obbligatori cambia o diminuisce, viene segnalato un Major Breaking Change.
    /// </summary>
    private static void CompareSignatures(Symbol oldSym, Symbol newSym, ApiDiffReport report)
    {
        if (oldSym.Kind is not (SymbolKind.Method or SymbolKind.Property))
            return;

        int oldParamCount = oldSym.Parameters.Count;
        int newParamCount = newSym.Parameters.Count;

        if (oldParamCount != newParamCount)
        {
            // Se sono stati rimossi parametri, oppure aggiunti parametri senza clausola Optional/valore di default, è un breaking change
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

    /// <summary>
    /// Verifica se un simbolo fa parte della superficie pubblica dell'API esportata.
    /// Esclude simboli di sistema o aggregatori (Project, Namespace) e controlla l'accessibilità effettiva.
    /// </summary>
    private static bool IsPublicApi(Symbol symbol)
    {
        if (symbol.Kind is SymbolKind.Project or SymbolKind.Namespace)
            return false;

        return PublicAccessibilities.Contains(symbol.Accessibility);
    }
}

