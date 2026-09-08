using System.Text;
using Macondo.Semantic.Model;

namespace Macondo.Semantic.Export;

/// <summary>
/// Esportatore del <see cref="CodeGraph"/> in formato Markdown ottimizzato per la comprensione da parte di modelli LLM (es. Antigravity).
/// Riduce il consumo di token condensando l'albero di chiamate, istanziazioni e tipi utilizzati.
/// </summary>
public static class CodeGraphMarkdownExporter
{
    /// <summary>
    /// Esporta l'intero grafo in formato Markdown compatto gerarchico (Repo Map).
    /// </summary>
    public static string Export(CodeGraph graph, MarkdownExportOptions? options = null)
    {
        options ??= new MarkdownExportOptions();
        var sb = new StringBuilder();

        if (options.IncludeSummary)
        {
            AppendSummary(sb, graph);
        }

        // Raggruppa i simboli di primo livello per file sorgente
        var topLevelKinds = new HashSet<SymbolKind>
        {
            SymbolKind.Module,
            SymbolKind.Class,
            SymbolKind.Form,
            SymbolKind.UserControl
        };

        var topLevelSymbols = graph.Symbols
            .Where(s => topLevelKinds.Contains(s.Kind))
            .OrderBy(s => s.FilePath)
            .ThenBy(s => s.Name)
            .ToList();

        // Mappa veloce degli ID simboli per lookup immediato
        var symbolLookup = graph.Symbols.ToDictionary(s => s.Id);

        // Mappa delle relazioni di contenimento
        var childrenLookup = graph.Relationships
            .Where(r => r.Kind == RelationshipKind.Contains)
            .GroupBy(r => r.SourceId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.TargetId).ToList());

