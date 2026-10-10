using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Helper per determinare la categoria di modulo (<see cref="SymbolKind"/>)
/// a partire dall'estensione del file sorgente VB6.
/// 
/// MAPPATURA:
/// - .cls -> <see cref="SymbolKind.Class"/> (Classe VB6)
/// - .frm -> <see cref="SymbolKind.Form"/> (Form / Finestra utente VB6)
/// - .bas -> <see cref="SymbolKind.Module"/> (Modulo di codice standard VB6)
/// - .ctl -> <see cref="SymbolKind.UserControl"/> (Controllo utente ActiveX VB6)
/// </summary>
public static class ModuleFileKind
{
    /// <summary>
    /// Restituisce il <see cref="SymbolKind"/> corrispondente all'estensione del file specificato.
    /// </summary>
    /// <param name="filePath">Percorso o nome del file da analizzare.</param>
    /// <returns>Il <see cref="SymbolKind"/> associato.</returns>
    /// <exception cref="NotSupportedException">Lanciata se l'estensione del file non è tra quelle supportate.</exception>
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
                "I formati supportati sono .cls, .frm, .bas, .ctl.")
        };
    }
}
