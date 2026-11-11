using System.Text.Json.Serialization;
using Macondo.Semantic.Model;

namespace Macondo.Semantic.Export;

/// <summary>
/// Configuration options for exporting CodeGraph to LLM-optimized Markdown format.
/// </summary>
public sealed class MarkdownExportOptions
{
    /// <summary>Includes a statistical summary (count modules, classes, routines, relationships) at the top of the markdown.</summary>
    public bool IncludeSummary { get; set; } = true;

    /// <summary>Includes declaration line numbers.</summary>
    public bool IncludeLineNumbers { get; set; } = true;

    /// <summary>Includes the parameters and return types of the routines.</summary>
    public bool IncludeSignatures { get; set; } = true;

    /// <summary>Includes outgoing calls (<c>Calls</c>) for each routine.</summary>
    public bool IncludeCalls { get; set; } = true;

    /// <summary>Includes the instantiated classes (<c>Instantiates</c>) for each routine.</summary>
    public bool IncludeInstantiations { get; set; } = true;

    /// <summary>Includes the types used (<c>UsesType</c>) for variables, parameters, and routines.</summary>
    public bool IncludeTypeUsages { get; set; } = true;

    /// <summary>Includes constants and user-defined types (UDT/Enum).</summary>
    public bool IncludeTypesAndEnums { get; set; } = true;

    /// <summary>Includes form/class fields.</summary>
    public bool IncludeFields { get; set; } = true;
}

/// <summary>
/// Root DTO for JSON serialization of the entire CodeGraph.
/// </summary>
public sealed class CodeGraphDto
{
    public int Version { get; set; } = 1;
    public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;
    public List<SymbolDto> Symbols { get; set; } = new();
    public List<RelationshipDto> Relationships { get; set; } = new();
}

/// <summary>
/// DTO for serialization of a single semantic symbol.
/// </summary>
public sealed class SymbolDto
{
    public Guid Id { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SymbolKind Kind { get; set; }

    public string Name { get; set; } = string.Empty;
    public string QualifiedName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int LineStart { get; set; }
    public int LineEnd { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Accessibility Accessibility { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeclaredTypeName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? ResolvedTypeSymbolId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReturnType { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? ResolvedReturnTypeSymbolId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsStatic { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsArray { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsNewInstance { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ParameterDto>? Parameters { get; set; }
}

/// <summary>
/// DTO for serializing the parameters of a routine.
/// </summary>
public sealed class ParameterDto
{
    public string Name { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeclaredTypeName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? ResolvedTypeSymbolId { get; set; }

    public bool IsByRef { get; set; } = true;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsByVal { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsOptional { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsParamArray { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultValue { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Line { get; set; }
}

/// <summary>
/// DTO for serialization of a semantic relation.
/// </summary>
public sealed class RelationshipDto
{
    public Guid SourceId { get; set; }
    public Guid TargetId { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RelationshipKind Kind { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Line { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ResolutionConfidence Confidence { get; set; }
}
