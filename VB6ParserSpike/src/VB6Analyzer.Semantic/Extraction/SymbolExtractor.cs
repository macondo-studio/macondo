using Antlr4.Runtime;
using VB6Analyzer.Parser.Grammar;
using VB6Analyzer.Semantic.Model;

namespace VB6Analyzer.Semantic.Extraction;

/// <summary>
/// M2 + M3 — Symbol extraction arricchito per la risoluzione semantica.
/// Cammina il parse tree ANTLR di un singolo modulo VB6 e produce Symbol + relazioni CONTAINS
/// nel CodeGraph condiviso, catturando anche visibilità, tipi dichiarati, parametri di routine,
/// UDT e costanti di enum.
/// </summary>
public sealed class SymbolExtractor : VisualBasic6ParserBaseVisitor<object?>
{
    private static readonly string[] CommonEventNames =
    {
        "Click", "DblClick", "Change", "Load", "Unload", "Activate", "Deactivate",
        "KeyPress", "KeyDown", "KeyUp", "GotFocus", "LostFocus", "Timer",
        "MouseDown", "MouseUp", "MouseMove", "Resize", "Paint"
    };

    private readonly CodeGraph _graph;
    private readonly Symbol _moduleSymbol;
    private readonly string _filePath;
    private readonly Stack<Symbol> _containerStack = new();
    private readonly Dictionary<string, Symbol> _propertiesByName = new(StringComparer.OrdinalIgnoreCase);

    // Contesto corrente per variabili/costanti dichiarate in serie
    private Accessibility _currentVarAccessibility = Accessibility.Default;
    private bool _currentVarIsStatic;

    /// <summary>
    /// Sub il cui nome somiglia a "Controllo_Evento" (es. Command1_Click).
    /// </summary>
    public List<string> LikelyEventHandlerNames { get; } = new();

    public SymbolExtractor(CodeGraph graph, Symbol moduleSymbol, string filePath)
    {
        _graph = graph;
        _moduleSymbol = moduleSymbol;
        _filePath = filePath;
        _containerStack.Push(moduleSymbol);
    }

    private Symbol CurrentContainer => _containerStack.Peek();

    // ---------- Sub / Function ----------

    public override object? VisitSubStmt(VisualBasic6Parser.SubStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        var symbol = CreateChildSymbol(SymbolKind.Method, name, context);
        symbol.Accessibility = GetAccessibility(context.visibility());
        symbol.IsStatic = context.STATIC() is not null;

        var parameters = ExtractParameters(context.argList());
        symbol.Parameters.AddRange(parameters);

        _graph.AddContains(CurrentContainer, symbol, context.Start.Line);

        if (LooksLikeEventHandler(name))
            LikelyEventHandlerNames.Add(name);

        VisitBodyAsNewContainer(symbol, context.block());
        return null;
    }

    public override object? VisitFunctionStmt(VisualBasic6Parser.FunctionStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        var symbol = CreateChildSymbol(SymbolKind.Method, name, context);
        symbol.Accessibility = GetAccessibility(context.visibility());
        symbol.IsStatic = context.STATIC() is not null;

        var returnType = ExtractTypeName(context.asTypeClause());
        symbol.ReturnType = returnType;
        symbol.DeclaredTypeName = returnType;

        var parameters = ExtractParameters(context.argList());
        symbol.Parameters.AddRange(parameters);

        _graph.AddContains(CurrentContainer, symbol, context.Start.Line);

        VisitBodyAsNewContainer(symbol, context.block());
        return null;
    }

    // ---------- Property Get/Let/Set (unificate per nome) ----------

    public override object? VisitPropertyGetStmt(VisualBasic6Parser.PropertyGetStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        var symbol = GetOrCreateProperty(name, context, out var wasCreated);
        symbol.Accessibility = GetAccessibility(context.visibility());
        symbol.IsStatic = context.STATIC() is not null;

        var returnType = ExtractTypeName(context.asTypeClause());
        if (returnType != null)
        {
            symbol.ReturnType = returnType;
            symbol.DeclaredTypeName = returnType;
        }

        var parameters = ExtractParameters(context.argList());
        MergeParameters(symbol.Parameters, parameters);

        if (wasCreated) _graph.AddContains(_moduleSymbol, symbol, context.Start.Line);

        VisitBodyAsNewContainer(symbol, context.block());
        return null;
    }

    public override object? VisitPropertySetStmt(VisualBasic6Parser.PropertySetStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        var symbol = GetOrCreateProperty(name, context, out var wasCreated);
        if (symbol.Accessibility == Accessibility.Default)
            symbol.Accessibility = GetAccessibility(context.visibility());

        var parameters = ExtractParameters(context.argList());
        MergeParameters(symbol.Parameters, parameters);

        if (wasCreated) _graph.AddContains(_moduleSymbol, symbol, context.Start.Line);

        VisitBodyAsNewContainer(symbol, context.block());
        return null;
    }

