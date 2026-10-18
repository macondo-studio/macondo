using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using Macondo.Semantic.Model;
using Symbol = Macondo.Semantic.Model.Symbol;
using SymbolKind = Macondo.Semantic.Model.SymbolKind;
using Accessibility = Macondo.Semantic.Model.Accessibility;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// SyntaxWalker specializzato di Roslyn (<see cref="VisualBasicSyntaxWalker"/>) che percorre ricorsivamente
/// l'albero sintattico (AST) di Visual Basic .NET per estrarre dichiarazioni strutturate, gerarchie di contenimento,
/// invocazioni di metodi, relazioni OOP ed espressioni di istanziazione.
/// 
/// ARCHITETTURA E RESPONSABILITÀ:
/// 1. Gestione Scope e Namespace:
///    - <see cref="_namespaceStack"/>: tiene traccia dei blocchi di namespace annidati per comporre i <see cref="Symbol.QualifiedName"/> corretti.
///    - <see cref="_scopeStack"/>: mantiene lo stack dei contenitori attivi (Namespace -> Tipo -> Metodo -> Blocco locale). Ogni nuovo simbolo
///      figlio viene registrato nel <see cref="CodeGraph"/> e collegato al genitore tramite arco di contenimento (<see cref="RelationshipKind.Contains"/>).
/// 
/// 2. Tipologie di Simboli Riconosciute:
///    - Classi (<see cref="ClassBlockSyntax"/>) mappate su <see cref="SymbolKind.Class"/> con supporto a modificatori (MustInherit, NotInheritable, Partial, Shared).
///    - Interfacce (<see cref="InterfaceBlockSyntax"/>) mappate su <see cref="SymbolKind.Interface"/>.
///    - Strutture (<see cref="StructureBlockSyntax"/>) mappate su <see cref="SymbolKind.Struct"/>.
///    - Moduli Standard (<see cref="ModuleBlockSyntax"/>) mappati su <see cref="SymbolKind.Module"/>, trattati come tipi statici contenitori di utility.
///    - Enumerazioni (<see cref="EnumBlockSyntax"/>) con creazione automatica di simboli <see cref="SymbolKind.Constant"/> per ogni valore enum dichiarato.
/// 
/// 3. Metodi e Membri:
///    - Routine Sub e Function (<see cref="MethodBlockSyntax"/> e <see cref="MethodStatementSyntax"/> per metodi astratti/interfaccia).
///    - Costruttori OOP classici (<see cref="ConstructorBlockSyntax"/>, "Sub New").
///    - Proprietà sia estese con blocchi Get/Set (<see cref="PropertyBlockSyntax"/>) sia Auto-Properties / interfaccia (<see cref="PropertyStatementSyntax"/>).
///    - Operatori sovraccaricati (<see cref="OperatorBlockSyntax"/>).
///    - Campi e costanti (<see cref="FieldDeclarationSyntax"/>).
///    - Eventi personalizzati (<see cref="EventStatementSyntax"/> ed <see cref="EventBlockSyntax"/>).
/// 
/// 4. Relazioni Semantiche & Call Graph:
///    - Relazioni OOP: tracciamento di clausole 'Inherits' (ereditarietà di classe base o interfaccia) e 'Implements' (implementazione interfaccia).
///    - Siti di Chiamata (Call Sites): intercettazione di chiamate a metodo qualificate ('receiver.Method()'), invocazioni 'Me'/'MyBase' e non qualificate.
///    - Istanziazioni Oggetto: intercettazione di espressioni 'New TypeName(...)' e clausole dichiarative 'Dim x As New TypeName(...)'.
/// </summary>
internal sealed class VisualBasicSymbolWalker : VisualBasicSyntaxWalker
{
    private readonly CodeGraph _graph;
    private readonly string _filePath;

    /// <summary>Stack dei simboli contenitori attivi (utilizzato per stabilire le relazioni CONTAINS nel grafo).</summary>
    private readonly Stack<Symbol> _scopeStack = new();

    /// <summary>Stack dei segmenti di namespace correnti (utilizzato per costruire i QualifiedName completi).</summary>
    private readonly Stack<string> _namespaceStack = new();

    /// <summary>Simboli dichiarati a livello radice (non contenuti in altri tipi all'interno del file).</summary>
    public List<Symbol> TopLevelSymbols { get; } = new();

