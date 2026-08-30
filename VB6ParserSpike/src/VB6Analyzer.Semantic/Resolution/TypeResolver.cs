using VB6Analyzer.Semantic.Model;

namespace VB6Analyzer.Semantic.Resolution;

/// <summary>
/// Descrittore della classificazione semantica di un tipo di dato in VB6.
/// Distingue tra tipi primitivi integrati, tipi dinamici (Object/Variant) e tipi definiti dall'utente nel progetto.
/// </summary>
public sealed class TypeInfo
{
    /// <summary>Nome pulito del tipo (es. "Long", "Customer", "Variant").</summary>
    public required string Name { get; init; }

    /// <summary>Indica se è un tipo primitivo standard del linguaggio (Integer, Long, String, Boolean, Double, ecc.).</summary>
    public bool IsPrimitive { get; init; }

    /// <summary>Indica se è un tipo non vincolato a compilazione / a tipizzazione dinamica ('Object' o 'Variant').</summary>
    public bool IsDynamicOrVariant { get; init; }

    /// <summary>Il simbolo utente nel CodeGraph corrispondente al tipo (se Class, Form, UserControl, Type o Enum).</summary>
    public Symbol? UserTypeSymbol { get; init; }

    /// <summary>Indica se il tipo è noto (primitivo o definito dall'utente nel progetto).</summary>
    public bool IsResolved => IsPrimitive || UserTypeSymbol != null;

    public override string ToString() =>
        IsPrimitive ? $"[Primitive] {Name}" : (UserTypeSymbol != null ? $"[UserType] {UserTypeSymbol.Kind} {UserTypeSymbol.QualifiedName}" : $"[Unknown/External] {Name}");
}

/// <summary>
/// Motore di risoluzione dei tipi per VB6 (sezione 12 e Milestone M3 del charter).
/// 
/// RESPONSABILITÀ:
/// 1. Mappatura Tipi Primitivi: riconosce i tipi scalari VB6 (Boolean, Byte, Integer, Long, Single, Double, Currency, Date, String, Collection).
/// 2. Gestione Tipi Dinamici: identifica Variant e Object per instradare la successiva risoluzione verso late binding.
/// 3. Risoluzione Tipi Utente: mappa clausole "As Customer" verso il corrispondente <see cref="SymbolKind.Class"/> "Customer" nel CodeGraph.
/// 4. Risoluzione Globale: attraversa tutti i simboli e parametri del grafo popolando i riferimenti <see cref="Symbol.ResolvedTypeSymbolId"/>.
/// </summary>
public sealed class TypeResolver
{
    /// <summary>Insieme dei tipi primitivi VB6 predefiniti (case-insensitive).</summary>
    private static readonly HashSet<string> PrimitiveTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Boolean", "Byte", "Integer", "Long", "Single", "Double",
        "Currency", "Date", "String", "Collection"
    };

    /// <summary>Tipi a binding dinamico / late binding in VB6.</summary>
    private static readonly HashSet<string> DynamicTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Object", "Variant"
    };

    private readonly CodeGraph _graph;

    /// <summary>Indice dei tipi definiti nel progetto per nome semplice (es. "Customer" -> Symbol Class Customer).</summary>
    private readonly Dictionary<string, Symbol> _userTypesByName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Inizializza il resolver indicizzando tutti i tipi dichiarati nel grafo.
    /// </summary>
    /// <param name="graph">Il CodeGraph contenente tutti i simboli del progetto.</param>
    public TypeResolver(CodeGraph graph)
    {
        _graph = graph;
        IndexUserTypes();
    }

    /// <summary>
    /// Costruisce l'indice dei tipi utente (Class, Form, UserControl, Type UDT, Enum) presenti nel grafo.
    /// </summary>
    private void IndexUserTypes()
    {
        foreach (var symbol in _graph.Symbols)
        {
            if (symbol.Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.UserControl or SymbolKind.Type or SymbolKind.Enum)
            {
                // In VB6 i tipi utente sono univoci a livello di progetto e accessibili per nome
                _userTypesByName[symbol.Name] = symbol;
            }
        }
    }

    /// <summary>
    /// Risolve una stringa di tipo (es. "Customer", "Long", "String()", "Object")
    /// nella relativa classificazione <see cref="TypeInfo"/>.
    /// </summary>
    /// <param name="typeName">Il nome del tipo dichiarato nel sorgente.</param>
    /// <returns>Le informazioni semantiche del tipo (<see cref="TypeInfo"/>).</returns>
    public TypeInfo ResolveType(string? typeName)
    {
        // In VB6, se la clausola 'As Tipo' è omessa, il tipo predefinito è Variant (dinamico)
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return new TypeInfo
            {
                Name = "Variant",
                IsPrimitive = true,
                IsDynamicOrVariant = true,
                UserTypeSymbol = null
            };
        }

        var cleanName = typeName.Trim();

        // Rimuove la notazione di array se presente (es. "Customer()" o "String()")
        if (cleanName.EndsWith("()", StringComparison.Ordinal))
            cleanName = cleanName[..^2].Trim();

        // 1. Verifica se è un tipo a tipizzazione dinamica (Object o Variant)
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

        // 2. Verifica se è un tipo primitivo integrato
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

        // 3. Verifica se è un tipo dichiarato dall'utente nel progetto locale
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

        // 4. Tipo esterno non presente nel progetto (es. libreria COM ADODB.Recordset, MSXML2.DOMDocument, ecc.)
        return new TypeInfo
        {
            Name = cleanName,
            IsPrimitive = false,
            IsDynamicOrVariant = false,
            UserTypeSymbol = null
        };
    }

    /// <summary>
    /// Esegue il passaggio globale di type resolution su tutti i simboli e parametri del <see cref="CodeGraph"/>,
    /// popolando gli ID dei tipi risolti (<see cref="Symbol.ResolvedTypeSymbolId"/>, <see cref="Symbol.ResolvedReturnTypeSymbolId"/>,
    /// <see cref="ParameterSymbol.ResolvedTypeSymbolId"/>).
    /// </summary>
    public void ResolveAllTypes()
    {
        foreach (var symbol in _graph.Symbols)
        {
            // Risolve il tipo dichiarato su campi, costanti, ecc.
            if (symbol.DeclaredTypeName != null)
            {
                var typeInfo = ResolveType(symbol.DeclaredTypeName);
                symbol.ResolvedTypeSymbolId = typeInfo.UserTypeSymbol?.Id;
            }

            // Risolve il tipo di ritorno su funzioni e proprietà
            if (symbol.ReturnType != null)
            {
                var returnTypeInfo = ResolveType(symbol.ReturnType);
                symbol.ResolvedReturnTypeSymbolId = returnTypeInfo.UserTypeSymbol?.Id;
            }

            // Risolve i tipi dichiarati sui parametri formali
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
