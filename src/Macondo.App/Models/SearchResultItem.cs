using System;
using Macondo.App.Models;

namespace Macondo.App.Models;

/// <summary>
/// Quick search result category.
/// </summary>
public enum SearchResultCategory
{
    /// <summary>Symbol or element of the source code (Form, Class, Module, Routine, etc.).</summary>
    Symbol,

    /// <summary>Tab / Central View View (Graph, DSM, Treemap, Hotspots, ScreenFlow, CFG, Blast Radius).</summary>
    View,

    /// <summary>Configuration or user preferences screen (Themes, Language, Behavior, Shortcuts).</summary>
    Settings,

    /// <summary>Documentation section or concept guide.</summary>
    Help,

    /// <summary>Quick action or application command (Blast Radius, Export, Clean Filters, etc.).</summary>
    Action
}

/// <summary>
/// Item displayed in the global search drop-down menu.
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
