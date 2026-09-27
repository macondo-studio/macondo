using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Macondo.Semantic.Model;
using Symbol = Macondo.Semantic.Model.Symbol;
using SymbolKind = Macondo.Semantic.Model.SymbolKind;
using Accessibility = Macondo.Semantic.Model.Accessibility;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Rappresenta un sito di chiamata a metodo (call-site) rilevato all'interno del corpo di un metodo o proprietà C#.
/// </summary>
public sealed class CSharpCallSite
{
    /// <summary>Simbolo della routine o membro C# chiamante (es. un metodo o property getter/setter).</summary>
    public required Symbol CallerSymbol { get; init; }

    /// <summary>Nome identificativo del metodo o membro invocato (es. "ProcessOrder", "Save").</summary>
    public required string InvokedMethodName { get; init; }

    /// <summary>Testo dell'espressione ricevitore (receiver) se presente (es. "customerService", "orderRepo", "this").</summary>
    public string? ReceiverExpressionText { get; init; }

    /// <summary>Numero di riga nel file sorgente C# in cui si trova l'invocazione (1-indexed).</summary>
    public int Line { get; init; }
}

/// <summary>
/// Rappresenta un'istanziazione di tipo oggetto (es. 'new Foo(...)', 'new()') rilevata nel codice C#.
/// </summary>
public sealed class CSharpInstantiation
{
    /// <summary>Simbolo sorgente in cui avviene l'istanziazione (metodo, proprietà, oppure campo con inizializzatore inline).</summary>
    public required Symbol SourceSymbol { get; init; }

    /// <summary>Nome semplice o qualificato del tipo istanziato (es. "CustomerService", "List", "var").</summary>
    public required string TargetTypeName { get; init; }

    /// <summary>Numero di riga nel file sorgente C# in cui avviene l'istanziazione (1-indexed).</summary>
    public int Line { get; init; }
}

/// <summary>
/// Rappresenta una clausola di ereditarietà di classe base o implementazione di interfaccia per un tipo C#.
/// </summary>
public sealed class CSharpTypeInheritance
{
    /// <summary>Simbolo del tipo derivato o che implementa l'interfaccia (Classe, Struct o Record).</summary>
    public required Symbol DerivedTypeSymbol { get; init; }

    /// <summary>Nome del tipo base o dell'interfaccia ereditata/implementata (es. "BaseEntity", "IDisposable").</summary>
    public required string BaseOrInterfaceTypeName { get; init; }

    /// <summary>Numero di riga nel file sorgente C# in cui è presente la clausola base (1-indexed).</summary>
    public int Line { get; init; }
}

/// <summary>
/// Risultato strutturato dell'estrazione semantica di un singolo file sorgente C# (.cs).
/// </summary>
public sealed class CSharpExtractionResult
{
    /// <summary>Percorso assoluto o relativo del file sorgente C# analizzato.</summary>
    public required string FilePath { get; init; }

    /// <summary>Elenco dei simboli di primo livello (classi, interfacce, record, struct, enum) dichiarati nel file.</summary>
    public required IReadOnlyList<Symbol> TopLevelSymbols { get; init; }

    /// <summary>Elenco dei call-site di invocazione rilevati nei corpi delle routine C#.</summary>
    public required IReadOnlyList<CSharpCallSite> CallSites { get; init; }

    /// <summary>Elenco delle istanziazioni 'new' rilevate nei metodi o nelle inizializzazioni di campo.</summary>
    public required IReadOnlyList<CSharpInstantiation> Instantiations { get; init; }

    /// <summary>Elenco delle relazioni di ereditarietà e implementazione di interfacce estratte dalle dichiarazioni di tipo.</summary>
    public required IReadOnlyList<CSharpTypeInheritance> Inheritances { get; init; }

    /// <summary>Elenco degli errori diagnostici di compilazione o parsing rilevati da Roslyn sul file.</summary>
    public required IReadOnlyList<string> ParseErrors { get; init; }

