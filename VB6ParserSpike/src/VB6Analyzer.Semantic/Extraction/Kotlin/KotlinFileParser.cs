using System.Globalization;
using System.Text;
using Macondo.Semantic.Model;
using Symbol = Macondo.Semantic.Model.Symbol;
using SymbolKind = Macondo.Semantic.Model.SymbolKind;
using Accessibility = Macondo.Semantic.Model.Accessibility;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Parser sintattico ricorsivo resiliente per file sorgente Kotlin (.kt / .kts).
/// Supporta costrutti moderni di Kotlin (classi, data class, sealed class/interface, interfacce,
/// object, companion object, enum class, funzioni top-level ed extension, proprietà val/var con getter/setter,
/// costruttori primari e secondari, call site, istanziazioni e gerarchie di ereditarietà).
/// </summary>
internal sealed class KotlinFileParser
{
    private readonly CodeGraph _graph;
    private readonly string _filePath;
    private readonly string _sourceText;
    private readonly List<KotlinToken> _tokens = new();
    private int _tokenIndex;

    /// <summary>Stack dei simboli contenitori attivi per tracciare la gerarchia semantica e i QualifiedName.</summary>
    private readonly Stack<Symbol> _scopeStack = new();

    /// <summary>Package Kotlin estratto dalla dichiarazione 'package ...'.</summary>
    private string _packageName = string.Empty;

    /// <summary>Elenco dei simboli radice di primo livello estratti dal file.</summary>
    private readonly List<Symbol> _topLevelSymbols = new();

    /// <summary>Elenco dei call-site di chiamata rilevati nei corpi.</summary>
    private readonly List<KotlinCallSite> _callSites = new();

    /// <summary>Elenco delle istanziazioni rilevate.</summary>
    private readonly List<KotlinInstantiation> _instantiations = new();

    /// <summary>Elenco delle clausole di supertipo (ereditarietà e interfacce) rilevate.</summary>
    private readonly List<KotlinTypeInheritance> _inheritances = new();

    /// <summary>Elenco degli errori o avvisi sintattici raccolti durante il parsing.</summary>
    private readonly List<string> _parseErrors = new();

    /// <summary>Simbolo sintetico di facciata di file (es. 'FileKt') per raggruppare funzioni e proprietà top-level.</summary>
    private Symbol? _fileFacadeSymbol;

    /// <summary>Insieme delle parole chiave riservate in Kotlin.</summary>
    private static readonly HashSet<string> KotlinKeywords = new(StringComparer.Ordinal)
    {
        "as", "break", "class", "continue", "do", "else", "false", "for", "fun", "if", "in",
        "interface", "is", "null", "object", "package", "return", "super", "this", "throw",
        "true", "try", "typealias", "val", "var", "when", "while", "by", "catch", "constructor",
        "delegate", "dynamic", "field", "file", "finally", "get", "import", "init", "param",
        "property", "receiver", "set", "setparam", "where"
    };

    /// <summary>Insieme dei modificatori di visibilità e caratteristiche dei tipi/membri Kotlin.</summary>
    private static readonly HashSet<string> ModifierKeywords = new(StringComparer.Ordinal)
    {
        "public", "protected", "private", "internal", "abstract", "final", "open", "sealed",
        "data", "inline", "value", "inner", "annotation", "enum", "override", "suspend",
        "operator", "infix", "tailrec", "external", "lateinit", "const", "vararg", "expect",
        "actual", "companion", "reified", "crossinline", "noinline"
    };

    public KotlinFileParser(CodeGraph graph, string filePath, string sourceText)
    {
        _graph = graph;
        _filePath = filePath;
        _sourceText = sourceText;
    }

