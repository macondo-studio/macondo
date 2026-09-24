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
using Macondo.Semantic.Analytics.ArchitecturalRules;
using Macondo.Semantic.Model;

namespace Macondo.App.Controls;

public sealed class RuleViolationGridRow
{
    public required RuleViolation Violation { get; init; }

    public string RuleName => Violation.Rule.Name;
    public string SeverityText => Violation.Severity.ToString();
    public string SourceName => Violation.SourceSymbol.Name;
    public string TargetName => Violation.TargetSymbol.Name;
    public string Relationship => Violation.Relationship.Kind.ToString();
    public string Message => Violation.Message;

    public string SeverityBadgeBg => Violation.Severity switch
    {
        RuleSeverity.Critical => "#B81D2D",
        RuleSeverity.Error => "#C86400",
        RuleSeverity.Warning => "#D97706",
        _ => "#2B6CB0"
    };
}

public sealed class ArchitecturalRulesControl : UserControl
{
    public static readonly StyledProperty<ArchitecturalRulesReport?> ReportProperty =
        AvaloniaProperty.Register<ArchitecturalRulesControl, ArchitecturalRulesReport?>(nameof(Report));

    public ArchitecturalRulesReport? Report
    {
        get => GetValue(ReportProperty);
        set => SetValue(ReportProperty, value);
    }

    private readonly ObservableCollection<RuleViolationGridRow> _rows = new();
    private List<RuleViolation> _allViolations = new();

    private readonly TextBlock _kpiRulesValue;
    private readonly TextBlock _kpiViolationsValue;
    private readonly TextBlock _kpiCriticalValue;
    private readonly TextBlock _kpiErrorValue;

    private readonly TextBox _searchBox;
    private readonly DataGrid _grid;
    private readonly TextBlock _statsText;

    public ArchitecturalRulesControl()
    {
        var mainGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Margin = new Thickness(16)
        };

        // 1. KPI Cards Header
        var kpiPanel = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 12) };

        var card1 = CreateKpiCard("Regole Valutate", out _kpiRulesValue, "#3B82F6");
        var card2 = CreateKpiCard("Violazioni Totali", out _kpiViolationsValue, "#EF4444");
        var card3 = CreateKpiCard("Violazioni Critiche", out _kpiCriticalValue, "#DC2626");
        var card4 = CreateKpiCard("Errori di Layering", out _kpiErrorValue, "#F59E0B");

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
            Watermark = "Cerca violazione per componente o messaggio...",
            Width = 320,
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

        _grid.Columns.Add(new DataGridTextColumn { Header = "Regola", Binding = new Binding(nameof(RuleViolationGridRow.RuleName)), Width = new DataGridLength(200) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Severità", Binding = new Binding(nameof(RuleViolationGridRow.SeverityText)), Width = new DataGridLength(90) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Componente Sorgente", Binding = new Binding(nameof(RuleViolationGridRow.SourceName)), Width = new DataGridLength(180) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Target Proibito", Binding = new Binding(nameof(RuleViolationGridRow.TargetName)), Width = new DataGridLength(180) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Relazione", Binding = new Binding(nameof(RuleViolationGridRow.Relationship)), Width = new DataGridLength(110) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Messaggio di Violazione", Binding = new Binding(nameof(RuleViolationGridRow.Message)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });

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
        if (change.Property == ReportProperty)
        {
            OnReportChanged(change.GetNewValue<ArchitecturalRulesReport?>());
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

    private void OnReportChanged(ArchitecturalRulesReport? report)
    {
        if (report == null)
        {
            _allViolations.Clear();
            _rows.Clear();
            _kpiRulesValue.Text = "0";
            _kpiViolationsValue.Text = "0";
            _kpiCriticalValue.Text = "0";
            _kpiErrorValue.Text = "0";
            _statsText.Text = "Nessun dato.";
            return;
        }

        _allViolations = report.Violations;
        _kpiRulesValue.Text = report.TotalRulesEvaluated.ToString();
        _kpiViolationsValue.Text = report.TotalViolations.ToString();
        _kpiCriticalValue.Text = report.CriticalViolations.ToString();
        _kpiErrorValue.Text = report.ErrorViolations.ToString();

        FilterData();
    }

    private void FilterData()
    {
        var filter = _searchBox.Text?.Trim() ?? string.Empty;

        var query = _allViolations.AsEnumerable();
        if (!string.IsNullOrEmpty(filter))
            query = query.Where(v => v.Rule.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                     v.SourceSymbol.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                     v.TargetSymbol.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                     v.Message.Contains(filter, StringComparison.OrdinalIgnoreCase));

        _rows.Clear();
        foreach (var v in query)
        {
            _rows.Add(new RuleViolationGridRow { Violation = v });
        }

        _statsText.Text = $"Violazioni visualizzate: {_rows.Count} / {_allViolations.Count}";
    }

    public void ApplyTheme(ThemeDefinition theme) { }
}
