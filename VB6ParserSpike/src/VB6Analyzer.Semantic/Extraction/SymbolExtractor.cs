using VB6Analyzer.Parser.Grammar;
using VB6Analyzer.Semantic.Model;

namespace VB6Analyzer.Semantic.Extraction;

/// <summary>
/// M2 — Symbol extraction. Cammina il parse tree ANTLR di un singolo modulo VB6
/// (già parsato da VisualBasic6Parser, M1) e produce Symbol + relazioni CONTAINS
/// nel CodeGraph condiviso.
///
/// Scope DELIBERATAMENTE limitato, per rispettare il principio della sezione 6
/// ("non bisogna confondere parsing e semantic analysis") e la definizione di M2
/// nella sezione 34 del charter:
///
///   ESTRATTO:    Method (Sub/Function), Property (Get/Let/Set uniti per nome),
///                Field (Dim/Public/Private/Const, sia a livello di modulo che
///                locali dentro un Sub/Function/Property), Enum (solo il nome,
///                non ancora i singoli valori)
///   RELAZIONI:   solo CONTAINS (strutturale, sempre Confidence.Exact — non
///                richiede alcuna risoluzione semantica)
///
///   FUORI SCOPE (rimandato a M3/M4, richiede symbol resolution):
///   - CALLS, USES_TYPE, INSTANTIATES, REFERENCES, READS, WRITES
///   - HANDLES_EVENT vero (richiederebbe conoscere i controlli dichiarati nel
///     .frm, che non parsiamo ancora — vedi sezione 44.4, rischio "metadata .frm").
///     Qui ci limitiamo a un'euristica sul NOME del Sub (Control_Event) riportata
///     separatamente in LikelyEventHandlerNames, che NON genera una relazione nel
///     CodeGraph: creare una relazione verso un simbolo che non esiste ancora
///     violerebbe il principio della sezione 11 ("il sistema non deve inventare
///     una relazione").
///   - INHERITS, IMPLEMENTS (visti nella grammar come blockStmt di alto livello,
///     non ancora collegati a un vero simbolo target)
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

    /// <summary>
    /// Sub il cui nome somiglia a "Controllo_Evento" (es. Command1_Click).
    /// Solo un'euristica per il report dello spike, NON una relazione nel grafo
    /// (vedi commento di classe sopra).
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
        _graph.AddContains(CurrentContainer, symbol, context.Start.Line);

        VisitBodyAsNewContainer(symbol, context.block());
        return null;
    }

    // ---------- Property Get/Let/Set (unificate per nome) ----------

    public override object? VisitPropertyGetStmt(VisualBasic6Parser.PropertyGetStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        var symbol = GetOrCreateProperty(name, context, out var wasCreated);
        if (wasCreated) _graph.AddContains(_moduleSymbol, symbol, context.Start.Line);

        VisitBodyAsNewContainer(symbol, context.block());
        return null;
    }

    public override object? VisitPropertySetStmt(VisualBasic6Parser.PropertySetStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        var symbol = GetOrCreateProperty(name, context, out var wasCreated);
        if (wasCreated) _graph.AddContains(_moduleSymbol, symbol, context.Start.Line);

        VisitBodyAsNewContainer(symbol, context.block());
        return null;
    }

    public override object? VisitPropertyLetStmt(VisualBasic6Parser.PropertyLetStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        var symbol = GetOrCreateProperty(name, context, out var wasCreated);
        if (wasCreated) _graph.AddContains(_moduleSymbol, symbol, context.Start.Line);

        VisitBodyAsNewContainer(symbol, context.block());
        return null;
    }

    // ---------- Variabili (Dim/Public/Private, sia a livello di modulo che locali) ----------

    public override object? VisitVariableSubStmt(VisualBasic6Parser.VariableSubStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        var symbol = CreateChildSymbol(SymbolKind.Field, name, context);
        _graph.AddContains(CurrentContainer, symbol, context.Start.Line);

        // Il tipo dichiarato (As Customer, As New Collection, ...) non viene ancora
        // estratto in un campo strutturato: il modello Symbol (sezione 9) non ha
        // ancora un campo "Type" dedicato, e USES_TYPE è relazione di M4. Rimandato
        // apposta per non introdurre nomi di accessor ANTLR non ancora verificati.

        return base.VisitVariableSubStmt(context);
    }

    // ---------- Costanti ----------

    public override object? VisitConstSubStmt(VisualBasic6Parser.ConstSubStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        var symbol = CreateChildSymbol(SymbolKind.Constant, name, context);
        _graph.AddContains(CurrentContainer, symbol, context.Start.Line);

        return base.VisitConstSubStmt(context);
    }

    // ---------- Enum (solo il nome per ora, non i singoli valori) ----------

    public override object? VisitEnumerationStmt(VisualBasic6Parser.EnumerationStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        var symbol = CreateChildSymbol(SymbolKind.Enum, name, context);
        _graph.AddContains(CurrentContainer, symbol, context.Start.Line);

        // I singoli enumerationStmt_Constant non vengono ancora estratti come
        // Symbol figli: rimandato, stesso motivo delle variabili sopra.
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

    private Symbol CreateChildSymbol(SymbolKind kind, string name, Antlr4.Runtime.ParserRuleContext context)
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

    private Symbol GetOrCreateProperty(string name, Antlr4.Runtime.ParserRuleContext context, out bool wasCreated)
    {
        if (_propertiesByName.TryGetValue(name, out var existing))
        {
            existing.LineEnd = Math.Max(existing.LineEnd, context.Stop?.Line ?? context.Start.Line);
            wasCreated = false;
            return existing;
        }

        // le property vivono sempre a livello di modulo in VB6, non dentro un altro Sub
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

    private static bool LooksLikeEventHandler(string subName)
    {
        var underscoreIndex = subName.LastIndexOf('_');
        if (underscoreIndex <= 0 || underscoreIndex == subName.Length - 1) return false;

        var eventPart = subName[(underscoreIndex + 1)..];
        return CommonEventNames.Contains(eventPart, StringComparer.OrdinalIgnoreCase);
    }
}