    /// <summary>Indica se il file è stato parsato ed estratto con successo senza errori critici di sintassi.</summary>
    public bool Success => ParseErrors.Count == 0;
}

/// <summary>
/// Estrattore semantico di file sorgente C# basato sul compilatore Microsoft Roslyn (<see cref="CSharpSyntaxTree"/>).
/// 
/// RESPONSABILITÀ:
/// 1. Effettua il parsing del codice sorgente C# costruendo un albero sintattico (Roslyn SyntaxTree).
/// 2. Attraversa l'albero tramite <see cref="CSharpSymbolWalker"/> estraendo tutti i simboli strutturati
///    (Classi, Interfacce, Record, Struct, Enum, Metodi, Proprietà, Campi, Eventi, Costanti).
/// 3. Inserisce i simboli nel <see cref="CodeGraph"/> condiviso collegandoli con relazioni di contenimento (<see cref="RelationshipKind.Contains"/>).
/// 4. Raccoglie i call-site di invocazione, le istanziazioni 'new' e le clausole di ereditarietà per consentire
///    al <see cref="Macondo.Semantic.Graph.CallGraphBuilder"/> di generare i collegamenti semantici cross-language.
/// </summary>
public static class CSharpExtractor
{
    /// <summary>
    /// Esegue il parsing e l'estrazione semantica di un file C# inserendo direttamente i simboli nel <see cref="CodeGraph"/>.
    /// </summary>
    /// <param name="graph">Il grafo semantico condiviso dell'analisi in memoria.</param>
    /// <param name="filePath">Percorso o nome del file C# analizzato.</param>
    /// <param name="sourceText">Il codice sorgente C# completo del file.</param>
    /// <returns>Il risultato strutturato contenente simboli, call site, istanziazioni ed ereditarietà estratte.</returns>
    public static CSharpExtractionResult ExtractCSharpFile(CodeGraph graph, string filePath, string sourceText)
    {
        // 1. Parsing del testo sorgente C# tramite Roslyn con supporto alla versione più recente del linguaggio
        var syntaxTree = CSharpSyntaxTree.ParseText(
            sourceText,
            path: filePath,
            options: new CSharpParseOptions(LanguageVersion.Latest));

        var root = syntaxTree.GetRoot();

        // 2. Raccolta di eventuali errori di diagnostica sintattica
        var errors = new List<string>();
        foreach (var diag in syntaxTree.GetDiagnostics())
        {
            if (diag.Severity == DiagnosticSeverity.Error)
            {
                errors.Add(diag.ToString());
            }
        }

        // 3. Esecuzione del walker specializzato per l'estrazione gerarchica dei simboli e delle relazioni
        var walker = new CSharpSymbolWalker(graph, filePath);
        walker.Visit(root);

        // 4. Confezionamento del risultato finale di estrazione
        return new CSharpExtractionResult
        {
            FilePath = filePath,
            TopLevelSymbols = walker.TopLevelSymbols,
            CallSites = walker.CallSites,
            Instantiations = walker.Instantiations,
            Inheritances = walker.Inheritances,
            ParseErrors = errors
        };
    }

