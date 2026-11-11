using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Macondo.Semantic.Model;
using Symbol = Macondo.Semantic.Model.Symbol;
using SymbolKind = Macondo.Semantic.Model.SymbolKind;
using Accessibility = Macondo.Semantic.Model.Accessibility;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Roslyn's specialized SyntaxWalker (<see cref="CSharpSyntaxWalker"/>) that walks recursively
/// C# AST nodes to locate declarations, containment hierarchies, invocations, and instantiations.
/// 
/// SCOPE AND SYMBOL MANAGEMENT STRATEGY:
/// - <see cref="_namespaceStack"/>: Keeps track of nested or file-scoped namespaces to construct complete QualifiedNames.
/// - <see cref="_scopeStack"/>: maintains the stack of active container symbols (e.g. Type -> Method -> Lambda/Local).
/// When a new child symbol is created, it is automatically linked to the parent at the top of the stack
/// via a CONTAINS relationship in the CodeGraph.
/// - Call Sites detection: intercept <see cref="InvocationExpressionSyntax"/> extracting the method name
/// and the possible receiver expression (receiver) to allow accurate resolution of the target in the Call Graph.
/// - Instance Detection: intercept both explicit <see cref="ObjectCreationExpressionSyntax"/> ('new Foo()')
/// both <see cref="ImplicitObjectCreationExpressionSyntax"/> (C# 9+ target-typed 'new()') and the field initializers.
/// - Inheritance Detection: Inspect <see cref="BaseListSyntax"/> on types to map class bases and implemented interfaces.
/// </summary>
internal sealed class CSharpSymbolWalker : CSharpSyntaxWalker
{
    private readonly CodeGraph _graph;
    private readonly string _filePath;

    /// <summary>Stack of active container symbols (used to establish CONTAINS relationships and identify the caller).</summary>
    private readonly Stack<Symbol> _scopeStack = new();

    /// <summary>Stack of current namespace segments (handles both block namespaces and file-scoped namespaces).</summary>
    private readonly Stack<string> _namespaceStack = new();

    /// <summary>Symbols declared at the root level (not contained in other types within the file).</summary>
    public List<Symbol> TopLevelSymbols { get; } = new();

    /// <summary>Invocation points detected within methods or properties.</summary>
    public List<CSharpCallSite> CallSites { get; } = new();

    /// <summary>Object instantiations ('new Type()') detected in methods or field initializers.</summary>
    public List<CSharpInstantiation> Instantiations { get; } = new();

    /// <summary>Inheritance clauses or interfaces declared for classes, structs, records, and interfaces.</summary>
    public List<CSharpTypeInheritance> Inheritances { get; } = new();

    /// <summary>
    /// Initializes a new walker instance for the specified file.
    /// </summary>
    /// <param name="graph">The CodeGraph in which to record the symbols and relationships CONTAINS.</param>
    /// <param name="filePath">The path to the analyzed C# source file.</param>
    public CSharpSymbolWalker(CodeGraph graph, string filePath)
    {
        _graph = graph;
        _filePath = filePath;
    }

    /// <summary>
    /// Returns the current complete namespace by combining segments on the stack with dots.
    /// </summary>
    private string CurrentNamespace => string.Join(".", _namespaceStack.Reverse());

    // =========================================================================
    // NAMESPACE MANAGEMENT
    // =========================================================================

    /// <summary>
    /// Visit a classic block namespace declaration ("namespace Foo.Bar { ... }").
    /// Pushes the namespace name onto the stack, visits the child nodes, then removes the namespace.
    /// </summary>
    public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
    {
        _namespaceStack.Push(node.Name.ToString());
        base.VisitNamespaceDeclaration(node);
        _namespaceStack.Pop();
    }

    /// <summary>
    /// Visits a file-scoped namespace declaration ("namespace Foo.Bar;").
    /// Available since C# 10, applies to all file contents.
    /// </summary>
    public override void VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
    {
        _namespaceStack.Push(node.Name.ToString());
        base.VisitFileScopedNamespaceDeclaration(node);
        _namespaceStack.Pop();
    }

