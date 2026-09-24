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
using Macondo.Semantic.Analytics.SocialCode;
using Macondo.Semantic.Model;

namespace Macondo.App.Controls;

public sealed class SocialCodeGridRow
{
    public required FileSocialReport Report { get; init; }

    public string FileName => Report.FileName;
    public string FilePath => Report.FilePath;
    public string PrimaryAuthor => Report.PrimaryAuthor;
    public double PrimaryOwnership => Report.PrimaryOwnership;
    public int TotalAuthors => Report.TotalAuthorsCount;
    public int BusFactor => Report.BusFactor;
    public double KnowledgeEntropy => Report.KnowledgeEntropy;
    public string RiskText => Report.Risk switch
    {
        BusFactorRisk.Orphan => "Codice Orfano",
        BusFactorRisk.High => "Rischio Alto (Bus = 1)",
        BusFactorRisk.Medium => "Rischio Medio",
        _ => "Basso Rischio"
    };

    public string RiskBadgeBg => Report.Risk switch
    {
        BusFactorRisk.Orphan => "#7C3AED",
        BusFactorRisk.High => "#EF4444",
        BusFactorRisk.Medium => "#F59E0B",
        _ => "#10B981"
    };
}

public sealed class SocialCodeControl : UserControl
{
    public static readonly StyledProperty<SocialCodeDataset?> DatasetProperty =
        AvaloniaProperty.Register<SocialCodeControl, SocialCodeDataset?>(nameof(Dataset));

    public SocialCodeDataset? Dataset
    {
        get => GetValue(DatasetProperty);
        set => SetValue(DatasetProperty, value);
    }

    private readonly ObservableCollection<SocialCodeGridRow> _rows = new();
    private List<FileSocialReport> _allFiles = new();

    private readonly TextBlock _kpiAvgBusFactorValue;
    private readonly TextBlock _kpiHighRiskValue;
    private readonly TextBlock _kpiOrphanFilesValue;
    private readonly TextBlock _kpiTotalAuthorsValue;

    private readonly TextBox _searchBox;
    private readonly DataGrid _grid;
    private readonly TextBlock _statsText;

    public SocialCodeControl()
    {
        var mainGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Margin = new Thickness(16)
        };

        // 1. KPI Cards Header
        var kpiPanel = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 12) };

        var card1 = CreateKpiCard("Bus Factor Medio", out _kpiAvgBusFactorValue, "#3B82F6");
        var card2 = CreateKpiCard("File ad Alto Rischio (BF = 1)", out _kpiHighRiskValue, "#EF4444");
        var card3 = CreateKpiCard("File Orfani (Autore Inattivo)", out _kpiOrphanFilesValue, "#8B5CF6");
        var card4 = CreateKpiCard("Autori Totali", out _kpiTotalAuthorsValue, "#10B981");

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
            Watermark = "Cerca per file o autore...",
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

        _grid.Columns.Add(new DataGridTextColumn { Header = "File", Binding = new Binding(nameof(SocialCodeGridRow.FileName)), Width = new DataGridLength(200) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Autore Principale", Binding = new Binding(nameof(SocialCodeGridRow.PrimaryAuthor)), Width = new DataGridLength(160) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Ownership %", Binding = new Binding(nameof(SocialCodeGridRow.PrimaryOwnership)), Width = new DataGridLength(110) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Bus Factor", Binding = new Binding(nameof(SocialCodeGridRow.BusFactor)), Width = new DataGridLength(90) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Rischio Conway", Binding = new Binding(nameof(SocialCodeGridRow.RiskText)), Width = new DataGridLength(160) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Autori Coinvolti", Binding = new Binding(nameof(SocialCodeGridRow.TotalAuthors)), Width = new DataGridLength(120) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Percorso File", Binding = new Binding(nameof(SocialCodeGridRow.FilePath)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });

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
            OnDatasetChanged(change.GetNewValue<SocialCodeDataset?>());
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

    private void OnDatasetChanged(SocialCodeDataset? dataset)
    {
        if (dataset == null)
        {
            _allFiles.Clear();
            _rows.Clear();
            _kpiAvgBusFactorValue.Text = "0.0";
            _kpiHighRiskValue.Text = "0";
            _kpiOrphanFilesValue.Text = "0";
            _kpiTotalAuthorsValue.Text = "0";
            _statsText.Text = "Nessun dato.";
            return;
        }

        _allFiles = dataset.Files;
        _kpiAvgBusFactorValue.Text = dataset.AverageBusFactor.ToString("F1");
        _kpiHighRiskValue.Text = dataset.HighRiskCount.ToString();
        _kpiOrphanFilesValue.Text = dataset.OrphanFilesCount.ToString();
        _kpiTotalAuthorsValue.Text = dataset.PrimaryOwnershipCountByAuthor.Count.ToString();

        FilterData();
    }

    private void FilterData()
    {
        var filter = _searchBox.Text?.Trim() ?? string.Empty;

        var query = _allFiles.AsEnumerable();
        if (!string.IsNullOrEmpty(filter))
            query = query.Where(f => f.FileName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                     f.PrimaryAuthor.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                     f.FilePath.Contains(filter, StringComparison.OrdinalIgnoreCase));

        _rows.Clear();
        foreach (var f in query)
        {
            _rows.Add(new SocialCodeGridRow { Report = f });
        }

        _statsText.Text = $"File visualizzati: {_rows.Count} / {_allFiles.Count}";
    }

    public void ApplyTheme(ThemeDefinition theme) { }
}
