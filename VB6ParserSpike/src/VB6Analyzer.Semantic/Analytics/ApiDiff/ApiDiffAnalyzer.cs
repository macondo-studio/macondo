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
    /// Genera una chiave di firma univoca per il simbolo esportato, includendo tipo, nome qualificato e parametri formali.
    /// Questo consente di distinguere sovraccarichi (overloads) di metodi o costruttori ed evitare collisioni.
    /// </summary>
    private static string GetApiSignatureKey(Symbol sym)
    {
        if (sym.Kind is SymbolKind.Method or SymbolKind.Property && sym.Parameters.Count > 0)
        {
            var paramSignature = string.Join(",", sym.Parameters.Select(p => p.DeclaredTypeName ?? "Variant"));
            return $"{sym.Kind}:{sym.QualifiedName}({paramSignature})";
        }

        return $"{sym.Kind}:{sym.QualifiedName}";
    }

    /// <summary>
    /// Confronta la superficie pubblica esportata tra due snapshot di CodeGraph e produce il report dettagliato SemVer.
    /// </summary>
    /// <param name="baselineGraph">Grafo dello snapshot di riferimento (es. versione rilasciata precedente).</param>
    /// <param name="currentGraph">Grafo dello snapshot corrente (es. codice modificato o branch di sviluppo).</param>
    /// <returns>Il report strutturato <see cref="ApiDiffReport"/> con tutte le differenze ordinate per gravità d'impatto.</returns>
    public static ApiDiffReport Compare(CodeGraph baselineGraph, CodeGraph currentGraph)
    {
        var report = new ApiDiffReport();

        // =========================================================================
        // PASSO 1: Indicizzazione robusta delle API pubbliche nei due snapshot
        // Isola i simboli pubblici (Classi, Interfacce, Metodi, Proprietà, Costanti)
        // indicizzandoli per chiave di firma univoca per supportare sovraccarichi
        // e classi parziali senza incorrere in ArgumentException su chiavi duplicate.
        // =========================================================================
        var oldPublic = new Dictionary<string, Symbol>(StringComparer.OrdinalIgnoreCase);
        foreach (var sym in baselineGraph.Symbols.Where(IsPublicApi))
        {
            var key = GetApiSignatureKey(sym);
            oldPublic.TryAdd(key, sym);
        }

        var newPublic = new Dictionary<string, Symbol>(StringComparer.OrdinalIgnoreCase);
        foreach (var sym in currentGraph.Symbols.Where(IsPublicApi))
        {
            var key = GetApiSignatureKey(sym);
            newPublic.TryAdd(key, sym);
        }

        var processedNewKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // =========================================================================
        // PASSO 2: Rilevamento rimozioni, declassamenti di visibilità e modifiche di firma
        // Itera su tutti i simboli pubblici della versione precedente (baseline).
        // =========================================================================
        foreach (var (key, oldSym) in oldPublic)
        {
            if (newPublic.TryGetValue(key, out var newSym))
            {
                processedNewKeys.Add(key);
                // Il simbolo con la stessa firma esiste in entrambe le versioni
                CompareSignatures(oldSym, newSym, report);
            }
            else
            {
                // 2a. Verifica se il simbolo esiste ancora nel grafo attuale ma con visibilità ridotta (es. Public -> Private/Internal)
                var stillExistsPrivate = currentGraph.Symbols.FirstOrDefault(s =>
                    s.QualifiedName.Equals(oldSym.QualifiedName, StringComparison.OrdinalIgnoreCase) &&
                    s.Kind == oldSym.Kind &&
                    !IsPublicApi(s));

                if (stillExistsPrivate != null)
                {
                    report.Differences.Add(new ApiDifferenceItem
                    {
                        SymbolQualifiedName = oldSym.QualifiedName,
                        SymbolKind = oldSym.Kind,
                        ChangeKind = ApiChangeKind.ChangedAccessibility,
                        Impact = ApiChangeImpact.MajorBreaking,
                        Description = $"Breaking API change: public symbol '{oldSym.QualifiedName}' reduced its visibility to '{stillExistsPrivate.Accessibility}'.",
                        OldSymbol = oldSym,
                        NewSymbol = stillExistsPrivate
                    });
                }
                else
                {
                    // 2b. Verifica se è una modifica di firma per un metodo con lo stesso QualifiedName
                    var sameNameCandidate = newPublic.Values.FirstOrDefault(s =>
                        s.QualifiedName.Equals(oldSym.QualifiedName, StringComparison.OrdinalIgnoreCase) &&
                        s.Kind == oldSym.Kind &&
                        !processedNewKeys.Contains(GetApiSignatureKey(s)));

                    if (sameNameCandidate != null)
                    {
                        processedNewKeys.Add(GetApiSignatureKey(sameNameCandidate));
                        CompareSignatures(oldSym, sameNameCandidate, report);
                    }
                    else
                    {
                        // 2c. Il simbolo è stato rimosso integralmente
                        report.Differences.Add(new ApiDifferenceItem
                        {
                            SymbolQualifiedName = oldSym.QualifiedName,
                            SymbolKind = oldSym.Kind,
                            ChangeKind = ApiChangeKind.RemovedSymbol,
                            Impact = ApiChangeImpact.MajorBreaking,
                            Description = $"Breaking API change: public symbol '{oldSym.QualifiedName}' was removed from the exported interface.",
                            OldSymbol = oldSym
                        });
                    }
                }
            }
        }

        // =========================================================================
        // PASSO 3: Rilevamento nuove API pubbliche (Minor Feature retrocompatibile)
        // Itera sui simboli pubblici correnti assenti nella baseline.
        // =========================================================================
        foreach (var (key, newSym) in newPublic)
        {
            if (!processedNewKeys.Contains(key))
            {
                report.Differences.Add(new ApiDifferenceItem
                {
                    SymbolQualifiedName = newSym.QualifiedName,
                    SymbolKind = newSym.Kind,
                    ChangeKind = ApiChangeKind.AddedSymbol,
                    Impact = ApiChangeImpact.Minor,
                    Description = $"Backward-compatible public API added: '{newSym.QualifiedName}' ({newSym.Kind}).",
                    NewSymbol = newSym
                });
            }
        }

        // =========================================================================
        // PASSO 4: Ordinamento per gravità decrescente (Major Breaking -> Minor -> Patch)
        // =========================================================================
        report.Differences.Sort((a, b) => b.Impact.CompareTo(a.Impact));
        return report;
    }

    /// <summary>
    /// Confronta la lista dei parametri di una routine tra vecchia e nuova versione per determinare se la variazione è breaking.
    /// Se il numero di parametri obbligatori cambia o diminuisce, viene segnalato un Major Breaking Change.
    /// </summary>
    private static void CompareSignatures(Symbol oldSym, Symbol newSym, ApiDiffReport report)
    {
        // Limita il confronto alle routine con parametri formali (Metodi o Proprietà)
        if (oldSym.Kind is not (SymbolKind.Method or SymbolKind.Property))
            return;

        int oldParamCount = oldSym.Parameters.Count;
        int newParamCount = newSym.Parameters.Count;

        if (oldParamCount != newParamCount)
        {
            // Se sono stati rimossi parametri, oppure aggiunti nuovi parametri senza clausola Optional/valore di default, è un breaking change
            bool isBreaking = newParamCount < oldParamCount || newSym.Parameters.Skip(oldParamCount).Any(p => !p.IsOptional);

            report.Differences.Add(new ApiDifferenceItem
            {
                SymbolQualifiedName = oldSym.QualifiedName,
                SymbolKind = oldSym.Kind,
                ChangeKind = ApiChangeKind.ChangedParameters,
                Impact = isBreaking ? ApiChangeImpact.MajorBreaking : ApiChangeImpact.Minor,
                Description = isBreaking
                    ? $"Signature break: method '{oldSym.QualifiedName}' changed required parameters count from {oldParamCount} to {newParamCount}."
                    : $"Backward-compatible change: optional parameters added to method '{oldSym.QualifiedName}'.",
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
        // Esclude i nodi macro di progetto e namespace
        if (symbol.Kind is SymbolKind.Project or SymbolKind.Namespace)
            return false;

        return PublicAccessibilities.Contains(symbol.Accessibility);
    }
}
