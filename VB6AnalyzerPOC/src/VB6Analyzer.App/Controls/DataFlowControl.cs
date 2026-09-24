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
using Macondo.Semantic.Analytics.DataFlow;
using Macondo.Semantic.Model;

namespace Macondo.App.Controls;

public sealed class TaintPathGridRow
{
    public required TaintPath Path { get; init; }

    public string SourceName => Path.SourceSymbol.Name;
    public string SourceKindText => Path.SourceKind.ToString();
    public string SinkName => Path.SinkSymbol.Name;
    public string SinkKindText => Path.SinkKind.ToString();
    public int HopCount => Path.HopCount;
    public string PropagationChainText => string.Join(" → ", Path.PropagationChain.Select(s => s.Name));
    public string Description => Path.Description;

    public string SinkBadgeBg => Path.SinkKind switch
    {
        TaintSinkKind.SqlExecution => "#EF4444",
        TaintSinkKind.ShellOrProcessExecution => "#DC2626",
        TaintSinkKind.FileSystemWrite => "#F59E0B",
        _ => "#8B5CF6"
    };
}

public sealed class DataFlowControl : UserControl
{
    public static readonly StyledProperty<DataFlowDataset?> DatasetProperty =
        AvaloniaProperty.Register<DataFlowControl, DataFlowDataset?>(nameof(Dataset));

    public DataFlowDataset? Dataset
    {
        get => GetValue(DatasetProperty);
        set => SetValue(DatasetProperty, value);
    }

    private readonly ObservableCollection<TaintPathGridRow> _rows = new();
    private List<TaintPath> _allPaths = new();

    private readonly TextBlock _kpiTotalPathsValue;
    private readonly TextBlock _kpiSqlRisksValue;
    private readonly TextBlock _kpiCommandRisksValue;
    private readonly TextBlock _kpiDeadStoresValue;

    private readonly TextBox _searchBox;
    private readonly DataGrid _grid;
    private readonly TextBlock _statsText;

    public DataFlowControl()
    {
        var mainGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Margin = new Thickness(16)
        };

        // 1. KPI Cards Header
        var kpiPanel = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 12) };

        var card1 = CreateKpiCard("Percorsi Taint (Source-Sink)", out _kpiTotalPathsValue, "#3B82F6");
        var card2 = CreateKpiCard("Rischi Query SQL", out _kpiSqlRisksValue, "#EF4444");
        var card3 = CreateKpiCard("Rischi Shell / Command", out _kpiCommandRisksValue, "#DC2626");
        var card4 = CreateKpiCard("Dead Stores (Def-Use)", out _kpiDeadStoresValue, "#F59E0B");

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
            Watermark = "Cerca sorgente, destinazione o percorso...",
            Width = 300,
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

        _grid.Columns.Add(new DataGridTextColumn { Header = "Sorgente Non Controllata", Binding = new Binding(nameof(TaintPathGridRow.SourceName)), Width = new DataGridLength(180) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Tipo Sorgente", Binding = new Binding(nameof(TaintPathGridRow.SourceKindText)), Width = new DataGridLength(120) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Punto Critico (Sink)", Binding = new Binding(nameof(TaintPathGridRow.SinkName)), Width = new DataGridLength(180) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Tipo Sink", Binding = new Binding(nameof(TaintPathGridRow.SinkKindText)), Width = new DataGridLength(130) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Salti", Binding = new Binding(nameof(TaintPathGridRow.HopCount)), Width = new DataGridLength(70) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Catena di Propagazione", Binding = new Binding(nameof(TaintPathGridRow.PropagationChainText)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });

        Grid.SetRow(_grid, 2);
        mainGrid.Children.Add(_grid);

        // 4. Footer stats
        _statsText = new TextBlock
        {
            Text = "Nessun dato caricato.",
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
        if (change.Property == DatasetProperty)
        {
            OnDatasetChanged(change.GetNewValue<DataFlowDataset?>());
        }
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

    private void OnDatasetChanged(DataFlowDataset? dataset)
    {
        if (dataset == null)
        {
            _allPaths.Clear();
            _rows.Clear();
            _kpiTotalPathsValue.Text = "0";
            _kpiSqlRisksValue.Text = "0";
            _kpiCommandRisksValue.Text = "0";
            _kpiDeadStoresValue.Text = "0";
            _statsText.Text = "Nessun dato.";
            return;
        }

        _allPaths = dataset.TaintPaths;
        _kpiTotalPathsValue.Text = dataset.TotalTaintPaths.ToString();
        _kpiSqlRisksValue.Text = dataset.SqlInjectionRisks.ToString();
        _kpiCommandRisksValue.Text = dataset.CommandExecutionRisks.ToString();
        _kpiDeadStoresValue.Text = dataset.TotalDeadStores.ToString();

        FilterData();
    }

    private void FilterData()
    {
        var filter = _searchBox.Text?.Trim() ?? string.Empty;

        var query = _allPaths.AsEnumerable();
        if (!string.IsNullOrEmpty(filter))
            query = query.Where(p => p.SourceSymbol.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                     p.SinkSymbol.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                     p.PropagationChain.Any(s => s.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)));

        _rows.Clear();
        foreach (var p in query)
        {
            _rows.Add(new TaintPathGridRow { Path = p });
        }

        _statsText.Text = $"Percorsi visualizzati: {_rows.Count} / {_allPaths.Count}";
    }

    public void ApplyTheme(ThemeDefinition theme) { }
}
