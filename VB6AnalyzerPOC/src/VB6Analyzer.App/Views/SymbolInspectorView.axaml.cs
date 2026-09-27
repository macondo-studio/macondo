using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Macondo.App.Models;
using Macondo.App.Services;
using Macondo.Semantic.Impact;
using Macondo.Semantic.Model;

namespace Macondo.App.Views;

/// <summary>
/// Controllo visuale per il pannello laterale Symbol Inspector &amp; Blast Radius.
/// Mostra i dettagli semantici, metadati di tipo/parametri, estrazione AI slice,
/// raggio d'impatto (Blast Radius) e relazioni di chiamata dirette e indirette del simbolo selezionato.
/// </summary>
public partial class SymbolInspectorView : UserControl
{
    /// <summary>
    /// Evento generato quando l'utente richiede di aprire il file originale del simbolo.
    /// </summary>
    public event EventHandler? OpenFileRequested;

    /// <summary>
    /// Evento generato quando l'utente richiede di aprire la cartella contenitrice del file sorgente.
    /// </summary>
    public event EventHandler? OpenFolderRequested;

    /// <summary>
    /// Evento generato quando l'utente richiede di copiare negli appunti il Context Slice per AI.
    /// </summary>
    public event EventHandler? CopySliceRequested;

    /// <summary>
    /// Evento generato quando l'utente richiede di salvare il Context Slice come file Markdown.
    /// </summary>
    public event EventHandler? SaveSliceRequested;

    public SymbolInspectorView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Mostra lo stato vuoto dell'Inspector quando nessun simbolo è selezionato nel grafo.
    /// Se non c'è alcun progetto in analisi (hasProject = false), nasconde il pannello dei suggerimenti.
    /// </summary>
    public void ShowEmpty(bool hasProject = true)
    {
        InspectorEmptyPanel.IsVisible = hasProject;
        InspectorDetailsPanel.IsVisible = false;
    }

    /// <summary>
    /// Visualizza i metadati statici immediati del nodo selezionato (nome, qualified name, categoria, visibilità, linguaggio, tipo, parametri e coordinate file).
    /// </summary>
    /// <param name="node">Il nodo del grafo selezionato.</param>
    public void ShowSymbolMetadata(GraphNode node)
    {
        InspectorEmptyPanel.IsVisible = false;
        InspectorDetailsPanel.IsVisible = true;

        InspectorName.Text = node.Name;
        InspectorQualifiedName.Text = node.QualifiedName;
        InspectorKind.Text = node.Kind.ToString();

        var sym = node.SemanticSymbol;

        // Visibilità
        InspectorAccessibility.Text = sym != null ? sym.Accessibility.ToString() : "N/A";

        // Linguaggio
        var (langBadge, langBg, langFg, langTip) = ExplorerTreeItem.GetLanguageBadgeInfo(node.Language);
        if (!string.IsNullOrEmpty(langBadge))
        {
            InspectorLanguageBadge.IsVisible = true;
            InspectorLanguage.Text = langBadge;
            InspectorLanguageBadge.Background = Brush.Parse(langBg ?? "#2563EB");
            InspectorLanguage.Foreground = Brush.Parse(langFg ?? "#FFFFFF");
            ToolTip.SetTip(InspectorLanguageBadge, langTip);
        }
        else
        {
            InspectorLanguageBadge.IsVisible = false;
        }

        // Tipo
        if (sym != null && (sym.DeclaredTypeName != null || sym.ReturnType != null))
        {
            InspectorTypeSection.IsVisible = true;
            var declared = sym.DeclaredTypeName ?? sym.ReturnType;
            InspectorType.Text = sym.ResolvedTypeSymbolId.HasValue || sym.ResolvedReturnTypeSymbolId.HasValue
                ? $"As {declared} (Risolto)"
                : $"As {declared}";
        }
        else
        {
            InspectorTypeSection.IsVisible = false;
        }

        // Parametri formali
        if (sym != null && sym.Parameters.Count > 0)
        {
            InspectorParamsSection.IsVisible = true;
            var paramList = sym.Parameters.Select(p =>
            {
                var mode = p.IsByVal ? "ByVal" : "ByRef";
                var opt = p.IsOptional ? "Optional " : "";
                var type = p.DeclaredTypeName != null ? $" As {p.DeclaredTypeName}" : "";
                return $"{opt}{mode} {p.Name}{type}";
            });
            InspectorParams.Text = string.Join("\n", paramList);
        }
        else
        {
            InspectorParamsSection.IsVisible = false;
        }

        // File e coordinate di riga
        if (sym != null && sym.FilePath != null)
        {
            InspectorFileSection.IsVisible = true;
            InspectorFile.Text = $"{Path.GetFileName(sym.FilePath)} (Righe {sym.LineStart}-{sym.LineEnd})";
        }
        else
        {
            InspectorFileSection.IsVisible = false;
        }
    }

