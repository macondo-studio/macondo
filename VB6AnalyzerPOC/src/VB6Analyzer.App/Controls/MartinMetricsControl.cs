using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Macondo.App.Models;
using Macondo.App.Services;
using Macondo.Semantic.Analytics.Martin;
using Macondo.Semantic.Model;

namespace Macondo.App.Controls;

/// <summary>
/// Controllo visuale interattivo per il Diagramma di Martin (Robert C. Martin).
/// Mappa l'Astrattezza (A, asse Y [0..1]) rispetto all'Instabilità (I, asse X [0..1]),
/// evidenziando la Sequenza Principale (A + I = 1), la Zona di Sofferenza e la Zona di Inutilità.
/// </summary>
public sealed class MartinMetricsControl : Control
{
    public static readonly StyledProperty<MartinMetricsDataset?> DatasetProperty =
        AvaloniaProperty.Register<MartinMetricsControl, MartinMetricsDataset?>(nameof(Dataset));

    public MartinMetricsDataset? Dataset
    {
        get => GetValue(DatasetProperty);
        set => SetValue(DatasetProperty, value);
    }

    public event EventHandler<MartinMetricItem?>? ItemSelected;

    private MartinMetricItem? _hoveredItem;
    private MartinMetricItem? _selectedItem;

    private static readonly Typeface TitleTypeface = new("Inter", FontStyle.Normal, FontWeight.Bold);
    private static readonly Typeface LabelTypeface = new("Inter", FontStyle.Normal, FontWeight.Medium);
    private static readonly Typeface AxisTypeface = new("Inter", FontStyle.Normal, FontWeight.SemiBold);

    private IBrush _backgroundBrush = new SolidColorBrush(Color.Parse("#14141E"));
    private IBrush _axisPenBrush = new SolidColorBrush(Color.Parse("#404058"));
    private IBrush _mainSeqPenBrush = new SolidColorBrush(Color.Parse("#4AE8D0"));
    private IBrush _painQuadrantBrush = new SolidColorBrush(Color.FromArgb(28, 255, 60, 80));
    private IBrush _uselessQuadrantBrush = new SolidColorBrush(Color.FromArgb(22, 255, 180, 30));
    private IBrush _mainSeqBandBrush = new SolidColorBrush(Color.FromArgb(16, 74, 232, 208));
    private IBrush _textMutedBrush = new SolidColorBrush(Color.Parse("#9090A5"));
    private IBrush _textWhiteBrush = Brushes.WhiteSmoke;
    private IBrush _cardBgBrush = new SolidColorBrush(Color.FromArgb(240, 24, 24, 34));
    private IBrush _cardAccentTextBrush = new SolidColorBrush(Color.Parse("#4AE8D0"));

    static MartinMetricsControl()
    {
        AffectsRender<MartinMetricsControl>(DatasetProperty);
        FocusableProperty.OverrideDefaultValue<MartinMetricsControl>(true);
    }

    public MartinMetricsControl()
    {
        ClipToBounds = true;
    }

