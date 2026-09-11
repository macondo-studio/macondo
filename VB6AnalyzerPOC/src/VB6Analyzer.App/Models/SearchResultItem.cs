using System;
using Macondo.App.Models;

namespace Macondo.App.Models;

/// <summary>
/// Categoria del risultato di ricerca rapida.
/// </summary>
public enum SearchResultCategory
{
    /// <summary>Simbolo o elemento del codice sorgente (Form, Classe, Modulo, Routine, etc.).</summary>
    Symbol,

    /// <summary>Scheda / Vista di visualizzazione centrale (Grafo, DSM, Treemap, Hotspots, ScreenFlow, CFG, Blast Radius).</summary>
    View,

    /// <summary>Schermata di configurazione o preferenze utente (Temi, Lingua, Comportamento, Scorciatoie).</summary>
    Settings,

    /// <summary>Sezione della documentazione o guida ai concetti.</summary>
    Help,

    /// <summary>Azione rapida o comando applicativo (Blast Radius, Export, Pulisci Filtri, etc.).</summary>
    Action
}

/// <summary>
/// Elemento visualizzato nel menu a discesa della ricerca globale.
/// </summary>
public sealed class SearchResultItem
{
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string BadgeText { get; init; } = string.Empty;
    public SearchResultCategory Category { get; init; } = SearchResultCategory.Symbol;
    public GraphNode? Node { get; init; }
    public Action? ExecuteAction { get; init; }
}
