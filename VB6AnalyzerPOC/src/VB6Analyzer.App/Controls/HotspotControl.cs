using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Macondo.App.Models;
using Macondo.App.Services;
using Macondo.Semantic.Analytics.Hotspots;
using Macondo.Semantic.Model;

namespace Macondo.App.Controls;

/// <summary>
/// Controllo custom Avalonia Skia per la matrice di rischio Hotspot (Complessità vs Churn / Fan-In).
/// Visualizza un grafico cartesiano a 4 quadranti con bubble chart interattivo.
/// </summary>
public sealed class HotspotControl : Control
{
    public static readonly StyledProperty<HotspotDataset?> DatasetProperty =
        AvaloniaProperty.Register<HotspotControl, HotspotDataset?>(nameof(Dataset));

    public HotspotDataset? Dataset
    {
        get => GetValue(DatasetProperty);
        set => SetValue(DatasetProperty, value);
    }

    public event EventHandler<HotspotItem?>? ItemSelected;

    private HotspotItem? _hoveredItem;
    private HotspotItem? _selectedItem;

    private static readonly Typeface TitleTypeface = new("Inter", FontStyle.Normal, FontWeight.Bold);
    private static readonly Typeface LabelTypeface = new("Inter", FontStyle.Normal, FontWeight.Medium);
    private static readonly Typeface AxisTypeface = new("Inter", FontStyle.Normal, FontWeight.SemiBold);

    private IBrush _backgroundBrush = new SolidColorBrush(Color.Parse("#14141E"));
    private IBrush _axisPenBrush = new SolidColorBrush(Color.Parse("#404058"));
    private IBrush _thresholdPenBrush = new SolidColorBrush(Color.Parse("#707090"));
    private IBrush _safeQuadrantBrush = new SolidColorBrush(Color.FromArgb(18, 40, 180, 100));
    private IBrush _dangerQuadrantBrush = new SolidColorBrush(Color.FromArgb(28, 255, 60, 80));
    private IBrush _amberQuadrantBrush = new SolidColorBrush(Color.FromArgb(18, 255, 180, 30));
    private IBrush _blueQuadrantBrush = new SolidColorBrush(Color.FromArgb(18, 60, 160, 255));
    private IBrush _textMutedBrush = new SolidColorBrush(Color.Parse("#9090A5"));
    private IBrush _textWhiteBrush = Brushes.WhiteSmoke;
    private IBrush _cardBgBrush = new SolidColorBrush(Color.FromArgb(240, 24, 24, 34));
    private IBrush _cardAccentTextBrush = new SolidColorBrush(Color.Parse("#4AE8D0"));

    static HotspotControl()
    {
        AffectsRender<HotspotControl>(DatasetProperty);
        FocusableProperty.OverrideDefaultValue<HotspotControl>(true);
    }

    public HotspotControl()
    {
        ClipToBounds = true;
    }

