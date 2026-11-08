using System.Collections.Generic;
using Macondo.App.Models;

namespace Macondo.App.Services;

/// <summary>
/// Catalogo di tutte le scorciatoie da tastiera e combinazioni di tasti dell'applicazione,
/// localizzato tramite <see cref="LocalizationService"/>.
/// </summary>
public static class KeyboardShortcutsCatalog
{
    /// <summary>
    /// Restituisce l'elenco completo delle scorciatoie da tastiera localizzate per la lingua specificata.
    /// </summary>
    /// <param name="language">La lingua dell'interfaccia utente (Italiano o Inglese).</param>
    /// <returns>La lista in sola lettura degli elementi di scorciatoia (<see cref="ShortcutItem"/>).</returns>
    public static IReadOnlyList<ShortcutItem> GetShortcuts(AppLanguage language)
    {
        var localizationService = LocalizationService.Instance;

        return new List<ShortcutItem>
        {
            // Categoria: Ricerca e Navigazione
            new()
            {
                KeyBadges = new[] { "Ctrl", "F" },
                ActionName = localizationService.GetForLanguage("Shortcut_SearchSymbol_Action", language),
                Category = localizationService.GetForLanguage("Shortcut_Category_Navigation", language),
                Description = localizationService.GetForLanguage("Shortcut_SearchSymbol_Desc", language)
            },
            new()
            {
                KeyBadges = new[] { "Ctrl", "B" },
                ActionName = localizationService.GetForLanguage("Shortcut_ToggleExplorer_Action", language),
                Category = localizationService.GetForLanguage("Shortcut_Category_Navigation", language),
                Description = localizationService.GetForLanguage("Shortcut_ToggleExplorer_Desc", language)
            },
            new()
            {
                KeyBadges = new[] { "Ctrl", "I" },
                ActionName = localizationService.GetForLanguage("Shortcut_ToggleInspector_Action", language),
                Category = localizationService.GetForLanguage("Shortcut_Category_Navigation", language),
                Description = localizationService.GetForLanguage("Shortcut_ToggleInspector_Desc", language)
            },
            new()
            {
                KeyBadges = new[] { "Escape" },
                ActionName = localizationService.GetForLanguage("Shortcut_Dismiss_Action", language),
                Category = localizationService.GetForLanguage("Shortcut_Category_Navigation", language),
                Description = localizationService.GetForLanguage("Shortcut_Dismiss_Desc", language)
            },

            // Categoria: File e Progetto
            new()
            {
                KeyBadges = new[] { "Ctrl", "O" },
                ActionName = localizationService.GetForLanguage("Shortcut_OpenFolder_Action", language),
                Category = localizationService.GetForLanguage("Shortcut_Category_File", language),
                Description = localizationService.GetForLanguage("Shortcut_OpenFolder_Desc", language)
            },
            new()
            {
                KeyBadges = new[] { "Ctrl", "R" },
                ActionName = localizationService.GetForLanguage("Shortcut_ReloadProject_Action", language),
                Category = localizationService.GetForLanguage("Shortcut_Category_File", language),
                Description = localizationService.GetForLanguage("Shortcut_ReloadProject_Desc", language)
            },
            new()
            {
                KeyBadges = new[] { "Ctrl", "," },
                ActionName = localizationService.GetForLanguage("Shortcut_OpenSettings_Action", language),
                Category = localizationService.GetForLanguage("Shortcut_Category_General", language),
                Description = localizationService.GetForLanguage("Shortcut_OpenSettings_Desc", language)
            },
            new()
            {
                KeyBadges = new[] { "F1" },
                ActionName = localizationService.GetForLanguage("Shortcut_Guide_Action", language),
                Category = localizationService.GetForLanguage("Shortcut_Category_General", language),
                Description = localizationService.GetForLanguage("Shortcut_Guide_Desc", language)
            },
            new()
            {
                KeyBadges = new[] { "Alt", "F4" },
                ActionName = localizationService.GetForLanguage("Shortcut_Exit_Action", language),
                Category = localizationService.GetForLanguage("Shortcut_Category_General", language),
                Description = localizationService.GetForLanguage("Shortcut_Exit_Desc", language)
            },

            // Categoria: Analisi e Grafo
            new()
            {
                KeyBadges = new[] { "F5" },
                ActionName = localizationService.GetForLanguage("Shortcut_ComputeBlastRadius_Action", language),
                Category = localizationService.GetForLanguage("Shortcut_Category_Analysis", language),
                Description = localizationService.GetForLanguage("Shortcut_ComputeBlastRadius_Desc", language)
            },
            new()
            {
                KeyBadges = new[] { localizationService.GetForLanguage("Shortcut_Key_DoubleClick", language) },
                ActionName = localizationService.GetForLanguage("Shortcut_FocusExpand_Action", language),
                Category = localizationService.GetForLanguage("Shortcut_Category_Interactivity", language),
                Description = localizationService.GetForLanguage("Shortcut_FocusExpand_Desc", language)
            },
            new()
            {
                KeyBadges = new[] { localizationService.GetForLanguage("Shortcut_Key_MouseDrag", language) },
                ActionName = localizationService.GetForLanguage("Shortcut_PanMove_Action", language),
                Category = localizationService.GetForLanguage("Shortcut_Category_Interactivity", language),
                Description = localizationService.GetForLanguage("Shortcut_PanMove_Desc", language)
            },
            new()
            {
                KeyBadges = new[] { localizationService.GetForLanguage("Shortcut_Key_MouseWheel", language) },
                ActionName = localizationService.GetForLanguage("Shortcut_Zoom_Action", language),
                Category = localizationService.GetForLanguage("Shortcut_Category_Interactivity", language),
                Description = localizationService.GetForLanguage("Shortcut_Zoom_Desc", language)
            }
        };
    }

    /// <summary>
    /// Restituisce l'elenco completo delle scorciatoie da tastiera localizzate per la lingua correntemente attiva.
    /// </summary>
    public static IReadOnlyList<ShortcutItem> GetShortcuts()
        => GetShortcuts(LocalizationService.Instance.CurrentLanguage);
}
