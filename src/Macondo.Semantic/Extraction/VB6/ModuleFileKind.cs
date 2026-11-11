using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Helper for determining the module category (<see cref="SymbolKind"/>)
/// starting from the source file extension VB6.
/// 
/// MAPPING:
/// - .cls -> <see cref="SymbolKind.Class"/> (Class VB6)
/// - .frm -> <see cref="SymbolKind.Form"/> (VB6 User Form/Window)
/// - .bas -> <see cref="SymbolKind.Module"/> (VB6 Standard Code Module)
/// - .ctl -> <see cref="SymbolKind.UserControl"/> (VB6 ActiveX User Control)
/// </summary>
public static class ModuleFileKind
{
    /// <summary>
    /// Returns the <see cref="SymbolKind"/> corresponding to the specified file extension.
    /// </summary>
    /// <param name="filePath">Path or name of the file to analyze.</param>
    /// <returns>The associated <see cref="SymbolKind"/>.</returns>
    /// <exception cref="NotSupportedException">Launched if the file extension is not among those supported.</exception>
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
