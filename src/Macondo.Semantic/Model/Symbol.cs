namespace Macondo.Semantic.Model;


/// <summary>
/// Represents an entity/node within the <see cref="CodeGraph"/>.
/// 
/// Every declaration identified in the VB6 source (modules, methods, variables, types) produces
/// a Symbol instance with unique identifier <see cref="Id"/>, location in the file,
/// declared type metadata, visibility and relationships with other symbols.
/// </summary>
public sealed class Symbol
{
    /// <summary>Globally unique identifier of the symbol in the graph.</summary>
    public required Guid Id { get; init; }

    /// <summary>Type of symbol (Class, Method, Field, etc.).</summary>
    public required SymbolKind Kind { get; init; }

    /// <summary>Simple unqualified name of the symbol (e.g. "Save", "mCount", "Customer").</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Full qualified name within the hierarchy (e.g. "Customer.Save", "Customer.Save.localCust").
    /// </summary>
    public required string QualifiedName { get; set; }

    /// <summary>Absolute or relative path to the source file where the symbol is declared.</summary>
    public required string FilePath { get; init; }

    /// <summary>Declaration start line number (1-indexed).</summary>
    public int LineStart { get; init; }

    /// <summary>End line number of declaration / block (1-indexed).</summary>
    public int LineEnd { get; set; }

    /// <summary>Symbol visibility level (Public, Private, Global, Friend, Default).</summary>
    public Accessibility Accessibility { get; set; } = Accessibility.Default;

    /// <summary>
    /// Textual name of the type declared in the source (e.g. "Customer", "Long", "String", "Object").
    /// If the type is not specified in VB6, it is implicitly Variant.
    /// </summary>
    public string? DeclaredTypeName { get; set; }

    /// <summary>
    /// ID of the type symbol in the CodeGraph if it is a user/project defined type (e.g. Class, Type, Enum).
    /// Populated by <see cref="Macondo.Semantic.Resolution.TypeResolver"/>.
    /// </summary>
    public Guid? ResolvedTypeSymbolId { get; set; }

    /// <summary>Return type declared for Function and Property Get (e.g. "Boolean", "Double").</summary>
    public string? ReturnType { get; set; }

    /// <summary>ID of the symbol corresponding to the return type in the CodeGraph.</summary>
    public Guid? ResolvedReturnTypeSymbolId { get; set; }

    /// <summary>List of formal parameters accepted by the routine (whether Method or Property).</summary>
    public List<ParameterSymbol> Parameters { get; } = new();

    /// <summary>Indicates whether the routine or variable was declared with the 'Static' keyword.</summary>
    public bool IsStatic { get; set; }

    /// <summary>Indicates whether the variable/field is an array (e.g. Dim arr(10) As String).</summary>
    public bool IsArray { get; set; }

    /// <summary>
    /// Indicates whether the variable was declared with self-instantiation (e.g. "Dim c As New Customer").
    /// </summary>
    public bool IsNewInstance { get; set; }

    /// <summary>Source programming language of the symbol (e.g. VB6, C#, C++, etc.).</summary>
    public SourceLanguage Language { get; set; } = SourceLanguage.Unknown;

    /// <summary>
    /// Returns the actual language of the symbol, taking it from the FilePath extension if not explicitly set.
    /// </summary>
    public SourceLanguage GetEffectiveLanguage()
    {
        if (Language != SourceLanguage.Unknown)
            return Language;

        if (!string.IsNullOrEmpty(FilePath))
        {
            var ext = System.IO.Path.GetExtension(FilePath).ToLowerInvariant();
            return ext switch
            {
                ".vbp" or ".vbg" or ".frm" or ".bas" or ".cls" or ".ctl" or ".pag" or ".dsx" or ".dob" => SourceLanguage.VisualBasic6,
                ".csproj" or ".sln" or ".cs" => SourceLanguage.CSharp,
                ".cpp" or ".c" or ".h" or ".hpp" or ".vcxproj" => SourceLanguage.CPlusPlus,
                ".java" => SourceLanguage.Java,
                ".kt" or ".kts" => SourceLanguage.Kotlin,
                ".py" => SourceLanguage.Python,
                ".ts" => SourceLanguage.TypeScript,
                ".js" => SourceLanguage.JavaScript,
                _ => SourceLanguage.Unknown
            };
        }

        return SourceLanguage.Unknown;
    }

    public override string ToString() => $"{Accessibility} {Kind} {QualifiedName}{(DeclaredTypeName != null ? $" As {DeclaredTypeName}" : "")}";
}
