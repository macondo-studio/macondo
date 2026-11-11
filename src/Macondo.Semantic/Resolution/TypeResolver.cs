using Macondo.Semantic.Model;

namespace Macondo.Semantic.Resolution;

/// <summary>
/// Descriptor for the semantic classification of a data type in VB6.
/// Distinguishes between built-in primitive types, dynamic types (Object/Variant), and user-defined types in the project.
/// </summary>
public sealed class TypeInfo
{
    /// <summary>Clean type name (e.g. "Long", "Customer", "Variant").</summary>
    public required string Name { get; init; }

    /// <summary>Indicates whether it is a standard primitive type of the language (Integer, Long, String, Boolean, Double, etc.).</summary>
    public bool IsPrimitive { get; init; }

    /// <summary>Indicates whether it is a type that is not bound to compilation / dynamic typing ('Object' or 'Variant').</summary>
    public bool IsDynamicOrVariant { get; init; }

    /// <summary>The user symbol in the CodeGraph corresponding to the type (whether Class, Form, UserControl, Type, or Enum).</summary>
    public Symbol? UserTypeSymbol { get; init; }

    /// <summary>Indicates whether the type is known (primitive or user-defined in the project).</summary>
    public bool IsResolved => IsPrimitive || UserTypeSymbol != null;

    public override string ToString() =>
        IsPrimitive ? $"[Primitive] {Name}" : (UserTypeSymbol != null ? $"[UserType] {UserTypeSymbol.Kind} {UserTypeSymbol.QualifiedName}" : $"[Unknown/External] {Name}");
}