    /// <summary>
    /// Imposta lo stato visivo di calcolo in corso per la sezione Blast Radius e per gli elenchi dei chiamanti/chiamati.
    /// </summary>
    public void SetCalculatingState()
    {
        var localizationService = LocalizationService.Instance;
        RiskLevelText.Text = localizationService["Inspector_Calculating"];
        RiskBadge.Background = new SolidColorBrush(Color.Parse("#FF6633"));
        BlastDirectCallersCount.Text = "...";
        BlastIndirectCallersCount.Text = "...";
        BlastMaxDepth.Text = "...";
        InspectorCallers.Text = localizationService["Inspector_CalculatingCallers"];
        InspectorCallees.Text = localizationService["Inspector_CalculatingCallees"];
        BlastFormsSection.IsVisible = false;
        BlastClassesSection.IsVisible = false;
        BlastPathsSection.IsVisible = false;
        InspectorInstantiatesSection.IsVisible = false;
    }

    /// <summary>
    /// Ripristina lo stato predefinito (vuoto) per le sezioni dinamiche dell'Inspector.
    /// </summary>
    public void ClearDynamicSections()
    {
        var localizationService = LocalizationService.Instance;
        InspectorCallers.Text = localizationService["Inspector_NoItems"];
        InspectorCallees.Text = localizationService["Inspector_NoItems"];
        InspectorInstantiatesSection.IsVisible = false;
        BlastFormsSection.IsVisible = false;
        BlastClassesSection.IsVisible = false;
        BlastPathsSection.IsVisible = false;
    }

    /// <summary>
    /// Aggiorna la sezione Blast Radius (badge di rischio, conteggi, form, moduli e call chains).
    /// </summary>
    /// <param name="impact">Il report d'impatto calcolato.</param>
    public void UpdateImpactSection(ImpactReport impact)
    {
        var localizationService = LocalizationService.Instance;

        // Risk Badge
        RiskLevelText.Text = localizationService.Get("Risk_Format", localizationService.GetRiskLevel(impact.RiskLevel));
        RiskBadge.Background = impact.RiskLevel switch
        {
            ImpactRiskLevel.Low => new SolidColorBrush(Color.Parse("#2E7D32")),
            ImpactRiskLevel.Medium => new SolidColorBrush(Color.Parse("#F57C00")),
            ImpactRiskLevel.High => new SolidColorBrush(Color.Parse("#D32F2F")),
            ImpactRiskLevel.Critical => new SolidColorBrush(Color.Parse("#880E4F")),
            _ => new SolidColorBrush(Color.Parse("#454555"))
        };

        BlastDirectCallersCount.Text = impact.DirectCallers.Count.ToString();
        BlastIndirectCallersCount.Text = impact.IndirectCallers.Count.ToString();
        BlastMaxDepth.Text = impact.MaxDependencyDepth.ToString();

        // Form UI impattate
        if (impact.AffectedForms.Count > 0)
        {
            BlastFormsSection.IsVisible = true;
            BlastFormsList.Text = string.Join(", ", impact.AffectedForms.Select(f => f.Name));
        }
        else
        {
            BlastFormsSection.IsVisible = false;
        }

        // Classi e Moduli toccati
        var touchedContainers = impact.AffectedClasses.Concat(impact.AffectedModules).ToList();
        if (touchedContainers.Count > 0)
        {
            BlastClassesSection.IsVisible = true;
            BlastClassesList.Text = string.Join(", ", touchedContainers.Select(c => c.Name));
        }
        else
        {
            BlastClassesSection.IsVisible = false;
        }

        // Percorsi di esecuzione (call chains)
        if (impact.AffectedExecutionPaths.Count > 0)
        {
            BlastPathsSection.IsVisible = true;
            var paths = impact.AffectedExecutionPaths.Take(5).Select(p => $"• {p}");
            BlastPathsList.Text = string.Join("\n", paths) + (impact.AffectedExecutionPaths.Count > 5 ? $"\n... (+ altri {impact.AffectedExecutionPaths.Count - 5})" : "");
        }
        else
        {
            BlastPathsSection.IsVisible = false;
        }
    }