    /// <summary>
    /// SyntaxWalker specializzato di Roslyn (<see cref="CSharpSyntaxWalker"/>) che percorre ricorsivamente
    /// i nodi AST C# per individuare dichiarazioni, gerarchie di contenimento, invocazioni e istanziazioni.
    /// 
    /// STRATEGIA DI GESTIONE DEGLI SCOPE E DEI SIMBOLI:
    /// - <see cref="_namespaceStack"/>: tiene traccia dei namespace annidati o file-scoped per costruire i QualifiedName completi.
    /// - <see cref="_scopeStack"/>: mantiene lo stack dei simboli contenitori attivi (es. Tipo -> Metodo -> Lambda/Local).
    ///   Quando viene creato un nuovo simbolo figlio, viene automaticamente collegato al genitore in cima allo stack
    ///   tramite una relazione CONTAINS nel CodeGraph.
    /// - Rilevamento Call Sites: intercetta <see cref="InvocationExpressionSyntax"/> estraendo il nome del metodo
    ///   e l'eventuale espressione ricevitore (receiver) per consentire una risoluzione accurata del target nel Call Graph.
    /// - Rilevamento Istanziazioni: intercetta sia <see cref="ObjectCreationExpressionSyntax"/> espliciti ('new Foo()')
    ///   sia <see cref="ImplicitObjectCreationExpressionSyntax"/> (C# 9+ target-typed 'new()') e gli inizializzatori di campo.
    /// - Rilevamento Ereditarietà: ispeziona <see cref="BaseListSyntax"/> su tipi per mappare basi di classe e interfacce implementate.
    /// </summary>
    private sealed class CSharpSymbolWalker : CSharpSyntaxWalker
    {
        private readonly CodeGraph _graph;
        private readonly string _filePath;

        /// <summary>Stack dei simboli contenitori attivi (usato per stabilire le relazioni CONTAINS e identificare il chiamante).</summary>
        private readonly Stack<Symbol> _scopeStack = new();

        /// <summary>Stack dei segmenti di namespace correnti (gestisce sia namespace a blocchi che file-scoped namespace).</summary>
        private readonly Stack<string> _namespaceStack = new();

        /// <summary>Simboli dichiarati a livello radice (non contenuti in altri tipi all'interno del file).</summary>
        public List<Symbol> TopLevelSymbols { get; } = new();

        /// <summary>Punti di invocazione rilevati all'interno dei metodi o proprietà.</summary>
        public List<CSharpCallSite> CallSites { get; } = new();

        /// <summary>Istanziazioni di oggetti ('new Type()') rilevate nei metodi o inizializzatori di campo.</summary>
        public List<CSharpInstantiation> Instantiations { get; } = new();

        /// <summary>Clausole di ereditarietà o interfacce dichiarate per classi, struct, record e interfacce.</summary>
        public List<CSharpTypeInheritance> Inheritances { get; } = new();

        /// <summary>
        /// Inizializza una nuova istanza del walker per il file specificato.
        /// </summary>
        /// <param name="graph">Il CodeGraph in cui registrare i simboli e le relazioni CONTAINS.</param>
        /// <param name="filePath">Il percorso del file sorgente C# analizzato.</param>
        public CSharpSymbolWalker(CodeGraph graph, string filePath)
        {
            _graph = graph;
            _filePath = filePath;
        }

        /// <summary>
        /// Restituisce il namespace completo corrente combinando con punti i segmenti presenti nello stack.
        /// </summary>
        private string CurrentNamespace => string.Join(".", _namespaceStack.Reverse());

        // =========================================================================
        // GESTIONE DEI NAMESPACE
        // =========================================================================

        /// <summary>
        /// Visita una dichiarazione di namespace classica a blocchi ("namespace Foo.Bar { ... }").
        /// Spinge il nome del namespace sullo stack, visita i nodi figli, quindi rimuove il namespace.
        /// </summary>
        public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
        {
            _namespaceStack.Push(node.Name.ToString());
            base.VisitNamespaceDeclaration(node);
            _namespaceStack.Pop();
        }

        /// <summary>
        /// Visita una dichiarazione di namespace con ambito di file ("namespace Foo.Bar;").
        /// Disponibile da C# 10, si applica a tutto il contenuto del file.
        /// </summary>
        public override void VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
        {
            _namespaceStack.Push(node.Name.ToString());
            base.VisitFileScopedNamespaceDeclaration(node);
            _namespaceStack.Pop();
        }

        // =========================================================================
        // DICHIARAZIONI DI TIPI (Class, Interface, Struct, Record, Enum)
        // =========================================================================