    public override object? VisitPropertyLetStmt(VisualBasic6Parser.PropertyLetStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        var symbol = GetOrCreateProperty(name, context, out var wasCreated);
        if (symbol.Accessibility == Accessibility.Default)
            symbol.Accessibility = GetAccessibility(context.visibility());

        var parameters = ExtractParameters(context.argList());
        MergeParameters(symbol.Parameters, parameters);

        // Se non abbiamo ancora un DeclaredTypeName sulla Property, prendiamo il tipo dell'ultimo parametro di Let (il value)
        if (symbol.DeclaredTypeName == null && parameters.Count > 0)
        {
            symbol.DeclaredTypeName = parameters[^1].DeclaredTypeName;
        }

        if (wasCreated) _graph.AddContains(_moduleSymbol, symbol, context.Start.Line);

        VisitBodyAsNewContainer(symbol, context.block());
        return null;
    }

    // ---------- Variabili (Dim/Public/Private/Static, sia a livello di modulo che locali) ----------

    public override object? VisitVariableStmt(VisualBasic6Parser.VariableStmtContext context)
    {
        var prevAccessibility = _currentVarAccessibility;
        var prevIsStatic = _currentVarIsStatic;

        if (context.DIM() is not null)
            _currentVarAccessibility = CurrentContainer == _moduleSymbol ? Accessibility.Private : Accessibility.Default;
        else if (context.STATIC() is not null)
        {
            _currentVarAccessibility = Accessibility.Default;
            _currentVarIsStatic = true;
        }
        else if (context.visibility() is not null)
            _currentVarAccessibility = GetAccessibility(context.visibility());

        var result = base.VisitVariableStmt(context);

        _currentVarAccessibility = prevAccessibility;
        _currentVarIsStatic = prevIsStatic;
        return result;
    }

    public override object? VisitVariableSubStmt(VisualBasic6Parser.VariableSubStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        var symbol = CreateChildSymbol(SymbolKind.Field, name, context);
        symbol.Accessibility = _currentVarAccessibility;
        symbol.IsStatic = _currentVarIsStatic;
        symbol.DeclaredTypeName = ExtractTypeName(context.asTypeClause());
        symbol.IsNewInstance = context.asTypeClause()?.NEW() is not null;
        symbol.IsArray = context.LPAREN() is not null;

        _graph.AddContains(CurrentContainer, symbol, context.Start.Line);
        return base.VisitVariableSubStmt(context);
    }

    // ---------- Costanti ----------

    public override object? VisitConstStmt(VisualBasic6Parser.ConstStmtContext context)
    {
        var prevAccessibility = _currentVarAccessibility;
        _currentVarAccessibility = GetAccessibility(context.publicPrivateGlobalVisibility());

        var result = base.VisitConstStmt(context);

        _currentVarAccessibility = prevAccessibility;
        return result;
    }

    public override object? VisitConstSubStmt(VisualBasic6Parser.ConstSubStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        var symbol = CreateChildSymbol(SymbolKind.Constant, name, context);
        symbol.Accessibility = _currentVarAccessibility;
        symbol.DeclaredTypeName = ExtractTypeName(context.asTypeClause());

        _graph.AddContains(CurrentContainer, symbol, context.Start.Line);
        return base.VisitConstSubStmt(context);
    }

    // ---------- Enum ----------

    public override object? VisitEnumerationStmt(VisualBasic6Parser.EnumerationStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        var symbol = CreateChildSymbol(SymbolKind.Enum, name, context);
        symbol.Accessibility = GetAccessibility(context.publicPrivateVisibility());
        _graph.AddContains(CurrentContainer, symbol, context.Start.Line);

        _containerStack.Push(symbol);
        foreach (var constantCtx in context.enumerationStmt_Constant())
        {
            var constName = constantCtx.ambiguousIdentifier().GetText();
            var constSymbol = CreateChildSymbol(SymbolKind.Constant, constName, constantCtx);
            constSymbol.Accessibility = Accessibility.Public;
            constSymbol.DeclaredTypeName = symbol.Name;
            _graph.AddContains(symbol, constSymbol, constantCtx.Start.Line);
        }
        _containerStack.Pop();

        return null;
    }

    // ---------- Type (UDT - User Defined Types) ----------

    public override object? VisitTypeStmt(VisualBasic6Parser.TypeStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        var symbol = CreateChildSymbol(SymbolKind.Type, name, context);
        symbol.Accessibility = GetAccessibility(context.visibility());
        _graph.AddContains(CurrentContainer, symbol, context.Start.Line);

        _containerStack.Push(symbol);
        foreach (var elem in context.typeStmt_Element())
        {
            var elemName = elem.ambiguousIdentifier().GetText();
            var elemSymbol = CreateChildSymbol(SymbolKind.Field, elemName, elem);
            elemSymbol.Accessibility = Accessibility.Public;
            elemSymbol.DeclaredTypeName = ExtractTypeName(elem.asTypeClause());
            elemSymbol.IsArray = elem.LPAREN() is not null;
            _graph.AddContains(symbol, elemSymbol, elem.Start.Line);
        }
        _containerStack.Pop();

        return null;
    }