/// <summary>
/// Type resolution engine for VB6.
/// 
/// RESPONSABILITÀ:
/// 1. Primitive Type Mapping: recognizes VB6 scalar types (Boolean, Byte, Integer, Long, Single, Double, Currency, Date, String, Collection).
/// 2. Dynamic Type Management: identify Variant and Object to route subsequent resolution towards late binding.
/// 3. User Type Resolution: Map "As Customer" clauses to the corresponding <see cref="SymbolKind.Class"/> "Customer" in the CodeGraph.
/// 4. Global Resolution: traverses all the symbols and parameters of the graph populating the <see cref="Symbol.ResolvedTypeSymbolId"/> references.
/// </summary>
public sealed class TypeResolver
{
    /// <summary>Set of predefined VB6, C# and Java primitive types (case-insensitive).</summary>
    private static readonly HashSet<string> PrimitiveTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Boolean", "Byte", "Integer", "Long", "Single", "Double",
        "Currency", "Date", "String", "Collection",
        "bool", "byte", "sbyte", "char", "decimal", "double", "float",
        "int", "uint", "nint", "nuint", "long", "ulong", "short", "ushort",
        "string", "object", "dynamic", "void", "Task", "ValueTask",
        "boolean", "Character", "Float", "Short", "List", "Map", "Set", "Optional"
    };

    /// <summary>
    /// Dynamically bound / late bound types in VB6.
    /// Includes both non-compile-time bound generic types ('Object', 'Variant', 'dynamic'),
    /// and the basic user interface types intrinsic to the VB6 runtime ('Form', 'Control', 'MDIForm').
    /// In VB6, variables declared 'As Form' or 'As Control' do not statically expose custom methods
    /// of the individual derived forms/controls: the related member calls take place via IDispatch (late binding).
    /// </summary>
    private static readonly HashSet<string> DynamicTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Object", "Variant", "dynamic", "Form", "Control", "MDIForm"
    };

    private readonly CodeGraph _graph;

    /// <summary>Index of the types defined in the project by simple name (e.g. "Customer" -> Symbol Class Customer).</summary>
    private readonly Dictionary<string, Symbol> _userTypesByName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes the resolver by indexing all types declared in the graph.
    /// </summary>
    /// <param name="graph">The CodeGraph containing all the symbols of the project.</param>
    /// <param name="cancellationToken">Optional delete token to stop processing.</param>
    public TypeResolver(CodeGraph graph, CancellationToken cancellationToken = default)
    {
        _graph = graph;

        // Constructs the index of the user types (Class, Form, UserControl, Module .bas, Type UDT, Enum, Interface, Struct, Record) present in the graph.
        // Explicitly includes SymbolKind.Module so that functions and properties that return instances or references
        // standard .bas modules can correctly resolve the relevant semantic container.
        foreach (var symbol in _graph.Symbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (symbol.Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.UserControl or SymbolKind.Module or SymbolKind.Type or SymbolKind.Enum
                            or SymbolKind.Interface or SymbolKind.Struct or SymbolKind.Record)
            {
                // User types are accessible by simple or qualified name
                _userTypesByName[symbol.Name] = symbol;
                if (!string.IsNullOrEmpty(symbol.QualifiedName) && !symbol.QualifiedName.Equals(symbol.Name, StringComparison.OrdinalIgnoreCase))
                    _userTypesByName[symbol.QualifiedName] = symbol;
            }
        }
    }

    /// <summary>
    /// Resolves a string of type (e.g. "Customer", "Long", "String()", "Object")
    /// in the relevant classification <see cref="TypeInfo"/>.
    /// </summary>
    /// <param name="typeName">The name of the type declared in the source.</param>
    /// <returns>The semantic information of the type (<see cref="TypeInfo"/>).</returns>
    public TypeInfo ResolveType(string? typeName)
    {
        // In VB6, if the 'As Type' clause is omitted, the default type is Variant (dynamic)
        if (string.IsNullOrWhiteSpace(typeName))
            return new TypeInfo
            {
                Name = "Variant",
                IsPrimitive = true,
                IsDynamicOrVariant = true,
                UserTypeSymbol = null
            };

        var cleanName = typeName.Trim();

        // Removes array notation if present (e.g. "Customer()" or "String()")
        if (cleanName.EndsWith("()", StringComparison.Ordinal))
            cleanName = cleanName[..^2].Trim();

        // 1. Check if it is a dynamically typed type (Object or Variant)
        if (DynamicTypes.Contains(cleanName))
            return new TypeInfo
            {
                Name = cleanName,
                IsPrimitive = true,
                IsDynamicOrVariant = true,
                UserTypeSymbol = null
            };

        // 2. Check if it is a built-in primitive type
        if (PrimitiveTypes.Contains(cleanName))
            return new TypeInfo
            {
                Name = cleanName,
                IsPrimitive = true,
                IsDynamicOrVariant = false,
                UserTypeSymbol = null
            };

        // 3. Check whether it is a user-declared type in the local project by simple or qualified name
        if (_userTypesByName.TryGetValue(cleanName, out var userSymbol))
            return new TypeInfo
            {
                Name = cleanName,
                IsPrimitive = false,
                IsDynamicOrVariant = false,
                UserTypeSymbol = userSymbol
            };

        // 3b. Fallback for qualified types with project or library prefix (e.g. "DUE.cntrDestination").
        // In the visual designer of the VB6 forms ('Begin ... End' sections), the project's UserControl instances
        // they are often generated with the project name prefixed to the control name (e.g. 'Begin DUE.cntrDestination cntrDestination').
        // Since the user control is defined in the source file as 'cntrDestination.ctl', we extract the unqualified suffix
        // after the last point to correctly resolve the corresponding type symbol present in the graph.
        var lastDot = cleanName.LastIndexOf('.');
        if (lastDot >= 0 && lastDot < cleanName.Length - 1)
        {
            var unqualified = cleanName[(lastDot + 1)..].Trim();
            if (_userTypesByName.TryGetValue(unqualified, out var unqualifiedSymbol))
                return new TypeInfo
                {
                    Name = cleanName,
                    IsPrimitive = false,
                    IsDynamicOrVariant = false,
                    UserTypeSymbol = unqualifiedSymbol
                };
        }

        // 4. External type not present in the project (e.g. COM library ADODB.Recordset, MSXML2.DOMDocument, etc.)
        return new TypeInfo
        {
            Name = cleanName,
            IsPrimitive = false,
            IsDynamicOrVariant = false,
            UserTypeSymbol = null
        };
    }

    /// <summary>
    /// Performs the global type resolution pass on all symbols and parameters of the <see cref="CodeGraph"/>,
    /// populating the IDs of the resolved types (<see cref="Symbol.ResolvedTypeSymbolId"/>, <see cref="Symbol.ResolvedReturnTypeSymbolId"/>,
    /// <see cref="ParameterSymbol.ResolvedTypeSymbolId"/>).
    /// </summary>
    /// <param name="cancellationToken">Optional delete token to stop resolution.</param>
    public void ResolveAllTypes(CancellationToken cancellationToken = default)
    {
        foreach (var symbol in _graph.Symbols)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Resolves declared type on fields, constants, etc.
            if (symbol.DeclaredTypeName != null)
            {
                var typeInfo = ResolveType(symbol.DeclaredTypeName);
                symbol.ResolvedTypeSymbolId = typeInfo.UserTypeSymbol?.Id;
            }

            // Fix return type on functions and properties
            if (symbol.ReturnType != null)
            {
                var returnTypeInfo = ResolveType(symbol.ReturnType);
                symbol.ResolvedReturnTypeSymbolId = returnTypeInfo.UserTypeSymbol?.Id;
            }

            // Resolves types declared to formal parameters
            foreach (var param in symbol.Parameters)
            {
                if (param.DeclaredTypeName != null)
                {
                    var paramTypeInfo = ResolveType(param.DeclaredTypeName);
                    param.ResolvedTypeSymbolId = paramTypeInfo.UserTypeSymbol?.Id;
                }
            }
        }
    }
}