        /// <summary>
        /// Visita la dichiarazione di una classe C# ("class Foo : IBar").
        /// </summary>
        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            ProcessTypeDeclaration(node, SymbolKind.Class, node.Identifier.ValueText, node.Modifiers, node.BaseList);
        }

        /// <summary>
        /// Visita la dichiarazione di un'interfaccia C# ("interface IFoo").
        /// </summary>
        public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
        {
            ProcessTypeDeclaration(node, SymbolKind.Interface, node.Identifier.ValueText, node.Modifiers, node.BaseList);
        }

        /// <summary>
        /// Visita la dichiarazione di una struct C# ("struct Point").
        /// </summary>
        public override void VisitStructDeclaration(StructDeclarationSyntax node)
        {
            ProcessTypeDeclaration(node, SymbolKind.Struct, node.Identifier.ValueText, node.Modifiers, node.BaseList);
        }

        /// <summary>
        /// Visita la dichiarazione di un record C# ("record Person(...)").
        /// </summary>
        public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
        {
            ProcessTypeDeclaration(node, SymbolKind.Record, node.Identifier.ValueText, node.Modifiers, node.BaseList);
        }

        /// <summary>
        /// Visita la dichiarazione di un'enumerazione C# ("enum OrderStatus { Pending, Completed }").
        /// Crea il simbolo Enum per il tipo e un simbolo Constant per ciascun membro enumerato,
        /// collegando i membri all'Enum tramite relazioni CONTAINS.
        /// </summary>
        public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
        {
            var span = node.GetLocation().GetLineSpan();
            var name = node.Identifier.ValueText;
            var qname = BuildQualifiedName(name);

            // 1. Crea e registra il simbolo che rappresenta l'intero Enum
            var symbol = new Symbol
            {
                Id = Guid.NewGuid(),
                Kind = SymbolKind.Enum,
                Name = name,
                QualifiedName = qname,
                FilePath = _filePath,
                LineStart = span.StartLinePosition.Line + 1,
                LineEnd = span.EndLinePosition.Line + 1,
                Accessibility = DetermineAccessibility(node.Modifiers, isType: true)
            };

            RegisterSymbol(symbol);
            _scopeStack.Push(symbol);

            // 2. Itera su ciascun valore costante dichiarato all'interno dell'Enum
            foreach (var member in node.Members)
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
                _graph.Symbols.Add(memSymbol);
                _graph.AddContains(symbol, memSymbol);
            }

