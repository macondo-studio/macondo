using VB6Analyzer.Semantic.Model;

namespace VB6Analyzer.Semantic.Extraction;

/// <summary>Determina il SymbolKind del modulo dall'estensione del file (sezione 7).</summary>
public static class ModuleFileKind
{
    public static SymbolKind FromExtension(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".cls" => SymbolKind.Class,
            ".frm" => SymbolKind.Form,
            ".bas" => SymbolKind.Module,
            ".ctl" => SymbolKind.UserControl,
            _ => throw new NotSupportedException(
                $"Estensione non supportata da ModuleFileKind: '{ext}' (file: {filePath}). " +
                "M2 copre .cls/.frm/.bas/.ctl come da sezione 7 del charter.")
        };
    }
}
