using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Macondo.App.Services;
using Macondo.Semantic.Analytics.Treemap;
using Macondo.Semantic.Model;

namespace Macondo.App.Controls;

/// <summary>
/// Controllo custom Avalonia Skia ad alte prestazioni per il rendering e l'interazione con la Treemap Gerarchica di metriche.
/// Supporta layout Squarified, color coding per complessità/rischio, hover, selezione e drill-down.
/// </summary>
public sealed class TreemapControl : Control
{
    public static readonly StyledProperty<TreemapItem?> RootItemProperty =
        AvaloniaProperty.Register<TreemapControl, TreemapItem?>(nameof(RootItem));

    public TreemapItem? RootItem
    {
        get => GetValue(RootItemProperty);
        set => SetValue(RootItemProperty, value);
    }

    public event EventHandler<TreemapItem?>? ItemSelected;

    private TreemapItem? _currentRoot;
    private TreemapItem? _hoveredItem;
    private TreemapItem? _selectedItem;

    private static readonly Typeface HeaderTypeface = new("Inter", FontStyle.Normal, FontWeight.Bold);
    private static readonly Typeface LabelTypeface = new("Inter", FontStyle.Normal, FontWeight.Medium);
    private static readonly Typeface SubLabelTypeface = new("Inter", FontStyle.Normal, FontWeight.Normal);

    private IBrush _backgroundBrush = new SolidColorBrush(Color.Parse("#14141E"));
    private IBrush _containerHeaderBrush = new SolidColorBrush(Color.Parse("#202030"));
    private IBrush _containerBorderBrush = new SolidColorBrush(Color.Parse("#35354A"));
    private IBrush _selectedBorderBrush = new SolidColorBrush(Color.Parse("#FFD700"));
    private IBrush _hoverBorderBrush = new SolidColorBrush(Color.Parse("#FFFFFF"));
    private IBrush _textWhiteBrush = Brushes.WhiteSmoke;
    private IBrush _textMutedBrush = new SolidColorBrush(Color.Parse("#A0A0B0"));
    private IBrush _hoverCardBgBrush = new SolidColorBrush(Color.FromArgb(235, 30, 30, 42));
    private IBrush _accentTextBrush = new SolidColorBrush(Color.Parse("#4AE8D0"));

    static TreemapControl()
    {
        AffectsRender<TreemapControl>(RootItemProperty);
        FocusableProperty.OverrideDefaultValue<TreemapControl>(true);
    }

    public TreemapControl()
    {
        ClipToBounds = true;
        DoubleTapped += OnDoubleTapped;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RootItemProperty)
        {
            _currentRoot = RootItem;
            _hoveredItem = null;
            _selectedItem = null;
            InvalidateLayoutAndVisual();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        InvalidateLayoutAndVisual();
    }

    public void DrillUp()
    {
        if (_currentRoot?.Parent != null)
        {
            _currentRoot = _currentRoot.Parent;
            InvalidateLayoutAndVisual();
        }
    }

    public void ResetDrillDown()
    {
        _currentRoot = RootItem;
        InvalidateLayoutAndVisual();
    }

    private void InvalidateLayoutAndVisual()
    {
        if (_currentRoot != null && Bounds.Width > 50 && Bounds.Height > 50)
        {
            TreemapLayoutEngine.Layout(_currentRoot, new TreemapRect(12, 36, Bounds.Width - 24, Bounds.Height - 48));
        }
        InvalidateVisual();
    }

    public void ApplyTheme(ThemeDefinition theme)
    {
        if (Color.TryParse(theme.GraphBackground, out var bgCol))
            _backgroundBrush = new SolidColorBrush(bgCol);

        if (Color.TryParse(theme.BackgroundElevated, out var elevatedCol))
        {
            _containerHeaderBrush = new SolidColorBrush(elevatedCol);
            _hoverCardBgBrush = new SolidColorBrush(Color.FromArgb(240, elevatedCol.R, elevatedCol.G, elevatedCol.B));
        }

        if (Color.TryParse(theme.BorderDefault, out var borderCol))
            _containerBorderBrush = new SolidColorBrush(borderCol);

        if (Color.TryParse(theme.TextPrimary, out var textPrimaryCol))
            _textWhiteBrush = new SolidColorBrush(textPrimaryCol);

        if (Color.TryParse(theme.TextMuted, out var textMutedCol))
            _textMutedBrush = new SolidColorBrush(textMutedCol);

        if (Color.TryParse(theme.AccentColor, out var accentCol))
            _accentTextBrush = new SolidColorBrush(accentCol);

        _hoverBorderBrush = theme.IsDark ? Brushes.White : new SolidColorBrush(Color.Parse("#1A1A24"));

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(_backgroundBrush, new Rect(Bounds.Size));

        var root = _currentRoot ?? RootItem;
        if (root is null || root.Children.Count == 0)
        {
            var placeholder = new FormattedText(
                "Nessuna Treemap calcolata. Carica un progetto per visualizzare la mappa delle metriche.",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                14,
                Brushes.Gray);
            context.DrawText(placeholder, new Point(20, 20));
            return;
        }

        // Header / Breadcrumb superiore
        string breadcrumb = _currentRoot?.Parent != null ? $"⬅ Torna su | Vista: {_currentRoot.Name} ({_currentRoot.Kind})" : "Tutti i Moduli del Progetto (Grandezza = LOC / Dimensione | Colore = Complessità)";
        var breadcrumbFmt = new FormattedText(
            breadcrumb,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            HeaderTypeface,
            12,
            _currentRoot?.Parent != null ? _accentTextBrush : _textMutedBrush);
        context.DrawText(breadcrumbFmt, new Point(16, 12));

        // Renderizza ricorsivamente tutti i nodi
        RenderItemRecursive(context, root);

        // Disegna tooltip/card flottante in basso a sinistra per l'elemento hovered
        if (_hoveredItem != null && _hoveredItem != root)
        {
            DrawHoverCard(context, _hoveredItem);
        }
    }