    /// <summary>
    /// Avvia il processo a due fasi: tokenizzazione lessicale seguita da parsing AST ricorsivo.
    /// </summary>
    public KotlinExtractionResult Parse()
    {
        try
        {
            // Fase 1: Lexing del sorgente in sequenza di KotlinToken
            Tokenize();

            // Fase 2: Parsing della Compilation Unit (package, imports, tipi, funzioni top-level)
            ParseCompilationUnit();
        }
        catch (Exception ex)
        {
            _parseErrors.Add($"Kotlin parser warning on {_filePath}: {ex.Message}");
        }
        finally
        {
            _tokens.Clear();
            _tokens.TrimExcess();
        }

        return new KotlinExtractionResult
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

    private void Tokenize()
    {
        var len = _sourceText.Length;
        var line = 1;
        var col = 1;
        var i = 0;

        while (i < len)
        {
            var ch = _sourceText[i];

            // 1. A capo Windows (\r\n) o Unix (\n)
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

            // 3. Commenti a blocchi '/*' con supporto all'annidamento ricorsivo (standard Kotlin)
            if (ch == '/' && i + 1 < len && _sourceText[i + 1] == '*')
            {
                i += 2;
                col += 2;
                var commentNesting = 1;

                while (i < len && commentNesting > 0)
                {
                    if (_sourceText[i] == '/' && i + 1 < len && _sourceText[i + 1] == '*')
                    {
                        commentNesting++;
                        i += 2;
                        col += 2;
                        continue;
                    }
                    if (_sourceText[i] == '*' && i + 1 < len && _sourceText[i + 1] == '/')
                    {
                        commentNesting--;
                        i += 2;
                        col += 2;
                        continue;
                    }
                    if (_sourceText[i] == '\r')
                    {
                        i++;
                        if (i < len && _sourceText[i] == '\n')
                            i++;
                        line++;
                        col = 1;
                        continue;
                    }
                    if (_sourceText[i] == '\n')
                    {
                        i++;
                        line++;
                        col = 1;
                        continue;
                    }
                    i++;
                    col++;
                }
                continue;
            }

            // 4. Identificatori con backtick `` `identifier with spaces or keywords` ``
            if (ch == '`')
            {
                var startLine = line;
                var startCol = col;
                i++;
                col++;
                var startIdx = i;

                while (i < len && _sourceText[i] != '`' && _sourceText[i] != '\r' && _sourceText[i] != '\n')
                {
                    i++;
                    col++;
                }

                var identVal = _sourceText[startIdx..Math.Min(i, len)];
                if (i < len && _sourceText[i] == '`')
                {
                    i++;
                    col++;
                }

                _tokens.Add(new KotlinToken
                {
                    Type = KotlinTokenType.Identifier,
                    Value = identVal,
                    Line = startLine,
                    Column = startCol
                });
                continue;
            }

            // 5. Stringhe raw multiriga """ ... """
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

                _tokens.Add(new KotlinToken
                {
                    Type = KotlinTokenType.StringLiteral,
                    Value = _sourceText[startIdx..Math.Min(i, len)],
                    Line = startLine,
                    Column = startCol
                });
                continue;
            }

            // 6. Stringhe standard " ... " (con gestione escape)
            if (ch == '"')
            {
                var startLine = line;
                var startCol = col;
                i++;
                col++;
                var sb = new StringBuilder();

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

                _tokens.Add(new KotlinToken
                {
                    Type = KotlinTokenType.StringLiteral,
                    Value = sb.ToString(),
                    Line = startLine,
                    Column = startCol
                });
                continue;
            }

            // 7. Caratteri letterali 'a'
            if (ch == '\'')
            {
                var startLine = line;
                var startCol = col;
                i++;
                col++;
                var sb = new StringBuilder();

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

                _tokens.Add(new KotlinToken
                {
                    Type = KotlinTokenType.CharLiteral,
                    Value = sb.ToString(),
                    Line = startLine,
                    Column = startCol
                });
                continue;
            }

            // 8. Costanti numeriche (decimali, hex, binary, float, long)
            if (char.IsAsciiDigit(ch) || (ch == '.' && i + 1 < len && char.IsAsciiDigit(_sourceText[i + 1])))
            {
                var startLine = line;
                var startCol = col;
                var startIdx = i;

                if (ch == '0' && i + 1 < len && (_sourceText[i + 1] is 'x' or 'X' or 'b' or 'B'))
                {
                    i += 2;
                    col += 2;
                    while (i < len && (char.IsAsciiHexDigit(_sourceText[i]) || _sourceText[i] == '_'))
                    {
                        i++;
                        col++;
                    }
                }
                else
                {
                    var seenDot = (ch == '.');
                    while (i < len)
                    {
                        var curr = _sourceText[i];
                        if (char.IsAsciiDigit(curr) || curr == '_')
                        {
                            i++;
                            col++;
                        }
                        else if (curr == '.' && !seenDot && i + 1 < len && char.IsAsciiDigit(_sourceText[i + 1]))
                        {
                            seenDot = true;
                            i++;
                            col++;
                        }
                        else if (curr is 'e' or 'E')
                        {
                            i++;
                            col++;
                            if (i < len && _sourceText[i] is '+' or '-')
                            {
                                i++;
                                col++;
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                }

                while (i < len && _sourceText[i] is 'f' or 'F' or 'L' or 'u' or 'U')
                {
                    i++;
                    col++;
                }

                _tokens.Add(new KotlinToken
                {
                    Type = KotlinTokenType.Number,
                    Value = _sourceText[startIdx..i],
                    Line = startLine,
                    Column = startCol
                });
                continue;
            }

            // 9. Identificatori e Parole Chiave
            if (IsKotlinIdentifierStart(ch))
            {
                var startLine = line;
                var startCol = col;
                var startIdx = i;

                while (i < len && IsKotlinIdentifierPart(_sourceText[i]))
                {
                    i++;
                    col++;
                }

                var text = _sourceText[startIdx..i];
                var isKw = KotlinKeywords.Contains(text) || ModifierKeywords.Contains(text);

                _tokens.Add(new KotlinToken
                {
                    Type = isKw ? KotlinTokenType.Keyword : KotlinTokenType.Identifier,
                    Value = text,
                    Line = startLine,
                    Column = startCol
                });
                continue;
            }

            // 10. Operatori a tre caratteri (===, !==, ..., ..<)
            var startOpLine = line;
            var startOpCol = col;
            if (i + 2 < len)
            {
                var triple = _sourceText.Substring(i, 3);
                if (triple is "===" or "!==" or "..." or "..<")
                {
                    _tokens.Add(new KotlinToken
                    {
                        Type = KotlinTokenType.Symbol,
                        Value = triple,
                        Line = startOpLine,
                        Column = startOpCol
                    });
                    i += 3;
                    col += 3;
                    continue;
                }
            }

            // 11. Operatori a due caratteri (?. ?: :: -> == != <= >= && || ++ -- += -= *= /= %= !! ..)
            if (i + 1 < len)
            {
                var pair = _sourceText.Substring(i, 2);
                if (pair is "?." or "?:" or "::" or "->" or "==" or "!=" or "<=" or ">=" or "&&" or "||" or "++" or "--" or "+=" or "-=" or "*=" or "/=" or "%=" or "!!" or "..")
                {
                    _tokens.Add(new KotlinToken
                    {
                        Type = KotlinTokenType.Symbol,
                        Value = pair,
                        Line = startOpLine,
                        Column = startOpCol
                    });
                    i += 2;
                    col += 2;
                    continue;
                }
            }

            // 12. Simboli singoli
            _tokens.Add(new KotlinToken
            {
                Type = KotlinTokenType.Symbol,
                Value = ch.ToString(),
                Line = startOpLine,
                Column = startOpCol
            });
            i++;
            col++;
        }

        // Sentinella di fine file
        _tokens.Add(new KotlinToken
        {
            Type = KotlinTokenType.EndOfFile,
            Value = string.Empty,
            Line = line,
            Column = col
        });
    }

    private static bool IsKotlinIdentifierStart(char ch)
    {
        if (ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '_')
            return true;

        var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
        return cat is UnicodeCategory.UppercaseLetter
                   or UnicodeCategory.LowercaseLetter
                   or UnicodeCategory.TitlecaseLetter
                   or UnicodeCategory.ModifierLetter
                   or UnicodeCategory.OtherLetter
                   or UnicodeCategory.LetterNumber;
    }

    private static bool IsKotlinIdentifierPart(char ch)
    {
        if (ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_')
            return true;

        var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
        return cat is UnicodeCategory.UppercaseLetter
                   or UnicodeCategory.LowercaseLetter
                   or UnicodeCategory.TitlecaseLetter
                   or UnicodeCategory.ModifierLetter
                   or UnicodeCategory.OtherLetter
                   or UnicodeCategory.LetterNumber
                   or UnicodeCategory.DecimalDigitNumber
                   or UnicodeCategory.NonSpacingMark
                   or UnicodeCategory.SpacingCombiningMark
                   or UnicodeCategory.Format;
    }

    // =========================================================================
    // HELPER DI NAVIGAZIONE DEI TOKEN
    // =========================================================================

    private KotlinToken Peek(int offset = 0)
    {
        var idx = _tokenIndex + offset;
        if (idx >= 0 && idx < _tokens.Count)
            return _tokens[idx];
        return _tokens[^1];
    }

    private KotlinToken Next()
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

    private bool Check(KotlinTokenType type) => Peek().Type == type;

    // =========================================================================
    // FASE 2: PARSER COMPILATION UNIT & TOP LEVEL
    // =========================================================================

    private void ParseCompilationUnit()
    {
        while (!Check(KotlinTokenType.EndOfFile))
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
                ParseQualifiedIdentifier();
                if (Match("."))
                    Match("*");
                if (Match("as"))
                    ParseQualifiedIdentifier();
                Match(";");
                continue;
            }

            ParseTopLevelDeclaration();
        }
    }

    /// <summary>
    /// Salta le annotazioni (@Annotation(...), @file:JvmName(...), @get:Rule, ecc.).
    /// </summary>
    private void SkipAnnotations()
    {
        while (Check("@"))
        {
            Next(); // consuma '@'

            // Target use-site opzionale (es. 'file:', 'get:', 'set:', 'field:', 'param:', 'property:')
            if (Check(KotlinTokenType.Identifier) || Check(KotlinTokenType.Keyword))
            {
                if (Peek(1).Value == ":")
                {
                    Next(); // nome target (es. file)
                    Next(); // ':'
                }
            }

            ParseQualifiedIdentifier();

            if (Check("("))
                ConsumeParenthesizedGroup();
            if (Check("["))
                ConsumeBracketedGroup();
        }
    }

    private void ConsumeParenthesizedGroup()
    {
        if (!Match("(")) return;
        var depth = 1;
        while (depth > 0 && !Check(KotlinTokenType.EndOfFile))
        {
            if (Check("(")) depth++;
            else if (Check(")")) depth--;
            Next();
        }
    }

    private void ConsumeBracketedGroup()
    {
        if (!Match("[")) return;
        var depth = 1;
        while (depth > 0 && !Check(KotlinTokenType.EndOfFile))
        {
            if (Check("[")) depth++;
            else if (Check("]")) depth--;
            Next();
        }
    }

    private void ConsumeBracedGroup()
    {
        if (!Match("{")) return;
        var depth = 1;
        while (depth > 0 && !Check(KotlinTokenType.EndOfFile))
        {
            if (Check("{")) depth++;
            else if (Check("}")) depth--;
            Next();
        }
    }

    private string ParseQualifiedIdentifier()
    {
        var sb = new StringBuilder();
        while (!Check(KotlinTokenType.EndOfFile))
        {
            var tok = Peek();
            if (tok.Type == KotlinTokenType.Identifier || tok.Type == KotlinTokenType.Keyword)
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
    /// Analizza una dichiarazione top-level del file: Classi, Interfacce, Object, Enum, Funzioni o Proprietà.
    /// </summary>
    private void ParseTopLevelDeclaration()
    {
        var modifiers = ParseModifiers();

        // 1. typealias
        if (Match("typealias"))
        {
            ParseTypeAlias(modifiers);
            return;
        }

        // 2. object / companion object
        if (Match("object"))
        {
            ParseTypeDeclaration(SymbolKind.Class, modifiers, isObject: true);
            return;
        }

        // 3. interface
        if (Match("interface"))
        {
            ParseTypeDeclaration(SymbolKind.Interface, modifiers);
            return;
        }

        // 4. class (standard, data, sealed, enum, annotation)
        if (Match("class"))
        {
            var isEnum = modifiers.Contains("enum");
            var kind = isEnum ? SymbolKind.Enum : (modifiers.Contains("annotation") ? SymbolKind.Interface : SymbolKind.Class);
            if (isEnum)
                ParseEnumDeclaration(modifiers);
            else
                ParseTypeDeclaration(kind, modifiers);
            return;
        }

        // 5. fun (funzione top-level)
        if (Match("fun"))
        {
            var facade = GetOrCreateFileFacade();
            ParseFunctionDeclaration(facade, modifiers);
            return;
        }

        // 6. val / var (proprietà top-level)
        if (Check("val") || Check("var"))
        {
            var isVal = Match("val");
            if (!isVal) Match("var");

            var facade = GetOrCreateFileFacade();
            ParsePropertyDeclaration(facade, modifiers, isVal);
            return;
        }

        // Fallback resiliente
        Next();
    }

    /// <summary>
    /// Restituisce o crea la classe di facciata sintetica per il file (es. 'MathUtilsKt')
    /// conforme allo standard di compilazione Kotlin JVM per funzioni e proprietà top-level.
    /// </summary>
    private Symbol GetOrCreateFileFacade()
    {
        if (_fileFacadeSymbol != null)
            return _fileFacadeSymbol;

        var baseName = Path.GetFileNameWithoutExtension(_filePath);
        if (string.IsNullOrEmpty(baseName))
            baseName = "File";

        var facadeName = $"{baseName}Kt";
        var qname = string.IsNullOrEmpty(_packageName) ? facadeName : $"{_packageName}.{facadeName}";

        _fileFacadeSymbol = new Symbol
        {
            Id = Guid.NewGuid(),
            Kind = SymbolKind.Class,
            Name = facadeName,
            QualifiedName = qname,
            FilePath = _filePath,
            LineStart = 1,
            LineEnd = 1,
            Accessibility = Accessibility.Public,
            Language = SourceLanguage.Kotlin,
            IsStatic = true
        };

        _graph.Symbols.Add(_fileFacadeSymbol);
        _topLevelSymbols.Add(_fileFacadeSymbol);
        return _fileFacadeSymbol;
    }

    /// <summary>
    /// Raccoglie tutti i modificatori Kotlin consecutivi.
    /// </summary>
    private List<string> ParseModifiers()
    {
        var mods = new List<string>();
        while (!Check(KotlinTokenType.EndOfFile))
        {
            if (Check("@"))
            {
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

    private Accessibility DetermineAccessibility(IEnumerable<string> modifiers)
    {
        if (modifiers.Contains("private")) return Accessibility.Private;
        if (modifiers.Contains("protected")) return Accessibility.Friend;
        if (modifiers.Contains("internal")) return Accessibility.Friend;
        // In Kotlin la visibilità di default è Public
        return Accessibility.Public;
    }

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

    private void RegisterSymbol(Symbol symbol)
    {
        symbol.Language = SourceLanguage.Kotlin;
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
    // DICHIARAZIONI DI TIPI: CLASSI, INTERFACCE, OBJECT, ENUM
    // =========================================================================

    private void ParseTypeDeclaration(SymbolKind kind, List<string> modifiers, bool isObject = false)
    {
        var nameToken = Peek();
        string name;

        if (isObject && (Check("{") || Check(":") || Check(KotlinTokenType.EndOfFile)))
        {
            // Anonymous object o companion object anonimo
            name = "Companion";
        }
        else if (nameToken.Type == KotlinTokenType.Identifier || nameToken.Type == KotlinTokenType.Keyword)
        {
            name = Next().Value;
        }
        else
        {
            Next();
            return;
        }

        var lineStart = nameToken.Line;
        var qname = BuildQualifiedName(name);

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
            IsStatic = isObject
        };

        RegisterSymbol(symbol);

        // Costruttore primario della classe: class Foo(val id: Int, var name: String)
        if (Check("(") || Check("constructor"))
        {
            if (Match("constructor"))
                SkipAnnotations();

            ParsePrimaryConstructor(symbol);
        }

        // Supertypi, ereditarietà e interfacce: ': BaseClass(), Interface1, Interface2'
        ParseSuperTypes(symbol);

        // Blocco corpo del tipo: '{ ... }'
        if (Match("{"))
        {
            _scopeStack.Push(symbol);
            ParseTypeBody(symbol);
            _scopeStack.Pop();
            symbol.LineEnd = Peek(-1).Line;
        }
    }

    private void ParsePrimaryConstructor(Symbol enclosingType)
    {
        if (!Match("(")) return;

        var ctorSymbol = new Symbol
        {
            Id = Guid.NewGuid(),
            Kind = SymbolKind.Method,
            Name = enclosingType.Name,
            QualifiedName = $"{enclosingType.QualifiedName}.{enclosingType.Name}",
            FilePath = _filePath,
            LineStart = Peek(-1).Line,
            LineEnd = Peek(-1).Line,
            Accessibility = Accessibility.Public,
            Language = SourceLanguage.Kotlin,
            IsStatic = false
        };

        while (!Check(")") && !Check(KotlinTokenType.EndOfFile))
        {
            SkipAnnotations();
            var pmods = ParseModifiers();
            var isProperty = false;
            var isVal = false;

            if (Check("val") || Check("var"))
            {
                isProperty = true;
                isVal = Match("val");
                if (!isVal) Match("var");
            }

            if (Check(KotlinTokenType.Identifier) || Check(KotlinTokenType.Keyword))
            {
                var paramToken = Next();
                var paramName = paramToken.Value;
                var paramType = string.Empty;

                if (Match(":"))
                    paramType = ParseTypeName();

                ctorSymbol.Parameters.Add(new ParameterSymbol
                {
                    Name = paramName,
                    DeclaredTypeName = paramType,
                    IsByRef = false,
                    IsOptional = Check("=")
                });

                if (isProperty)
                {
                    var propSymbol = new Symbol
                    {
                        Id = Guid.NewGuid(),
                        Kind = SymbolKind.Property,
                        Name = paramName,
                        QualifiedName = $"{enclosingType.QualifiedName}.{paramName}",
                        FilePath = _filePath,
                        LineStart = paramToken.Line,
                        LineEnd = paramToken.Line,
                        Accessibility = DetermineAccessibility(pmods),
                        DeclaredTypeName = paramType,
                        Language = SourceLanguage.Kotlin,
                        IsStatic = false
                    };
                    _graph.Symbols.Add(propSymbol);
                    _graph.AddContains(enclosingType, propSymbol);
                }

                // Salta valore di default '= ...'
                if (Match("="))
                    SkipExpressionUntilParamEnd();
            }

            if (!Match(",")) break;
        }

        Match(")");

        _graph.Symbols.Add(ctorSymbol);
        _graph.AddContains(enclosingType, ctorSymbol);
    }

    private void SkipExpressionUntilParamEnd()
    {
        var parenDepth = 0;
        var braceDepth = 0;
        var bracketDepth = 0;

        while (!Check(KotlinTokenType.EndOfFile))
        {
            if (parenDepth == 0 && braceDepth == 0 && bracketDepth == 0)
            {
                if (Check(",") || Check(")"))
                    break;
            }

            if (Check("(")) parenDepth++;
            else if (Check(")")) parenDepth--;
            else if (Check("{")) braceDepth++;
            else if (Check("}")) braceDepth--;
            else if (Check("[")) bracketDepth++;
            else if (Check("]")) bracketDepth--;

            Next();
        }
    }

    private void ParseSuperTypes(Symbol derivedType)
    {
        if (!Match(":")) return;

        do
        {
            SkipAnnotations();
            var typeToken = Peek();
            var typeName = ParseTypeName();
            if (string.IsNullOrEmpty(typeName))
                break;

            var isInterface = true;

            // Se seguito da '(', è una chiamata al costruttore della classe base ('extends Base()')
            if (Check("("))
            {
                isInterface = false;
                ConsumeParenthesizedGroup();
            }

            // Delega tramite 'by' (es. ': BaseInterface by delegate')
            if (Match("by"))
            {
                ParseQualifiedIdentifier();
            }

            _inheritances.Add(new KotlinTypeInheritance
            {
                DerivedTypeSymbol = derivedType,
                BaseOrInterfaceTypeName = typeName,
                Line = typeToken.Line,
                IsInterface = isInterface
            });

        } while (Match(","));
    }

    private void ParseEnumDeclaration(List<string> modifiers)
    {
        var nameToken = Peek();
        if (nameToken.Type != KotlinTokenType.Identifier && nameToken.Type != KotlinTokenType.Keyword)
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
            IsStatic = true
        };

        RegisterSymbol(symbol);

        // Eventuale costruttore primario dell'enum
        if (Check("("))
            ParsePrimaryConstructor(symbol);

        ParseSuperTypes(symbol);

        if (!Match("{")) return;

        _scopeStack.Push(symbol);

        // Estrazione costanti enum
        while (!Check("}") && !Check(";") && !Check(KotlinTokenType.EndOfFile))
        {
            SkipAnnotations();
            if (Check(KotlinTokenType.Identifier) || Check(KotlinTokenType.Keyword))
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
                    Language = SourceLanguage.Kotlin,
                    DeclaredTypeName = name
                };
                _graph.Symbols.Add(constSymbol);
                _graph.AddContains(symbol, constSymbol);

                if (Check("("))
                    ConsumeParenthesizedGroup();
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
            // Membri aggiuntivi dell'enum (metodi, proprietà)
            ParseTypeBody(symbol);
        }
        else
        {
            Match("}");
        }

        _scopeStack.Pop();
        symbol.LineEnd = Peek(-1).Line;
    }

    private void ParseTypeAlias(List<string> modifiers)
    {
        if (Check(KotlinTokenType.Identifier) || Check(KotlinTokenType.Keyword))
        {
            var nameTok = Next();
            SkipTypeParameters();
            if (Match("="))
                ParseTypeName();
            Match(";");
        }
    }

    // =========================================================================
    // CORPO DEI TIPI: METODI, PROPRIETÀ, INNER TYPES, INIT, COSTRUTTORI
    // =========================================================================

    private void ParseTypeBody(Symbol enclosingType)
    {
        while (!Check("}") && !Check(KotlinTokenType.EndOfFile))
        {
            SkipAnnotations();

            if (Check("}")) break;

            // 1. Blocco 'init { ... }'
            if (Match("init"))
            {
                ParseMethodBody(enclosingType);
                continue;
            }

            var modifiers = ParseModifiers();

            // 2. companion object
            if (Match("companion"))
            {
                Match("object");
                ParseTypeDeclaration(SymbolKind.Class, modifiers, isObject: true);
                continue;
            }

            // 3. object annidato
            if (Match("object"))
            {
                ParseTypeDeclaration(SymbolKind.Class, modifiers, isObject: true);
                continue;
            }

            // 4. interface annidata
            if (Match("interface"))
            {
                ParseTypeDeclaration(SymbolKind.Interface, modifiers);
                continue;
            }

            // 5. class annidata (enum, data, sealed, inner, ecc.)
            if (Match("class"))
            {
                var isEnum = modifiers.Contains("enum");
                var kind = isEnum ? SymbolKind.Enum : (modifiers.Contains("annotation") ? SymbolKind.Interface : SymbolKind.Class);
                if (isEnum)
                    ParseEnumDeclaration(modifiers);
                else
                    ParseTypeDeclaration(kind, modifiers);
                continue;
            }

            // 6. Costruttore secondario 'constructor(...) : this(...) { ... }'
            if (Match("constructor"))
            {
                ParseSecondaryConstructor(enclosingType, modifiers);
                continue;
            }

            // 7. Funzione membro 'fun ...'
            if (Match("fun"))
            {
                ParseFunctionDeclaration(enclosingType, modifiers);
                continue;
            }

            // 8. Proprietà membro 'val' / 'var'
            if (Check("val") || Check("var"))
            {
                var isVal = Match("val");
                if (!isVal) Match("var");
                ParsePropertyDeclaration(enclosingType, modifiers, isVal);
                continue;
            }

            Next();
        }

        if (Match("}"))
        {
            enclosingType.LineEnd = Peek(-1).Line;
        }
    }

    private void ParseSecondaryConstructor(Symbol enclosingType, List<string> modifiers)
    {
        var lineStart = Peek(-1).Line;
        var ctorSymbol = new Symbol
        {
            Id = Guid.NewGuid(),
            Kind = SymbolKind.Method,
            Name = enclosingType.Name,
            QualifiedName = $"{enclosingType.QualifiedName}.{enclosingType.Name}",
            FilePath = _filePath,
            LineStart = lineStart,
            LineEnd = lineStart,
            Accessibility = DetermineAccessibility(modifiers),
            Language = SourceLanguage.Kotlin,
            IsStatic = false
        };

        ExtractParameters(ctorSymbol);
        RegisterSymbol(ctorSymbol);

        // Invocazione delegata del costruttore: ': this(...)' o ': super(...)'
        if (Match(":"))
        {
            if (Match("this") || Match("super"))
            {
                var target = Peek(-1).Value;
                _callSites.Add(new KotlinCallSite
                {
                    CallerSymbol = ctorSymbol,
                    InvokedMethodName = target,
                    ReceiverExpressionText = null,
                    Line = Peek(-1).Line
                });
                if (Check("("))
                    ConsumeParenthesizedGroup();
            }
        }

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

    private void ParseFunctionDeclaration(Symbol enclosingType, List<string> modifiers)
    {
        SkipTypeParameters();

        // Supporto a funzioni membro ed Extension Functions: fun String.toSlug()
        var namePart1 = ParseTypeName();
        string funcName;

        if (Match("."))
        {
            // Extension function: namePart1 è il tipo ricevitore
            if (Check(KotlinTokenType.Identifier) || Check(KotlinTokenType.Keyword))
                funcName = Next().Value;
            else
                funcName = namePart1;
        }
        else
        {
            funcName = namePart1;
        }

        if (string.IsNullOrEmpty(funcName))
            funcName = "anonymous";

        var lineStart = Peek(-1).Line;
        var qname = $"{enclosingType.QualifiedName}.{funcName}";

        var methodSymbol = new Symbol
        {
            Id = Guid.NewGuid(),
            Kind = SymbolKind.Method,
            Name = funcName,
            QualifiedName = qname,
            FilePath = _filePath,
            LineStart = lineStart,
            LineEnd = lineStart,
            Accessibility = DetermineAccessibility(modifiers),
            Language = SourceLanguage.Kotlin,
            IsStatic = enclosingType.IsStatic || modifiers.Contains("inline")
        };

        ExtractParameters(methodSymbol);

        // Tipo di ritorno opzionale ': ReturnType'
        if (Match(":"))
            methodSymbol.ReturnType = ParseTypeName();

        RegisterSymbol(methodSymbol);

        // Clausola 'where T : Any'
        SkipWhereClause();

        // Corpo a blocco '{ ... }' oppure espressione singola '= expr'
        if (Check("{"))
        {
            _scopeStack.Push(methodSymbol);
            ParseMethodBody(methodSymbol);
            _scopeStack.Pop();
            methodSymbol.LineEnd = Peek(-1).Line;
        }
        else if (Match("="))
        {
            _scopeStack.Push(methodSymbol);
            ParseExpressionBody(methodSymbol);
            _scopeStack.Pop();
            methodSymbol.LineEnd = Peek(-1).Line;
        }
        else
        {
            Match(";");
        }
    }

    private void ParsePropertyDeclaration(Symbol enclosingType, List<string> modifiers, bool isVal)
    {
        SkipTypeParameters();

        if (!Check(KotlinTokenType.Identifier) && !Check(KotlinTokenType.Keyword))
        {
            Next();
            return;
        }

        var propToken = Next();
        var propName = propToken.Value;
        var propType = string.Empty;

        if (Match(":"))
            propType = ParseTypeName();

        var isConst = modifiers.Contains("const");
        var propSymbol = new Symbol
        {
            Id = Guid.NewGuid(),
            Kind = isConst ? SymbolKind.Constant : SymbolKind.Property,
            Name = propName,
            QualifiedName = $"{enclosingType.QualifiedName}.{propName}",
            FilePath = _filePath,
            LineStart = propToken.Line,
            LineEnd = propToken.Line,
            Accessibility = DetermineAccessibility(modifiers),
            DeclaredTypeName = propType,
            Language = SourceLanguage.Kotlin,
            IsStatic = isConst || enclosingType.IsStatic
        };

        RegisterSymbol(propSymbol);

        // Inizializzatore inline: '= expr'
        if (Match("="))
        {
            ParseInitializerExpression(propSymbol);
        }

        // Delegato di proprietà: 'by lazy { ... }'
        if (Match("by"))
        {
            ParseInitializerExpression(propSymbol);
        }

        // Eventuali getter e setter espliciti: 'get() = ...', 'set(value) { ... }'
        while (Check("get") || Check("set"))
        {
            var isGetter = Match("get");
            if (!isGetter) Match("set");

            if (Check("("))
                ConsumeParenthesizedGroup();

            if (Check("{"))
                ParseMethodBody(propSymbol);
            else if (Match("="))
                ParseExpressionBody(propSymbol);
        }
    }

    private void ExtractParameters(Symbol routineSymbol)
    {
        if (!Match("(")) return;

        while (!Check(")") && !Check(KotlinTokenType.EndOfFile))
        {
            SkipAnnotations();
            var pmods = ParseModifiers();

            if (Check("vararg"))
            {
                pmods.Add("vararg");
                Next();
            }

            if (Check(KotlinTokenType.Identifier) || Check(KotlinTokenType.Keyword))
            {
                var pname = Next().Value;
                var ptype = string.Empty;

                if (Match(":"))
                    ptype = ParseTypeName();

                routineSymbol.Parameters.Add(new ParameterSymbol
                {
                    Name = pname,
                    DeclaredTypeName = ptype,
                    IsByRef = false,
                    IsOptional = Check("=")
                });

                if (Match("="))
                    SkipExpressionUntilParamEnd();
            }

            if (!Match(",")) break;
        }

        Match(")");
    }

    private void SkipWhereClause()
    {
        if (Match("where"))
        {
            while (!Check("{") && !Check("=") && !Check(";") && !Check(KotlinTokenType.EndOfFile))
                Next();
        }
    }

    private void SkipTypeParameters()
    {
        if (Match("<"))
        {
            var depth = 1;
            while (depth > 0 && !Check(KotlinTokenType.EndOfFile))
            {
                if (Check("<")) depth++;
                else if (Check(">")) depth--;
                Next();
            }
        }
    }

    private string ParseTypeName()
    {
        var sb = new StringBuilder();

        while (!Check(KotlinTokenType.EndOfFile))
        {
            var tok = Peek();
            if (tok.Type == KotlinTokenType.Identifier || tok.Type == KotlinTokenType.Keyword)
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
            while (depth > 0 && !Check(KotlinTokenType.EndOfFile))
            {
                if (Check("<")) depth++;
                else if (Check(">")) depth--;
                Next();
            }
        }

        // Nullable modifier '?'
        while (Match("?"))
        {
            // ignora nullable operator
        }

        return GetSimpleTypeName(sb.ToString());
    }

    private static string GetSimpleTypeName(string rawName)
    {
        if (string.IsNullOrEmpty(rawName)) return string.Empty;
        var genericIdx = rawName.IndexOf('<');
        var baseName = genericIdx > 0 ? rawName[..genericIdx].Trim() : rawName.Trim();
        var lastDot = baseName.LastIndexOf('.');
        return lastDot >= 0 ? baseName[(lastDot + 1)..] : baseName;
    }

    // =========================================================================
    // ANALISI CORPI E CALL SITE / INSTANTIATIONS
    // =========================================================================

    private void ParseMethodBody(Symbol callerSymbol)
    {
        if (!Match("{")) return;
        var depth = 1;

        string? lastReceiver = null;

        while (depth > 0 && !Check(KotlinTokenType.EndOfFile))
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

            ProcessTokenInBody(callerSymbol, ref lastReceiver);
        }
    }

    private void ParseExpressionBody(Symbol callerSymbol)
    {
        var parenDepth = 0;
        var braceDepth = 0;
        var bracketDepth = 0;
        string? lastReceiver = null;

        while (!Check(KotlinTokenType.EndOfFile))
        {
            if (parenDepth == 0 && braceDepth == 0 && bracketDepth == 0)
            {
                if (Check(";") || (Peek().Line > callerSymbol.LineStart && IsTopLevelOrMemberKeyword(Peek().Value)))
                    break;
            }

            if (Check("(")) parenDepth++;
            else if (Check(")")) { parenDepth--; if (parenDepth < 0) break; }
            else if (Check("{")) braceDepth++;
            else if (Check("}")) { braceDepth--; if (braceDepth < 0) break; }
            else if (Check("[")) bracketDepth++;
            else if (Check("]")) { bracketDepth--; if (bracketDepth < 0) break; }

            ProcessTokenInBody(callerSymbol, ref lastReceiver);
        }
    }

    private void ParseInitializerExpression(Symbol callerSymbol)
    {
        var parenDepth = 0;
        var braceDepth = 0;
        var bracketDepth = 0;
        string? lastReceiver = null;

        while (!Check(KotlinTokenType.EndOfFile))
        {
            if (parenDepth == 0 && braceDepth == 0 && bracketDepth == 0)
            {
                if (Check(";") || (Peek().Line > callerSymbol.LineStart && IsTopLevelOrMemberKeyword(Peek().Value)))
                    break;
            }

            if (Check("(")) parenDepth++;
            else if (Check(")")) { parenDepth--; if (parenDepth < 0) break; }
            else if (Check("{")) braceDepth++;
            else if (Check("}")) { braceDepth--; if (braceDepth < 0) break; }
            else if (Check("[")) bracketDepth++;
            else if (Check("]")) { bracketDepth--; if (bracketDepth < 0) break; }

            ProcessTokenInBody(callerSymbol, ref lastReceiver);
        }
    }

    private static bool IsTopLevelOrMemberKeyword(string val) =>
        val is "fun" or "val" or "var" or "class" or "interface" or "object" or "enum" or "override" or "public" or "private" or "protected" or "internal";

    private void ProcessTokenInBody(Symbol callerSymbol, ref string? lastReceiver)
    {
        // 1. Chiamata a costruttore con 'new' (se presente in interop)
        if (Match("new"))
        {
            var line = Peek(-1).Line;
            var typeName = ParseTypeName();
            if (!string.IsNullOrEmpty(typeName))
            {
                _instantiations.Add(new KotlinInstantiation
                {
                    SourceSymbol = callerSymbol,
                    TargetTypeName = typeName,
                    Line = line
                });
            }
            return;
        }

        // 2. Identificatori: Invocazioni di metodi, accessi e costruttori
        if (Check(KotlinTokenType.Identifier) || Check(KotlinTokenType.Keyword))
        {
            var tok = Next();
            var line = tok.Line;
            var val = tok.Value;

            // Chiamata con parentesi: name(...)
            if (Check("("))
            {
                // In Kotlin le istanziazioni di classe si scrivono come 'Customer("Alice")' o 'ArrayList()'
                if (char.IsUpper(val[0]) && string.IsNullOrEmpty(lastReceiver))
                {
                    _instantiations.Add(new KotlinInstantiation
                    {
                        SourceSymbol = callerSymbol,
                        TargetTypeName = val,
                        Line = line
                    });
                }

                _callSites.Add(new KotlinCallSite
                {
                    CallerSymbol = callerSymbol,
                    InvokedMethodName = val,
                    ReceiverExpressionText = lastReceiver,
                    Line = line
                });

                lastReceiver = null;
                return;
            }

            // Accesso a membro: receiver.member o receiver?.member
            if (Check(".") || Check("?."))
            {
                lastReceiver = val;
                Next(); // consuma '.' o '?.'
                return;
            }

            // Callable reference: Type::method o instance::method
            if (Check("::"))
            {
                Next(); // consuma '::'
                if (Check(KotlinTokenType.Identifier) || Check(KotlinTokenType.Keyword))
                {
                    var refTok = Next();
                    _callSites.Add(new KotlinCallSite
                    {
                        CallerSymbol = callerSymbol,
                        InvokedMethodName = refTok.Value,
                        ReceiverExpressionText = val,
                        Line = refTok.Line
                    });
                }
                lastReceiver = null;
                return;
            }

            if (!Check(".") && !Check("?."))
                lastReceiver = null;

            return;
        }

        // 3. super o this
        if (Match("super") || Match("this"))
        {
            var kw = Peek(-1).Value;
            var line = Peek(-1).Line;

            if (Check(".") || Check("?."))
            {
                lastReceiver = kw;
                Next();
                return;
            }

            if (Check("("))
            {
                _callSites.Add(new KotlinCallSite
                {
                    CallerSymbol = callerSymbol,
                    InvokedMethodName = kw,
                    ReceiverExpressionText = null,
                    Line = line
                });
                lastReceiver = null;
                return;
            }

            return;
        }

        if (!Check(".") && !Check("?."))
            lastReceiver = null;

        Next();
    }
}
