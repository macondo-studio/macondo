using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.ApiDiff;

/// <summary>
/// Surface Diff and Breaking Change Detection API polyglot analyzer (compliant with SemVer 2.0.0 specifications).
/// Compare two states of the <see cref="CodeGraph"/> (e.g. baseline snapshot/previous release vs current snapshot)
/// to identify backwards compatible changes and binary/source compatibility breaks.
/// 
/// CHANGE CLASSIFICATION:
/// 1. Major Breaking Change: removal of public symbols, reduction of accessibility (e.g. Public -> Private/Internal),
/// changing the number of required parameters in methods or constructors.
/// 2. Minor Feature: Adding new public types, methods, or constructors, or adding optional parameters.
/// 3. Patch / Neutral: Internal changes that do not impact the exported public API surface.
/// </summary>
public static class ApiDiffAnalyzer
{
    /// <summary>
    /// Set of visibility levels that make up the public API exported by the module or assembly.
    /// In VB6, routines without an explicit modifier are Public by default in .bas files.
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
    /// Generates a unique signing key for the exported symbol, including type, qualified name, and formal parameters.
    /// This allows you to distinguish overloads of methods or constructors and avoid collisions.
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
    /// Compares the exported public surface between two CodeGraph snapshots and produces the detailed SemVer report.
    /// </summary>
    /// <param name="baselineGraph">Reference snapshot graph (e.g. previous released version).</param>
    /// <param name="currentGraph">Graph of the current snapshot (e.g. modified code or development branch).</param>
    /// <returns>The structured report <see cref="ApiDiffReport"/> with all the differences sorted by impact severity.</returns>
    public static ApiDiffReport Compare(CodeGraph baselineGraph, CodeGraph currentGraph)
    {
        var report = new ApiDiffReport();

        // =========================================================================
        // STEP 1: Robust indexing of public APIs in the two snapshots
        // Isolate public symbols (Classes, Interfaces, Methods, Properties, Constants)
        // indexing them by unique signing key to support overloads
        // and partial classes without running into ArgumentException on duplicate keys.
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
        // STEP 2: Detecting removals, visibility downgrades and signature changes
        // Iterate over all public symbols from the previous version (baseline).
        // =========================================================================
        foreach (var (key, oldSym) in oldPublic)
        {
            if (newPublic.TryGetValue(key, out var newSym))
            {
                processedNewKeys.Add(key);
                // The symbol with the same signature exists in both versions
                CompareSignatures(oldSym, newSym, report);
            }
            else
            {
                // 2a. Check if the symbol still exists in the current graph but with reduced visibility (e.g. Public -> Private/Internal)
                var stillExistsPrivate = currentGraph.Symbols.FirstOrDefault(s =>
                    s.QualifiedName.Equals(oldSym.QualifiedName, StringComparison.OrdinalIgnoreCase) &&
                    s.Kind == oldSym.Kind &&
                    !IsPublicApi(s));

                if (stillExistsPrivate != null)

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

                else
                {
                    // 2b. Checks if it is a signature change for a method with the same QualifiedName
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
                        // 2c. The symbol has been removed entirely
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
        // STEP 3: Detection of new public APIs (backwards compatible Minor Feature)
        // Iterate over the current public symbols absent in the baseline.
        // =========================================================================
        foreach (var (key, newSym) in newPublic)
        {
            if (!processedNewKeys.Contains(key))

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

        // =========================================================================
        // STEP 4: Sort by decreasing severity (Major Breaking -> Minor -> Patch)
        // =========================================================================
        report.Differences.Sort((a, b) => b.Impact.CompareTo(a.Impact));
        return report;
    }

    /// <summary>
    /// Compare the list of parameters of a routine between the old and new versions to determine whether the change is breaking.
    /// If the number of mandatory parameters changes or decreases, a Major Breaking Change is reported.
    /// </summary>
    private static void CompareSignatures(Symbol oldSym, Symbol newSym, ApiDiffReport report)
    {
        // Limit comparison to routines with formal parameters (Methods or Properties)
        if (oldSym.Kind is not (SymbolKind.Method or SymbolKind.Property))
            return;

        // Check static modifier change
        if (oldSym.IsStatic != newSym.IsStatic)

            report.Differences.Add(new ApiDifferenceItem
            {
                SymbolQualifiedName = oldSym.QualifiedName,
                SymbolKind = oldSym.Kind,
                ChangeKind = ApiChangeKind.ChangedAccessibility,
                Impact = ApiChangeImpact.MajorBreaking,
                Description = $"Breaking API change: method '{oldSym.QualifiedName}' static modifier changed from {oldSym.IsStatic} to {newSym.IsStatic}.",
                OldSymbol = oldSym,
                NewSymbol = newSym
            });

        // Verify return type change
        var oldReturn = oldSym.ReturnType ?? oldSym.DeclaredTypeName;
        var newReturn = newSym.ReturnType ?? newSym.DeclaredTypeName;
        if (!string.IsNullOrEmpty(oldReturn) && !string.IsNullOrEmpty(newReturn) &&
            !string.Equals(oldReturn, newReturn, StringComparison.OrdinalIgnoreCase))

            report.Differences.Add(new ApiDifferenceItem
            {
                SymbolQualifiedName = oldSym.QualifiedName,
                SymbolKind = oldSym.Kind,
                ChangeKind = ApiChangeKind.ChangedReturnType,
                Impact = ApiChangeImpact.MajorBreaking,
                Description = $"Breaking API change: return type of '{oldSym.QualifiedName}' changed from '{oldReturn}' to '{newReturn}'.",
                OldSymbol = oldSym,
                NewSymbol = newSym
            });

        int oldParamCount = oldSym.Parameters.Count;
        int newParamCount = newSym.Parameters.Count;

        if (oldParamCount != newParamCount)
        {
            // If parameters have been removed, or new parameters added without Optional clause/default value, it is a breaking change
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
        else if (oldParamCount > 0)
        {
            for (int i = 0; i < oldParamCount; i++)
            {
                var oldType = oldSym.Parameters[i].DeclaredTypeName ?? "Variant";
                var newType = newSym.Parameters[i].DeclaredTypeName ?? "Variant";
                if (!string.Equals(oldType, newType, StringComparison.OrdinalIgnoreCase))
                {
                    report.Differences.Add(new ApiDifferenceItem
                    {
                        SymbolQualifiedName = oldSym.QualifiedName,
                        SymbolKind = oldSym.Kind,
                        ChangeKind = ApiChangeKind.ChangedParameters,
                        Impact = ApiChangeImpact.MajorBreaking,
                        Description = $"Breaking API change: parameter '{oldSym.Parameters[i].Name}' of '{oldSym.QualifiedName}' changed type from '{oldType}' to '{newType}'.",
                        OldSymbol = oldSym,
                        NewSymbol = newSym
                    });
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Checks whether a symbol is part of the public surface of the exported API.
    /// Excludes system symbols or aggregators (Project, Namespace) and checks actual accessibility.
    /// </summary>
    private static bool IsPublicApi(Symbol symbol)
    {
        // Excludes project and namespace macro nodes
        if (symbol.Kind is SymbolKind.Project or SymbolKind.Namespace)
            return false;

        return PublicAccessibilities.Contains(symbol.Accessibility);
    }
}
