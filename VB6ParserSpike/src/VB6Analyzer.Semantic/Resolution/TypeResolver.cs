using VB6Analyzer.Semantic.Model;

namespace VB6Analyzer.Semantic.Resolution;

/// <summary>
/// Informazioni di classificazione di un tipo in VB6.
/// </summary>
public sealed class TypeInfo
{
    public required string Name { get; init; }
    public bool IsPrimitive { get; init; }
    public bool IsDynamicOrVariant { get; init; }
    public Symbol? UserTypeSymbol { get; init; }
    public bool IsResolved => IsPrimitive || UserTypeSymbol != null;

    public override string ToString() =>
        IsPrimitive ? $"[Primitive] {Name}" : (UserTypeSymbol != null ? $"[UserType] {UserTypeSymbol.Kind} {UserTypeSymbol.QualifiedName}" : $"[Unknown/External] {Name}");
}

/// <summary>
/// Motore di risoluzione dei tipi per VB6 (sezione 12 e M3 del charter).
/// Riconosce i tipi primitivi integrati del linguaggio e risolve i tipi definiti nel progetto
/// (Classi, Form, UserControl, UDT, Enum).
/// </summary>
public sealed class TypeResolver
{
    private static readonly HashSet<string> PrimitiveTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Boolean", "Byte", "Integer", "Long", "Single", "Double",
        "Currency", "Date", "String", "Collection"
    };

    private static readonly HashSet<string> DynamicTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Object", "Variant"
    };

    private readonly CodeGraph _graph;
    private readonly Dictionary<string, Symbol> _userTypesByName = new(StringComparer.OrdinalIgnoreCase);

    public TypeResolver(CodeGraph graph)
    {
        _graph = graph;
        IndexUserTypes();
    }

    private void IndexUserTypes()
    {
        foreach (var symbol in _graph.Symbols)
        {
            if (symbol.Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.UserControl or SymbolKind.Type or SymbolKind.Enum)
            {
                // In VB6 i tipi definiti dall'utente sono indicizzati sia per nome semplice (es. "Customer")
                // sia per nome qualificato se necessario.
                _userTypesByName[symbol.Name] = symbol;
            }
        }
    }

    /// <summary>
    /// Risolve un nome di tipo testuale in un TypeInfo strutturato.
    /// </summary>
    public TypeInfo ResolveType(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            // In VB6 se il tipo non è specificato (es. Dim x), è Variant (dynamic)
            return new TypeInfo
            {
                Name = "Variant",
                IsPrimitive = true,
                IsDynamicOrVariant = true,
                UserTypeSymbol = null
            };
        }

        var cleanName = typeName.Trim();

        // Rimuove eventuali parentesi di array (es. "String()" o "Customer()")
        if (cleanName.EndsWith("()", StringComparison.Ordinal))
            cleanName = cleanName[..^2].Trim();

        if (DynamicTypes.Contains(cleanName))
        {
            return new TypeInfo
            {
                Name = cleanName,
                IsPrimitive = true,
                IsDynamicOrVariant = true,
                UserTypeSymbol = null
            };
        }

        if (PrimitiveTypes.Contains(cleanName))
        {
            return new TypeInfo
            {
                Name = cleanName,
                IsPrimitive = true,
                IsDynamicOrVariant = false,
                UserTypeSymbol = null
            };
        }

        if (_userTypesByName.TryGetValue(cleanName, out var userSymbol))
        {
            return new TypeInfo
            {
                Name = cleanName,
                IsPrimitive = false,
                IsDynamicOrVariant = false,
                UserTypeSymbol = userSymbol
            };
        }

        // Tipo non trovato nel progetto locale (es. libreria COM esterna tipo ADODB.Recordset)
        return new TypeInfo
        {
            Name = cleanName,
            IsPrimitive = false,
            IsDynamicOrVariant = false,
            UserTypeSymbol = null
        };
    }

    /// <summary>
    /// Esegue la risoluzione di tutti i tipi dichiarati nel CodeGraph,
    /// popolando ResolvedTypeSymbolId su simboli e parametri.
    /// </summary>
    public void ResolveAllTypes()
    {
        foreach (var symbol in _graph.Symbols)
        {
            if (symbol.DeclaredTypeName != null)
            {
                var typeInfo = ResolveType(symbol.DeclaredTypeName);
                symbol.ResolvedTypeSymbolId = typeInfo.UserTypeSymbol?.Id;
            }

            if (symbol.ReturnType != null)
            {
                var returnTypeInfo = ResolveType(symbol.ReturnType);
                symbol.ResolvedReturnTypeSymbolId = returnTypeInfo.UserTypeSymbol?.Id;
            }

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