    /// <summary>Call-site di invocazione rilevati all'interno dei metodi o proprietà analizzati.</summary>
    public List<VisualBasicNetCallSite> CallSites { get; } = new();

    /// <summary>Istanziazioni di oggetti ('New Type()') rilevate nei metodi o nelle inizializzazioni di campo.</summary>
    public List<VisualBasicNetInstantiation> Instantiations { get; } = new();

    /// <summary>Clausole di ereditarietà ('Inherits') e implementazione di interfacce ('Implements') estratte.</summary>
    public List<VisualBasicNetTypeInheritance> Inheritances { get; } = new();

    /// <summary>
    /// Inizializza una nuova istanza del walker semantico per il file sorgente VB.NET specificato.
    /// </summary>
    /// <param name="graph">Il <see cref="CodeGraph"/> condiviso in cui registrare simboli e relazioni CONTAINS.</param>
    /// <param name="filePath">Percorso assoluto o relativo del file sorgente VB.NET analizzato.</param>
    public VisualBasicSymbolWalker(CodeGraph graph, string filePath)
    {
        _graph = graph;
        _filePath = filePath;
    }

    /// <summary>
    /// Restituisce la stringa del namespace completo corrente unendo con punti i segmenti presenti nello stack.
    /// </summary>
    private string CurrentNamespace => string.Join(".", _namespaceStack.Reverse());

    // =========================================================================
    // GESTIONE DEI BLOCCHI DI NAMESPACE
    // =========================================================================

    /// <summary>
    /// Visita una dichiarazione di blocco namespace ("Namespace Foo.Bar ... End Namespace").
    /// Spinge il nome sullo stack, visita tutti i tipi e i membri contenuti, e rimuove il namespace al termine.
    /// </summary>
    public override void VisitNamespaceBlock(NamespaceBlockSyntax node)
    {
        _namespaceStack.Push(node.NamespaceStatement.Name.ToString());
        base.VisitNamespaceBlock(node);
        _namespaceStack.Pop();
    }

    // =========================================================================
    // DICHIARAZIONI DI TIPI (Class, Interface, Structure, Module, Enum)
    // =========================================================================

    /// <summary>
    /// Visita la dichiarazione di una classe VB.NET ("Class Foo ... End Class").
    /// Registra il simbolo di classe, estrae le clausole 'Inherits' e 'Implements', e visita i membri interni.
    /// </summary>
    public override void VisitClassBlock(ClassBlockSyntax node)
    {
        var stmt = node.ClassStatement;
        ProcessTypeBlock(
            node,
            SymbolKind.Class,
            stmt.Identifier.ValueText,
            stmt.Modifiers,
            node.Inherits,
            node.Implements,
            () => base.VisitClassBlock(node));
    }

    /// <summary>
    /// Visita la dichiarazione di un'interfaccia VB.NET ("Interface IFoo ... End Interface").
    /// Registra il simbolo interfaccia, estrae eventuali interfacce base ('Inherits') e visita le firme dei metodi.
    /// </summary>
    public override void VisitInterfaceBlock(InterfaceBlockSyntax node)
    {
        var stmt = node.InterfaceStatement;
        ProcessTypeBlock(
            node,
            SymbolKind.Interface,
            stmt.Identifier.ValueText,
            stmt.Modifiers,
            node.Inherits,
            node.Implements,
            () => base.VisitInterfaceBlock(node));
    }

    /// <summary>
    /// Visita la dichiarazione di una struttura VB.NET ("Structure Point ... End Structure").
    /// Registra il simbolo struttura, estrae eventuali interfacce implementate ('Implements') e visita i campi/metodi.
    /// </summary>
    public override void VisitStructureBlock(StructureBlockSyntax node)
    {
        var stmt = node.StructureStatement;
        ProcessTypeBlock(
            node,
            SymbolKind.Struct,
            stmt.Identifier.ValueText,
            stmt.Modifiers,
            node.Inherits,
            node.Implements,
            () => base.VisitStructureBlock(node));
    }

