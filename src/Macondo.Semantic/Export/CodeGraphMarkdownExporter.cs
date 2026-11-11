using System.Text;
using Macondo.Semantic.Model;

namespace Macondo.Semantic.Export;

/// <summary>
/// Exporter of the <see cref="CodeGraph"/> in Markdown format optimized for understanding by LLM models (e.g. Antigravity).
/// Reduces token consumption by condensing the tree of calls, instantiations, and types used.
/// </summary>
public static class CodeGraphMarkdownExporter
{
    /// <summary>
    /// Export the entire graph in hierarchical compact Markdown format (Repo Map).
    /// </summary>
    public static string Export(CodeGraph graph, MarkdownExportOptions? options = null)
    {
        options ??= new MarkdownExportOptions();
        var sb = new StringBuilder();

        if (options.IncludeSummary)

            AppendSummary(sb, graph);

        // Groups top-level symbols by source file
        var topLevelKinds = new HashSet<SymbolKind>
        {
            SymbolKind.Module,
            SymbolKind.Class,
            SymbolKind.Form,
            SymbolKind.UserControl,
            SymbolKind.Interface
        };

        var topLevelSymbols = graph.Symbols
            .Where(s => topLevelKinds.Contains(s.Kind))
            .OrderBy(s => s.FilePath)
            .ThenBy(s => s.Name)
            .ToList();

        // Quick map of symbol IDs for instant lookup
        var symbolLookup = graph.Symbols.ToDictionary(s => s.Id);

        // Containment relationship map
        var childrenLookup = graph.Relationships
            .Where(r => r.Kind == RelationshipKind.Contains)
            .GroupBy(r => r.SourceId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.TargetId).ToList());

