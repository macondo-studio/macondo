using System.Text.Json;
using Macondo.Semantic.Model;

namespace Macondo.Semantic.Export;

/// <summary>
/// Esportatore e deserializzatore del <see cref="CodeGraph"/> in formato JSON.
/// Consente la persistenza su disco, l'interscambio con strumenti esterni e il caching dell'analisi semantica.
/// </summary>
public static class CodeGraphJsonExporter
{
    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions CompactOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Serializza il <see cref="CodeGraph"/> in una stringa JSON.
    /// </summary>
    public static string ExportToJson(CodeGraph graph, bool indented = true)
    {
        var dto = ExportToDto(graph);
        return JsonSerializer.Serialize(dto, indented ? DefaultOptions : CompactOptions);
    }

    /// <summary>
    /// Esporta il context slice di un singolo simbolo e delle sue relazioni inbound/outbound in formato JSON.
    /// </summary>
    public static string ExportSliceToJson(CodeGraph graph, Symbol rootSymbol, bool indented = true)
    {
        var symbolLookup = graph.Symbols.ToDictionary(s => s.Id);

        var incomingCalls = graph.Relationships
            .Where(r => r.TargetId == rootSymbol.Id && r.Kind is RelationshipKind.Calls or RelationshipKind.UsesType or RelationshipKind.Instantiates)
            .Select(r => new
            {
                kind = r.Kind.ToString(),
                sourceId = r.SourceId,
                sourceName = symbolLookup.GetValueOrDefault(r.SourceId)?.QualifiedName ?? symbolLookup.GetValueOrDefault(r.SourceId)?.Name ?? "Unknown",
                sourceKind = symbolLookup.GetValueOrDefault(r.SourceId)?.Kind.ToString(),
                filePath = symbolLookup.GetValueOrDefault(r.SourceId)?.FilePath,
                line = r.Line,
                confidence = r.Confidence.ToString()
            })
            .ToList();

        var outgoing = graph.Relationships
            .Where(r => r.SourceId == rootSymbol.Id && r.Kind is RelationshipKind.Calls or RelationshipKind.Instantiates or RelationshipKind.UsesType)
            .Select(r => new
            {
                kind = r.Kind.ToString(),
                targetId = r.TargetId,
                targetName = symbolLookup.GetValueOrDefault(r.TargetId)?.QualifiedName ?? symbolLookup.GetValueOrDefault(r.TargetId)?.Name ?? "Unknown",
                targetKind = symbolLookup.GetValueOrDefault(r.TargetId)?.Kind.ToString(),
                filePath = symbolLookup.GetValueOrDefault(r.TargetId)?.FilePath,
                line = r.Line,
                confidence = r.Confidence.ToString()
            })
            .ToList();

        var children = graph.Relationships
            .Where(r => r.SourceId == rootSymbol.Id && r.Kind == RelationshipKind.Contains)
            .Select(r => symbolLookup.GetValueOrDefault(r.TargetId))
            .Where(s => s != null)
            .Select(s => new
            {
                id = s!.Id,
                name = s.Name,
                qualifiedName = s.QualifiedName,
                kind = s.Kind.ToString(),
                accessibility = s.Accessibility.ToString(),
                returnType = s.ReturnType,
                lineStart = s.LineStart,
                lineEnd = s.LineEnd
            })
            .ToList();

        var sliceObj = new
        {
            symbol = new
            {
                id = rootSymbol.Id,
                name = rootSymbol.Name,
                qualifiedName = rootSymbol.QualifiedName,
                kind = rootSymbol.Kind.ToString(),
                accessibility = rootSymbol.Accessibility.ToString(),
                filePath = rootSymbol.FilePath,
                lineStart = rootSymbol.LineStart,
                lineEnd = rootSymbol.LineEnd,
                declaredTypeName = rootSymbol.DeclaredTypeName,
                returnType = rootSymbol.ReturnType,
                isStatic = rootSymbol.IsStatic,
                parameters = rootSymbol.Parameters.Select(p => new
                {
                    name = p.Name,
                    type = p.DeclaredTypeName,
                    isByRef = p.IsByRef,
                    isByVal = p.IsByVal,
                    isOptional = p.IsOptional,
                    defaultValue = p.DefaultValue
                })
            },
            inbound = incomingCalls,
            outbound = outgoing,
            children = children
        };

        return JsonSerializer.Serialize(sliceObj, indented ? DefaultOptions : CompactOptions);
    }