    /// <summary>
    /// Visita la dichiarazione di un modulo standard VB.NET ("Module Utils ... End Module").
    /// I moduli VB.NET contengono funzioni e procedure statiche globali all'interno del loro namespace.
    /// Vengono registrati come <see cref="SymbolKind.Module"/> con flag <see cref="Symbol.IsStatic"/> impostato a true.
    /// </summary>
    public override void VisitModuleBlock(ModuleBlockSyntax node)
    {
        var stmt = node.ModuleStatement;
        ProcessTypeBlock(
            node,
            SymbolKind.Module,
            stmt.Identifier.ValueText,
            stmt.Modifiers,
            node.Inherits,
            node.Implements,
            () => base.VisitModuleBlock(node),
            forceStatic: true);
    }

    /// <summary>
    /// Visita la dichiarazione di un'enumerazione VB.NET ("Enum OrderStatus ... End Enum").
    /// Crea il simbolo <see cref="SymbolKind.Enum"/> principale e un simbolo figlio <see cref="SymbolKind.Constant"/>
    /// per ciascun membro enumerato, collegandoli tramite relazione CONTAINS.
    /// </summary>
    public override void VisitEnumBlock(EnumBlockSyntax node)
    {
        var span = node.GetLocation().GetLineSpan();
        var name = node.EnumStatement.Identifier.ValueText;
        var qname = BuildQualifiedName(name);

        // 1. Creazione e registrazione del simbolo che rappresenta l'intero Enum
        var symbol = new Symbol
        {
            Id = Guid.NewGuid(),
            Kind = SymbolKind.Enum,
            Name = name,
            QualifiedName = qname,
            FilePath = _filePath,
            LineStart = span.StartLinePosition.Line + 1,
            LineEnd = span.EndLinePosition.Line + 1,
            Accessibility = DetermineAccessibility(node.EnumStatement.Modifiers, isType: true)
        };

        RegisterSymbol(symbol);
        _scopeStack.Push(symbol);

        // 2. Itera su ciascuna costante dichiarata all'interno dell'Enum
        foreach (var statement in node.Members)
        {
            if (statement is EnumMemberDeclarationSyntax member)
            {
                var mspan = member.GetLocation().GetLineSpan();
                var memSymbol = new Symbol
                {
                    Id = Guid.NewGuid(),
                    Kind = SymbolKind.Constant,
                    Name = member.Identifier.ValueText,
                    QualifiedName = $"{qname}.{member.Identifier.ValueText}",
                    FilePath = _filePath,
                    LineStart = mspan.StartLinePosition.Line + 1,
                    LineEnd = mspan.EndLinePosition.Line + 1,
                    Accessibility = Accessibility.Public,
                    DeclaredTypeName = name
                };
                memSymbol.Language = SourceLanguage.VisualBasicNet;
                _graph.Symbols.Add(memSymbol);
                _graph.AddContains(symbol, memSymbol);
            }
        }

        _scopeStack.Pop();
    }

    /// <summary>
    /// Metodo unificato per la registrazione e attraversamento di un blocco di tipo VB.NET (Class, Interface, Structure, Module).
    /// </summary>
    private void ProcessTypeBlock(
        SyntaxNode node,
        SymbolKind kind,
        string name,
        SyntaxTokenList modifiers,
        SyntaxList<InheritsStatementSyntax> inheritsList,
        SyntaxList<ImplementsStatementSyntax> implementsList,
        Action visitChildren,
        bool forceStatic = false)
    {
        var span = node.GetLocation().GetLineSpan();
        var qname = BuildQualifiedName(name);

        var isStatic = forceStatic || modifiers.Any(m => m.IsKind(SyntaxKind.SharedKeyword));
        var symbol = new Symbol
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            Name = name,
            QualifiedName = qname,
            FilePath = _filePath,
            LineStart = span.StartLinePosition.Line + 1,
            LineEnd = span.EndLinePosition.Line + 1,
            Accessibility = DetermineAccessibility(modifiers, isType: true),
            IsStatic = isStatic
        };

        RegisterSymbol(symbol);

        // 1. Estrazione delle clausole di ereditarietà ('Inherits BaseClass')
        foreach (var inhStmt in inheritsList)
        {
            foreach (var t in inhStmt.Types)
            {
                var baseName = GetSimpleTypeName(t.ToString());
                Inheritances.Add(new VisualBasicNetTypeInheritance
                {
                    DerivedTypeSymbol = symbol,
                    BaseOrInterfaceTypeName = baseName,
                    IsInheritance = true,
                    Line = t.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                });
            }
        }

