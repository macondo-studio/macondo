using Antlr4.Runtime;

namespace Macondo.Parser.Diagnostics;

/// <summary>
/// Capture lexer/parser errors in a list instead of printing them raw to
/// stderr, so whoever uses it (spike M1, spike M2, in the future the real pipeline) can
/// decide how to show them.
/// </summary>
public sealed class CollectingErrorListener(List<string> errors) : IAntlrErrorListener<IToken>, IAntlrErrorListener<int>
{
    public void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line,
        int charPositionInLine, string msg, RecognitionException e)
    {
        errors.Add($"linea {line}:{charPositionInLine} — {msg}");
    }

    public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol, int line,
        int charPositionInLine, string msg, RecognitionException e)
    {
        errors.Add($"linea {line}:{charPositionInLine} — {msg}");
    }
}
