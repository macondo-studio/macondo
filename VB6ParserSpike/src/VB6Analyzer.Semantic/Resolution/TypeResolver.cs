using Macondo.Semantic.Model;

namespace Macondo.Semantic.Resolution;

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
/// Motore di risoluzione dei tipi per VB6.
/// 
/// RESPONSABILITÀ:
/// 1. Mappatura Tipi Primitivi: riconosce i tipi scalari VB6 (Boolean, Byte, Integer, Long, Single, Double, Currency, Date, String, Collection).
/// 2. Gestione Tipi Dinamici: identifica Variant e Object per instradare la successiva risoluzione verso late binding.
/// 3. Risoluzione Tipi Utente: mappa clausole "As Customer" verso il corrispondente <see cref="SymbolKind.Class"/> "Customer" nel CodeGraph.
/// 4. Risoluzione Globale: attraversa tutti i simboli e parametri del grafo popolando i riferimenti <see cref="Symbol.ResolvedTypeSymbolId"/>.
/// </summary>
public sealed class TypeResolver
{
    /// <summary>Insieme dei tipi primitivi VB6, C# e Java predefiniti (case-insensitive).</summary>
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
    /// Tipi a binding dinamico / late binding in VB6.
    /// Include sia i tipi generici non vincolati a tempo di compilazione ('Object', 'Variant', 'dynamic'),
    /// sia i tipi base di interfaccia utente intrinseci del runtime VB6 ('Form', 'Control', 'MDIForm').
    /// In VB6, variabili dichiarate 'As Form' o 'As Control' non espongono staticamente i metodi custom
    /// delle singole form/controlli derivati: le relative chiamate a membro avvengono via IDispatch (late binding).
    /// </summary>
    private static readonly HashSet<string> DynamicTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Object", "Variant", "dynamic", "Form", "Control", "MDIForm"
    };

    private readonly CodeGraph _graph;

    /// <summary>Indice dei tipi definiti nel progetto per nome semplice (es. "Customer" -> Symbol Class Customer).</summary>
    private readonly Dictionary<string, Symbol> _userTypesByName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Inizializza il resolver indicizzando tutti i tipi dichiarati nel grafo.
    /// </summary>
    /// <param name="graph">Il CodeGraph contenente tutti i simboli del progetto.</param>
    /// <param name="cancellationToken">Token opzionale di cancellazione per interrompere l'elaborazione.</param>
    public TypeResolver(CodeGraph graph, CancellationToken cancellationToken = default)
    {
        _graph = graph;

        // Costruisce l'indice dei tipi utente (Class, Form, UserControl, Module .bas, Type UDT, Enum, Interface, Struct, Record) presenti nel grafo.
        // Include esplicitamente SymbolKind.Module in modo che funzioni e proprietà che restituiscono istanze o riferimenti
        // a moduli standard .bas possano risolvere correttamente il relativo contenitore semantico.
        foreach (var symbol in _graph.Symbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (symbol.Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.UserControl or SymbolKind.Module or SymbolKind.Type or SymbolKind.Enum
                            or SymbolKind.Interface or SymbolKind.Struct or SymbolKind.Record)
            {
                // I tipi utente sono accessibili per nome semplice o qualificato
                _userTypesByName[symbol.Name] = symbol;
                if (!string.IsNullOrEmpty(symbol.QualifiedName) && !symbol.QualifiedName.Equals(symbol.Name, StringComparison.OrdinalIgnoreCase))
                    _userTypesByName[symbol.QualifiedName] = symbol;
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
            return new TypeInfo
            {
                Name = "Variant",
                IsPrimitive = true,
                IsDynamicOrVariant = true,
                UserTypeSymbol = null
            };

        var cleanName = typeName.Trim();

        // Rimuove la notazione di array se presente (es. "Customer()" o "String()")
        if (cleanName.EndsWith("()", StringComparison.Ordinal))
            cleanName = cleanName[..^2].Trim();

        // 1. Verifica se è un tipo a tipizzazione dinamica (Object o Variant)
        if (DynamicTypes.Contains(cleanName))
            return new TypeInfo
            {
                Name = cleanName,
                IsPrimitive = true,
                IsDynamicOrVariant = true,
                UserTypeSymbol = null
            };

        // 2. Verifica se è un tipo primitivo integrato
        if (PrimitiveTypes.Contains(cleanName))
            return new TypeInfo
            {
                Name = cleanName,
                IsPrimitive = true,
                IsDynamicOrVariant = false,
                UserTypeSymbol = null
            };

        // 3. Verifica se è un tipo dichiarato dall'utente nel progetto locale per nome semplice o qualificato
        if (_userTypesByName.TryGetValue(cleanName, out var userSymbol))
            return new TypeInfo
            {
                Name = cleanName,
                IsPrimitive = false,
                IsDynamicOrVariant = false,
                UserTypeSymbol = userSymbol
            };

        // 3b. Fallback per tipi qualificati con prefisso di progetto o libreria (es. "DUE.cntrDestinazione").
        // Nel designer visivo delle form VB6 (sezioni 'Begin ... End'), le istanze di UserControl del progetto
        // vengono spesso generate con il nome del progetto anteposto al nome del controllo (es. 'Begin DUE.cntrDestinazione cntrDestinazione').
        // Poiché il controllo utente è definito nel file sorgente come 'cntrDestinazione.ctl', estraiamo il suffisso non qualificato
        // dopo l'ultimo punto per risolvere correttamente il simbolo di tipo corrispondente presente nel grafo.
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
    /// <param name="cancellationToken">Token opzionale di cancellazione per interrompere la risoluzione.</param>
    public void ResolveAllTypes(CancellationToken cancellationToken = default)
    {
        foreach (var symbol in _graph.Symbols)
        {
            cancellationToken.ThrowIfCancellationRequested();

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
