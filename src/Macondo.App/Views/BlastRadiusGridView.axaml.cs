using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Macondo.App.Models;
using Macondo.App.Services;
using Macondo.Semantic.Impact;
using Macondo.Semantic.Model;

namespace Macondo.App.Views;

/// <summary>
/// Visual control for the Blast Radius results tabular sheet and risk matrix of the entire project.
/// </summary>
public partial class BlastRadiusGridView : UserControl
{
    private List<BlastRadiusGridRow> _allBlastRadiusRows = new();
    private HashSet<Guid> _hiddenProjectIds = new();
    private bool _isSyncingSelection;
    private VisualCodeGraph? _currentGraph;
    private string? _currentFolderPath;
    private bool _hasUserMovedNodes;
    private Task? _ongoingPopulateTask;

    /// <summary>
    /// Event triggered when a grid row is selected.
    /// </summary>
    public event EventHandler<BlastRadiusGridRow>? RowSelected;

    /// <summary>
    /// Event triggered when the user requests to navigate and view the node in the graph canvas (double click or action button).
    /// </summary>
    public event EventHandler<Guid>? NavigateToNodeRequested;

    /// <summary>
    /// Event triggered when the user requests to recalculate the full audit.
    /// </summary>
    public event EventHandler? RecalculateAuditRequested;

    /// <summary>
    /// Event triggered to report a status message to the main bar.
    /// </summary>
    public event EventHandler<string>? StatusReported;

    /// <summary>
    /// Gets or sets the complete list of calculated rows.
    /// </summary>
    public List<BlastRadiusGridRow> AllRows
    {
        get => _allBlastRadiusRows;
        set
        {
            _allBlastRadiusRows = value ?? new();
            ApplyGridFilters();
        }
    }

    /// <summary>
    /// Initializes a new instance of <see cref="BlastRadiusGridView"/>.
    /// </summary>
    public BlastRadiusGridView()
    {
        InitializeComponent();
        var defaultRiskIndex = SettingsService.Instance.Settings.DefaultRiskFilterIndex;
        if (RiskFilterComboBox != null)

            RiskFilterComboBox.SelectedIndex = Math.Clamp(defaultRiskIndex, 0, 4);
    }

    /// <summary>
    /// Apply filters on hidden/visible projects.
    /// </summary>
    public void SetHiddenProjects(IEnumerable<Guid>? hiddenProjectIds)
    {
        _hiddenProjectIds = hiddenProjectIds != null ? new HashSet<Guid>(hiddenProjectIds) : new();
        ApplyGridFilters();
    }

    /// <summary>
    /// Synchronizes the active row in the grid to the selected node in the graph.
    /// </summary>
    public void SelectNode(Guid nodeId)
    {
        if (BlastRadiusDataGrid == null) return;
        if (BlastRadiusDataGrid.ItemsSource is IEnumerable<BlastRadiusGridRow> rows)
        {
            var matchingRow = rows.FirstOrDefault(r => r.NodeId == nodeId);
            if (matchingRow != null && BlastRadiusDataGrid.SelectedItem != matchingRow)
            {
                _isSyncingSelection = true;
                try
                {
                    BlastRadiusDataGrid.SelectedItem = matchingRow;
                    BlastRadiusDataGrid.ScrollIntoView(matchingRow, null);
                }
                finally
                {
                    _isSyncingSelection = false;
                }
            }
        }
    }

    /// <summary>
    /// Cleans table data and resets counters.
    /// </summary>
    public void Clear()
    {
        _allBlastRadiusRows = new();
        _currentGraph = null;
        if (BlastRadiusDataGrid != null) BlastRadiusDataGrid.ItemsSource = null;
        if (GridStatsTextBlock != null) GridStatsTextBlock.Text = LocalizationService.Instance.Get("Grid_StatsFormat", 0, 0);
        if (GridLoadingProgressBar != null) GridLoadingProgressBar.IsVisible = false;
    }