    /// <summary>
    /// Converte il <see cref="CodeGraph"/> in un DTO strutturato.
    /// </summary>
    private static CodeGraphDto ExportToDto(CodeGraph graph)
    {
        var dto = new CodeGraphDto();

        foreach (var sym in graph.Symbols)
        {
            var symDto = new SymbolDto
            {
                Id = sym.Id,
                Kind = sym.Kind,
                Name = sym.Name,
                QualifiedName = sym.QualifiedName,
                FilePath = sym.FilePath,
                LineStart = sym.LineStart,
                LineEnd = sym.LineEnd,
                Accessibility = sym.Accessibility,
                DeclaredTypeName = sym.DeclaredTypeName,
                ResolvedTypeSymbolId = sym.ResolvedTypeSymbolId,
                ReturnType = sym.ReturnType,
                ResolvedReturnTypeSymbolId = sym.ResolvedReturnTypeSymbolId,
                IsStatic = sym.IsStatic,
                IsArray = sym.IsArray,
                IsNewInstance = sym.IsNewInstance
            };

            if (sym.Parameters.Count > 0)

                symDto.Parameters = sym.Parameters.Select(p => new ParameterDto
                {
                    Name = p.Name,
                    DeclaredTypeName = p.DeclaredTypeName,
                    ResolvedTypeSymbolId = p.ResolvedTypeSymbolId,
                    IsByRef = p.IsByRef,
                    IsByVal = p.IsByVal,
                    IsOptional = p.IsOptional,
                    IsParamArray = p.IsParamArray,
                    DefaultValue = p.DefaultValue,
                    Line = p.Line
                }).ToList();

            dto.Symbols.Add(symDto);
        }

        foreach (var rel in graph.Relationships)
        {
            dto.Relationships.Add(new RelationshipDto
            {
                SourceId = rel.SourceId,
                TargetId = rel.TargetId,
                Kind = rel.Kind,
                Line = rel.Line,
                Confidence = rel.Confidence
            });
        }

        return dto;
    }

    /// <summary>
    /// Deserializza una stringa JSON ricostruendo un'istanza funzionante di <see cref="CodeGraph"/>.
    /// </summary>
    public static CodeGraph ImportFromJson(string json)
    {
        var dto = JsonSerializer.Deserialize<CodeGraphDto>(json, DefaultOptions)
                  ?? throw new InvalidOperationException("Impossibile deserializzare il JSON in un CodeGraphDto valido.");

        return ImportFromDto(dto);
    }

    /// <summary>
    /// Ricostruisce un'istanza di <see cref="CodeGraph"/> a partire da un <see cref="CodeGraphDto"/>.
    /// </summary>
    private static CodeGraph ImportFromDto(CodeGraphDto dto)
    {
        var graph = new CodeGraph();

        foreach (var symDto in dto.Symbols)
        {
            var symbol = new Symbol
            {
                Id = symDto.Id,
                Kind = symDto.Kind,
                Name = symDto.Name,
                QualifiedName = symDto.QualifiedName,
                FilePath = symDto.FilePath,
                LineStart = symDto.LineStart,
                LineEnd = symDto.LineEnd,
                Accessibility = symDto.Accessibility,
                DeclaredTypeName = symDto.DeclaredTypeName,
                ResolvedTypeSymbolId = symDto.ResolvedTypeSymbolId,
                ReturnType = symDto.ReturnType,
                ResolvedReturnTypeSymbolId = symDto.ResolvedReturnTypeSymbolId,
                IsStatic = symDto.IsStatic,
                IsArray = symDto.IsArray,
                IsNewInstance = symDto.IsNewInstance
            };

            if (symDto.Parameters != null)
            {
                foreach (var pDto in symDto.Parameters)
                {
                    symbol.Parameters.Add(new ParameterSymbol
                    {
                        Name = pDto.Name,
                        DeclaredTypeName = pDto.DeclaredTypeName,
                        ResolvedTypeSymbolId = pDto.ResolvedTypeSymbolId,
                        IsByRef = pDto.IsByRef,
                        IsByVal = pDto.IsByVal,
                        IsOptional = pDto.IsOptional,
                        IsParamArray = pDto.IsParamArray,
                        DefaultValue = pDto.DefaultValue,
                        Line = pDto.Line
                    });
                }
            }

            graph.Symbols.Add(symbol);
        }

        foreach (var relDto in dto.Relationships)
        {
            graph.Relationships.Add(new Relationship
            {
                SourceId = relDto.SourceId,
                TargetId = relDto.TargetId,
                Kind = relDto.Kind,
                Line = relDto.Line,
                Confidence = relDto.Confidence
            });
        }

        return graph;
    }
}