            _scopeStack.Pop();
        }

        /// <summary>
        /// Logica unificata per la creazione, registrazione e attraversamento di un tipo C# (Classe, Interfaccia, Struct, Record).
        /// Gestisce:
        /// 1. Calcolo del nome qualificato (inclusi namespace e classi contenitrici/annidate).
        /// 2. Rilevamento modificatori di visibilità e flag statico.
        /// 3. Estrazione delle clausole di ereditarietà e interfacce (BaseListSyntax).
        /// 4. Gestione dello stack di scope e visita ricorsiva di tutti i membri interni.
        /// </summary>
        private void ProcessTypeDeclaration(
            TypeDeclarationSyntax node,
            SymbolKind kind,
            string name,
            SyntaxTokenList modifiers,
            BaseListSyntax? baseList)
        {
            var span = node.GetLocation().GetLineSpan();
            var qname = BuildQualifiedName(name);

            // 1. Creazione del simbolo per il tipo
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
                IsStatic = modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword))
            };

            RegisterSymbol(symbol);

            // 2. Estrazione delle relazioni di ereditarietà e implementazione interfacce
            if (baseList != null)
            {
                foreach (var baseType in baseList.Types)
                {
                    var baseName = GetSimpleTypeName(baseType.Type.ToString());
                    Inheritances.Add(new CSharpTypeInheritance
                    {
                        DerivedTypeSymbol = symbol,
                        BaseOrInterfaceTypeName = baseName,
                        Line = baseType.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                    });
                }
            }

            // 3. Spinge il tipo sullo stack e visita ricorsivamente tutti i membri dichiarati
            _scopeStack.Push(symbol);
            foreach (var member in node.Members)
            {
                Visit(member);
            }
            _scopeStack.Pop();
        }

        // =========================================================================
        // MEMBRI DI ROUTINE (Metodi, Costruttori, Proprietà)
        // =========================================================================

        /// <summary>
        /// Visita la dichiarazione di un metodo C# ("public void Process(int id) { ... }").
        /// Estrae i parametri formali, il tipo di ritorno, i flag di visibilità/static,
        /// registra il metodo nel grafo e visita il corpo per intercettare invocazioni e istanziazioni.
        /// </summary>
        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            var span = node.GetLocation().GetLineSpan();
            var name = node.Identifier.ValueText;
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
                Accessibility = DetermineAccessibility(node.Modifiers, isType: false),
                ReturnType = node.ReturnType.ToString(),
                IsStatic = node.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword))
            };

            // Estrae i parametri formali del metodo
            ExtractParameters(node.ParameterList, symbol);
            RegisterSymbol(symbol);

            // Spinge il metodo sullo scope stack e visita il corpo (istruzioni o espressioni lambda)
            _scopeStack.Push(symbol);
            base.VisitMethodDeclaration(node);
            _scopeStack.Pop();
        }

        /// <summary>
        /// Visita la dichiarazione di un costruttore C# ("public Customer(string name) { ... }").
        /// Trattato come un metodo speciale senza tipo di ritorno esplicito.
        /// </summary>
        public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            var span = node.GetLocation().GetLineSpan();
            var name = node.Identifier.ValueText;
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
                Accessibility = DetermineAccessibility(node.Modifiers, isType: false),
                ReturnType = null,
                IsStatic = node.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword))
            };

            // Estrae i parametri del costruttore
            ExtractParameters(node.ParameterList, symbol);
            RegisterSymbol(symbol);

            // Spinge il costruttore sullo stack e visita il corpo (inclusi eventuali 'this()' / 'base()')
            _scopeStack.Push(symbol);
            base.VisitConstructorDeclaration(node);
            _scopeStack.Pop();
        }

        /// <summary>
        /// Visita la dichiarazione di una proprietà C# ("public string Name { get; set; }").
        /// Registra il simbolo Property e visita gli accessor get/set o l'expression body.
        /// </summary>
        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            var span = node.GetLocation().GetLineSpan();
            var name = node.Identifier.ValueText;
            var qname = BuildQualifiedName(name);

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
                DeclaredTypeName = node.Type.ToString(),
                IsStatic = node.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword))
            };

            RegisterSymbol(symbol);

            // Spinge la proprietà sullo stack e visita accessor o espressioni interne
            _scopeStack.Push(symbol);
            base.VisitPropertyDeclaration(node);
            _scopeStack.Pop();
        }

        // =========================================================================
        // CAMPI, COSTANTI ED EVENTI
        // =========================================================================

        /// <summary>
        /// Visita la dichiarazione di uno o più campi o costanti C# ("private readonly int _count = 0;", "public const int Max = 10;").
        /// Rileva anche eventuali espressioni di istanziazione inline ('new Foo()') nell'inizializzatore del campo.
        /// </summary>
        public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            var span = node.GetLocation().GetLineSpan();
            var isStatic = node.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
            var isConst = node.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword));
            var access = DetermineAccessibility(node.Modifiers, isType: false);
            var typeName = node.Declaration.Type.ToString();

            // Una singola FieldDeclarationSyntax può dichiarare più variabili (es. "int a = 1, b = 2;")
            foreach (var variable in node.Declaration.Variables)
            {
                var vname = variable.Identifier.ValueText;
                var qname = BuildQualifiedName(vname);
                var vspan = variable.GetLocation().GetLineSpan();

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
                    DeclaredTypeName = typeName,
                    IsStatic = isStatic || isConst
                };

                RegisterSymbol(symbol);

                // Se il campo ha un'inizializzazione inline con creazione oggetto (es. 'private List<int> list = new List<int>();')
                if (variable.Initializer?.Value is ObjectCreationExpressionSyntax objCreation)
                {
                    var createdTypeName = GetSimpleTypeName(objCreation.Type.ToString());
                    Instantiations.Add(new CSharpInstantiation
                    {
                        SourceSymbol = symbol,
                        TargetTypeName = createdTypeName,
                        Line = vspan.StartLinePosition.Line + 1
                    });
                }
            }

            base.VisitFieldDeclaration(node);
        }

        /// <summary>
        /// Visita la dichiarazione di un campo evento C# ("public event EventHandler? OrderCompleted;").
        /// </summary>
        public override void VisitEventFieldDeclaration(EventFieldDeclarationSyntax node)
        {
            var access = DetermineAccessibility(node.Modifiers, isType: false);
            var typeName = node.Declaration.Type.ToString();

            foreach (var variable in node.Declaration.Variables)
            {
                var vname = variable.Identifier.ValueText;
                var qname = BuildQualifiedName(vname);
                var vspan = variable.GetLocation().GetLineSpan();

                var symbol = new Symbol
                {
                    Id = Guid.NewGuid(),
                    Kind = SymbolKind.Event,
                    Name = vname,
                    QualifiedName = qname,
                    FilePath = _filePath,
                    LineStart = vspan.StartLinePosition.Line + 1,
                    LineEnd = vspan.EndLinePosition.Line + 1,
                    Accessibility = access,
                    DeclaredTypeName = typeName
                };

                RegisterSymbol(symbol);
            }

            base.VisitEventFieldDeclaration(node);
        }

        // =========================================================================
        // INVOCAZIONI E ISTANZIAZIONI (Call Sites & Instantiations)
        // =========================================================================

        /// <summary>
        /// Visita un'espressione di invocazione a metodo o funzione C# ("service.Execute()", "DoWork()", "Console.WriteLine()").
        /// Identifica la routine chiamante dallo scope stack, estrae il nome del metodo invocato e l'eventuale espressione receiver,
        /// e registra il call site per la successiva risoluzione nel Call Graph.
        /// </summary>
        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var caller = GetCurrentRoutineSymbol();
            if (caller != null)
            {
                var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                string methodName;
                string? receiver = null;

                // Caso 1: Invocazione tramite accesso a membro (es. 'obj.Method()' oppure 'ClassName.StaticMethod()')
                if (node.Expression is MemberAccessExpressionSyntax memberAccess)
                {
                    methodName = memberAccess.Name.Identifier.ValueText;
                    receiver = memberAccess.Expression.ToString();
                }
                // Caso 2: Invocazione diretta di metodo locale/ereditato (es. 'DoSomething()')
                else if (node.Expression is IdentifierNameSyntax identifier)
                {
                    methodName = identifier.Identifier.ValueText;
                }
                // Caso 3: Invocazioni complesse o su delegati (es. 'actions[0]()')
                else
                {
                    methodName = node.Expression.ToString();
                }

                CallSites.Add(new CSharpCallSite
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
        /// Visita un'espressione esplicita di creazione oggetto C# ("new Customer()", "new List&lt;string&gt;()").
        /// Registra l'istanziazione associandola alla routine corrente.
        /// </summary>
        public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            var caller = GetCurrentRoutineSymbol();
            if (caller != null)
            {
                var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                var createdTypeName = GetSimpleTypeName(node.Type.ToString());

                Instantiations.Add(new CSharpInstantiation
                {
                    SourceSymbol = caller,
                    TargetTypeName = createdTypeName,
                    Line = line
                });
            }

            base.VisitObjectCreationExpression(node);
        }

        /// <summary>
        /// Visita un'espressione implicita target-typed di creazione oggetto (C# 9+, "Customer c = new();").
        /// Registra l'istanziazione con placeholder "var" che verrà risolto contestualmente.
        /// </summary>
        public override void VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
        {
            var caller = GetCurrentRoutineSymbol();
            if (caller != null)
            {
                var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                Instantiations.Add(new CSharpInstantiation
                {
                    SourceSymbol = caller,
                    TargetTypeName = "var",
                    Line = line
                });
            }

            base.VisitImplicitObjectCreationExpression(node);
        }

        // =========================================================================
        // METODI DI SUPPORTO E UTILITY
        // =========================================================================

        /// <summary>
        /// Registra un simbolo nel grafo, imposta il linguaggio C# e crea la relazione CONTAINS
        /// con il simbolo contenitore attivo in cima allo stack (o aggiunge a TopLevelSymbols se radice).
        /// </summary>
        private void RegisterSymbol(Symbol symbol)
        {
            symbol.Language = SourceLanguage.CSharp;
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
        /// Risale lo stack degli scope per trovare il simbolo di routine (Metodo o Proprietà) più vicino che racchiude l'istruzione corrente.
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
        /// Estrae la lista dei parametri formali da una dichiarazione di metodo o costruttore
        /// e li popola all'interno della proprietà <see cref="Symbol.Parameters"/> del simbolo routine.
        /// </summary>
        private void ExtractParameters(ParameterListSyntax? parameterList, Symbol routineSymbol)
        {
            if (parameterList == null) return;

            foreach (var param in parameterList.Parameters)
            {
                var pspan = param.GetLocation().GetLineSpan();
                var ptype = param.Type?.ToString();
                routineSymbol.Parameters.Add(new ParameterSymbol
                {
                    Name = param.Identifier.ValueText,
                    DeclaredTypeName = ptype,
                    Line = pspan.StartLinePosition.Line + 1,
                    IsOptional = param.Default != null
                });
            }
        }

        /// <summary>
        /// Costruisce il nome completamente qualificato (QualifiedName) concatenando il namespace corrente,
        /// i nomi dei tipi contenitori/annidati e il nome del simbolo stesso.
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
        /// Normalizza una stringa di tipo C# rimuovendo parametri di tipo generico ('&lt;T&gt;'),
        /// prefissi di namespace ('System.') e indicatori di nullabilità ('?').
        /// Esempio: 'System.Collections.Generic.List&lt;Customer&gt;?' -> 'List'.
        /// </summary>
        private static string GetSimpleTypeName(string rawType)
        {
            var type = rawType.Trim();
            var genericIdx = type.IndexOf('<');
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
        /// Determina il livello semantico di accessibilità (<see cref="Accessibility"/>) da una lista di modificatori sintattici C#.
        /// Applica le regole predefinite del linguaggio quando i modificatori sono omessi:
        /// - Tipi (classi, struct, enum, record): <see cref="Accessibility.Internal"/>
        /// - Membri (metodi, proprietà, campi): <see cref="Accessibility.Private"/>
        /// </summary>
        private static Accessibility DetermineAccessibility(SyntaxTokenList modifiers, bool isType)
        {
            bool isPublic = modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword));
            bool isPrivate = modifiers.Any(m => m.IsKind(SyntaxKind.PrivateKeyword));
            bool isProtected = modifiers.Any(m => m.IsKind(SyntaxKind.ProtectedKeyword));
            bool isInternal = modifiers.Any(m => m.IsKind(SyntaxKind.InternalKeyword));

            if (isProtected && isInternal) return Accessibility.ProtectedInternal;
            if (isPrivate && isProtected) return Accessibility.PrivateProtected;
            if (isPublic) return Accessibility.Public;
            if (isPrivate) return Accessibility.Private;
            if (isProtected) return Accessibility.Protected;
            if (isInternal) return Accessibility.Internal;

            return isType ? Accessibility.Internal : Accessibility.Private;
        }
    }
}

