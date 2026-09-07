namespace VB6Analyzer.App.Services;

public sealed class ShortcutItem
{
    public required IReadOnlyList<string> KeyBadges { get; init; }
    public required string ActionName { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }

    public string KeyChord => string.Join(" + ", KeyBadges);
}

public static class KeyboardShortcutsCatalog
{
    public static IReadOnlyList<ShortcutItem> GetShortcuts(AppLanguage language)
    {
        var isEn = language == AppLanguage.English;

        return new List<ShortcutItem>
        {
            // Categoria: Ricerca e Navigazione
            new()
            {
                KeyBadges = new[] { "Ctrl", "F" },
                ActionName = isEn ? "Search Symbol" : "Cerca Simbolo",
                Category = isEn ? "Navigation" : "Navigazione",
                Description = isEn
                    ? "Places focus directly in the search bar and selects existing text."
                    : "Sposta il cursore direttamente nella barra di ricerca selezionando tutto il testo."
            },
            new()
            {
                KeyBadges = new[] { "Escape" },
                ActionName = isEn ? "Dismiss / Close Popup" : "Chiudi Risultati / Annulla",
                Category = isEn ? "Navigation" : "Navigazione",
                Description = isEn
                    ? "Closes search results dropdown or clears active dialogs."
                    : "Chiude il menu a tendina dei risultati di ricerca o dialoghi attivi."
            },

            // Categoria: File e Progetto
            new()
            {
                KeyBadges = new[] { "Ctrl", "O" },
                ActionName = isEn ? "Open VB6 Folder" : "Apri Cartella VB6",
                Category = "File",
                Description = isEn
                    ? "Opens a folder picker to analyze and visualize any VB6 source directory."
                    : "Apre il selettore cartella per analizzare un nuovo progetto VB6."
            },
            new()
            {
                KeyBadges = new[] { "Ctrl", "," },
                ActionName = isEn ? "Open Settings" : "Apri Impostazioni",
                Category = isEn ? "General" : "Generale",
                Description = isEn
                    ? "Opens the settings dialog to customize themes, language, and shortcuts."
                    : "Apre la finestra delle impostazioni (Temi, Lingua e Scorciatoie)."
            },
            new()
            {
                KeyBadges = new[] { "Alt", "F4" },
                ActionName = isEn ? "Exit Application" : "Esci dall'Applicazione",
                Category = isEn ? "General" : "Generale",
                Description = isEn
                    ? "Safely terminates the VB6 Analyzer application."
                    : "Chiude in sicurezza l'applicazione VB6 Analyzer."
            },

            // Categoria: Analisi e Grafo
            new()
            {
                KeyBadges = new[] { "F5" },
                ActionName = isEn ? "Compute Blast Radius" : "Calcola Blast Radius",
                Category = isEn ? "Analysis" : "Analisi",
                Description = isEn
                    ? "Calculates dependency impact paths and risk level for the selected routine."
                    : "Calcola l'impatto di modifica, i chiamanti transitivi e il livello di rischio."
            },
            new()
            {
                KeyBadges = new[] { isEn ? "Double Click" : "Doppio Clic" },
                ActionName = isEn ? "Focus / Expand" : "Centra / Espandi",
                Category = isEn ? "Interactivity" : "Interattività",
                Description = isEn
                    ? "In Grid: jumps to node in Graph view. In Tree/Graph: expands/collapses container."
                    : "Nella Griglia: salta al nodo nel Grafo. Nel Grafo/Albero: espande/comprime modulo."
            },
            new()
            {
                KeyBadges = new[] { isEn ? "Mouse Drag" : "Trascina Mouse" },
                ActionName = isEn ? "Pan / Move" : "Pan Vista / Sposta Nodi",
                Category = isEn ? "Interactivity" : "Interattività",
                Description = isEn
                    ? "Pan across the graph canvas or drag individual nodes to reorganize the layout."
                    : "Sposta la visuale libera sul canvas o trascina i nodi per riorganizzarli."
            },
            new()
            {
                KeyBadges = new[] { isEn ? "Mouse Wheel" : "Rotella Mouse" },
                ActionName = isEn ? "Zoom In / Out" : "Zoom Avanti / Indietro",
                Category = isEn ? "Interactivity" : "Interattività",
                Description = isEn
                    ? "Smoothly zooms the graph view in or out centered at cursor position."
                    : "Esegue lo zoom fluido della vista grafo centrato sul cursore del mouse."
            }
        };
    }
}
