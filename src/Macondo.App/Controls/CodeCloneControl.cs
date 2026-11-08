using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Macondo.App.Models;
using Macondo.App.Services;
using Macondo.Semantic.Analytics.CodeClones;
using Macondo.Semantic.Model;

namespace Macondo.App.Controls;

public sealed class CodeCloneGridRow
{
    public required CodeCloneGroup Group { get; init; }

    public string TypeText => Group.Type == CloneType.Type1Exact
        ? LocalizationService.Instance["CodeClone_Type1"]
        : LocalizationService.Instance["CodeClone_Type2"];
    public int LineCount => Group.LineCount;
    public int OccurrencesCount => Group.Occurrences.Count;
    public int RedundantLines => Group.RedundantLines;
    public string FirstOccurrence => Group.Occurrences.Count > 0 ? $"{Group.Occurrences[0].ModuleName} ({Group.Occurrences[0].FilePath})" : string.Empty;
    public string AllOccurrencesSummary => string.Join(", ", Group.Occurrences.Select(o => $"{o.ModuleName}:{o.LineStart}"));
}

public sealed class CodeCloneControl : UserControl
{
    public static readonly StyledProperty<CodeCloneReport?> ReportProperty =
        AvaloniaProperty.Register<CodeCloneControl, CodeCloneReport?>(nameof(Report));

    public CodeCloneReport? Report
    {
        get => GetValue(ReportProperty);
        set => SetValue(ReportProperty, value);
    }

    private readonly ObservableCollection<CodeCloneGridRow> _rows = new();
    private List<CodeCloneGroup> _allGroups = new();

    private readonly TextBlock _kpiGroupsValue;
    private readonly TextBlock _kpiOccurrencesValue;
    private readonly TextBlock _kpiRedundantLinesValue;
    private readonly TextBlock _kpiExactClonesValue;

    private readonly TextBox _searchBox;
    private readonly DataGrid _grid;
    private readonly TextBlock _statsText;

    public CodeCloneControl()
    {
        var mainGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Margin = new Thickness(16)
        };

        // 1. KPI Cards Header
        var kpiPanel = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 12) };

        var card1 = CreateKpiCard(LocalizationService.Instance["CodeClone_Kpi_Groups"], out _kpiGroupsValue, "#3B82F6");
        var card2 = CreateKpiCard(LocalizationService.Instance["CodeClone_Kpi_Occurrences"], out _kpiOccurrencesValue, "#F59E0B");
        var card3 = CreateKpiCard(LocalizationService.Instance["CodeClone_Kpi_RedundantLines"], out _kpiRedundantLinesValue, "#EF4444");
        var card4 = CreateKpiCard(LocalizationService.Instance["CodeClone_Kpi_ExactClones"], out _kpiExactClonesValue, "#8B5CF6");

        kpiPanel.Children.Add(card1);
        kpiPanel.Children.Add(card2);
        kpiPanel.Children.Add(card3);
        kpiPanel.Children.Add(card4);
        Grid.SetRow(kpiPanel, 0);
        mainGrid.Children.Add(kpiPanel);

        // 2. Toolbar
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Thickness(0, 0, 0, 12)
        };

        _searchBox = new TextBox
        {
            Watermark = LocalizationService.Instance["CodeClone_SearchWatermark"],
            Width = 280,
            VerticalAlignment = VerticalAlignment.Center
        };
        _searchBox.KeyUp += (_, _) => FilterData();

        toolbar.Children.Add(_searchBox);
        Grid.SetRow(toolbar, 1);
        mainGrid.Children.Add(toolbar);

        // 3. DataGrid
        _grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            ItemsSource = _rows,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HeadersVisibility = DataGridHeadersVisibility.Column
        };

        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["CodeClone_Col_Type"], Binding = new Binding(nameof(CodeCloneGridRow.TypeText)), Width = new DataGridLength(150) });
        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["CodeClone_Col_Lines"], Binding = new Binding(nameof(CodeCloneGridRow.LineCount)), Width = new DataGridLength(120) });
        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["CodeClone_Col_Occurrences"], Binding = new Binding(nameof(CodeCloneGridRow.OccurrencesCount)), Width = new DataGridLength(100) });
        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["CodeClone_Col_Redundant"], Binding = new Binding(nameof(CodeCloneGridRow.RedundantLines)), Width = new DataGridLength(130) });
        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["CodeClone_Col_Summary"], Binding = new Binding(nameof(CodeCloneGridRow.AllOccurrencesSummary)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });

        Grid.SetRow(_grid, 2);
        mainGrid.Children.Add(_grid);

        // 4. Footer stats
        _statsText = new TextBlock
        {
            Text = LocalizationService.Instance["CodeClone_Placeholder"],
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 8, 0, 0)
        };
        Grid.SetRow(_statsText, 3);
        mainGrid.Children.Add(_statsText);

        Content = mainGrid;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ReportProperty)

            OnReportChanged(change.GetNewValue<CodeCloneReport?>());
    }

    private static Border CreateKpiCard(string title, out TextBlock valueBlock, string accentHex)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#1A1A24")),
            BorderBrush = new SolidColorBrush(Color.Parse(accentHex)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Margin = new Thickness(4)
        };

        var sp = new StackPanel { Spacing = 4 };
        var titleBlock = new TextBlock { Text = title, FontSize = 11, Foreground = Brushes.LightGray };
        valueBlock = new TextBlock { Text = "0", FontSize = 18, FontWeight = FontWeight.Bold, Foreground = Brushes.White };

        sp.Children.Add(titleBlock);
        sp.Children.Add(valueBlock);
        border.Child = sp;
        return border;
    }

    private void OnReportChanged(CodeCloneReport? report)
    {
        if (report == null)
        {
            _allGroups.Clear();
            _rows.Clear();
            _kpiGroupsValue.Text = "0";
            _kpiOccurrencesValue.Text = "0";
            _kpiRedundantLinesValue.Text = "0";
            _kpiExactClonesValue.Text = "0";
            _statsText.Text = LocalizationService.Instance["CodeClone_Placeholder"];
            return;
        }

        _allGroups = report.CloneGroups;
        _kpiGroupsValue.Text = report.TotalDuplicateGroups.ToString();
        _kpiOccurrencesValue.Text = report.TotalDuplicateOccurrences.ToString();
        _kpiRedundantLinesValue.Text = report.TotalRedundantLines.ToString();
        _kpiExactClonesValue.Text = report.ExactClonesCount.ToString();

        FilterData();
    }

    private void FilterData()
    {
        var filter = _searchBox.Text?.Trim() ?? string.Empty;

        var query = _allGroups.AsEnumerable();
        if (!string.IsNullOrEmpty(filter))
            query = query.Where(g => g.Occurrences.Any(o => o.ModuleName.Contains(filter, StringComparison.OrdinalIgnoreCase) || o.FilePath.Contains(filter, StringComparison.OrdinalIgnoreCase)));

        _rows.Clear();
        foreach (var g in query)
        {
            _rows.Add(new CodeCloneGridRow { Group = g });
        }

        _statsText.Text = LocalizationService.Instance.Get("CodeClone_StatsFormat", _rows.Count, _allGroups.Count);
    }

    public void ApplyTheme(ThemeDefinition theme) { }
}
