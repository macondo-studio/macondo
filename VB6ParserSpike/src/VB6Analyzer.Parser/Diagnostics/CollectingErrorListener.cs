using Antlr4.Runtime;

namespace Macondo.Parser.Diagnostics;

/// <summary>
/// Cattura gli errori di lexer/parser in una lista invece di stamparli grezzi su
/// stderr, così chi lo usa (spike M1, spike M2, in futuro la vera pipeline) può
/// decidere come mostrarli.
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