    public void ApplyTheme(ThemeDefinition theme)
    {
        if (Color.TryParse(theme.GraphBackground, out var bgCol))
            _backgroundBrush = new SolidColorBrush(bgCol);

        if (Color.TryParse(theme.BorderDefault, out var borderCol))
            _axisPenBrush = new SolidColorBrush(borderCol);

        if (Color.TryParse(theme.BorderSubtle, out var borderSubtleCol))
            _thresholdPenBrush = new SolidColorBrush(borderSubtleCol);

        if (Color.TryParse(theme.TextPrimary, out var textPrimaryCol))
            _textWhiteBrush = new SolidColorBrush(textPrimaryCol);

        if (Color.TryParse(theme.TextMuted, out var textMutedCol))
            _textMutedBrush = new SolidColorBrush(textMutedCol);

        if (Color.TryParse(theme.BackgroundElevated, out var elevatedCol))
            _cardBgBrush = new SolidColorBrush(Color.FromArgb(240, elevatedCol.R, elevatedCol.G, elevatedCol.B));

        if (Color.TryParse(theme.AccentColor, out var accentCol))
            _cardAccentTextBrush = new SolidColorBrush(accentCol);

        if (theme.IsDark)
        {
            _safeQuadrantBrush = new SolidColorBrush(Color.FromArgb(18, 40, 180, 100));
            _dangerQuadrantBrush = new SolidColorBrush(Color.FromArgb(28, 255, 60, 80));
            _amberQuadrantBrush = new SolidColorBrush(Color.FromArgb(18, 255, 180, 30));
            _blueQuadrantBrush = new SolidColorBrush(Color.FromArgb(18, 60, 160, 255));
        }
        else
        {
            _safeQuadrantBrush = new SolidColorBrush(Color.FromArgb(30, 40, 180, 100));
            _dangerQuadrantBrush = new SolidColorBrush(Color.FromArgb(35, 255, 60, 80));
            _amberQuadrantBrush = new SolidColorBrush(Color.FromArgb(30, 255, 180, 30));
            _blueQuadrantBrush = new SolidColorBrush(Color.FromArgb(30, 60, 160, 255));
        }

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(_backgroundBrush, new Rect(Bounds.Size));

        var loc = LocalizationService.Instance;
        var dataset = Dataset;
        if (dataset is null || dataset.Items.Count == 0)
        {
            var placeholder = new FormattedText(
                loc["Hotspot_Placeholder"],
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                14,
                Brushes.Gray);
            context.DrawText(placeholder, new Point(20, 20));
            return;
        }

        double marginL = 70.0;
        double marginR = 40.0;
        double marginT = 40.0;
        double marginB = 60.0;

        double plotW = Math.Max(100.0, Bounds.Width - marginL - marginR);
        double plotH = Math.Max(100.0, Bounds.Height - marginT - marginB);

        double maxX = Math.Max(1.0, dataset.MaxX);
        double maxY = Math.Max(1.0, dataset.MaxY);

        double splitX = marginL + (dataset.ThresholdX / maxX) * plotW;
        double splitY = marginT + plotH - (dataset.ThresholdY / maxY) * plotH;

        // 1. Quadranti con colori di sfondo
        // Basso-Sinistra: SAFE
        context.FillRectangle(_safeQuadrantBrush, new Rect(marginL, splitY, splitX - marginL, marginT + plotH - splitY));
        // Alto-Sinistra: Low Churn / High Complexity
        context.FillRectangle(_amberQuadrantBrush, new Rect(marginL, marginT, splitX - marginL, splitY - marginT));
        // Basso-Destra: High Churn / Low Complexity
        context.FillRectangle(_blueQuadrantBrush, new Rect(splitX, splitY, marginL + plotW - splitX, marginT + plotH - splitY));
        // Alto-Destra: DANGER ZONE / HOTSPOT
        context.FillRectangle(_dangerQuadrantBrush, new Rect(splitX, marginT, marginL + plotW - splitX, splitY - marginT));

        // Etichette dei quadranti
        var dangerBadge = new FormattedText(loc["Hotspot_DangerBadge"], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, TitleTypeface, 11, new SolidColorBrush(Color.Parse("#FF5566")));
        context.DrawText(dangerBadge, new Point(marginL + plotW - dangerBadge.Width - 10, marginT + 10));

        var safeBadge = new FormattedText(loc["Hotspot_SafeBadge"], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, TitleTypeface, 11, new SolidColorBrush(Color.Parse("#40C070")));
        context.DrawText(safeBadge, new Point(marginL + 10, marginT + plotH - 24));

        // 2. Linee Soglia e Assi
        var thresholdPen = new Pen(_thresholdPenBrush, 1.2, DashStyle.Dash);
        context.DrawLine(thresholdPen, new Point(splitX, marginT), new Point(splitX, marginT + plotH));
        context.DrawLine(thresholdPen, new Point(marginL, splitY), new Point(marginL + plotW, splitY));

        var axisPen = new Pen(_axisPenBrush, 1.5);
        context.DrawLine(axisPen, new Point(marginL, marginT), new Point(marginL, marginT + plotH));
        context.DrawLine(axisPen, new Point(marginL, marginT + plotH), new Point(marginL + plotW, marginT + plotH));

        // Titoli Assi
        var axisYFmt = new FormattedText(loc["Hotspot_AxisY"], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, AxisTypeface, 10, _textMutedBrush);
        context.DrawText(axisYFmt, new Point(marginL, 16));

        var axisXFmt = new FormattedText(loc["Hotspot_AxisX"], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, AxisTypeface, 10, _textMutedBrush);
        context.DrawText(axisXFmt, new Point(marginL + plotW - axisXFmt.Width, marginT + plotH + 30));

        // 3. Disegno Bolle / Punti
        foreach (var item in dataset.Items)
        {
            double px = marginL + (item.ChurnOrFanIn / maxX) * plotW;
            double py = marginT + plotH - (item.Complexity / maxY) * plotH;
            double radius = Math.Clamp(item.SizeMetric * 0.6, 5.0, 22.0);

            var bubbleBrush = GetRiskBrush(item.RiskScore);
            var center = new Point(px, py);

            context.DrawEllipse(bubbleBrush, null, center, radius, radius);

            if (_selectedItem?.Id == item.Id)
            {
                context.DrawEllipse(null, new Pen(Brushes.Gold, 2.5), center, radius + 3, radius + 3);
            }
            else if (_hoveredItem?.Id == item.Id)
            {
                context.DrawEllipse(null, new Pen(Brushes.White, 2.0), center, radius + 2, radius + 2);
            }
            else
            {
                context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 0.8), center, radius, radius);
            }