    // ---------- helper ----------

    private void VisitBodyAsNewContainer(Symbol container, VisualBasic6Parser.BlockContext? body)
    {
        _containerStack.Push(container);
        if (body is not null)
        {
            Visit(body);
        }
        _containerStack.Pop();
    }

    private Symbol CreateChildSymbol(SymbolKind kind, string name, ParserRuleContext context)
    {
        var symbol = new Symbol
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            Name = name,
            QualifiedName = $"{CurrentContainer.QualifiedName}.{name}",
            FilePath = _filePath,
            LineStart = context.Start.Line,
            LineEnd = context.Stop?.Line ?? context.Start.Line
        };
        _graph.Symbols.Add(symbol);
        return symbol;
    }

    private Symbol GetOrCreateProperty(string name, ParserRuleContext context, out bool wasCreated)
    {
        if (_propertiesByName.TryGetValue(name, out var existing))
        {
            existing.LineEnd = Math.Max(existing.LineEnd, context.Stop?.Line ?? context.Start.Line);
            wasCreated = false;
            return existing;
        }

        var symbol = new Symbol
        {
            Id = Guid.NewGuid(),
            Kind = SymbolKind.Property,
            Name = name,
            QualifiedName = $"{_moduleSymbol.QualifiedName}.{name}",
            FilePath = _filePath,
            LineStart = context.Start.Line,
            LineEnd = context.Stop?.Line ?? context.Start.Line
        };
        _graph.Symbols.Add(symbol);
        _propertiesByName[name] = symbol;
        wasCreated = true;
        return symbol;
    }

    private static void MergeParameters(List<ParameterSymbol> target, List<ParameterSymbol> incoming)
    {
        foreach (var p in incoming)
        {
            if (!target.Any(t => string.Equals(t.Name, p.Name, StringComparison.OrdinalIgnoreCase)))
            {
                target.Add(p);
            }
        }
    }

    private static string? ExtractTypeName(VisualBasic6Parser.AsTypeClauseContext? ctx)
    {
        if (ctx?.type_() is null) return null;
        return ctx.type_().GetText();
    }

    private static List<ParameterSymbol> ExtractParameters(VisualBasic6Parser.ArgListContext? argListCtx)
    {
        var result = new List<ParameterSymbol>();
        if (argListCtx is null) return result;

        foreach (var argCtx in argListCtx.arg())
        {
            var name = argCtx.ambiguousIdentifier().GetText();
            var isByVal = argCtx.BYVAL() is not null;
            var isByRef = argCtx.BYREF() is not null || !isByVal;
            var isOptional = argCtx.OPTIONAL() is not null;
            var isParamArray = argCtx.PARAMARRAY() is not null;
            var typeName = ExtractTypeName(argCtx.asTypeClause());
            var defVal = argCtx.argDefaultValue()?.valueStmt()?.GetText();

            result.Add(new ParameterSymbol
            {
                Name = name,
                DeclaredTypeName = typeName,
                IsByVal = isByVal,
                IsByRef = isByRef,
                IsOptional = isOptional,
                IsParamArray = isParamArray,
                DefaultValue = defVal,
                Line = argCtx.Start.Line
            });
        }
        return result;
    }

    private static Accessibility GetAccessibility(VisualBasic6Parser.VisibilityContext? ctx) =>
        ctx?.GetText().ToUpperInvariant() switch
        {
            "PUBLIC" => Accessibility.Public,
            "PRIVATE" => Accessibility.Private,
            "FRIEND" => Accessibility.Friend,
            "GLOBAL" => Accessibility.Global,
            _ => Accessibility.Default
        };

    private static Accessibility GetAccessibility(VisualBasic6Parser.PublicPrivateVisibilityContext? ctx) =>
        ctx?.GetText().ToUpperInvariant() switch
        {
            "PUBLIC" => Accessibility.Public,
            "PRIVATE" => Accessibility.Private,
            _ => Accessibility.Default
        };

    private static Accessibility GetAccessibility(VisualBasic6Parser.PublicPrivateGlobalVisibilityContext? ctx) =>
        ctx?.GetText().ToUpperInvariant() switch
        {
            "PUBLIC" => Accessibility.Public,
            "PRIVATE" => Accessibility.Private,
            "GLOBAL" => Accessibility.Global,
            _ => Accessibility.Default
        };

    private static bool LooksLikeEventHandler(string subName)
    {
        var underscoreIndex = subName.LastIndexOf('_');
        if (underscoreIndex <= 0 || underscoreIndex == subName.Length - 1) return false;

        var eventPart = subName[(underscoreIndex + 1)..];
        return CommonEventNames.Contains(eventPart, StringComparer.OrdinalIgnoreCase);
    }
}
