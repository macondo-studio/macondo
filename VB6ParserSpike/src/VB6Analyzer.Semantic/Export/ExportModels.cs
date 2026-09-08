using System.Text.Json.Serialization;
using Macondo.Semantic.Model;

namespace Macondo.Semantic.Export;

/// <summary>
/// Opzioni di configurazione per l'esportazione di CodeGraph in formato Markdown ottimizzato per LLM.
/// </summary>
public sealed class MarkdownExportOptions
{
    /// <summary>Include un riepilogo statistico (conteggio moduli, classi, routine, relazioni) in cima al markdown.</summary>
    public bool IncludeSummary { get; set; } = true;

    /// <summary>Include i numeri di riga delle dichiarazioni.</summary>
    public bool IncludeLineNumbers { get; set; } = true;

    /// <summary>Include i parametri e i tipi di ritorno delle routine.</summary>
    public bool IncludeSignatures { get; set; } = true;

    /// <summary>Include le chiamate in uscita (<c>Calls</c>) per ciascuna routine.</summary>
    public bool IncludeCalls { get; set; } = true;

    /// <summary>Include le classi istanziate (<c>Instantiates</c>) per ciascuna routine.</summary>
    public bool IncludeInstantiations { get; set; } = true;

    /// <summary>Include i tipi utilizzati (<c>UsesType</c>) per variabili, parametri e routine.</summary>
    public bool IncludeTypeUsages { get; set; } = true;

    /// <summary>Include le costanti e i tipi definiti dall'utente (UDT/Enum).</summary>
    public bool IncludeTypesAndEnums { get; set; } = true;

    /// <summary>Include i campi di modulo/classe.</summary>
    public bool IncludeFields { get; set; } = true;
}

/// <summary>
/// DTO radice per la serializzazione JSON dell'intero CodeGraph.
/// </summary>
public sealed class CodeGraphDto
{
    public int Version { get; set; } = 1;
    public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;
    public List<SymbolDto> Symbols { get; set; } = new();
    public List<RelationshipDto> Relationships { get; set; } = new();
}

/// <summary>
/// DTO per la serializzazione di un singolo simbolo semantico.
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
/// DTO per la serializzazione dei parametri di una routine.
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
/// DTO per la serializzazione di una relazione semantica.
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