    /// <summary>
    /// Calculates the semantic impact and Blast Radius for all workspace routines (methods and properties) in the background,
    /// populating the table grid with caller counts, depth, forms involved and risk level.
    /// </summary>
    public async Task PopulateBlastRadiusGridAsync(VisualCodeGraph? graph, string? currentFolderPath, bool hasUserMovedNodes)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            await Dispatcher.UIThread.InvokeAsync(() => PopulateBlastRadiusGridAsync(graph, currentFolderPath, hasUserMovedNodes));
            return;
        }

        if (_ongoingPopulateTask != null && !_ongoingPopulateTask.IsCompleted)
        {
            await _ongoingPopulateTask;
            return;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ongoingPopulateTask = tcs.Task;
        try
        {
            await PopulateBlastRadiusGridInternalAsync(graph, currentFolderPath, hasUserMovedNodes);
            tcs.TrySetResult();
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
            throw;
        }
        finally
        {
            _ongoingPopulateTask = null;
        }
    }

    private async Task PopulateBlastRadiusGridInternalAsync(VisualCodeGraph? graph, string? currentFolderPath, bool hasUserMovedNodes)
    {
        _currentGraph = graph;
        _currentFolderPath = currentFolderPath;
        _hasUserMovedNodes = hasUserMovedNodes;

        if (graph is null)
        {
            Clear();
            return;
        }

        var queries = graph.Queries;
        if (queries == null) return;

        // We collect all the routine type nodes (Method and Property)
        var routineNodes = graph.Nodes
            .Where(n => n.SemanticSymbol is not null &&
                        (n.SemanticSymbol.Kind == SymbolKind.Method || n.SemanticSymbol.Kind == SymbolKind.Property))
            .ToList();

        if (routineNodes.Count == 0)
        {
            Clear();
            if (!string.IsNullOrEmpty(_currentFolderPath) && _currentGraph != null)

                _ = ProjectCacheService.Instance.SaveSnapshotAsync(_currentFolderPath, _currentGraph, Array.Empty<BlastRadiusGridRow>(), _hasUserMovedNodes);
            return;
        }

        // Show and configure the calculation progress bar
        if (GridLoadingProgressBar != null)
        {
            GridLoadingProgressBar.Minimum = 0;
            GridLoadingProgressBar.Maximum = routineNodes.Count;
            GridLoadingProgressBar.Value = 0;
            GridLoadingProgressBar.IsVisible = true;
        }

        if (GridStatsTextBlock != null)
            GridStatsTextBlock.Text = LocalizationService.Instance.Get("Grid_Calculating", routineNodes.Count);

        try
        {
            var totalCount = routineNodes.Count;
            var processedCount = 0;
            var lastReportTime = DateTime.UtcNow;

            var rows = await Task.Run(() =>
            {
                var result = new List<BlastRadiusGridRow>(totalCount);
                for (int i = 0; i < totalCount; i++)
                {
                    var node = routineNodes[i];
                    var sym = node.SemanticSymbol!;
                    var report = queries.GetImpactReport(sym);
                    string containerName = "(globale)";
                    if (node.ParentId.HasValue && graph.GetNode(node.ParentId.Value) is { } parentNode)

                        containerName = parentNode.Name;

                    else if (!string.IsNullOrEmpty(sym.FilePath))

    containerName = Path.GetFileName(sym.FilePath);
                    var affectedForms = report.AffectedForms.Select(f => f.Name).ToList();

                    result.Add(new BlastRadiusGridRow
                    {
                        TargetSymbol = sym,
                        NodeId = node.Id,
                        ProjectId = node.ProjectId ?? (node.Kind == SymbolKind.Project ? node.Id : null),
                        ProjectName = node.ProjectName ?? "(predefinito)",
                        Name = sym.Name,
                        QualifiedName = sym.QualifiedName,
                        ContainerName = containerName,
                        Kind = sym.Kind.ToString(),
                        DirectCallersCount = report.DirectCallers.Count,
                        IndirectCallersCount = report.IndirectCallers.Count,
                        MaxDepth = report.MaxDependencyDepth,
                        RiskLevel = report.RiskLevel,
                        AffectedFormsCount = report.AffectedForms.Count,
                        AffectedFormsSummary = affectedForms.Count > 0 ? string.Join(", ", affectedForms) : "-",
                        TotalImpactedCount = report.DirectCallers.Count + report.IndirectCallers.Count + report.AffectedForms.Count + report.AffectedClasses.Count + report.AffectedModules.Count
                    });

                    processedCount++;

                    // Periodic notification of progress on the UI
                    var now = DateTime.UtcNow;
                    if (processedCount % 25 == 0 || (now - lastReportTime).TotalMilliseconds >= 100 || processedCount == totalCount)
                    {
                        lastReportTime = now;
                        var current = processedCount;
                        var percent = (int)((double)current / totalCount * 100);
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (GridLoadingProgressBar != null && GridLoadingProgressBar.IsVisible)

                                GridLoadingProgressBar.Value = current;
                            if (GridStatsTextBlock != null)

                                GridStatsTextBlock.Text = LocalizationService.Instance.Get("Grid_CalculatingProgress", current, totalCount, percent);
                        });
                    }
                }

                // Default sorting: Risk decreasing (Critical -> High -> Medium -> Low), then Total Impact decreasing
                return result
                    .OrderByDescending(r => r.RiskLevel)
                    .ThenByDescending(r => r.TotalImpactedCount)
                    .ToList();
            });

            _allBlastRadiusRows = rows;
            ApplyGridFilters();

            // Save in the background to the persistent cache if we are analyzing a folder on disk
            if (!string.IsNullOrEmpty(_currentFolderPath) && _currentGraph != null)

                _ = ProjectCacheService.Instance.SaveSnapshotAsync(_currentFolderPath, _currentGraph, _allBlastRadiusRows, _hasUserMovedNodes);
        }
        catch (Exception ex)
        {
            StatusReported?.Invoke(this, LocalizationService.Instance.Get("Status_ErrorGridCalc", ex.Message));
        }
        finally
        {
            if (GridLoadingProgressBar != null)

                GridLoadingProgressBar.IsVisible = false;
        }
    }

    /// <summary>
    /// Applies the current filter criteria to the complete list of rows.
    /// </summary>
    public void ApplyGridFilters()
    {
        if (BlastRadiusDataGrid == null || GridStatsTextBlock == null) return;

        var filterText = GridFilterTextBox?.Text?.Trim() ?? string.Empty;
        var selectedRiskIndex = RiskFilterComboBox?.SelectedIndex ?? 0;

        IEnumerable<BlastRadiusGridRow> query = _allBlastRadiusRows;

        if (_hiddenProjectIds.Count > 0)

            query = query.Where(r => !r.ProjectId.HasValue || !_hiddenProjectIds.Contains(r.ProjectId.Value));

        if (!string.IsNullOrEmpty(filterText))

            query = query.Where(r =>
                r.Name.Contains(filterText, StringComparison.OrdinalIgnoreCase) ||
                r.ContainerName.Contains(filterText, StringComparison.OrdinalIgnoreCase) ||
                r.ProjectName.Contains(filterText, StringComparison.OrdinalIgnoreCase) ||
                r.QualifiedName.Contains(filterText, StringComparison.OrdinalIgnoreCase));

        if (selectedRiskIndex > 0)
        {
            var targetRisk = selectedRiskIndex switch
            {
                1 => ImpactRiskLevel.Critical,
                2 => ImpactRiskLevel.High,
                3 => ImpactRiskLevel.Medium,
                4 => ImpactRiskLevel.Low,
                _ => (ImpactRiskLevel?)null
            };

            if (targetRisk.HasValue)

                query = query.Where(r => r.RiskLevel == targetRisk.Value);
        }

        var filteredList = query.ToList();
        BlastRadiusDataGrid.ItemsSource = filteredList;
        GridStatsTextBlock.Text = LocalizationService.Instance.Get("Grid_StatsFormat", filteredList.Count, _allBlastRadiusRows.Count);
    }

    /// <summary>
    /// Update localized text in the grid's toolbar, filters, and column headers.
    /// </summary>
    public void UpdateLocalizedUI()
    {
        var localizationService = LocalizationService.Instance;

        GridFilterLabel.Text = localizationService["Grid_Filter"];
        GridFilterTextBox.Watermark = localizationService["Grid_FilterWatermark"];
        GridRiskLabel.Text = localizationService["Grid_Risk"];
        RiskItemAll.Content = localizationService["Grid_Risk_All"];
        RiskItemCritical.Content = localizationService["Grid_Risk_Critical"];
        RiskItemHigh.Content = localizationService["Grid_Risk_High"];
        RiskItemMedium.Content = localizationService["Grid_Risk_Medium"];
        RiskItemLow.Content = localizationService["Grid_Risk_Low"];
        RefreshGridButtonText.Text = localizationService["Grid_RecalculateAudit"];
        ToolTip.SetTip(RefreshGridButton, localizationService["Tooltip_RecalculateAudit"]);

        if (BlastRadiusDataGrid?.Columns.Count >= 10)
        {
            BlastRadiusDataGrid.Columns[0].Header = localizationService["Col_Project"];
            BlastRadiusDataGrid.Columns[1].Header = localizationService["Col_Routine"];
            BlastRadiusDataGrid.Columns[2].Header = localizationService["Col_Module"];
            BlastRadiusDataGrid.Columns[3].Header = localizationService["Col_DirectCallers"];
            BlastRadiusDataGrid.Columns[4].Header = localizationService["Col_IndirectCallers"];
            BlastRadiusDataGrid.Columns[5].Header = localizationService["Col_Depth"];
            BlastRadiusDataGrid.Columns[6].Header = localizationService["Col_Risk"];
            BlastRadiusDataGrid.Columns[7].Header = localizationService["Col_Forms"];
            BlastRadiusDataGrid.Columns[8].Header = localizationService["Col_TotalImpact"];
            BlastRadiusDataGrid.Columns[9].Header = localizationService["Col_Action"];
        }

        ApplyGridFilters();
    }

    /// <summary>
    /// Applies the current theme's background and border colors to the grid and toolbar.
    /// </summary>
    public void ApplyTheme(ThemeDefinition theme)
    {
        if (Color.TryParse(theme.BackgroundSurface, out var bgSurface))
        {
            var surfaceBrush = new SolidColorBrush(bgSurface);
            GridToolbarBorder.Background = surfaceBrush;
            BlastRadiusDataGrid.Background = surfaceBrush;
            BlastRadiusDataGrid.RowBackground = surfaceBrush;
        }

        if (Color.TryParse(theme.BorderDefault, out var borderCol))
        {
            var borderBrush = new SolidColorBrush(borderCol);
            GridToolbarBorder.BorderBrush = borderBrush;
        }
    }

    private void OnGridFilterTextChanged(object? sender, TextChangedEventArgs e)
    {
        ApplyGridFilters();
    }

    private void OnRiskFilterChanged(object? sender, SelectionChangedEventArgs e)
    {
        ApplyGridFilters();
        if (RiskFilterComboBox != null)

            SettingsService.Instance.Update(s => s.DefaultRiskFilterIndex = RiskFilterComboBox.SelectedIndex);
    }

    private void OnRefreshGridClicked(object? sender, RoutedEventArgs e)
    {
        RecalculateAuditRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnGridRowSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingSelection || BlastRadiusDataGrid.SelectedItem is not BlastRadiusGridRow row)
            return;

        RowSelected?.Invoke(this, row);
    }

    private void OnGridRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (BlastRadiusDataGrid.SelectedItem is BlastRadiusGridRow row)

            NavigateToNodeRequested?.Invoke(this, row.NodeId);
    }

    private void OnShowInGraphButtonClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: BlastRadiusGridRow row })

            NavigateToNodeRequested?.Invoke(this, row.NodeId);
    }
}
