using Macondo.Semantic.Model;
using Symbol = Macondo.Semantic.Model.Symbol;
using SymbolKind = Macondo.Semantic.Model.SymbolKind;
using Accessibility = Macondo.Semantic.Model.Accessibility;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Parser sintattico ricorsivo per file sorgente Java (.java).
/// </summary>
internal sealed class JavaFileParser
{
    private readonly CodeGraph _graph;
    private readonly string _filePath;
    private readonly string _sourceText;
    private readonly List<JavaToken> _tokens = new();
    private int _tokenIndex;

    /// <summary>Stack dei simboli contenitori attivi (per gerarchia CONTAINS e QualifiedName).</summary>
    private readonly Stack<Symbol> _scopeStack = new();

    /// <summary>Package Java corrente estratto dalla dichiarazione 'package ...;'.</summary>
    private string _packageName = string.Empty;

    /// <summary>Elenco dei simboli radice di primo livello estratti dal file.</summary>
    private readonly List<Symbol> _topLevelSymbols = new();

    /// <summary>Elenco dei call-site di chiamata a metodo rilevati nei corpi.</summary>
    private readonly List<JavaCallSite> _callSites = new();

    /// <summary>Elenco delle istanziazioni 'new' rilevate.</summary>
    private readonly List<JavaInstantiation> _instantiations = new();

    /// <summary>Elenco delle clausole 'extends' e 'implements' rilevate.</summary>
    private readonly List<JavaTypeInheritance> _inheritances = new();

    /// <summary>Elenco degli errori o avvisi sintattici raccolti durante il parsing.</summary>
    private readonly List<string> _parseErrors = new();

