using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Macondo.Semantic.Model;
using Symbol = Macondo.Semantic.Model.Symbol;
using SymbolKind = Macondo.Semantic.Model.SymbolKind;
using Accessibility = Macondo.Semantic.Model.Accessibility;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Rappresenta un sito di chiamata a metodo rilevato all'interno di un metodo/proprietà C#.
/// </summary>
public sealed class CSharpCallSite
{
    public required Symbol CallerSymbol { get; init; }
    public required string InvokedMethodName { get; init; }
    public string? ReceiverExpressionText { get; init; }
    public int Line { get; init; }
}

/// <summary>
/// Rappresenta un'istanziazione di oggetto ('new Foo(...)') rilevata in C#.
/// </summary>
public sealed class CSharpInstantiation
{
    public required Symbol SourceSymbol { get; init; }
    public required string TargetTypeName { get; init; }
    public int Line { get; init; }
}

/// <summary>
/// Rappresenta una clausola di ereditarietà o implementazione di interfaccia per un tipo C#.
/// </summary>
public sealed class CSharpTypeInheritance
{
    public required Symbol DerivedTypeSymbol { get; init; }
    public required string BaseOrInterfaceTypeName { get; init; }
    public int Line { get; init; }
}

/// <summary>
/// Risultato dell'estrazione semantica di un file sorgente C# (.cs).
/// </summary>
public sealed class CSharpExtractionResult
{
    public required string FilePath { get; init; }
    public required IReadOnlyList<Symbol> TopLevelSymbols { get; init; }
    public required IReadOnlyList<CSharpCallSite> CallSites { get; init; }
    public required IReadOnlyList<CSharpInstantiation> Instantiations { get; init; }
    public required IReadOnlyList<CSharpTypeInheritance> Inheritances { get; init; }
    public required IReadOnlyList<string> ParseErrors { get; init; }
    public bool Success => ParseErrors.Count == 0;
}

/// <summary>
/// Estrattore semantico di file sorgente C# basato su Microsoft Roslyn.
/// Effettua il parsing del SyntaxTree ed estrae tutti i simboli (Classi, Interfacce, Record, Struct, Enum, Metodi, Proprietà, Campi)
/// e le relazioni (Contenimento, Chiamate, Istanziazioni, Ereditarietà/Interfacce).
/// </summary>
public static class CSharpExtractor
{
    /// <summary>
    /// Esegue il parsing e l'estrazione semantica di un file C# inserendo i simboli nel <see cref="CodeGraph"/>.
    /// </summary>
    public static CSharpExtractionResult ExtractCSharpFile(CodeGraph graph, string filePath, string sourceText)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, path: filePath, options: new CSharpParseOptions(LanguageVersion.Latest));
        var root = syntaxTree.GetRoot();

        var errors = new List<string>();
        foreach (var diag in syntaxTree.GetDiagnostics())
        {
            if (diag.Severity == DiagnosticSeverity.Error)
            {
                errors.Add(diag.ToString());
            }
        }

        var walker = new CSharpSymbolWalker(graph, filePath);
        walker.Visit(root);

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

    private sealed class CSharpSymbolWalker : CSharpSyntaxWalker
    {
        private readonly CodeGraph _graph;
        private readonly string _filePath;
        private readonly Stack<Symbol> _scopeStack = new();
        private readonly Stack<string> _namespaceStack = new();

        public List<Symbol> TopLevelSymbols { get; } = new();
        public List<CSharpCallSite> CallSites { get; } = new();
        public List<CSharpInstantiation> Instantiations { get; } = new();
        public List<CSharpTypeInheritance> Inheritances { get; } = new();

        public CSharpSymbolWalker(CodeGraph graph, string filePath)
        {
            _graph = graph;
            _filePath = filePath;
        }

        private string CurrentNamespace => string.Join(".", _namespaceStack.Reverse());

        public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
        {
            _namespaceStack.Push(node.Name.ToString());
            base.VisitNamespaceDeclaration(node);
            _namespaceStack.Pop();
        }

        public override void VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
        {
            _namespaceStack.Push(node.Name.ToString());
            base.VisitFileScopedNamespaceDeclaration(node);
            _namespaceStack.Pop();
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            ProcessTypeDeclaration(node, SymbolKind.Class, node.Identifier.ValueText, node.Modifiers, node.BaseList);
        }

        public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
        {
            ProcessTypeDeclaration(node, SymbolKind.Interface, node.Identifier.ValueText, node.Modifiers, node.BaseList);
        }

        public override void VisitStructDeclaration(StructDeclarationSyntax node)
        {
            ProcessTypeDeclaration(node, SymbolKind.Struct, node.Identifier.ValueText, node.Modifiers, node.BaseList);
        }

        public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
        {
            ProcessTypeDeclaration(node, SymbolKind.Record, node.Identifier.ValueText, node.Modifiers, node.BaseList);
        }

        public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
        {
            var span = node.GetLocation().GetLineSpan();
            var name = node.Identifier.ValueText;
            var qname = BuildQualifiedName(name);

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

        private void ProcessTypeDeclaration(TypeDeclarationSyntax node, SymbolKind kind, string name, SyntaxTokenList modifiers, BaseListSyntax? baseList)
        {
            var span = node.GetLocation().GetLineSpan();
            var qname = BuildQualifiedName(name);

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

            // Estrae le clausole di ereditarietà / interfacce
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

            _scopeStack.Push(symbol);
            foreach (var member in node.Members)
            {
                Visit(member);
            }
            _scopeStack.Pop();
        }

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

            ExtractParameters(node.ParameterList, symbol);
            RegisterSymbol(symbol);

            _scopeStack.Push(symbol);
            base.VisitMethodDeclaration(node);
            _scopeStack.Pop();
        }

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

            ExtractParameters(node.ParameterList, symbol);
            RegisterSymbol(symbol);

            _scopeStack.Push(symbol);
            base.VisitConstructorDeclaration(node);
            _scopeStack.Pop();
        }

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

            _scopeStack.Push(symbol);
            base.VisitPropertyDeclaration(node);
            _scopeStack.Pop();
        }

        public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            var span = node.GetLocation().GetLineSpan();
            var isStatic = node.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
            var isConst = node.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword));
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

                // Se c'è un'inizializzazione con 'new Foo()' sul campo
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

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var caller = GetCurrentRoutineSymbol();
            if (caller != null)
            {
                var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                string methodName;
                string? receiver = null;

                if (node.Expression is MemberAccessExpressionSyntax memberAccess)
                {
                    methodName = memberAccess.Name.Identifier.ValueText;
                    receiver = memberAccess.Expression.ToString();
                }
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

        private Symbol? GetCurrentRoutineSymbol()
        {
            foreach (var sym in _scopeStack)
            {
                if (sym.Kind is SymbolKind.Method or SymbolKind.Property)
                    return sym;
            }
            return _scopeStack.Count > 0 ? _scopeStack.Peek() : null;
        }

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