    // =========================================================================
    // TYPE DECLARATIONS (Class, Interface, Struct, Record, Enum)
    // =========================================================================

    /// <summary>
    /// Visit the declaration of a C# class ("class Foo : IBar").
    /// </summary>
    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        ProcessTypeDeclaration(node, SymbolKind.Class, node.Identifier.ValueText, node.Modifiers, node.BaseList);
    }

    /// <summary>
    /// Visit the declaration of a C# interface ("interface IFoo").
    /// </summary>
    public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
    {
        ProcessTypeDeclaration(node, SymbolKind.Interface, node.Identifier.ValueText, node.Modifiers, node.BaseList);
    }

    /// <summary>
    /// Visit the declaration of a C# struct ("struct Point").
    /// </summary>
    public override void VisitStructDeclaration(StructDeclarationSyntax node)
    {
        ProcessTypeDeclaration(node, SymbolKind.Struct, node.Identifier.ValueText, node.Modifiers, node.BaseList);
    }

    /// <summary>
    /// Visit the declaration of a C# record ("Person record(...)").
    /// </summary>
    public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
    {
        var kind = node.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword)
            ? SymbolKind.Struct
            : SymbolKind.Record;
        ProcessTypeDeclaration(node, kind, node.Identifier.ValueText, node.Modifiers, node.BaseList);
    }

    /// <summary>
    /// Visit the declaration of a C# enumeration ("enum OrderStatus { Pending, Completed }").
    /// Creates the Enum symbol for the type and a Constant symbol for each enumerated member,
    /// connecting members to the Enum via CONTAINS relationships.
    /// </summary>
    public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
    {
        var span = node.GetLocation().GetLineSpan();
        var name = node.Identifier.ValueText;
        var qname = BuildQualifiedName(name);

        // 1. Create and register the symbol representing the entire Enum
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

        // 2. Iterate over each constant value declared within the Enum
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
    /// Unified logic for creating, recording and traversing a C# type (Class, Interface, Struct, Record).
    /// Handles:
    /// 1. Calculating the qualified name (including namespaces and containing/nested classes).
    /// 2. Visibility modifiers and static flag detection.
    /// 3. Extraction of inheritance clauses and interfaces (BaseListSyntax).
    /// 4. Managing the scope stack and recursively visiting all internal members.
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

        // 1. Creating the symbol for the type
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

        // 2. Extraction of inheritance relations and interface implementation
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

        // 3. Pushes the type onto the stack and recursively visits all declared members
        _scopeStack.Push(symbol);
        foreach (var member in node.Members)
        {
            Visit(member);
        }
        _scopeStack.Pop();
    }

    // =========================================================================
    // ROUTINE MEMBERS (Methods, Constructors, Properties)
    // =========================================================================

    /// <summary>
    /// Visit the declaration of a C# method ("public void Process(int id) { ... }").
    /// Extracts formal parameters, return type, visibility/static flags,
    /// registers the method in the graph and visits the body to intercept invocations and instantiations.
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

        // Extracts the formal parameters of the method
        ExtractParameters(node.ParameterList, symbol);
        RegisterSymbol(symbol);

        // Pushes the method onto the scope stack and visits the body (statements or lambda expressions)
        _scopeStack.Push(symbol);
        base.VisitMethodDeclaration(node);
        _scopeStack.Pop();
    }

    /// <summary>
    /// Visit the declaration of a C# constructor ("public Customer(string name) { ... }").
    /// Treated as a special method with no explicit return type.
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

        // Extracts the constructor parameters
        ExtractParameters(node.ParameterList, symbol);
        RegisterSymbol(symbol);

        // Pushes the constructor onto the stack and visits the body (including any 'this()' / 'base()')
        _scopeStack.Push(symbol);
        base.VisitConstructorDeclaration(node);
        _scopeStack.Pop();
    }

    /// <summary>
    /// Visit the declaration of a C# property ("public string Name { get; set; }").
    /// Register the Property symbol and visit the get/set accessories or the expression body.
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

        // Pushes the property onto the stack and visits accessors or internal expressions
        _scopeStack.Push(symbol);
        base.VisitPropertyDeclaration(node);
        _scopeStack.Pop();
    }

    // =========================================================================
    // FIELDS, CONSTANTS AND EVENTS
    // =========================================================================

    /// <summary>
    /// Visit the declaration of one or more C# fields or constants ("private readonly int _count = 0;", "public const int Max = 10;").
    /// Also detects any inline instantiation expressions ('new Foo()') in the field initializer.
    /// </summary>
    public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        var span = node.GetLocation().GetLineSpan();
        var isStatic = node.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
        var isConst = node.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword));
        var access = DetermineAccessibility(node.Modifiers, isType: false);
        var typeName = node.Declaration.Type.ToString();

        // A single FieldDeclarationSyntax can declare multiple variables (e.g. "int a = 1, b = 2;")
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

            // If the field has an inline initialization with object creation (e.g. 'private List<int> list = new List<int>();')
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
    /// Visit the declaration of a C# event field ("public event EventHandler? OrderCompleted;").
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
    // INVOCATIONS AND INSTANTIATIONS (Call Sites & Instantiations)
    // =========================================================================

    /// <summary>
    /// Visits a C# method or function invocation expression ("service.Execute()", "DoWork()", "Console.WriteLine()").
    /// Identifies the calling routine from the scope stack, extracts the name of the invoked method and any receiver expression,
    /// and records the call site for subsequent resolution in the Call Graph.
    /// </summary>
    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        var caller = GetCurrentRoutineSymbol();
        if (caller != null)
        {
            var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            string methodName;
            string? receiver = null;

            // Case 1: Invocation via member access (e.g. 'obj.Method()' or 'ClassName.StaticMethod()')
            if (node.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                methodName = memberAccess.Name.Identifier.ValueText;
                receiver = memberAccess.Expression.ToString();
            }
            // Case 2: Direct invocation of local/inherited method (e.g. 'DoSomething()')
            else if (node.Expression is IdentifierNameSyntax identifier)
            {
                methodName = identifier.Identifier.ValueText;
            }
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
    /// Visits an explicit C# object creation expression ("new Customer()", "new List<string>()").
    /// Register the instantiation by associating it with the current routine.
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
    /// Visits an object creation target-typed implicit expression (C# 9+, "Customer c = new();").
    /// Register the instantiation with placeholder "var" which will be resolved contextually.
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
    // SUPPORT METHODS AND UTILITIES
    // =========================================================================

    /// <summary>
    /// Register a symbol in the graph, set the C# language, and create the CONTAINS relationship
    /// with the active container symbol at the top of the stack (or adds to TopLevelSymbols if root).
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
    /// It moves up the scope stack to find the closest routine symbol (Method or Property) that encloses the current statement.
    /// If it does not find a routine, it returns the symbol at the top of the stack (e.g. the class or field for initializers).
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
    /// Extracts the list of formal parameters from a method or constructor declaration
    /// and populates them inside the <see cref="Symbol.Parameters"/> property of the symbol routine.
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
    /// Constructs the fully qualified name (QualifiedName) by concatenating the current namespace,
    /// the names of the container/nested types and the name of the symbol itself.
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
    /// Normalize a C#-style string by removing generic type parameters ('<T>'),
    /// namespace prefixes ('System.') and nullability indicators ('?').
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
    /// Determines the semantic level of accessibility (<see cref="Accessibility"/>) from a list of C# syntactic modifiers.
    /// Apply the language's default rules when modifiers are omitted:
    /// - Tipi (classi, struct, enum, record): <see cref="Accessibility.Internal"/>
    /// - Members (methods, properties, fields): <see cref="Accessibility.Private"/>
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