    private void RenderItemRecursive(DrawingContext context, TreemapItem item)
    {
        if (item.Bounds.Width <= 2 || item.Bounds.Height <= 2) return;

        var rect = new Rect(item.Bounds.X, item.Bounds.Y, item.Bounds.Width, item.Bounds.Height);

        if (!item.IsLeaf)
        {
            // Container (Modulo / Classe)
            if (item.Depth > 0)
            {
                context.FillRectangle(_containerHeaderBrush, rect);
                context.DrawRectangle(new Pen(_containerBorderBrush, 1), rect);

                // Titolo del modulo
                if (rect.Width > 50 && rect.Height > 18)
                {
                    string header = $"{item.Name} ({item.Children.Count} membri)";
                    var hFmt = new FormattedText(
                        header,
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        HeaderTypeface,
                        10,
                        _textWhiteBrush);
                    context.DrawText(hFmt, new Point(rect.X + 6, rect.Y + 3));
                }
            }

            foreach (var child in item.Children)
            {
                RenderItemRecursive(context, child);
            }
        }
        else
        {
            // Foglia (Metodo, Proprietà o Modulo foglia)
            var fillBrush = GetComplexityBrush(item.MetricScore);
            context.FillRectangle(fillBrush, rect);

            if (_selectedItem?.Id == item.Id)
            {
                context.DrawRectangle(new Pen(_selectedBorderBrush, 2.5), rect);
            }
            else if (_hoveredItem?.Id == item.Id)
            {
                context.DrawRectangle(new Pen(_hoverBorderBrush, 1.5), rect);
            }
            else
            {
                context.DrawRectangle(new Pen(new SolidColorBrush(Color.FromArgb(50, 0, 0, 0)), 1), rect);
            }

            // Testo etichetta se lo spazio è sufficiente
            if (rect.Width > 45 && rect.Height > 22)
            {
                string label = item.Name;
                if (rect.Width < 80 && label.Length > 8) label = label[..7] + "…";

                var nameFmt = new FormattedText(
                    label,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    LabelTypeface,
                    rect.Width > 90 ? 11 : 9,
                    _textWhiteBrush);
                context.DrawText(nameFmt, new Point(rect.X + 4, rect.Y + 3));

                if (rect.Height > 38 && rect.Width > 60)
                {
                    var valFmt = new FormattedText(
                        $"CC: {item.MetricScore:F1}",
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        SubLabelTypeface,
                        9,
                        new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)));
                    context.DrawText(valFmt, new Point(rect.X + 4, rect.Y + nameFmt.Height + 2));
                }
            }
        }
    }

    private static IBrush GetComplexityBrush(double score)
    {
        if (score <= 2.0)
            return new SolidColorBrush(Color.Parse("#164E3D")); // Verde scuro / Bassa complessità
        if (score <= 4.5)
            return new SolidColorBrush(Color.Parse("#1D6B52")); // Verde medio
        if (score <= 7.0)
            return new SolidColorBrush(Color.Parse("#8C6B14")); // Giallo ambra / Media
        if (score <= 11.0)
            return new SolidColorBrush(Color.Parse("#A84B1A")); // Arancione / Alta
        return new SolidColorBrush(Color.Parse("#961C2C")); // Rosso / Altissima complessità
    }

    private void DrawHoverCard(DrawingContext context, TreemapItem item)
    {
        var cardRect = new Rect(16, Bounds.Height - 65, 320, 52);
        context.FillRectangle(_hoverCardBgBrush, cardRect);
        context.DrawRectangle(new Pen(_containerBorderBrush, 1), cardRect);

        var titleFmt = new FormattedText(
            $"{item.Kind}: {item.QualifiedName}",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            HeaderTypeface,
            12,
            _textWhiteBrush);
        context.DrawText(titleFmt, new Point(cardRect.X + 10, cardRect.Y + 8));

        var subFmt = new FormattedText(
            $"Complessità Ciclomatica: {item.MetricScore:F1} | Dimensione: {item.Value:F0} LOC",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            11,
            _accentTextBrush);
        context.DrawText(subFmt, new Point(cardRect.X + 10, cardRect.Y + 28));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);

        var hit = FindItemAt(_currentRoot ?? RootItem, pos.X, pos.Y);
        if (_hoveredItem != hit)
        {
            _hoveredItem = hit;
            InvalidateVisual();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pos = e.GetPosition(this);

        if (pos.Y < 30 && _currentRoot?.Parent != null)
        {
            DrillUp();
            return;
        }

        var hit = FindItemAt(_currentRoot ?? RootItem, pos.X, pos.Y);
        if (hit != null)
        {
            _selectedItem = hit;
            ItemSelected?.Invoke(this, hit);
            InvalidateVisual();
        }
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        var pos = e.GetPosition(this);
        var hit = FindItemAt(_currentRoot ?? RootItem, pos.X, pos.Y);

        if (hit != null && !hit.IsLeaf)
        {
            _currentRoot = hit;
            InvalidateLayoutAndVisual();
        }
    }

    private TreemapItem? FindItemAt(TreemapItem? current, double px, double py)
    {
        if (current == null || !current.Bounds.Contains(px, py)) return null;

        // Cerca prima nei figli
        foreach (var child in current.Children)
        {
            var found = FindItemAt(child, px, py);
            if (found != null) return found;
        }

        return current;
    }
}