    /// <summary>Insieme delle parole chiave riservate in Java (fino a Java 21).</summary>
    private static readonly HashSet<string> JavaKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "assert", "boolean", "break", "byte", "case", "catch", "char", "class",
        "const", "continue", "default", "do", "double", "else", "enum", "extends", "final",
        "finally", "float", "for", "goto", "if", "implements", "import", "instanceof", "int",
        "interface", "long", "native", "new", "non-sealed", "package", "permits", "private",
        "protected", "public", "record", "return", "sealed", "short", "static", "strictfp",
        "super", "switch", "synchronized", "this", "throw", "throws", "transient", "try",
        "void", "volatile", "while", "yield", "var"
    };

    /// <summary>Insieme dei modificatori di visibilità e caratteristiche di tipo/membro Java.</summary>
    private static readonly HashSet<string> ModifierKeywords = new(StringComparer.Ordinal)
    {
        "public", "protected", "private", "static", "final", "abstract", "synchronized",
        "native", "transient", "volatile", "strictfp", "default", "sealed", "non-sealed"
    };

    public JavaFileParser(CodeGraph graph, string filePath, string sourceText)
    {
        _graph = graph;
        _filePath = filePath;
        _sourceText = sourceText;
    }

    /// <summary>
    /// Avvia il processo a due fasi: tokenizzazione lessicale seguita da parsing AST ricorsivo.
    /// </summary>
    public JavaExtractionResult Parse()
    {
        try
        {
            // Fase 1: Lexing del sorgente in sequenza di JavaToken
            Tokenize();

            // Fase 2: Parsing della Compilation Unit (package, imports, tipi, metodi, campi)
            ParseCompilationUnit();
        }
        catch (Exception ex)
        {
            _parseErrors.Add($"Java parser warning on {_filePath}: {ex.Message}");
        }

        return new JavaExtractionResult
        {
            FilePath = _filePath,
            TopLevelSymbols = _topLevelSymbols,
            CallSites = _callSites,
            Instantiations = _instantiations,
            Inheritances = _inheritances,
            ParseErrors = _parseErrors
        };
    }

    // =========================================================================
    // FASE 1: LEXER / TOKENIZZAZIONE LESSICALE
    // =========================================================================

    /// <summary>
    /// Scansiona il testo sorgente carattere per carattere producendo la lista piatta dei token.
    /// Gestisce commenti riga (//), commenti blocco (/* */), JavaDoc (/** */), stringhe e text block multiriga (""").
    /// </summary>
    private void Tokenize()
    {
        var len = _sourceText.Length;
        var line = 1;
        var col = 1;
        var i = 0;

        while (i < len)
        {
            var ch = _sourceText[i];

            // 1. Gestione a capo Windows (\r\n) o Unix (\n)
            if (ch == '\r')
            {
                i++;
                if (i < len && _sourceText[i] == '\n')
                    i++;
                line++;
                col = 1;
                continue;
            }
            if (ch == '\n')
            {
                i++;
                line++;
                col = 1;
                continue;
            }
            if (char.IsWhiteSpace(ch))
            {
                i++;
                col++;
                continue;
            }

            // 2. Commenti a riga singola '//'
            if (ch == '/' && i + 1 < len && _sourceText[i + 1] == '/')
            {
                i += 2;
                while (i < len && _sourceText[i] != '\r' && _sourceText[i] != '\n')
                    i++;
                continue;
            }

            // 3. Commenti a blocchi '/*' o JavaDoc '/**'
            if (ch == '/' && i + 1 < len && _sourceText[i + 1] == '*')
            {
                i += 2;
                while (i < len)
                {
                    if (_sourceText[i] == '\r')
                    {
                        i++;
                        if (i < len && _sourceText[i] == '\n') i++;
                        line++;
                        col = 1;
                    }
                    else if (_sourceText[i] == '\n')
                    {
                        i++;
                        line++;
                        col = 1;
                    }
                    else if (_sourceText[i] == '*' && i + 1 < len && _sourceText[i + 1] == '/')
                    {
                        i += 2;
                        col += 2;
                        break;
                    }
                    else
                    {
                        i++;
                        col++;
                    }
                }
                continue;
            }

            // 4. Stringhe Java Text Block """ ... """ (Java 15+)
            if (ch == '"' && i + 2 < len && _sourceText[i + 1] == '"' && _sourceText[i + 2] == '"')
            {
                var startLine = line;
                var startCol = col;
                i += 3;
                col += 3;
                var startIdx = i;
                while (i < len)
                {
                    if (_sourceText[i] == '"' && i + 2 < len && _sourceText[i + 1] == '"' && _sourceText[i + 2] == '"')
                    {
                        i += 3;
                        col += 3;
                        break;
                    }
                    if (_sourceText[i] == '\n')
                    {
                        line++;
                        col = 1;
                    }
                    else if (_sourceText[i] == '\r')
                    {
                        // ignore \r
                    }
                    else
                    {
                        col++;
                    }
                    i++;
                }
                _tokens.Add(new JavaToken
                {
                    Type = JavaTokenType.StringLiteral,
                    Value = _sourceText[startIdx..Math.Min(i, len)],
                    Line = startLine,
                    Column = startCol
                });
                continue;
            }

            // 5. Stringhe standard " ... "
            if (ch == '"')
            {
                var startLine = line;
                var startCol = col;
                i++;
                col++;
                var sb = new System.Text.StringBuilder();
                while (i < len && _sourceText[i] != '"')
                {
                    if (_sourceText[i] == '\\' && i + 1 < len)
                    {
                        sb.Append(_sourceText[i]);
                        sb.Append(_sourceText[i + 1]);
                        i += 2;
                        col += 2;
                        continue;
                    }
                    if (_sourceText[i] == '\n' || _sourceText[i] == '\r')
                        break;
                    sb.Append(_sourceText[i]);
                    i++;
                    col++;
                }
                if (i < len && _sourceText[i] == '"')
                {
                    i++;
                    col++;
                }
                _tokens.Add(new JavaToken
                {
                    Type = JavaTokenType.StringLiteral,
                    Value = sb.ToString(),
                    Line = startLine,
                    Column = startCol
                });
                continue;
            }

            // 6. Costanti carattere 'c'
            if (ch == '\'')
            {
                var startLine = line;
                var startCol = col;
                i++;
                col++;
                var sb = new System.Text.StringBuilder();
                while (i < len && _sourceText[i] != '\'')
                {
                    if (_sourceText[i] == '\\' && i + 1 < len)
                    {
                        sb.Append(_sourceText[i]);
                        sb.Append(_sourceText[i + 1]);
                        i += 2;
                        col += 2;
                        continue;
                    }
                    if (_sourceText[i] == '\n' || _sourceText[i] == '\r')
                        break;
                    sb.Append(_sourceText[i]);
                    i++;
                    col++;
                }
                if (i < len && _sourceText[i] == '\'')
                {
                    i++;
                    col++;
                }
                _tokens.Add(new JavaToken
                {
                    Type = JavaTokenType.CharLiteral,
                    Value = sb.ToString(),
                    Line = startLine,
                    Column = startCol
                });
                continue;
            }

            // 7. Identificatori e Parole Chiave
            if (IsJavaIdentifierStart(ch))
            {
                var startLine = line;
                var startCol = col;
                var startIdx = i;
                while (i < len && IsJavaIdentifierPart(_sourceText[i]))
                {
                    i++;
                    col++;
                }
                var text = _sourceText[startIdx..i];
                var isKw = JavaKeywords.Contains(text);
                _tokens.Add(new JavaToken
                {
                    Type = isKw ? JavaTokenType.Keyword : JavaTokenType.Identifier,
                    Value = text,
                    Line = startLine,
                    Column = startCol
                });
                continue;
            }

            // 8. Costanti Numeriche
            if (char.IsDigit(ch) || (ch == '.' && i + 1 < len && char.IsDigit(_sourceText[i + 1])))
            {
                var startLine = line;
                var startCol = col;
                var startIdx = i;
                while (i < len && (char.IsLetterOrDigit(_sourceText[i]) || _sourceText[i] == '.' || _sourceText[i] == '_'))
                {
                    i++;
                    col++;
                }
                _tokens.Add(new JavaToken
                {
                    Type = JavaTokenType.Number,
                    Value = _sourceText[startIdx..i],
                    Line = startLine,
                    Column = startCol
                });
                continue;
            }

            // 9. Operatori a tre caratteri (varargs ...)
            var startOpLine = line;
            var startOpCol = col;
            if (i + 2 < len)
            {
                var triple = _sourceText.Substring(i, 3);
                if (triple is "...")
                {
                    _tokens.Add(new JavaToken
                    {
                        Type = JavaTokenType.Symbol,
                        Value = triple,
                        Line = startOpLine,
                        Column = startOpCol
                    });
                    i += 3;
                    col += 3;
                    continue;
                }
            }

            // 10. Operatori a due caratteri (::, ->, ==, !=, <=, >=, &&, ||, ++, --, +=, -=, *=, /=)
            if (i + 1 < len)
            {
                var pair = _sourceText.Substring(i, 2);
                if (pair is "::" or "->" or "==" or "!=" or "<=" or ">=" or "&&" or "||" or "++" or "--" or "+=" or "-=" or "*=" or "/=")
                {
                    _tokens.Add(new JavaToken
                    {
                        Type = JavaTokenType.Symbol,
                        Value = pair,
                        Line = startOpLine,
                        Column = startOpCol
                    });
                    i += 2;
                    col += 2;
                    continue;
                }
            }

            // 11. Simboli e Punteggiatura a singolo carattere
            _tokens.Add(new JavaToken
            {
                Type = JavaTokenType.Symbol,
                Value = ch.ToString(),
                Line = startOpLine,
                Column = startOpCol
            });
            i++;
            col++;
        }

        // Token sentinella di fine file
        _tokens.Add(new JavaToken
        {
            Type = JavaTokenType.EndOfFile,
            Value = string.Empty,
            Line = line,
            Column = col
        });
    }

    private static bool IsJavaIdentifierStart(char ch)
    {
        if (ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '_' or '$')
            return true;

        var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
        return cat is System.Globalization.UnicodeCategory.UppercaseLetter
                   or System.Globalization.UnicodeCategory.LowercaseLetter
                   or System.Globalization.UnicodeCategory.TitlecaseLetter
                   or System.Globalization.UnicodeCategory.ModifierLetter
                   or System.Globalization.UnicodeCategory.OtherLetter
                   or System.Globalization.UnicodeCategory.LetterNumber
                   or System.Globalization.UnicodeCategory.CurrencySymbol
                   or System.Globalization.UnicodeCategory.ConnectorPunctuation;
    }

    private static bool IsJavaIdentifierPart(char ch)
    {
        if (ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '$')
            return true;

        var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
        return cat is System.Globalization.UnicodeCategory.UppercaseLetter
                   or System.Globalization.UnicodeCategory.LowercaseLetter
                   or System.Globalization.UnicodeCategory.TitlecaseLetter
                   or System.Globalization.UnicodeCategory.ModifierLetter
                   or System.Globalization.UnicodeCategory.OtherLetter
                   or System.Globalization.UnicodeCategory.LetterNumber
                   or System.Globalization.UnicodeCategory.CurrencySymbol
                   or System.Globalization.UnicodeCategory.ConnectorPunctuation
                   or System.Globalization.UnicodeCategory.DecimalDigitNumber
                   or System.Globalization.UnicodeCategory.NonSpacingMark
                   or System.Globalization.UnicodeCategory.SpacingCombiningMark
                   or System.Globalization.UnicodeCategory.Format;
    }

    // =========================================================================
    // HELPER DI NAVIGAZIONE DEI TOKEN
    // =========================================================================

    private JavaToken Peek(int offset = 0)
    {
        var idx = _tokenIndex + offset;
        if (idx >= 0 && idx < _tokens.Count)
            return _tokens[idx];
        return _tokens[^1];
    }

    private JavaToken Next()
    {
        var token = Peek();
        if (_tokenIndex < _tokens.Count - 1)
            _tokenIndex++;
        return token;
    }

    private bool Match(string val)
    {
        if (Peek().Value == val)
        {
            Next();
            return true;
        }
        return false;
    }

    private bool Check(string val) => Peek().Value == val;

    private bool Check(JavaTokenType type) => Peek().Type == type;

    // =========================================================================
    // FASE 2: PARSER DELLA COMPILATION UNIT E TOP LEVEL
    // =========================================================================

    /// <summary>
    /// Analizza la struttura complessiva del file Java: pacchetto, importazioni e dichiarazioni top level.
    /// </summary>
    private void ParseCompilationUnit()
    {
        while (!Check(JavaTokenType.EndOfFile))
        {
            SkipAnnotations();

            if (Match("package"))
            {
                _packageName = ParseQualifiedIdentifier();
                Match(";");
                continue;
            }

            if (Match("import"))
            {
                Match("static");
                ParseQualifiedIdentifier();
                if (Match("."))
                    Match("*");
                Match(";");
                continue;
            }

            ParseTopLevelDeclaration();
        }
    }

    /// <summary>
    /// Salta le annotazioni (@Annotation(...)) presenti prima di dichiarazioni di classi, metodi o campi.
    /// </summary>
    private void SkipAnnotations()
    {
        while (Check("@") && Peek(1).Value != "interface")
        {
            Next(); // consuma '@'
            ParseQualifiedIdentifier();
            if (Check("("))
            {
                ConsumeParenthesizedGroup();
            }
        }
    }

    /// <summary>
    /// Consuma ricorsivamente un blocco racchiuso tra parentesi tonde '(' e ')'.
    /// </summary>
    private void ConsumeParenthesizedGroup()
    {
        if (!Match("(")) return;
        var depth = 1;
        while (depth > 0 && !Check(JavaTokenType.EndOfFile))
        {
            if (Check("(")) depth++;
            else if (Check(")")) depth--;
            Next();
        }
    }

    /// <summary>
    /// Consuma ricorsivamente un blocco racchiuso tra parentesi graffe '{' e '}'.
    /// </summary>
    private void ConsumeBracedGroup()
    {
        if (!Match("{")) return;
        var depth = 1;
        while (depth > 0 && !Check(JavaTokenType.EndOfFile))
        {
            if (Check("{")) depth++;
            else if (Check("}")) depth--;
            Next();
        }
    }

    /// <summary>
    /// Analizza un identificatore qualificato con punti (es. "java.util.concurrent.atomic").
    /// </summary>
    private string ParseQualifiedIdentifier()
    {
        var sb = new System.Text.StringBuilder();
        while (!Check(JavaTokenType.EndOfFile))
        {
            var tok = Peek();
            if (tok.Type == JavaTokenType.Identifier || tok.Type == JavaTokenType.Keyword)
            {
                sb.Append(Next().Value);
                if (Check("."))
                {
                    sb.Append(Next().Value);
                    continue;
                }
            }
            break;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Analizza una dichiarazione di primo livello (Classe, Interfaccia, Enum, Record o Annotazione).
    /// </summary>
    private void ParseTopLevelDeclaration()
    {
        var modifiers = ParseModifiers();

        // Riconosce dichiarazione di annotazione: @interface MyAnnotation
        if (Check("@") && Peek(1).Value == "interface")
        {
            Next(); // '@'
            Next(); // 'interface'
            ParseTypeDeclaration(SymbolKind.Interface, modifiers);
            return;
        }

        if (Match("class"))
        {
            ParseTypeDeclaration(SymbolKind.Class, modifiers);
            return;
        }
        if (Match("interface"))
        {
            ParseTypeDeclaration(SymbolKind.Interface, modifiers);
            return;
        }
        if (Match("enum"))
        {
            ParseEnumDeclaration(modifiers);
            return;
        }
        if (Match("record"))
        {
            ParseRecordDeclaration(modifiers);
            return;
        }

        // Fallback resiliente: avanza di un token se non riconosciuto
        Next();
    }

    /// <summary>
    /// Raccoglie tutti i modificatori Java consecutivi (public, private, static, final, abstract, ecc.).
    /// </summary>
    private List<string> ParseModifiers()
    {
        var mods = new List<string>();
        while (!Check(JavaTokenType.EndOfFile))
        {
            if (Check("@"))
            {
                if (Peek(1).Value == "interface")
                    break;

                SkipAnnotations();
                continue;
            }

            var val = Peek().Value;
            if (ModifierKeywords.Contains(val))
            {
                mods.Add(Next().Value);
                continue;
            }
            break;
        }
        return mods;
    }

    /// <summary>
    /// Determina il livello di visibilità semantico (<see cref="Accessibility"/>) dai modificatori Java.
    /// In Java l'assenza di modificatori espliciti indica visibilità di package (Default).
    /// </summary>
    private Accessibility DetermineAccessibility(IEnumerable<string> modifiers)
    {
        if (modifiers.Contains("public")) return Accessibility.Public;
        if (modifiers.Contains("protected")) return Accessibility.Friend; // Mappato su visibilità intermedia/package
        if (modifiers.Contains("private")) return Accessibility.Private;
        return Accessibility.Default; // Package-private in Java
    }

    /// <summary>
    /// Costruisce il nome qualificato completo tenendo conto del package e della gerarchia di classi annidate.
    /// </summary>
    private string BuildQualifiedName(string simpleName)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(_packageName))
            parts.Add(_packageName);

        foreach (var sym in _scopeStack.Reverse())
        {
            if (sym.Kind is SymbolKind.Class or SymbolKind.Interface or SymbolKind.Enum or SymbolKind.Record or SymbolKind.Type)
                parts.Add(sym.Name);
        }

        parts.Add(simpleName);
        return string.Join(".", parts);
    }

    /// <summary>
    /// Registra un simbolo nel CodeGraph impostando il linguaggio Java e creando la relazione CONTAINS con il genitore.
    /// </summary>
    private void RegisterSymbol(Symbol symbol)
    {
        symbol.Language = SourceLanguage.Java;
        _graph.Symbols.Add(symbol);

        if (_scopeStack.Count > 0)
        {
            var parent = _scopeStack.Peek();
            _graph.AddContains(parent, symbol);
        }
        else
        {
            _topLevelSymbols.Add(symbol);
        }
    }

    // =========================================================================
    // DICHIARAZIONI DI TIPI: CLASSI, INTERFACCE, RECORD, ENUM
    // =========================================================================

    /// <summary>
    /// Analizza una dichiarazione di classe o interfaccia Java ("class Foo<T> extends Base implements IFoo").
    /// </summary>
    private void ParseTypeDeclaration(SymbolKind kind, List<string> modifiers)
    {
        var nameToken = Peek();
        if (nameToken.Type != JavaTokenType.Identifier)
        {
            Next();
            return;
        }

        var name = Next().Value;
        var lineStart = nameToken.Line;
        var qname = BuildQualifiedName(name);

        // Salta parametri di tipo generici: <T, K extends Comparable<K>>
        SkipTypeParameters();

        var symbol = new Symbol
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            Name = name,
            QualifiedName = qname,
            FilePath = _filePath,
            LineStart = lineStart,
            LineEnd = lineStart,
            Accessibility = DetermineAccessibility(modifiers),
            IsStatic = modifiers.Contains("static")
        };

        RegisterSymbol(symbol);

        // Ereditarietà di classe o interfacce: extends BaseClass, BaseIface
        if (Match("extends"))
        {
            do
            {
                var baseNameToken = Peek();
                var baseName = ParseTypeName();
                if (!string.IsNullOrEmpty(baseName))
                {
                    _inheritances.Add(new JavaTypeInheritance
                    {
                        DerivedTypeSymbol = symbol,
                        BaseOrInterfaceTypeName = baseName,
                        Line = baseNameToken.Line,
                        IsInterface = kind == SymbolKind.Interface
                    });
                }
            } while (Match(","));
        }

        // Implementazione di interfacce: implements Iface1, Iface2
        if (Match("implements") || Match("permits"))
        {
            do
            {
                var ifaceToken = Peek();
                var ifaceName = ParseTypeName();
                if (!string.IsNullOrEmpty(ifaceName))
                {
                    _inheritances.Add(new JavaTypeInheritance
                    {
                        DerivedTypeSymbol = symbol,
                        BaseOrInterfaceTypeName = ifaceName,
                        Line = ifaceToken.Line,
                        IsInterface = true
                    });
                }
            } while (Match(","));
        }

        if (!Match("{"))
            return;

        _scopeStack.Push(symbol);
        ParseTypeBody(symbol);
        _scopeStack.Pop();
    }

    /// <summary>
    /// Analizza una dichiarazione di Java Record ("record Person(String name, int age) implements IPerson").
    /// </summary>
    private void ParseRecordDeclaration(List<string> modifiers)
    {
        var nameToken = Peek();
        if (nameToken.Type != JavaTokenType.Identifier)
        {
            Next();
            return;
        }

        var name = Next().Value;
        var lineStart = nameToken.Line;
        var qname = BuildQualifiedName(name);

        SkipTypeParameters();

        var symbol = new Symbol
        {
            Id = Guid.NewGuid(),
            Kind = SymbolKind.Record,
            Name = name,
            QualifiedName = qname,
            FilePath = _filePath,
            LineStart = lineStart,
            LineEnd = lineStart,
            Accessibility = DetermineAccessibility(modifiers),
            IsStatic = modifiers.Contains("static")
        };

        RegisterSymbol(symbol);

        // Componenti del record nell'intestazione: record Point(int x, int y)
        if (Match("("))
        {
            while (!Check(")") && !Check(JavaTokenType.EndOfFile))
            {
                SkipAnnotations();
                var paramModifiers = ParseModifiers();
                var ptype = ParseTypeName();
                if (Match("..."))
                    ptype += "[]";
                if (Check(JavaTokenType.Identifier))
                {
                    var pnameToken = Next();
                    var fieldSymbol = new Symbol
                    {
                        Id = Guid.NewGuid(),
                        Kind = SymbolKind.Field,
                        Name = pnameToken.Value,
                        QualifiedName = $"{qname}.{pnameToken.Value}",
                        FilePath = _filePath,
                        LineStart = pnameToken.Line,
                        LineEnd = pnameToken.Line,
                        Accessibility = Accessibility.Private,
                        DeclaredTypeName = ptype
                    };
                    _graph.Symbols.Add(fieldSymbol);
                    _graph.AddContains(symbol, fieldSymbol);
                }
                if (!Match(",")) break;
            }
            Match(")");
        }

        if (Match("implements"))
        {
            do
            {
                var ifaceToken = Peek();
                var ifaceName = ParseTypeName();
                if (!string.IsNullOrEmpty(ifaceName))
                {
                    _inheritances.Add(new JavaTypeInheritance
                    {
                        DerivedTypeSymbol = symbol,
                        BaseOrInterfaceTypeName = ifaceName,
                        Line = ifaceToken.Line,
                        IsInterface = true
                    });
                }
            } while (Match(","));
        }

        if (!Match("{")) return;

        _scopeStack.Push(symbol);
        ParseTypeBody(symbol);
        _scopeStack.Pop();
    }

    /// <summary>
    /// Analizza una dichiarazione di Java Enum ("enum Status { PENDING, COMPLETED; void helper() {} }").
    /// </summary>
    private void ParseEnumDeclaration(List<string> modifiers)
    {
        var nameToken = Peek();
        if (nameToken.Type != JavaTokenType.Identifier)
        {
            Next();
            return;
        }

        var name = Next().Value;
        var lineStart = nameToken.Line;
        var qname = BuildQualifiedName(name);

        var symbol = new Symbol
        {
            Id = Guid.NewGuid(),
            Kind = SymbolKind.Enum,
            Name = name,
            QualifiedName = qname,
            FilePath = _filePath,
            LineStart = lineStart,
            LineEnd = lineStart,
            Accessibility = DetermineAccessibility(modifiers),
            IsStatic = modifiers.Contains("static")
        };

        RegisterSymbol(symbol);

        if (Match("implements"))
        {
            do
            {
                var ifaceToken = Peek();
                var ifaceName = ParseTypeName();
                if (!string.IsNullOrEmpty(ifaceName))
                {
                    _inheritances.Add(new JavaTypeInheritance
                    {
                        DerivedTypeSymbol = symbol,
                        BaseOrInterfaceTypeName = ifaceName,
                        Line = ifaceToken.Line,
                        IsInterface = true
                    });
                }
            } while (Match(","));
        }

        if (!Match("{")) return;

        _scopeStack.Push(symbol);

        // Estrazione delle costanti dichiarate nell'Enum
        while (!Check("}") && !Check(";") && !Check(JavaTokenType.EndOfFile))
        {
            SkipAnnotations();
            if (Check(JavaTokenType.Identifier))
            {
                var constToken = Next();
                var constSymbol = new Symbol
                {
                    Id = Guid.NewGuid(),
                    Kind = SymbolKind.Constant,
                    Name = constToken.Value,
                    QualifiedName = $"{qname}.{constToken.Value}",
                    FilePath = _filePath,
                    LineStart = constToken.Line,
                    LineEnd = constToken.Line,
                    Accessibility = Accessibility.Public,
                    DeclaredTypeName = name
                };
                _graph.Symbols.Add(constSymbol);
                _graph.AddContains(symbol, constSymbol);

                // Argomenti del costruttore enum (es. STATUS_OK(200, "OK"))
                if (Check("("))
                    ConsumeParenthesizedGroup();

                // Corpo specifico per singola costante enum (es. CONST { void execute() {} })
                if (Check("{"))
                    ConsumeBracedGroup();

                if (Match(",")) continue;
                if (Check(";") || Check("}")) break;
            }
            else
            {
                break;
            }
        }

        if (Match(";"))
        {
            // Corpo esteso dell'enum contenente metodi o campi aggiuntivi
            ParseTypeBody(symbol);
        }
        else
        {
            Match("}");
        }

        _scopeStack.Pop();
    }

    // =========================================================================
    // CORPO DEI TIPI: METODI, COSTRUTTORI, CAMPI, INNER TYPES
    // =========================================================================

    /// <summary>
    /// Analizza tutti i membri interni dichiarati all'interno di un tipo Java.
    /// </summary>
    private void ParseTypeBody(Symbol enclosingType)
    {
        while (!Check("}") && !Check(JavaTokenType.EndOfFile))
        {
            SkipAnnotations();

            if (Check("}")) break;

            // Blocchi statici o di inizializzazione di istanza (static { ... } o { ... })
            if (Match("static") && Check("{"))
            {
                ParseMethodBody(enclosingType);
                continue;
            }
            if (Check("{"))
            {
                ParseMethodBody(enclosingType);
                continue;
            }

            var modifiers = ParseModifiers();

            // Inner Types (Classi, Interfacce, Enum, Record annidati)
            if (Check("@") && Peek(1).Value == "interface")
            {
                Next(); Next();
                ParseTypeDeclaration(SymbolKind.Interface, modifiers);
                continue;
            }
            if (Match("class"))
            {
                ParseTypeDeclaration(SymbolKind.Class, modifiers);
                continue;
            }
            if (Match("interface"))
            {
                ParseTypeDeclaration(SymbolKind.Interface, modifiers);
                continue;
            }
            if (Match("enum"))
            {
                ParseEnumDeclaration(modifiers);
                continue;
            }
            if (Match("record"))
            {
                ParseRecordDeclaration(modifiers);
                continue;
            }

            // Generics a livello di metodo: <T, R>
            SkipTypeParameters();

            // Rilevamento Costruttore: NomeTipo(parametri) oppure compact constructor NomeTipo { ... } nei Record
            if (Check(JavaTokenType.Identifier) && Peek().Value == enclosingType.Name && (Peek(1).Value == "(" || (enclosingType.Kind == SymbolKind.Record && Peek(1).Value == "{")))
            {
                ParseConstructorDeclaration(enclosingType, modifiers);
                continue;
            }

            // Altrimenti: Dichiarazione di Metodo o Campo
            var typeName = ParseTypeName();
            if (string.IsNullOrEmpty(typeName))
            {
                Next();
                continue;
            }

            if (!Check(JavaTokenType.Identifier))
            {
                Match(";");
                continue;
            }

            var memberNameToken = Next();

            // Se seguito da '(', è una dichiarazione di Metodo
            if (Check("("))
            {
                ParseMethodDeclaration(enclosingType, memberNameToken, typeName, modifiers);
            }
            else
            {
                // È una dichiarazione di Campo o Costante
                ParseFieldDeclaration(enclosingType, memberNameToken, typeName, modifiers);
            }
        }

        if (Match("}"))
        {
            enclosingType.LineEnd = Peek(-1).Line;
        }
    }

    /// <summary>
    /// Analizza una dichiarazione di costruttore Java ("public MyClass(String name) { ... }").
    /// </summary>
    private void ParseConstructorDeclaration(Symbol enclosingType, List<string> modifiers)
    {
        var nameToken = Next();
        var qname = $"{enclosingType.QualifiedName}.{nameToken.Value}";

        var ctorSymbol = new Symbol
        {
            Id = Guid.NewGuid(),
            Kind = SymbolKind.Method,
            Name = nameToken.Value,
            QualifiedName = qname,
            FilePath = _filePath,
            LineStart = nameToken.Line,
            LineEnd = nameToken.Line,
            Accessibility = DetermineAccessibility(modifiers),
            ReturnType = null,
            IsStatic = false
        };

        ExtractParameters(ctorSymbol);
        RegisterSymbol(ctorSymbol);

        // Salta clausola throws Exception1, Exception2
        SkipThrows();

        if (Check("{"))
        {
            _scopeStack.Push(ctorSymbol);
            ParseMethodBody(ctorSymbol);
            _scopeStack.Pop();
            ctorSymbol.LineEnd = Peek(-1).Line;
        }
        else
        {
            Match(";");
        }
    }

    /// <summary>
    /// Analizza una dichiarazione di metodo Java ("public int calculate(int x) throws IOException { ... }").
    /// </summary>
    private void ParseMethodDeclaration(Symbol enclosingType, JavaToken nameToken, string returnType, List<string> modifiers)
    {
        var qname = $"{enclosingType.QualifiedName}.{nameToken.Value}";
        var isStatic = modifiers.Contains("static");

        var methodSymbol = new Symbol
        {
            Id = Guid.NewGuid(),
            Kind = SymbolKind.Method,
            Name = nameToken.Value,
            QualifiedName = qname,
            FilePath = _filePath,
            LineStart = nameToken.Line,
            LineEnd = nameToken.Line,
            Accessibility = DetermineAccessibility(modifiers),
            ReturnType = returnType,
            IsStatic = isStatic
        };

        ExtractParameters(methodSymbol);
        RegisterSymbol(methodSymbol);

        // Salta clausola throws
        SkipThrows();

        // Valore predefinito in annotazioni @interface (es. default "test"; o default {};)
        if (Match("default"))
        {
            while (!Check(";") && !Check(JavaTokenType.EndOfFile))
            {
                if (Check("{"))
                    ConsumeBracedGroup();
                else if (Check("("))
                    ConsumeParenthesizedGroup();
                else
                    Next();
            }
        }

        if (Check("{"))
        {
            _scopeStack.Push(methodSymbol);
            ParseMethodBody(methodSymbol);
            _scopeStack.Pop();
            methodSymbol.LineEnd = Peek(-1).Line;
        }
        else
        {
            Match(";");
        }
    }

    /// <summary>
    /// Analizza una dichiarazione di uno o più campi ("private final int count = 0, total = 10;").
    /// Se static final o membro di interfaccia, viene classificato come <see cref="SymbolKind.Constant"/>.
    /// </summary>
    private void ParseFieldDeclaration(Symbol enclosingType, JavaToken firstFieldNameToken, string typeName, List<string> modifiers)
    {
        var isStatic = modifiers.Contains("static");
        var isFinal = modifiers.Contains("final");
        var isConst = (isStatic && isFinal) || enclosingType.Kind == SymbolKind.Interface;
        var access = DetermineAccessibility(modifiers);

        var fieldTokens = new List<JavaToken> { firstFieldNameToken };

        // Gestione di campi multipli separati da virgola
        while (!Check(";") && !Check(JavaTokenType.EndOfFile))
        {
            if (Check("="))
            {
                Next(); // consuma '='
                // Ispeziona l'inizializzatore per catturare 'new Type()'
                ParseFieldInitializer(enclosingType, firstFieldNameToken);
            }

            if (Match(","))
            {
                if (Check(JavaTokenType.Identifier))
                {
                    fieldTokens.Add(Next());
                }
            }
            else
            {
                break;
            }
        }

        Match(";");

        foreach (var tok in fieldTokens)
        {
            var symbol = new Symbol
            {
                Id = Guid.NewGuid(),
                Kind = isConst ? SymbolKind.Constant : SymbolKind.Field,
                Name = tok.Value,
                QualifiedName = $"{enclosingType.QualifiedName}.{tok.Value}",
                FilePath = _filePath,
                LineStart = tok.Line,
                LineEnd = tok.Line,
                Accessibility = access,
                DeclaredTypeName = typeName,
                IsStatic = isStatic || isConst
            };
            RegisterSymbol(symbol);
        }
    }

    /// <summary>
    /// Analizza l'espressione di inizializzazione inline di un campo per intercettare creazioni di oggetti 'new'.
    /// </summary>
    private void ParseFieldInitializer(Symbol enclosingType, JavaToken fieldToken)
    {
        var parenDepth = 0;
        var braceDepth = 0;

        while (!Check(JavaTokenType.EndOfFile))
        {
            if (Check("(") || Check("[")) { parenDepth++; Next(); continue; }
            if (Check(")") || Check("]")) { parenDepth--; Next(); continue; }
            if (Check("{")) { braceDepth++; Next(); continue; }
            if (Check("}")) { braceDepth--; Next(); continue; }

            if (parenDepth == 0 && braceDepth == 0)
            {
                if (Check(";") || Check(","))
                    break;
            }

            if (Match("new"))
            {
                var createdTypeName = ParseTypeName();
                if (!string.IsNullOrEmpty(createdTypeName))
                {
                    _instantiations.Add(new JavaInstantiation
                    {
                        SourceSymbol = enclosingType,
                        TargetTypeName = createdTypeName,
                        Line = fieldToken.Line
                    });
                }
                continue;
            }

            Next();
        }
    }

    /// <summary>
    /// Estrae la lista dei parametri formali di un metodo o costruttore Java.
    /// </summary>
    private void ExtractParameters(Symbol routineSymbol)
    {
        if (!Match("(")) return;

        while (!Check(")") && !Check(JavaTokenType.EndOfFile))
        {
            SkipAnnotations();
            var pmods = ParseModifiers();
            var ptype = ParseTypeName();

            // Varargs: String... args -> String[]
            if (Match("..."))
                ptype += "[]";

            if (Check(JavaTokenType.Identifier))
            {
                var pname = Next().Value;
                // Notazione array sul nome del parametro: byte buffer[]
                while (Match("["))
                {
                    Match("]");
                    ptype += "[]";
                }

                routineSymbol.Parameters.Add(new ParameterSymbol
                {
                    Name = pname,
                    DeclaredTypeName = ptype,
                    IsByRef = false,
                    IsOptional = false
                });
            }

            if (!Match(",")) break;
        }

        Match(")");
    }

    /// <summary>
    /// Salta la clausola 'throws Exception1, Exception2'.
    /// </summary>
    private void SkipThrows()
    {
        if (Match("throws"))
        {
            do
            {
                ParseQualifiedIdentifier();
            } while (Match(","));
        }
    }

    /// <summary>
    /// Salta i parametri di tipo generico '<...>' gestendo l'annidamento.
    /// </summary>
    private void SkipTypeParameters()
    {
        if (Match("<"))
        {
            var depth = 1;
            while (depth > 0 && !Check(JavaTokenType.EndOfFile))
            {
                if (Check("<")) depth++;
                else if (Check(">")) depth--;
                Next();
            }
        }
    }

    /// <summary>
    /// Estrae e ripulisce il nome di un tipo Java (gestendo generics, array [] e package completi).
    /// </summary>
    private string ParseTypeName()
    {
        var sb = new System.Text.StringBuilder();

        while (!Check(JavaTokenType.EndOfFile))
        {
            var tok = Peek();
            if (tok.Type == JavaTokenType.Identifier || tok.Type == JavaTokenType.Keyword)
            {
                sb.Append(Next().Value);
                if (Check("."))
                {
                    sb.Append(Next().Value);
                    continue;
                }
            }
            break;
        }

        // Generics: List<String>
        if (Match("<"))
        {
            var depth = 1;
            while (depth > 0 && !Check(JavaTokenType.EndOfFile))
            {
                if (Check("<")) depth++;
                else if (Check(">")) depth--;
                Next();
            }
        }

        // Notazione array: String[] o int[][]
        while (Match("["))
        {
            Match("]");
            sb.Append("[]");
        }

        var typeStr = sb.ToString();
        return GetSimpleTypeName(typeStr);
    }

    /// <summary>
    /// Restituisce il nome semplice del tipo rimuovendo generics e prefissi di package.
    /// </summary>
    private static string GetSimpleTypeName(string rawName)
    {
        if (string.IsNullOrEmpty(rawName)) return string.Empty;
        var genericIdx = rawName.IndexOf('<');
        var baseName = genericIdx > 0 ? rawName[..genericIdx].Trim() : rawName.Trim();
        var lastDot = baseName.LastIndexOf('.');
        return lastDot >= 0 ? baseName[(lastDot + 1)..] : baseName;
    }

    // =========================================================================
    // ANALISI CORPO METODI (Call Sites, Instantiations, Method References)
    // =========================================================================

    /// <summary>
    /// Attraversa il corpo del metodo analizzando le istruzioni per rilevare:
    /// 1. Invocazioni di metodi (CALLS): 'service.process()', 'doSomething()', 'super.init()'.
    /// 2. Creazioni di oggetti (INSTANTIATES): 'new HashMap<>()', 'new Customer(id)'.
    /// 3. Method references: 'System.out::println', 'User::getId'.
    /// </summary>
    private void ParseMethodBody(Symbol callerSymbol)
    {
        if (!Match("{")) return;
        var depth = 1;

        string? lastReceiver = null;
        var lastReceiverLine = 0;

        while (depth > 0 && !Check(JavaTokenType.EndOfFile))
        {
            if (Check("{"))
            {
                depth++;
                Next();
                continue;
            }
            if (Check("}"))
            {
                depth--;
                Next();
                continue;
            }

            // 1. Riconoscimento 'new TypeName(...)'
            if (Match("new"))
            {
                var line = Peek(-1).Line;
                var createdTypeName = ParseTypeName();
                if (!string.IsNullOrEmpty(createdTypeName))
                {
                    _instantiations.Add(new JavaInstantiation
                    {
                        SourceSymbol = callerSymbol,
                        TargetTypeName = createdTypeName,
                        Line = line
                    });
                }
                continue;
            }

            // 2. Riconoscimento Invocazioni e Accessi a Metodi
            if (Check(JavaTokenType.Identifier))
            {
                var idTok = Next();
                var line = idTok.Line;

                if (Check("("))
                {
                    // Invocazione diretta o con ricevitore precedente: receiver.methodName(...)
                    _callSites.Add(new JavaCallSite
                    {
                        CallerSymbol = callerSymbol,
                        InvokedMethodName = idTok.Value,
                        ReceiverExpressionText = lastReceiver,
                        Line = line
                    });
                    lastReceiver = null;
                    continue;
                }
                else if (Check("."))
                {
                    // Accesso a membro 'receiver.'
                    lastReceiver = idTok.Value;
                    lastReceiverLine = line;
                    Next(); // consuma '.'
                    continue;
                }
                else if (Check("::"))
                {
                    // Method reference 'ClassName::methodName'
                    Next(); // consuma '::'
                    if (Check(JavaTokenType.Identifier))
                    {
                        var refMethodTok = Next();
                        _callSites.Add(new JavaCallSite
                        {
                            CallerSymbol = callerSymbol,
                            InvokedMethodName = refMethodTok.Value,
                            ReceiverExpressionText = idTok.Value,
                            Line = refMethodTok.Line
                        });
                    }
                    lastReceiver = null;
                    continue;
                }
            }
            else if (Match("super") || Match("this"))
            {
                var kw = Peek(-1).Value;
                if (Check("."))
                {
                    Next(); // consuma '.'
                    lastReceiver = kw;
                    continue;
                }
                else if (Check("("))
                {
                    // Chiamata esplicita al costruttore 'super(...)' o 'this(...)'
                    _callSites.Add(new JavaCallSite
                    {
                        CallerSymbol = callerSymbol,
                        InvokedMethodName = kw,
                        ReceiverExpressionText = null,
                        Line = Peek(-1).Line
                    });
                    lastReceiver = null;
                    continue;
                }
            }
            else
            {
                if (!Check("."))
                    lastReceiver = null;
                Next();
            }
        }
    }
}