    public void ApplyTheme(ThemeDefinition theme)
    {
        if (Color.TryParse(theme.GraphBackground, out var bgCol))
            _backgroundBrush = new SolidColorBrush(bgCol);

        if (Color.TryParse(theme.BorderDefault, out var borderCol))
            _axisPenBrush = new SolidColorBrush(borderCol);

        if (Color.TryParse(theme.AccentColor, out var accentCol))
        {
            _mainSeqPenBrush = new SolidColorBrush(accentCol);
            _cardAccentTextBrush = new SolidColorBrush(accentCol);
        }

        if (Color.TryParse(theme.TextPrimary, out var textPrimaryCol))
            _textWhiteBrush = new SolidColorBrush(textPrimaryCol);

        if (Color.TryParse(theme.TextMuted, out var textMutedCol))
            _textMutedBrush = new SolidColorBrush(textMutedCol);

        if (Color.TryParse(theme.BackgroundElevated, out var elevatedCol))
            _cardBgBrush = new SolidColorBrush(Color.FromArgb(240, elevatedCol.R, elevatedCol.G, elevatedCol.B));

        if (theme.IsDark)
        {
            _painQuadrantBrush = new SolidColorBrush(Color.FromArgb(28, 255, 60, 80));
            _uselessQuadrantBrush = new SolidColorBrush(Color.FromArgb(22, 255, 180, 30));
            _mainSeqBandBrush = new SolidColorBrush(Color.FromArgb(16, 74, 232, 208));
        }
        else
        {
            _painQuadrantBrush = new SolidColorBrush(Color.FromArgb(35, 255, 60, 80));
            _uselessQuadrantBrush = new SolidColorBrush(Color.FromArgb(30, 255, 180, 30));
            _mainSeqBandBrush = new SolidColorBrush(Color.FromArgb(25, 74, 232, 208));
        }

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        if (bounds.Width <= 10 || bounds.Height <= 10)
            return;

        // Sfondo
        context.FillRectangle(_backgroundBrush, bounds);

        var dataset = Dataset;
        if (dataset == null || dataset.Items.Count == 0)
        {
            var noDataText = new FormattedText(
                LocalizationService.Instance["Martin_NoData"],
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                TitleTypeface,
                13,
                _textMutedBrush);
            context.DrawText(noDataText, new Point((bounds.Width - noDataText.Width) / 2.0, (bounds.Height - noDataText.Height) / 2.0));
            return;
        }

        const double marginL = 70;
        const double marginR = 50;
        const double marginT = 50;
        const double marginB = 60;

        double plotW = Math.Max(100, bounds.Width - marginL - marginR);
        double plotH = Math.Max(100, bounds.Height - marginT - marginB);

        // 1. Aree Sfondi Quadranti
        // Zona di Sofferenza (Pain Zone): A in [0..0.35], I in [0..0.35] -> Basso a sinistra
        double painW = 0.35 * plotW;
        double painH = 0.35 * plotH;
        context.FillRectangle(_painQuadrantBrush, new Rect(marginL, marginT + plotH - painH, painW, painH));

        // Zona di Inutilità (Uselessness Zone): A in [0.65..1.0], I in [0.65..1.0] -> Alto a destra
        double uselessX = marginL + 0.65 * plotW;
        double uselessY = marginT;
        double uselessW = 0.35 * plotW;
        double uselessH = 0.35 * plotH;
        context.FillRectangle(_uselessQuadrantBrush, new Rect(uselessX, uselessY, uselessW, uselessH));

        // 2. Linea della Sequenza Principale (Main Sequence: A + I = 1 -> da (0, 1) a (1, 0))
        var mainSeqP1 = new Point(marginL, marginT); // I=0, A=1 (Top-Left)
        var mainSeqP2 = new Point(marginL + plotW, marginT + plotH); // I=1, A=0 (Bottom-Right)

        var mainSeqPen = new Pen(_mainSeqPenBrush, 1.8, DashStyle.Dash);
        context.DrawLine(mainSeqPen, mainSeqP1, mainSeqP2);

        // Testo Sequenza Principale lungo la diagonale
        var loc = LocalizationService.Instance;
        var seqLabelFmt = new FormattedText("Main Sequence (A + I = 1)", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelTypeface, 10, _mainSeqPenBrush);
        context.DrawText(seqLabelFmt, new Point(marginL + plotW * 0.4, marginT + plotH * 0.4 - 14));

        var painLabelFmt = new FormattedText(loc["Martin_ZonePain"], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, TitleTypeface, 11, new SolidColorBrush(Color.Parse("#FF5566")));
        context.DrawText(painLabelFmt, new Point(marginL + 8, marginT + plotH - 22));

        var uselessLabelFmt = new FormattedText(loc["Martin_ZoneUseless"], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, TitleTypeface, 11, new SolidColorBrush(Color.Parse("#FFB030")));
        context.DrawText(uselessLabelFmt, new Point(marginL + plotW - uselessLabelFmt.Width - 8, marginT + 8));

        // 3. Assi Cartesiani
        var axisPen = new Pen(_axisPenBrush, 1.5);
        context.DrawLine(axisPen, new Point(marginL, marginT), new Point(marginL, marginT + plotH));
        context.DrawLine(axisPen, new Point(marginL, marginT + plotH), new Point(marginL + plotW, marginT + plotH));

        // Tacche Assi
        for (double val = 0.0; val <= 1.01; val += 0.2)
        {
            // Asse Y (A)
            double yPos = marginT + plotH - val * plotH;
            context.DrawLine(axisPen, new Point(marginL - 4, yPos), new Point(marginL, yPos));
            var yValFmt = new FormattedText(val.ToString("0.0", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelTypeface, 9, _textMutedBrush);
            context.DrawText(yValFmt, new Point(marginL - yValFmt.Width - 8, yPos - yValFmt.Height / 2.0));

            // Asse X (I)
            double xPos = marginL + val * plotW;
            context.DrawLine(axisPen, new Point(xPos, marginT + plotH), new Point(xPos, marginT + plotH + 4));
            var xValFmt = new FormattedText(val.ToString("0.0", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelTypeface, 9, _textMutedBrush);
            context.DrawText(xValFmt, new Point(xPos - xValFmt.Width / 2.0, marginT + plotH + 6));
        }

        // Intestazioni Assi
        var axisYTitle = new FormattedText(loc["Martin_AxisY"], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, AxisTypeface, 11, _textWhiteBrush);
        context.DrawText(axisYTitle, new Point(marginL, 16));

        var axisXTitle = new FormattedText(loc["Martin_AxisX"], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, AxisTypeface, 11, _textWhiteBrush);
        context.DrawText(axisXTitle, new Point(marginL + plotW - axisXTitle.Width, marginT + plotH + 32));

        // 4. Disegno Punti / Bolle per ciascun componente
        foreach (var item in dataset.Items)
        {
            double px = marginL + item.Instability * plotW;
            double py = marginT + plotH - item.Abstractness * plotH;
            double radius = Math.Clamp(Math.Sqrt(item.LocOrSize) * 0.7, 5.0, 18.0);

            var bubbleBrush = GetZoneBrush(item.Zone);
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

            if (radius > 10.0 || item.Zone is MartinZone.ZoneOfPain or MartinZone.ZoneOfUselessness)
            {
                string shortName = item.Name.Length > 14 ? item.Name[..12] + "…" : item.Name;
                var lblFmt = new FormattedText(shortName, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelTypeface, 9, _textWhiteBrush);
                context.DrawText(lblFmt, new Point(px - lblFmt.Width / 2.0, py + radius + 2));
            }
        }

        // 5. Hover Card
        if (_hoveredItem != null)
        {
            DrawHoverCard(context, _hoveredItem);
        }
    }

    private static IBrush GetZoneBrush(MartinZone zone)
    {
        return zone switch
        {
            MartinZone.ZoneOfPain => new SolidColorBrush(Color.FromArgb(230, 240, 60, 70)),
            MartinZone.ZoneOfUselessness => new SolidColorBrush(Color.FromArgb(230, 245, 160, 40)),
            MartinZone.MainSequence => new SolidColorBrush(Color.FromArgb(230, 74, 232, 208)),
            _ => new SolidColorBrush(Color.FromArgb(230, 80, 160, 240))
        };
    }

    private void DrawHoverCard(DrawingContext context, MartinMetricItem item)
    {
        var loc = LocalizationService.Instance;
        var titleFmt = new FormattedText(
            $"{item.Kind}: {item.QualifiedName}",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            TitleTypeface,
            12,
            _textWhiteBrush);

        string zoneText = item.Zone switch
        {
            MartinZone.ZoneOfPain => loc["Martin_ZonePain"],
            MartinZone.ZoneOfUselessness => loc["Martin_ZoneUseless"],
            MartinZone.MainSequence => "Main Sequence",
            _ => "Balanced"
        };

        var subFmt = new FormattedText(
            $"Zone: {zoneText} | Distanza (D): {item.Distance:F2} | Instabilità (I): {item.Instability:F2} (Ce={item.EfferentCoupling}, Ca={item.AfferentCoupling}) | Astrattezza (A): {item.Abstractness:F2} | LOC: {item.LocOrSize}",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            11,
            _cardAccentTextBrush);

        const double paddingH = 12;
        const double paddingV = 8;
        const double lineSpacing = 4;

        double maxTextWidth = Math.Max(titleFmt.Width, subFmt.Width);
        double cardW = maxTextWidth + paddingH * 2;
        double cardH = titleFmt.Height + subFmt.Height + lineSpacing + paddingV * 2;

        var bounds = Bounds;
        double cardX = Math.Clamp(bounds.Width - cardW - 20, 20, bounds.Width - cardW - 20);
        double cardY = 20;

        var cardRect = new Rect(cardX, cardY, cardW, cardH);
        context.FillRectangle(_cardBgBrush, cardRect, 6);
        context.DrawRectangle(new Pen(_axisPenBrush, 1), cardRect, 6);

        context.DrawText(titleFmt, new Point(cardX + paddingH, cardY + paddingV));
        context.DrawText(subFmt, new Point(cardX + paddingH, cardY + paddingV + titleFmt.Height + lineSpacing));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pt = e.GetPosition(this);
        var hit = HitTestPoint(pt);

        if (_hoveredItem?.Id != hit?.Id)
        {
            _hoveredItem = hit;
            InvalidateVisual();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pt = e.GetPosition(this);
        _selectedItem = HitTestPoint(pt);
        ItemSelected?.Invoke(this, _selectedItem);
        InvalidateVisual();
    }

    private MartinMetricItem? HitTestPoint(Point pt)
    {
        var dataset = Dataset;
        if (dataset == null || dataset.Items.Count == 0)
            return null;

        var bounds = Bounds;
        const double marginL = 70;
        const double marginR = 50;
        const double marginT = 50;
        const double marginB = 60;

        double plotW = Math.Max(100, bounds.Width - marginL - marginR);
        double plotH = Math.Max(100, bounds.Height - marginT - marginB);

        for (int i = dataset.Items.Count - 1; i >= 0; i--)
        {
            var item = dataset.Items[i];
            double px = marginL + item.Instability * plotW;
            double py = marginT + plotH - item.Abstractness * plotH;
            double radius = Math.Clamp(Math.Sqrt(item.LocOrSize) * 0.7, 5.0, 18.0) + 4.0;

            double dx = pt.X - px;
            double dy = pt.Y - py;

            if (dx * dx + dy * dy <= radius * radius)
            {
                return item;
            }
        }

        return null;
    }
}