        // Outgoing relationship map (Calls, Instantiates, UsesType)
        var outgoingRels = graph.Relationships
            .Where(r => r.Kind is RelationshipKind.Calls or RelationshipKind.Instantiates or RelationshipKind.UsesType)
            .GroupBy(r => r.SourceId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var renderedSymbolIds = new HashSet<Guid>();

        foreach (var topSymbol in topLevelSymbols)
        {
            renderedSymbolIds.Add(topSymbol.Id);
            sb.AppendLine($"## {topSymbol.Kind}: `{topSymbol.Name}` ({Path.GetFileName(topSymbol.FilePath)})");
            if (options.IncludeLineNumbers && topSymbol.LineStart > 0)

                sb.AppendLine($"*Lines: {topSymbol.LineStart}-{topSymbol.LineEnd} | Scope: {topSymbol.Accessibility}*");
            sb.AppendLine();

            var childIds = childrenLookup.GetValueOrDefault(topSymbol.Id, new List<Guid>());
            var children = childIds
                .Select(id => symbolLookup.GetValueOrDefault(id))
                .Where(s => s != null)
                .Select(s => s!)
                .OrderBy(s => s.LineStart)
                .ToList();

            AppendMembers(sb, children, symbolLookup, childrenLookup, outgoingRels, options, renderedSymbolIds);

            sb.AppendLine("---");
            sb.AppendLine();
        }

        // Orphan or isolated symbols not contained in top-level symbols
        var remainingSymbols = graph.Symbols
            .Where(s => !renderedSymbolIds.Contains(s.Id))
            .OrderBy(s => s.FilePath)
            .ThenBy(s => s.LineStart)
            .ThenBy(s => s.Name)
            .ToList();

        if (remainingSymbols.Count > 0)
        {
            var groupedByFile = remainingSymbols.GroupBy(s => s.FilePath ?? string.Empty);
            foreach (var fileGroup in groupedByFile)
            {
                var fileName = !string.IsNullOrWhiteSpace(fileGroup.Key) ? Path.GetFileName(fileGroup.Key) : "Isolated Symbols";
                sb.AppendLine($"## Isolated Symbols ({fileName})");
                sb.AppendLine();

                AppendMembers(sb, fileGroup.ToList(), symbolLookup, childrenLookup, outgoingRels, options, renderedSymbolIds);

                sb.AppendLine("---");
                sb.AppendLine();
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static void AppendMembers(
        StringBuilder sb,
        List<Symbol> members,
        Dictionary<Guid, Symbol> symbolLookup,
        Dictionary<Guid, List<Guid>> childrenLookup,
        Dictionary<Guid, List<Relationship>> outgoingRels,
        MarkdownExportOptions options,
        HashSet<Guid> renderedSymbolIds)
    {
        foreach (var m in members)
        {
            renderedSymbolIds.Add(m.Id);
        }

        // 1. Types, Enums, Constants
        if (options.IncludeTypesAndEnums)
        {
            var typeSymbols = members.Where(c => c.Kind is SymbolKind.Type or SymbolKind.Enum or SymbolKind.Constant or SymbolKind.Struct or SymbolKind.Record).ToList();
            if (typeSymbols.Count > 0)
            {
                sb.AppendLine("### Types & Constants");
                foreach (var typeSym in typeSymbols)
                {
                    var lineInfo = (options.IncludeLineNumbers && typeSym.LineStart > 0) ? $" *(L{typeSym.LineStart})*" : "";
                    var typeDetail = typeSym.DeclaredTypeName != null ? $" As {typeSym.DeclaredTypeName}" : "";
                    sb.AppendLine($"- **{typeSym.Accessibility} {typeSym.Kind}** `{typeSym.Name}`{typeDetail}{lineInfo}");

                    // If it is a Type or Enum, list the fields/constants contained
                    var childMembers = childrenLookup.GetValueOrDefault(typeSym.Id, new List<Guid>())
                        .Select(id => symbolLookup.GetValueOrDefault(id))
                        .Where(m => m != null);

                    foreach (var m in childMembers)
                    {
                        renderedSymbolIds.Add(m!.Id);
                        var mType = m.DeclaredTypeName != null ? $" As {m.DeclaredTypeName}" : "";
                        sb.AppendLine($"  - `{m.Name}`{mType}");
                    }
                }
                sb.AppendLine();
            }
        }

        // 2. Fields / State
        if (options.IncludeFields)
        {
            var fields = members.Where(c => c.Kind == SymbolKind.Field).ToList();
            if (fields.Count > 0)
            {
                sb.AppendLine("### Fields / State");
                foreach (var f in fields)
                {
                    var lineInfo = (options.IncludeLineNumbers && f.LineStart > 0) ? $" *(L{f.LineStart})*" : "";
                    var arrInfo = f.IsArray ? "()" : "";
                    var newInfo = f.IsNewInstance ? "New " : "";
                    var typeInfo = f.DeclaredTypeName != null ? $" As {newInfo}{f.DeclaredTypeName}" : " As Variant";
                    sb.AppendLine($"- `{f.Accessibility} {f.Name}{arrInfo}`{typeInfo}{lineInfo}");
                }
                sb.AppendLine();
            }
        }

        // 3. Methods & Properties
        var routines = members.Where(c => c.Kind is SymbolKind.Method or SymbolKind.Property or SymbolKind.Event).ToList();
        if (routines.Count > 0)
        {
            sb.AppendLine("### Routines & Properties");
            foreach (var routine in routines)
            {
                var sig = FormatSignature(routine, options);
                var lineInfo = (options.IncludeLineNumbers && routine.LineStart > 0)
                    ? (routine.LineEnd > routine.LineStart ? $" *(L{routine.LineStart}-{routine.LineEnd})*" : $" *(L{routine.LineStart})*")
                    : "";
                sb.AppendLine($"- **{routine.Accessibility} {routine.Kind}** `{sig}`{lineInfo}");

                // Outgoing relationship details
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
    }

    /// <summary>
    /// Export a subgraph focused on a single symbol (Impact Slice / Call Slice),
    /// ideal to include as minimal context in a refactoring or modification prompt for an LLM.
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

        // 1. UPSTREAM: Who calls this symbol? (Direct and transitive callers)
        sb.AppendLine("## Inbound Impact (Who calls / uses this symbol)");
        var incomingCalls = graph.Relationships
            .Where(r => r.TargetId == rootSymbol.Id && r.Kind is RelationshipKind.Calls or RelationshipKind.UsesType or RelationshipKind.Instantiates)
            .ToList();

        if (incomingCalls.Count == 0)

            sb.AppendLine("*No direct callers or references found in graph.*");

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

        // 2. DOWNSTREAM: What do you call this symbol? (Direct calls, instances, types)
        sb.AppendLine("## Outbound Dependencies (What this symbol calls / instantiates / uses)");
        var outgoing = graph.Relationships
            .Where(r => r.SourceId == rootSymbol.Id && r.Kind is RelationshipKind.Calls or RelationshipKind.Instantiates or RelationshipKind.UsesType)
            .ToList();

        if (outgoing.Count == 0)

            sb.AppendLine("*No outbound routine calls or type instantiations.*");

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