        // Mappa delle relazioni in uscita (Calls, Instantiates, UsesType)
        var outgoingRels = graph.Relationships
            .Where(r => r.Kind is RelationshipKind.Calls or RelationshipKind.Instantiates or RelationshipKind.UsesType)
            .GroupBy(r => r.SourceId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var topSymbol in topLevelSymbols)
        {
            sb.AppendLine($"## {topSymbol.Kind}: `{topSymbol.Name}` ({Path.GetFileName(topSymbol.FilePath)})");
            if (options.IncludeLineNumbers && topSymbol.LineStart > 0)
            {
                sb.AppendLine($"*Lines: {topSymbol.LineStart}-{topSymbol.LineEnd} | Scope: {topSymbol.Accessibility}*");
            }
            sb.AppendLine();

            var childIds = childrenLookup.GetValueOrDefault(topSymbol.Id, new List<Guid>());
            var children = childIds
                .Select(id => symbolLookup.GetValueOrDefault(id))
                .Where(s => s != null)
                .Select(s => s!)
                .OrderBy(s => s.LineStart)
                .ToList();

            // 1. Types, Enums, Constants
            if (options.IncludeTypesAndEnums)
            {
                var typeSymbols = children.Where(c => c.Kind is SymbolKind.Type or SymbolKind.Enum or SymbolKind.Constant).ToList();
                if (typeSymbols.Count > 0)
                {
                    sb.AppendLine("### Types & Constants");
                    foreach (var typeSym in typeSymbols)
                    {
                        var lineInfo = options.IncludeLineNumbers ? $" *(L{typeSym.LineStart})*" : "";
                        var typeDetail = typeSym.DeclaredTypeName != null ? $" As {typeSym.DeclaredTypeName}" : "";
                        sb.AppendLine($"- **{typeSym.Accessibility} {typeSym.Kind}** `{typeSym.Name}`{typeDetail}{lineInfo}");

                        // Se è un Type o Enum, elenca i campi/costanti contenuti
                        var members = childrenLookup.GetValueOrDefault(typeSym.Id, new List<Guid>())
                            .Select(id => symbolLookup.GetValueOrDefault(id))
                            .Where(m => m != null);

                        foreach (var m in members)
                        {
                            var mType = m!.DeclaredTypeName != null ? $" As {m.DeclaredTypeName}" : "";
                            sb.AppendLine($"  - `{m.Name}`{mType}");
                        }
                    }
                    sb.AppendLine();
                }
            }

            // 2. Module/Class Fields
            if (options.IncludeFields)
            {
                var fields = children.Where(c => c.Kind == SymbolKind.Field).ToList();
                if (fields.Count > 0)
                {
                    sb.AppendLine("### Fields / State");
                    foreach (var f in fields)
                    {
                        var lineInfo = options.IncludeLineNumbers ? $" *(L{f.LineStart})*" : "";
                        var arrInfo = f.IsArray ? "()" : "";
                        var newInfo = f.IsNewInstance ? "New " : "";
                        var typeInfo = f.DeclaredTypeName != null ? $" As {newInfo}{f.DeclaredTypeName}" : " As Variant";
                        sb.AppendLine($"- `{f.Accessibility} {f.Name}{arrInfo}`{typeInfo}{lineInfo}");
                    }
                    sb.AppendLine();
                }
            }

            // 3. Methods & Properties
            var routines = children.Where(c => c.Kind is SymbolKind.Method or SymbolKind.Property or SymbolKind.Event).ToList();
            if (routines.Count > 0)
            {
                sb.AppendLine("### Routines & Properties");
                foreach (var routine in routines)
                {
                    var sig = FormatSignature(routine, options);
                    var lineInfo = options.IncludeLineNumbers ? $" *(L{routine.LineStart}-{routine.LineEnd})*" : "";
                    sb.AppendLine($"- **{routine.Accessibility} {routine.Kind}** `{sig}`{lineInfo}");

                    // Dettagli relazioni in uscita
                    if (outgoingRels.TryGetValue(routine.Id, out var rels))
                    {
                        // Chiamate effettuate
                        if (options.IncludeCalls)
                        {
                            var calls = rels.Where(r => r.Kind == RelationshipKind.Calls).ToList();
                            if (calls.Count > 0)
                            {
                                var callTargets = calls.Select(c =>
                                {
                                    var target = symbolLookup.GetValueOrDefault(c.TargetId);
                                    var targetName = target?.QualifiedName ?? "Unknown";
                                    var line = (options.IncludeLineNumbers && c.Line.HasValue) ? $" (L{c.Line})" : "";
                                    var conf = c.Confidence != ResolutionConfidence.Exact ? $" [{c.Confidence}]" : "";
                                    return $"`{targetName}`{line}{conf}";
                                });
                                sb.AppendLine($"  - *Calls:* {string.Join(", ", callTargets)}");
                            }
                        }

                        // Classi istanziate
                        if (options.IncludeInstantiations)
                        {
                            var instantiations = rels.Where(r => r.Kind == RelationshipKind.Instantiates).ToList();
                            if (instantiations.Count > 0)
                            {
                                var instTargets = instantiations.Select(i =>
                                {
                                    var target = symbolLookup.GetValueOrDefault(i.TargetId);
                                    return $"`{target?.Name ?? "Unknown"}`";
                                }).Distinct();
                                sb.AppendLine($"  - *Instantiates:* {string.Join(", ", instTargets)}");
                            }
                        }

                        // Tipi utilizzati
                        if (options.IncludeTypeUsages)
                        {
                            var typesUsed = rels.Where(r => r.Kind == RelationshipKind.UsesType).ToList();
                            if (typesUsed.Count > 0)
                            {
                                var typeNames = typesUsed.Select(t =>
                                {
                                    var target = symbolLookup.GetValueOrDefault(t.TargetId);
                                    return $"`{target?.Name ?? "Unknown"}`";
                                }).Distinct();
                                sb.AppendLine($"  - *Uses Types:* {string.Join(", ", typeNames)}");
                            }
                        }
                    }
                }
                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Esporta un subgrafo focalizzato su un singolo simbolo (Impact Slice / Call Slice),
    /// ideale da includere come contesto minimo in un prompt di refactoring o modifica per un LLM.
    /// </summary>
    public static string ExportSlice(CodeGraph graph, Symbol rootSymbol, int maxDepth = 2)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Context Slice for Symbol: `{rootSymbol.QualifiedName}`");
        sb.AppendLine($"- **Kind**: {rootSymbol.Kind}");
        sb.AppendLine($"- **File**: `{rootSymbol.FilePath}` (Lines {rootSymbol.LineStart}-{rootSymbol.LineEnd})");
        sb.AppendLine($"- **Visibility**: {rootSymbol.Accessibility}");
        if (rootSymbol.ReturnType != null)
            sb.AppendLine($"- **Return Type**: `{rootSymbol.ReturnType}`");
        sb.AppendLine();

        var symbolLookup = graph.Symbols.ToDictionary(s => s.Id);

        // 1. UPSTREAM: Chi chiama questo simbolo? (Direct and transitive callers)
        sb.AppendLine("## Inbound Impact (Who calls / uses this symbol)");
        var incomingCalls = graph.Relationships
            .Where(r => r.TargetId == rootSymbol.Id && r.Kind is RelationshipKind.Calls or RelationshipKind.UsesType or RelationshipKind.Instantiates)
            .ToList();

        if (incomingCalls.Count == 0)
        {
            sb.AppendLine("*No direct callers or references found in graph.*");
        }
        else
        {
            foreach (var inc in incomingCalls)
            {
                var caller = symbolLookup.GetValueOrDefault(inc.SourceId);
                var callerName = caller?.QualifiedName ?? "Unknown";
                var file = caller != null ? Path.GetFileName(caller.FilePath) : "";
                var line = inc.Line.HasValue ? $" at line {inc.Line}" : "";
                sb.AppendLine($"- `<- [{inc.Kind}]` **{callerName}** (`{file}`){line} [{inc.Confidence}]");
            }
        }
        sb.AppendLine();

        // 2. DOWNSTREAM: Cosa chiama questo simbolo? (Direct callees, instantiations, types)
        sb.AppendLine("## Outbound Dependencies (What this symbol calls / instantiates / uses)");
        var outgoing = graph.Relationships
            .Where(r => r.SourceId == rootSymbol.Id && r.Kind is RelationshipKind.Calls or RelationshipKind.Instantiates or RelationshipKind.UsesType)
            .ToList();

        if (outgoing.Count == 0)
        {
            sb.AppendLine("*No outbound routine calls or type instantiations.*");
        }
        else
        {
            foreach (var outRel in outgoing)
            {
                var target = symbolLookup.GetValueOrDefault(outRel.TargetId);
                var targetName = target?.QualifiedName ?? target?.Name ?? "Unknown";
                var file = target != null ? Path.GetFileName(target.FilePath) : "";
                var line = outRel.Line.HasValue ? $" (invoked at line {outRel.Line})" : "";
                sb.AppendLine($"- `-> [{outRel.Kind}]` **{targetName}** (`{file}`){line}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatSignature(Symbol routine, MarkdownExportOptions options)
    {
        if (!options.IncludeSignatures)
            return routine.Name;

        var paramList = routine.Parameters.Count == 0
            ? ""
            : string.Join(", ", routine.Parameters.Select(p =>
            {
                var by = p.IsByVal ? "ByVal " : "";
                var opt = p.IsOptional ? "Optional " : "";
                var arr = p.IsParamArray ? "ParamArray " : "";
                var typeStr = p.DeclaredTypeName != null ? $" As {p.DeclaredTypeName}" : "";
                var def = p.DefaultValue != null ? $" = {p.DefaultValue}" : "";
                return $"{opt}{arr}{by}{p.Name}{typeStr}{def}";
            }));

        var ret = routine.ReturnType != null ? $" As {routine.ReturnType}" : "";
        return $"{routine.Name}({paramList}){ret}";
    }

    private static void AppendSummary(StringBuilder sb, CodeGraph graph)
    {
        var modulesCount = graph.Symbols.Count(s => s.Kind is SymbolKind.Module or SymbolKind.Class or SymbolKind.Form or SymbolKind.UserControl);
        var routinesCount = graph.Symbols.Count(s => s.Kind is SymbolKind.Method or SymbolKind.Property);
        var callsCount = graph.Relationships.Count(r => r.Kind == RelationshipKind.Calls);
        var instCount = graph.Relationships.Count(r => r.Kind == RelationshipKind.Instantiates);
        var typesCount = graph.Relationships.Count(r => r.Kind == RelationshipKind.UsesType);

        sb.AppendLine("# CodeBase Knowledge Graph (Repo Map)");
        sb.AppendLine($"> **Summary**: {modulesCount} Modules/Classes/Forms | {routinesCount} Routines | {callsCount} Calls | {instCount} Instantiations | {typesCount} Type Dependencies");
        sb.AppendLine();
    }
}
