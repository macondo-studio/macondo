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
/// Visual control for the Symbol Inspector & Blast Radius side panel.
/// Show semantic details, type/parameter metadata, AI slice extraction,
/// impact radius (Blast Radius) and direct and indirect call relations of the selected symbol.
/// </summary>
public partial class SymbolInspectorView : UserControl
{
    /// <summary>
    /// Event raised when the user requests to open the original symbol file.
    /// </summary>
    public event EventHandler? OpenFileRequested;

    /// <summary>
    /// Event generated when the user requests to open the folder containing the source file.
    /// </summary>
    public event EventHandler? OpenFolderRequested;

    /// <summary>
    /// Event raised when the user requests to copy the Context Slice for AI to the clipboard.
    /// </summary>
    public event EventHandler? CopySliceRequested;

    /// <summary>
    /// Event raised when the user requests to save the Context Slice as a Markdown file.
    /// </summary>
    public event EventHandler? SaveSliceRequested;

    public SymbolInspectorView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Shows the empty state of the Inspector when no symbols are selected in the graph.
    /// If there is no project under analysis (hasProject = false), hides the suggestions panel.
    /// </summary>
    public void ShowEmpty(bool hasProject = true)
    {
        InspectorEmptyPanel.IsVisible = hasProject;
        InspectorDetailsPanel.IsVisible = false;
    }

    /// <summary>
    /// Displays immediate static metadata of the selected node (name, qualified name, category, visibility, language, type, parameters, file coordinates and code preview).
    /// </summary>
    /// <param name="node">The node of the selected graph.</param>
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

        // Formal parameters
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

        // File, row coordinates and row count
        if (sym != null && !string.IsNullOrEmpty(sym.FilePath))
        {
            InspectorFileSection.IsVisible = true;
            var fileName = Path.GetFileName(sym.FilePath);
            var startLine = sym.LineStart;
            var endLine = sym.LineEnd;
            var loc = (startLine > 0 && endLine >= startLine) ? (endLine - startLine + 1) : (startLine > 0 ? 1 : 0);

            if (startLine > 0 && endLine >= startLine)

                InspectorFile.Text = $"{fileName} ({string.Format(LocalizationService.Instance["Inspector_CodeLines_Format"], startLine, endLine, loc)})";

            else if (startLine > 0)

    InspectorFile.Text = $"{fileName} ({string.Format(LocalizationService.Instance["Inspector_CodeLines_Single"], startLine)})";

else
            {
                InspectorFile.Text = fileName;
            }
        }
        else
        {
            InspectorFileSection.IsVisible = false;
        }
    }

    /// <summary>
    /// Sets the calculation-in-progress visual state for the Blast Radius section and caller/callee lists.
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
    /// Restores the default (empty) state for the dynamic sections of the Inspector.
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
    /// Update the Blast Radius section (risk badges, counts, forms, modules and call chains).
    /// </summary>
    /// <param name="impact">The calculated impact report.</param>
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

        // Execution paths (call chains)
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
    /// Updates the entire Impact and Relationships section in the Symbol Inspector, including the textual lists of callers, callees, and instantiations.
    /// </summary>
    /// <param name="impact">The calculated impact report.</param>
    /// <param name="callees">The list of directly called symbols.</param>
    /// <param name="instantiations">The list of instantiated classes.</param>
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
    /// Updates all localized Symbol Inspector UI strings.
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