        // 2. Estrazione delle clausole di implementazione ('Implements IFoo, IBar')
        foreach (var implStmt in implementsList)
        {
            foreach (var t in implStmt.Types)
            {
                var ifaceName = GetSimpleTypeName(t.ToString());
                Inheritances.Add(new VisualBasicNetTypeInheritance
                {
                    DerivedTypeSymbol = symbol,
                    BaseOrInterfaceTypeName = ifaceName,
                    IsInheritance = false,
                    Line = t.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                });
            }
        }

        // 3. Spinge il tipo sullo stack degli scope e visita i membri figli
        _scopeStack.Push(symbol);
        visitChildren();
        _scopeStack.Pop();
    }

    // =========================================================================
    // ROUTINE (Sub, Function, Costruttori, Proprietà, Operatori)
    // =========================================================================

    /// <summary>
    /// Visita la dichiarazione di un metodo VB.NET ("Sub Foo(...) ... End Sub" o "Function Bar(...) As Integer ... End Function").
    /// Estrae parametri formali, tipo di ritorno, modificatori (Shared, Overrides), registra il simbolo e visita il corpo.
    /// </summary>
    public override void VisitMethodBlock(MethodBlockSyntax node)
    {
        var stmt = node.SubOrFunctionStatement;
        var span = node.GetLocation().GetLineSpan();
        var name = stmt.Identifier.ValueText;
        var qname = BuildQualifiedName(name);
        var returnType = ExtractReturnType(stmt.AsClause);

        var symbol = new Symbol
        {
            Id = Guid.NewGuid(),
            Kind = SymbolKind.Method,
            Name = name,
            QualifiedName = qname,
            FilePath = _filePath,
            LineStart = span.StartLinePosition.Line + 1,
            LineEnd = span.EndLinePosition.Line + 1,
            Accessibility = DetermineAccessibility(stmt.Modifiers, isType: false),
            ReturnType = returnType,
            IsStatic = stmt.Modifiers.Any(m => m.IsKind(SyntaxKind.SharedKeyword))
        };

        ExtractParameters(stmt.ParameterList, symbol);
        RegisterSymbol(symbol);

        _scopeStack.Push(symbol);
        base.VisitMethodBlock(node);
        _scopeStack.Pop();
    }

    /// <summary>
    /// Visita la dichiarazione di una firma di metodo senza blocco di corpo (es. metodi all'interno di un'interfaccia
    /// o metodi astratti dichiarati con 'MustOverride').
    /// </summary>
    public override void VisitMethodStatement(MethodStatementSyntax node)
    {
        // Se non è contenuto all'interno di un MethodBlockSyntax, si tratta di una dichiarazione astratta o d'interfaccia
        if (node.Parent is not MethodBlockSyntax && _scopeStack.Count > 0)
        {
            var span = node.GetLocation().GetLineSpan();
            var name = node.Identifier.ValueText;
            var qname = BuildQualifiedName(name);
            var returnType = ExtractReturnType(node.AsClause);

            var symbol = new Symbol
            {
                Id = Guid.NewGuid(),
                Kind = SymbolKind.Method,
                Name = name,
                QualifiedName = qname,
                FilePath = _filePath,
                LineStart = span.StartLinePosition.Line + 1,
                LineEnd = span.EndLinePosition.Line + 1,
                Accessibility = DetermineAccessibility(node.Modifiers, isType: false),
                ReturnType = returnType,
                IsStatic = node.Modifiers.Any(m => m.IsKind(SyntaxKind.SharedKeyword))
            };

            ExtractParameters(node.ParameterList, symbol);
            RegisterSymbol(symbol);
        }

        base.VisitMethodStatement(node);
    }

    /// <summary>
    /// Visita la dichiarazione di un costruttore orientato a oggetti ("Sub New(...) ... End Sub").
    /// Trattato come un metodo con nome "New", estraendo parametri e visitando il corpo.
    /// </summary>
    public override void VisitConstructorBlock(ConstructorBlockSyntax node)
    {
        var stmt = node.SubNewStatement;
        var span = node.GetLocation().GetLineSpan();
        var name = "New";
        var qname = BuildQualifiedName(name);

        var symbol = new Symbol
        {
            Id = Guid.NewGuid(),
            Kind = SymbolKind.Method,
            Name = name,
            QualifiedName = qname,
            FilePath = _filePath,
            LineStart = span.StartLinePosition.Line + 1,
            LineEnd = span.EndLinePosition.Line + 1,
            Accessibility = DetermineAccessibility(stmt.Modifiers, isType: false),
            ReturnType = null,
            IsStatic = stmt.Modifiers.Any(m => m.IsKind(SyntaxKind.SharedKeyword))
        };

        ExtractParameters(stmt.ParameterList, symbol);
        RegisterSymbol(symbol);

        _scopeStack.Push(symbol);
        base.VisitConstructorBlock(node);
        _scopeStack.Pop();
    }

    /// <summary>
    /// Visita una dichiarazione di proprietà estesa ("Property Foo As String ... Get ... Set ... End Property").
    /// Registra il simbolo Property e visita gli accessor interni.
    /// </summary>
    public override void VisitPropertyBlock(PropertyBlockSyntax node)
    {
        var stmt = node.PropertyStatement;
        var span = node.GetLocation().GetLineSpan();
        var name = stmt.Identifier.ValueText;
        var qname = BuildQualifiedName(name);
        var propType = ExtractReturnType(stmt.AsClause);

        var symbol = new Symbol
        {
            Id = Guid.NewGuid(),
            Kind = SymbolKind.Property,
            Name = name,
            QualifiedName = qname,
            FilePath = _filePath,
            LineStart = span.StartLinePosition.Line + 1,
            LineEnd = span.EndLinePosition.Line + 1,
            Accessibility = DetermineAccessibility(stmt.Modifiers, isType: false),
            DeclaredTypeName = propType,
            IsStatic = stmt.Modifiers.Any(m => m.IsKind(SyntaxKind.SharedKeyword))
        };

        RegisterSymbol(symbol);

        _scopeStack.Push(symbol);
        base.VisitPropertyBlock(node);
        _scopeStack.Pop();
    }

    /// <summary>
    /// Visita una dichiarazione di proprietà sintetica (Auto-Property introdotta in VB 10, es. "Public Property Title As String",
    /// oppure proprietà astratta/interfaccia priva di blocco Get/Set).
    /// </summary>
    public override void VisitPropertyStatement(PropertyStatementSyntax node)
    {
        // Se il nodo genitore non è un PropertyBlockSyntax, è una singola riga di auto-property o proprietà di interfaccia
        if (node.Parent is not PropertyBlockSyntax && _scopeStack.Count > 0)
        {
            var span = node.GetLocation().GetLineSpan();
            var name = node.Identifier.ValueText;
            var qname = BuildQualifiedName(name);
            var propType = ExtractReturnType(node.AsClause);

            var symbol = new Symbol
            {
                Id = Guid.NewGuid(),
                Kind = SymbolKind.Property,
                Name = name,
                QualifiedName = qname,
                FilePath = _filePath,
                LineStart = span.StartLinePosition.Line + 1,
                LineEnd = span.EndLinePosition.Line + 1,
                Accessibility = DetermineAccessibility(node.Modifiers, isType: false),
                DeclaredTypeName = propType,
                IsStatic = node.Modifiers.Any(m => m.IsKind(SyntaxKind.SharedKeyword))
            };

            RegisterSymbol(symbol);
        }

        base.VisitPropertyStatement(node);
    }

    /// <summary>
    /// Visita un blocco operatore sovraccaricato ("Public Shared Operator +(v1, v2) As ...").
    /// </summary>
    public override void VisitOperatorBlock(OperatorBlockSyntax node)
    {
        var stmt = node.OperatorStatement;
        var span = node.GetLocation().GetLineSpan();
        var name = stmt.OperatorToken.ValueText;
        var qname = BuildQualifiedName(name);
        var returnType = ExtractReturnType(stmt.AsClause);

        var symbol = new Symbol
        {
            Id = Guid.NewGuid(),
            Kind = SymbolKind.Method,
            Name = name,
            QualifiedName = qname,
            FilePath = _filePath,
            LineStart = span.StartLinePosition.Line + 1,
            LineEnd = span.EndLinePosition.Line + 1,
            Accessibility = DetermineAccessibility(stmt.Modifiers, isType: false),
            ReturnType = returnType,
            IsStatic = true
        };

        ExtractParameters(stmt.ParameterList, symbol);
        RegisterSymbol(symbol);

        _scopeStack.Push(symbol);
        base.VisitOperatorBlock(node);
        _scopeStack.Pop();
    }

    // =========================================================================
    // CAMPI, COSTANTI ED EVENTI
    // =========================================================================

    /// <summary>
    /// Visita una dichiarazione di campo o costante ("Dim x As Integer", "Private Const Max As Integer = 10").
    /// Supporta dichiaratori multipli e intercetta istanziazioni inline ('Dim x As New Foo()', 'Dim x = New Foo()').
    /// </summary>
    public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        var isConst = node.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword));
        var isStatic = isConst || node.Modifiers.Any(m => m.IsKind(SyntaxKind.SharedKeyword));
        var access = DetermineAccessibility(node.Modifiers, isType: false);

        foreach (var decl in node.Declarators)
        {
            var declaredType = ExtractReturnType(decl.AsClause);

            foreach (var nameNode in decl.Names)
            {
                var vname = nameNode.Identifier.ValueText;
                var qname = BuildQualifiedName(vname);
                var vspan = nameNode.GetLocation().GetLineSpan();

                var symbol = new Symbol
                {
                    Id = Guid.NewGuid(),
                    Kind = isConst ? SymbolKind.Constant : SymbolKind.Field,
                    Name = vname,
                    QualifiedName = qname,
                    FilePath = _filePath,
                    LineStart = vspan.StartLinePosition.Line + 1,
                    LineEnd = vspan.EndLinePosition.Line + 1,
                    Accessibility = access,
                    DeclaredTypeName = declaredType,
                    IsStatic = isStatic
                };

                RegisterSymbol(symbol);

                // Caso A: Inizializzazione esplicita 'Dim x = New Foo()'
                if (decl.Initializer?.Value is ObjectCreationExpressionSyntax initObj)
                {
                    RecordInstantiation(symbol, initObj, vspan.StartLinePosition.Line + 1);
                }
                // Caso B: Auto-istanziazione classica 'Dim x As New Foo()' tramite AsNewClauseSyntax
                else if (decl.AsClause is AsNewClauseSyntax asNew && asNew.NewExpression is ObjectCreationExpressionSyntax asNewObj)
                {
                    RecordInstantiation(symbol, asNewObj, vspan.StartLinePosition.Line + 1);
                }
            }
        }

        base.VisitFieldDeclaration(node);
    }

    /// <summary>
    /// Visita una dichiarazione di evento su singola riga ("Public Event OrderPlaced(orderId As Integer)").
    /// </summary>
    public override void VisitEventStatement(EventStatementSyntax node)
    {
        var span = node.GetLocation().GetLineSpan();
        var name = node.Identifier.ValueText;
        var qname = BuildQualifiedName(name);

        var symbol = new Symbol
        {
            Id = Guid.NewGuid(),
            Kind = SymbolKind.Event,
            Name = name,
            QualifiedName = qname,
            FilePath = _filePath,
            LineStart = span.StartLinePosition.Line + 1,
            LineEnd = span.EndLinePosition.Line + 1,
            Accessibility = DetermineAccessibility(node.Modifiers, isType: false),
            DeclaredTypeName = ExtractReturnType(node.AsClause),
            IsStatic = node.Modifiers.Any(m => m.IsKind(SyntaxKind.SharedKeyword))
        };

        ExtractParameters(node.ParameterList, symbol);
        RegisterSymbol(symbol);

        base.VisitEventStatement(node);
    }

    /// <summary>
    /// Visita una dichiarazione di evento personalizzato con blocco di gestione ("Custom Event MyEvent ... End Event").
    /// </summary>
    public override void VisitEventBlock(EventBlockSyntax node)
    {
        var stmt = node.EventStatement;
        var span = node.GetLocation().GetLineSpan();
        var name = stmt.Identifier.ValueText;
        var qname = BuildQualifiedName(name);

        var symbol = new Symbol
        {
            Id = Guid.NewGuid(),
            Kind = SymbolKind.Event,
            Name = name,
            QualifiedName = qname,
            FilePath = _filePath,
            LineStart = span.StartLinePosition.Line + 1,
            LineEnd = span.EndLinePosition.Line + 1,
            Accessibility = DetermineAccessibility(stmt.Modifiers, isType: false),
            DeclaredTypeName = ExtractReturnType(stmt.AsClause),
            IsStatic = stmt.Modifiers.Any(m => m.IsKind(SyntaxKind.SharedKeyword))
        };

        ExtractParameters(stmt.ParameterList, symbol);
        RegisterSymbol(symbol);

        _scopeStack.Push(symbol);
        base.VisitEventBlock(node);
        _scopeStack.Pop();
    }

    // =========================================================================
    // INVOCAZIONI E ISTANZIAZIONI (Call Sites & Instantiations)
    // =========================================================================

    /// <summary>
    /// Visita un'espressione di invocazione a metodo o funzione VB.NET ("service.ProcessOrder()", "DoWork()", "Console.WriteLine()").
    /// Estrae il chiamante corrente, il nome del metodo invocato e l'eventuale ricevitore, registrando il call-site.
    /// </summary>
    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        var caller = GetCurrentRoutineSymbol();
        if (caller != null)
        {
            var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            string methodName;
            string? receiver = null;

            // Invocazione qualificata con accesso a membro (es. 'logger.WriteLog' o 'Me.Validate')
            if (node.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                methodName = memberAccess.Name.Identifier.ValueText;
                receiver = memberAccess.Expression.ToString();
            }
            // Invocazione diretta semplice (es. 'Validate()')
            else if (node.Expression is SimpleNameSyntax simpleName)
            {
                methodName = simpleName.Identifier.ValueText;
            }
            else
            {
                methodName = node.Expression.ToString();
            }

            CallSites.Add(new VisualBasicNetCallSite
            {
                CallerSymbol = caller,
                InvokedMethodName = methodName,
                ReceiverExpressionText = receiver,
                Line = line
            });
        }

        base.VisitInvocationExpression(node);
    }

    /// <summary>
    /// Visita un'espressione di creazione di oggetto VB.NET ("New OrderRepository()", "New List(Of String)()").
    /// </summary>
    public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        var caller = GetCurrentRoutineSymbol();
        if (caller != null)
        {
            var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            RecordInstantiation(caller, node, line);
        }

        base.VisitObjectCreationExpression(node);
    }

    /// <summary>
    /// Registra un'istanziazione di tipo oggetto associandola al simbolo sorgente (routine o campo).
    /// </summary>
    private void RecordInstantiation(Symbol source, ObjectCreationExpressionSyntax node, int line)
    {
        var createdTypeName = GetSimpleTypeName(node.Type.ToString());
        Instantiations.Add(new VisualBasicNetInstantiation
        {
            SourceSymbol = source,
            TargetTypeName = createdTypeName,
            Line = line
        });
    }

    // =========================================================================
    // UTILITY DI SUPPORTO E RISOLUZIONE
    // =========================================================================

    /// <summary>
    /// Registra un nuovo simbolo nel <see cref="CodeGraph"/>, assegna il linguaggio <see cref="SourceLanguage.VisualBasicNet"/>
    /// e crea l'arco di contenimento gerarchico CONTAINS con il genitore in cima allo stack (o a <see cref="TopLevelSymbols"/> se radice).
    /// </summary>
    private void RegisterSymbol(Symbol symbol)
    {
        symbol.Language = SourceLanguage.VisualBasicNet;
        _graph.Symbols.Add(symbol);

        if (_scopeStack.Count > 0)
        {
            var parent = _scopeStack.Peek();
            _graph.AddContains(parent, symbol);
        }
        else
        {
            TopLevelSymbols.Add(symbol);
        }
    }

    /// <summary>
    /// Risale lo stack degli scope per identificare la routine (Metodo o Proprietà) che racchiude l'istruzione corrente.
    /// Se non trova una routine, restituisce il simbolo in cima allo stack (es. la classe o il campo per inizializzatori).
    /// </summary>
    private Symbol? GetCurrentRoutineSymbol()
    {
        foreach (var sym in _scopeStack)
        {
            if (sym.Kind is SymbolKind.Method or SymbolKind.Property)
                return sym;
        }
        return _scopeStack.Count > 0 ? _scopeStack.Peek() : null;
    }

    /// <summary>
    /// Estrae i parametri formali da una lista di parametri sintattici VB.NET,
    /// rilevando clausole ByVal, ByRef (ByVal è il default in VB.NET a differenza di VB6) e parametri opzionali con valore di default.
    /// </summary>
    private static void ExtractParameters(ParameterListSyntax? parameterList, Symbol routineSymbol)
    {
        if (parameterList == null) return;

        foreach (var param in parameterList.Parameters)
        {
            var pspan = param.GetLocation().GetLineSpan();
            var ptype = ExtractReturnType(param.AsClause);
            var isByRef = param.Modifiers.Any(m => m.IsKind(SyntaxKind.ByRefKeyword));
            var isOptional = param.Modifiers.Any(m => m.IsKind(SyntaxKind.OptionalKeyword)) || param.Default != null;

            routineSymbol.Parameters.Add(new ParameterSymbol
            {
                Name = param.Identifier.Identifier.ValueText,
                DeclaredTypeName = ptype,
                Line = pspan.StartLinePosition.Line + 1,
                IsByRef = isByRef,
                IsOptional = isOptional
            });
        }
    }

    /// <summary>
    /// Estrae il nome del tipo testuale da una clausola sintattica 'As' (gestisce sia <see cref="SimpleAsClauseSyntax"/> che <see cref="AsNewClauseSyntax"/>).
    /// </summary>
    private static string? ExtractReturnType(AsClauseSyntax? asClause)
    {
        if (asClause == null) return null;
        if (asClause is SimpleAsClauseSyntax simpleAs)
            return simpleAs.Type.ToString();
        if (asClause is AsNewClauseSyntax asNew)
            return asNew.NewExpression.ToString();
        return asClause.ToString();
    }

    /// <summary>
    /// Costruisce il nome completamente qualificato (QualifiedName) combinando il namespace corrente,
    /// i nomi di tutti i tipi o contenitori genitori nello stack e il nome del simbolo stesso.
    /// </summary>
    private string BuildQualifiedName(string name)
    {
        var parts = new List<string>();
        var ns = CurrentNamespace;
        if (!string.IsNullOrEmpty(ns))
            parts.Add(ns);

        foreach (var parent in _scopeStack.Reverse())
        {
            parts.Add(parent.Name);
        }

        parts.Add(name);
        return string.Join(".", parts);
    }

    /// <summary>
    /// Normalizza una stringa di tipo VB.NET estraendone il nome semplice.
    /// Rimuove parametri di tipo generico ('(Of T)'), qualificatori di namespace ('System.') e suffissi di nullabilità ('?').
    /// </summary>
    private static string GetSimpleTypeName(string rawType)
    {
        var type = rawType.Trim();
        var genericIdx = type.IndexOf("(Of ", StringComparison.OrdinalIgnoreCase);
        if (genericIdx > 0)
            type = type[..genericIdx].Trim();

        var dotIdx = type.LastIndexOf('.');
        if (dotIdx >= 0)
            type = type[(dotIdx + 1)..].Trim();

        if (type.EndsWith("?", StringComparison.Ordinal))
            type = type[..^1].Trim();

        return type;
    }

    /// <summary>
    /// Determina il livello di visibilità semantica (<see cref="Accessibility"/>) da una lista di modificatori sintattici VB.NET.
    /// In VB.NET:
    /// - Friend corrisponde a Internal.
    /// - Se la visibilità è omessa su membri di classe, il default di VB.NET è Public (a differenza di C# dove è Private).
    /// - Se la visibilità è omessa su tipi, il default è Friend (Internal).
    /// </summary>
    private static Accessibility DetermineAccessibility(SyntaxTokenList modifiers, bool isType)
    {
        bool isPublic = modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword));
        bool isPrivate = modifiers.Any(m => m.IsKind(SyntaxKind.PrivateKeyword));
        bool isProtected = modifiers.Any(m => m.IsKind(SyntaxKind.ProtectedKeyword));
        bool isFriend = modifiers.Any(m => m.IsKind(SyntaxKind.FriendKeyword));

        if (isProtected && isFriend) return Accessibility.ProtectedInternal;
        if (isPublic) return Accessibility.Public;
        if (isPrivate) return Accessibility.Private;
        if (isProtected) return Accessibility.Protected;
        if (isFriend) return Accessibility.Internal;

        // Regole predefinite di VB.NET
        return isType ? Accessibility.Internal : Accessibility.Public;
    }
}
