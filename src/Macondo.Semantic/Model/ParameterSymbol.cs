namespace Macondo.Semantic.Model;

/// <summary>
/// Represents a formal parameter in the signature of a procedure (Sub, Function, Property) in VB6.
/// 
/// CARATTERISTICHE VB6:
/// - Pass mode: 'ByRef' is the default in VB6 if omitted; 'ByVal' creates a copy.
/// - Optional parameters: 'Optional' with possible default value.
/// - Variable parameters: 'ParamArray' (Variant array).
/// </summary>
public sealed class ParameterSymbol
{
    /// <summary>Formal argument name (e.g. "cust", "taxRate").</summary>
    public required string Name { get; init; }

    /// <summary>Name of the type declared (e.g. "Customer", "Double", "Single").</summary>
    public string? DeclaredTypeName { get; set; }

    /// <summary>
    /// ID of the corresponding symbol in the CodeGraph if it is a user/project defined type.
    /// </summary>
    public Guid? ResolvedTypeSymbolId { get; set; }

    /// <summary>Indicates whether the parameter is passed by reference (default in VB6).</summary>
    public bool IsByRef { get; init; } = true;

    /// <summary>Indicates whether the parameter is explicitly passed by value ('ByVal').</summary>
    public bool IsByVal { get; init; }

    /// <summary>Indicates whether the parameter is optional ('Optional').</summary>
    public bool IsOptional { get; init; }

    /// <summary>Indicates whether the parameter accepts a variable list of arguments ('ParamArray').</summary>
    public bool IsParamArray { get; init; }

    /// <summary>Expression of the textual default value for optional parameters (e.g. "0#", "\"default\"").</summary>
    public string? DefaultValue { get; init; }

    /// <summary>Line of the source file in which the parameter is declared.</summary>
    public int Line { get; init; }

    public override string ToString() =>
        $"{(IsOptional ? "Optional " : "")}{(IsByVal ? "ByVal " : "ByRef ")}{(IsParamArray ? "ParamArray " : "")}{Name}{(DeclaredTypeName != null ? $" As {DeclaredTypeName}" : "")}";
}