            // Testo etichetta per nodi di grande rilievo
            if (radius > 12.0 || item.Quadrant == HotspotQuadrant.DangerZone)
            {
                string shortName = item.Name.Length > 12 ? item.Name[..10] + "…" : item.Name;
                var lblFmt = new FormattedText(shortName, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelTypeface, 9, _textWhiteBrush);
                context.DrawText(lblFmt, new Point(px - lblFmt.Width / 2.0, py + radius + 2));
            }
        }

        // 4. Card di Dettaglio Hovered
        if (_hoveredItem != null)
        {
            DrawHoverCard(context, _hoveredItem);
        }
    }

    /// <summary>
    /// Restituisce il pennello di riempimento associato allo score di rischio composito:
    /// >= 75: Rosso (Critico / High Danger)
    /// >= 50: Arancione (Alto)
    /// >= 25: Blu (Medio)
    /// &lt; 25: Verde (Basso / Safe)
    /// </summary>
    private static IBrush GetRiskBrush(double riskScore)
    {
        if (riskScore >= 75) return new SolidColorBrush(Color.FromArgb(220, 240, 50, 60));
        if (riskScore >= 50) return new SolidColorBrush(Color.FromArgb(220, 245, 140, 30));
        if (riskScore >= 25) return new SolidColorBrush(Color.FromArgb(220, 70, 160, 240));
        return new SolidColorBrush(Color.FromArgb(220, 50, 190, 110));
    }

    /// <summary>
    /// Disegna la scheda informativa fluttuante (Hover Card) contenente il nome qualificato dell'elemento,
    /// lo score di rischio, la complessità ciclomatica, il churn/fan-in e il numero di chiamanti diretti.
    /// </summary>
    private void DrawHoverCard(DrawingContext context, HotspotItem item)
    {
        var titleFmt = new FormattedText(
            $"{item.Kind}: {item.QualifiedName}",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            TitleTypeface,
            12,
            _textWhiteBrush);

        var loc = LocalizationService.Instance;
        var subFmt = new FormattedText(
            loc.Get("Hotspot_HoverSub", item.RiskScore, loc.GetRiskLevel(item.RiskLevel), item.Complexity, item.ChurnOrFanIn, item.DirectCallersCount),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            11,
            _cardAccentTextBrush);

        const double paddingH = 12;
        const double paddingV = 8;
        const double lineSpacing = 4;

        double maxTextWidth = Math.Max(titleFmt.Width, subFmt.Width);
        double maxWidthAvailable = Math.Max(100, Bounds.Width - 96);
        double cardWidth = Math.Min(Math.Max(360, maxTextWidth + (paddingH * 2)), maxWidthAvailable);

        double contentWidth = Math.Max(10, cardWidth - (paddingH * 2));
        titleFmt.MaxTextWidth = contentWidth;
        titleFmt.Trimming = TextTrimming.CharacterEllipsis;

        subFmt.MaxTextWidth = contentWidth;
        subFmt.Trimming = TextTrimming.CharacterEllipsis;

        double cardHeight = Math.Max(54, paddingV + titleFmt.Height + lineSpacing + subFmt.Height + paddingV);
        var cardRect = new Rect(80, Bounds.Height - cardHeight - 12, cardWidth, cardHeight);

        context.FillRectangle(_cardBgBrush, cardRect);
        context.DrawRectangle(new Pen(_axisPenBrush, 1), cardRect);

        context.DrawText(titleFmt, new Point(cardRect.X + paddingH, cardRect.Y + paddingV));
        context.DrawText(subFmt, new Point(cardRect.X + paddingH, cardRect.Y + paddingV + titleFmt.Height + lineSpacing));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        var hit = FindItemAt(pos.X, pos.Y);
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
        var hit = FindItemAt(pos.X, pos.Y);
        if (hit != null)
        {
            _selectedItem = hit;
            ItemSelected?.Invoke(this, hit);
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Esegue l'hit-testing cartesiano per individuare la bolla Hotspot sotto le coordinate del puntatore (px, py),
    /// calcolando la distanza radiale rispetto al centro di ciascun punto proiettato.
    /// </summary>
    private HotspotItem? FindItemAt(double px, double py)
    {
        if (Dataset == null || Dataset.Items.Count == 0) return null;

        double marginL = 70.0;
        double marginR = 40.0;
        double marginT = 40.0;
        double marginB = 60.0;

        double plotW = Math.Max(100.0, Bounds.Width - marginL - marginR);
        double plotH = Math.Max(100.0, Bounds.Height - marginT - marginB);

        double maxX = Math.Max(1.0, Dataset.MaxX);
        double maxY = Math.Max(1.0, Dataset.MaxY);

        foreach (var item in Dataset.Items)
        {
            double cx = marginL + (item.ChurnOrFanIn / maxX) * plotW;
            double cy = marginT + plotH - (item.Complexity / maxY) * plotH;
            double radius = Math.Clamp(item.SizeMetric * 0.6, 5.0, 22.0) + 4.0;

            double dx = px - cx;
            double dy = py - cy;
            if (dx * dx + dy * dy <= radius * radius)
            {
                return item;
            }
        }

        return null;
    }
}