    /// <summary>
    /// Aggiorna l'intera sezione d'impatto e relazioni nel Symbol Inspector, compresi gli elenchi testuali di chiamanti, chiamati e istanziazioni.
    /// </summary>
    /// <param name="impact">Il report d'impatto calcolato.</param>
    /// <param name="callees">L'elenco dei simboli chiamati direttamente.</param>
    /// <param name="instantiations">L'elenco delle classi istanziate.</param>
    public void UpdateImpactDetails(
        ImpactReport impact,
        IReadOnlyList<Symbol> callees,
        IReadOnlyList<Symbol> instantiations)
    {
        UpdateImpactSection(impact);

        var localizationService = LocalizationService.Instance;

        // Callers diretti standard
        InspectorCallers.Text = impact.DirectCallers.Count > 0
            ? string.Join("\n", impact.DirectCallers.Select(c => $"• {c.QualifiedName}"))
            : localizationService["Inspector_NoCallers"];

        // Callees diretti
        InspectorCallees.Text = callees.Count > 0
            ? string.Join("\n", callees.Select(c => $"• {c.QualifiedName}"))
            : localizationService["Inspector_NoCallees"];

        // Classi istanziate
        if (instantiations.Count > 0)
        {
            InspectorInstantiatesSection.IsVisible = true;
            InspectorInstantiates.Text = string.Join("\n", instantiations.Select(i => $"• {i.QualifiedName}"));
        }
        else
        {
            InspectorInstantiatesSection.IsVisible = false;
        }
    }

    /// <summary>
    /// Aggiorna tutte le stringhe localizzate dell'interfaccia utente del Symbol Inspector.
    /// </summary>
    public void UpdateLocalizedUI()
    {
        var localizationService = LocalizationService.Instance;

        InspectorTitleText.Text = localizationService["Inspector_Title"];
        InspectorEmptyTitle.Text = localizationService["Inspector_NoSelection"];
        InspectorEmptyHint1.Text = localizationService["Inspector_Hint1"];
        InspectorEmptyHint2.Text = localizationService["Inspector_Hint2"];
        InspectorEmptyHint3.Text = localizationService["Inspector_Hint3"];
        InspectorSecSymbol.Text = localizationService["Inspector_Sec_Symbol"];
        InspectorSecKind.Text = localizationService["Inspector_Sec_Kind"];
        InspectorSecVisibility.Text = localizationService["Inspector_Sec_Visibility"];
        InspectorSecLanguage.Text = localizationService["Inspector_Sec_Language"];
        InspectorSecType.Text = localizationService["Inspector_Sec_Type"];
        InspectorSecParams.Text = localizationService["Inspector_Sec_Params"];
        InspectorSecFile.Text = localizationService["Inspector_Sec_File"];
        ToolTip.SetTip(InspectorOpenFileButton, localizationService["Inspector_Tooltip_OpenFile"]);
        ToolTip.SetTip(InspectorOpenFolderButton, localizationService["Inspector_Tooltip_OpenFolder"]);

        // AI Context
        InspectorSecAiContext.Text = localizationService["Inspector_Sec_AIContext"];
        InspectorAiContextDesc.Text = localizationService["Inspector_AiContextDesc"];
        CopyLlmSliceButtonText.Text = localizationService["Inspector_CopyLlmSlice"];
        SaveLlmSliceButtonText.Text = localizationService["Inspector_SaveLlmSlice"];
        ToolTip.SetTip(CopyLlmSliceButton, localizationService["Tooltip_CopyLlmSlice"]);
        ToolTip.SetTip(SaveLlmSliceButton, localizationService["Tooltip_SaveLlmSlice"]);

        // Blast Radius
        InspectorSecBlast.Text = localizationService["Inspector_Sec_BlastRadius"];
        InspectorLabelDirectCallers.Text = localizationService["Inspector_DirectCallersCount"];
        InspectorLabelIndirectCallers.Text = localizationService["Inspector_IndirectCallersCount"];
        InspectorLabelMaxDepth.Text = localizationService["Inspector_MaxDepth"];
        InspectorSecForms.Text = localizationService["Inspector_Sec_Forms"];
        InspectorSecClasses.Text = localizationService["Inspector_Sec_Classes"];
        InspectorSecPaths.Text = localizationService["Inspector_Sec_Paths"];
        InspectorSecCallers.Text = localizationService["Inspector_Sec_WhoCallsMe"];
        InspectorSecCallees.Text = localizationService["Inspector_Sec_WhatDoICall"];
        InspectorSecInstantiates.Text = localizationService["Inspector_Sec_Instantiates"];
    }

    private void OnOpenFileClicked(object? sender, RoutedEventArgs e)
    {
        OpenFileRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnOpenFolderClicked(object? sender, RoutedEventArgs e)
    {
        OpenFolderRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnCopySliceClicked(object? sender, RoutedEventArgs e)
    {
        CopySliceRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnSaveSliceClicked(object? sender, RoutedEventArgs e)
    {
        SaveSliceRequested?.Invoke(this, EventArgs.Empty);
    }
}
