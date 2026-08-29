namespace VB6Analyzer.Semantic.Model;

/// <summary>
/// Rappresenta un parametro formale di una routine (Sub, Function, Property) in VB6.
/// </summary>
public sealed class ParameterSymbol
{
    public required string Name { get; init; }
    public string? DeclaredTypeName { get; set; }
    public Guid? ResolvedTypeSymbolId { get; set; }
    public bool IsByRef { get; init; } = true; // In VB6 ByRef è il default
    public bool IsByVal { get; init; }
    public bool IsOptional { get; init; }
    public bool IsParamArray { get; init; }
    public string? DefaultValue { get; init; }
    public int Line { get; init; }

    public override string ToString() =>
        $"{(IsOptional ? "Optional " : "")}{(IsByVal ? "ByVal " : "ByRef ")}{(IsParamArray ? "ParamArray " : "")}{Name}{(DeclaredTypeName != null ? $" As {DeclaredTypeName}" : "")}";
}
