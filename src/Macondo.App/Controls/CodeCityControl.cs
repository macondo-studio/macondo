using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Macondo.App.Models;
using Macondo.App.Services;
using Macondo.Semantic.Analytics.CodeCity;

namespace Macondo.App.Controls;

/// <summary>
/// Polymetric 3D / Isometric 2.5D urban viewer (CodeCity) based on software metrics (Wettel & Lanza).
/// Represent the codebase as a city: blocks for folders/projects, buildings for classes/modules, height for complexity, and base area for LOC.
/// </summary>
public sealed class CodeCityControl : Control
{
    public static readonly StyledProperty<CodeCityDataset?> DatasetProperty =
        AvaloniaProperty.Register<CodeCityControl, CodeCityDataset?>(nameof(Dataset));

    public CodeCityDataset? Dataset
    {
        get => GetValue(DatasetProperty);
        set => SetValue(DatasetProperty, value);
    }

    public event EventHandler<CityBuilding?>? BuildingSelected;
    public event EventHandler<CityBuilding?>? BuildingDoubleTapped;

    private CityBuilding? _hoveredBuilding;
    private CityBuilding? _selectedBuilding;
    private Point _mousePosition;

    // Pan & Zoom
    private double _zoom = 0.85;
    private double _offsetX = 0;
    private double _offsetY = 0;
    private bool _isPanning = false;
    private Point _panStart;

    private static readonly Typeface DistrictFont = new("Inter", FontStyle.Normal, FontWeight.Bold);
    private static readonly Typeface TooltipFont = new("Inter", FontStyle.Normal, FontWeight.Normal);
    private static readonly Typeface KpiFont = new("Inter", FontStyle.Normal, FontWeight.Bold);

    private IBrush _backgroundBrush = new SolidColorBrush(Color.Parse("#10121A"));
    private IBrush _gridBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
    private IBrush _districtBgBrush = new SolidColorBrush(Color.FromArgb(90, 24, 28, 40));
    private IBrush _districtBorderBrush = new SolidColorBrush(Color.FromArgb(160, 60, 75, 105));
    private IBrush _textBrush = new SolidColorBrush(Color.Parse("#E0E0E0"));
    private IBrush _secondaryTextBrush = new SolidColorBrush(Color.Parse("#9090A0"));

    public CodeCityControl()
    {
        ClipToBounds = true;
        LocalizationService.Instance.LanguageChanged += (_, _) => InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DatasetProperty)
        {
            _hoveredBuilding = null;
            _selectedBuilding = null;
            ResetView();
            InvalidateVisual();
        }
    }

    public void ResetView()
    {
        _zoom = 0.85;
        _offsetX = Bounds.Width > 0 ? Bounds.Width / 2.0 : 400;
        _offsetY = Bounds.Height > 0 ? Bounds.Height / 3.2 : 180;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var pointerPos = e.GetPosition(this);

        double zoomFactor = e.Delta.Y > 0 ? 1.15 : 0.87;
        double newZoom = Math.Clamp(_zoom * zoomFactor, 0.25, 4.0);

        _offsetX = pointerPos.X - (pointerPos.X - _offsetX) * (newZoom / _zoom);
        _offsetY = pointerPos.Y - (pointerPos.Y - _offsetY) * (newZoom / _zoom);
        _zoom = newZoom;

        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var prop = e.GetCurrentPoint(this).Properties;
        if (prop.IsLeftButtonPressed)
        {
            if (_hoveredBuilding != null)
            {
                _selectedBuilding = _hoveredBuilding;
                BuildingSelected?.Invoke(this, _selectedBuilding);

                if (e.ClickCount == 2)

                    BuildingDoubleTapped?.Invoke(this, _selectedBuilding);
                InvalidateVisual();
            }
            else
            {
                _isPanning = true;
                _panStart = e.GetPosition(this);
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _mousePosition = e.GetPosition(this);

        if (_isPanning)
        {
            var cur = e.GetPosition(this);
            _offsetX += (cur.X - _panStart.X);
            _offsetY += (cur.Y - _panStart.Y);
            _panStart = cur;
            InvalidateVisual();
            return;
        }

        var dataset = Dataset;
        if (dataset == null) return;

        // 2.5D hit testing for buildings (from the last drawn to the first to respect visual depth)
        CityBuilding? hit = null;
        var sorted = dataset.AllBuildings.OrderByDescending(b => b.X + b.Y).ToList();

        foreach (var b in sorted)
        {
            if (IsPointInsideBuildingPrism(_mousePosition, b))
            {
                hit = b;
                break;
            }
        }

        if (hit != _hoveredBuilding)
        {
            _hoveredBuilding = hit;
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _isPanning = false;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        if (bounds.Width <= 10 || bounds.Height <= 10) return;

        // Sfondo città
        context.FillRectangle(_backgroundBrush, new Rect(0, 0, bounds.Width, bounds.Height));

        var dataset = Dataset;
        if (dataset == null || dataset.TotalBuildingsCount == 0)
        {
            RenderEmptyState(context, bounds);
            return;
        }

        // 1. Barra KPI superiore
        RenderKpiHeader(context, bounds, dataset);

        // 2. Disegno distretti e griglia urbana
        foreach (var district in dataset.Districts)
        {
            RenderDistrictFoundation(context, district);
        }

        // 3. Draw buildings in order of depth (Painter's Algorithm X + Y ascending)
        var orderedBuildings = dataset.AllBuildings.OrderBy(b => b.X + b.Y).ToList();
        foreach (var building in orderedBuildings)
        {
            RenderBuildingPrism(context, building, building == _hoveredBuilding, building == _selectedBuilding);
        }

        // 4. Disegno etichette distretti
        foreach (var district in dataset.Districts)
        {
            RenderDistrictLabel(context, district);
        }

        // 5. Hover detail tooltip
        if (_hoveredBuilding != null)

            RenderTooltip(context, bounds, _hoveredBuilding);
    }

    private void RenderKpiHeader(DrawingContext context, Rect bounds, CodeCityDataset dataset)
    {
        double headerH = 44.0;
        var headerRect = new Rect(12, 8, bounds.Width - 24, headerH);
        var bg = new SolidColorBrush(Color.FromArgb(80, 24, 28, 40));
        var border = new Pen(new SolidColorBrush(Color.FromArgb(120, 50, 60, 80)), 1);

        context.DrawRectangle(bg, border, new RoundedRect(headerRect, 6));

        double colW = headerRect.Width / 4.0;
        var localizationService = LocalizationService.Instance;

        RenderKpiItem(context, headerRect.X, headerRect.Y, colW, localizationService["City_Kpi_Buildings"], dataset.TotalBuildingsCount.ToString("N0"), "#3B82F6");
        RenderKpiItem(context, headerRect.X + colW, headerRect.Y, colW, localizationService["City_Kpi_Districts"], dataset.TotalDistrictsCount.ToString("N0"), "#10B981");
        RenderKpiItem(context, headerRect.X + colW * 2, headerRect.Y, colW, localizationService["City_Kpi_TotalLoc"], dataset.TotalLoc.ToString("N0"), "#8B5CF6");
        RenderKpiItem(context, headerRect.X + colW * 3, headerRect.Y, colW, localizationService["City_Kpi_MaxComplexity"], dataset.MaxHeight.ToString("F0"), "#F59E0B");
    }

    private static void RenderKpiItem(
        DrawingContext context,
        double x,
        double y,
        double w,
        string label,
        string val,
        string colorHex)
    {
        var lblText = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, KpiFont, 9.5, new SolidColorBrush(Color.Parse("#9090A0")));
        var valText = new FormattedText(val, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, KpiFont, 15.0, new SolidColorBrush(Color.Parse(colorHex)));

        context.DrawText(lblText, new Point(x + 16, y + 6));
        context.DrawText(valText, new Point(x + 16, y + 20));
    }

    private Point WorldToScreen(double x, double y, double z = 0)
    {
        // Standard 30 degree isometric projection
        double cos30 = 0.8660254;
        double sin30 = 0.5;

        double isoX = (x - y) * cos30 * _zoom + _offsetX;
        double isoY = ((x + y) * sin30 - z) * _zoom + _offsetY;

        return new Point(isoX, isoY);
    }

    private void RenderDistrictFoundation(DrawingContext context, CityDistrict district)
    {
        var pTop = WorldToScreen(district.X, district.Y, 0);
        var pRight = WorldToScreen(district.X + district.Width, district.Y, 0);
        var pBottom = WorldToScreen(district.X + district.Width, district.Y + district.Depth, 0);
        var pLeft = WorldToScreen(district.X, district.Y + district.Depth, 0);

        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            ctx.BeginFigure(pTop, true);
            ctx.LineTo(pRight);
            ctx.LineTo(pBottom);
            ctx.LineTo(pLeft);
            ctx.EndFigure(true);
        }

        context.DrawGeometry(_districtBgBrush, new Pen(_districtBorderBrush, 1.5), geom);
    }

    private void RenderDistrictLabel(DrawingContext context, CityDistrict district)
    {
        var p = WorldToScreen(district.X + 10, district.Y + district.Depth + 15, 0);
        var ft = new FormattedText(district.Name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, DistrictFont, 11.0 * Math.Clamp(_zoom, 0.7, 1.4), new SolidColorBrush(Color.Parse("#A0AEC0")));
        context.DrawText(ft, p);
    }

    private void RenderBuildingPrism(
        DrawingContext context,
        CityBuilding b,
        bool isHovered,
        bool isSelected)
    {
        double x = b.X;
        double y = b.Y;
        double w = b.Width;
        double d = b.Depth;
        double h = b.Height;

        // Vertici Base Inferiore (z = 0)
        var b0 = WorldToScreen(x, y, 0);         // Top
        var b1 = WorldToScreen(x + w, y, 0);     // Right
        var b2 = WorldToScreen(x + w, y + d, 0); // Bottom
        var b3 = WorldToScreen(x, y + d, 0);     // Left

        // Vertici Tetto Superiore (z = h)
        var t0 = WorldToScreen(x, y, h);         // Top
        var t1 = WorldToScreen(x + w, y, h);     // Right
        var t2 = WorldToScreen(x + w, y + d, h); // Bottom
        var t3 = WorldToScreen(x, y + d, h);     // Left

        // Facciata Frontale Sinistra (b3 -> b2 -> t2 -> t3)
        var geomLeft = new StreamGeometry();
        using (var ctx = geomLeft.Open())
        {
            ctx.BeginFigure(b3, true);
            ctx.LineTo(b2);
            ctx.LineTo(t2);
            ctx.LineTo(t3);
            ctx.EndFigure(true);
        }

        // Facciata Frontale Destra (b2 -> b1 -> t1 -> t2)
        var geomRight = new StreamGeometry();
        using (var ctx = geomRight.Open())
        {
            ctx.BeginFigure(b2, true);
            ctx.LineTo(b1);
            ctx.LineTo(t1);
            ctx.LineTo(t2);
            ctx.EndFigure(true);
        }

        // Tetto Superiore (t0 -> t1 -> t2 -> t3)
        var geomRoof = new StreamGeometry();
        using (var ctx = geomRoof.Open())
        {
            ctx.BeginFigure(t0, true);
            ctx.LineTo(t1);
            ctx.LineTo(t2);
            ctx.LineTo(t3);
            ctx.EndFigure(true);
        }

        IBrush roofBrush = isHovered ? (IBrush)Brushes.White : new SolidColorBrush(Color.Parse(b.RoofColorHex));
        var sideBrush = new SolidColorBrush(Color.Parse(b.SideColorHex));
        var frontBrush = new SolidColorBrush(Color.Parse(b.ColorHex));

        var borderPen = isSelected
            ? new Pen(Brushes.Cyan, 2.5)
            : (isHovered ? new Pen(Brushes.White, 1.8) : new Pen(new SolidColorBrush(Color.FromArgb(100, 0, 0, 0)), 1.0));

        context.DrawGeometry(frontBrush, borderPen, geomLeft);
        context.DrawGeometry(sideBrush, borderPen, geomRight);
        context.DrawGeometry(roofBrush, borderPen, geomRoof);
    }

    private bool IsPointInsideBuildingPrism(Point pt, CityBuilding b)
    {
        // Calculate the approximate screen bounding box for the projected building
        var t0 = WorldToScreen(b.X, b.Y, b.Height);
        var b2 = WorldToScreen(b.X + b.Width, b.Y + b.Depth, 0);
        var t3 = WorldToScreen(b.X, b.Y + b.Depth, b.Height);
        var t1 = WorldToScreen(b.X + b.Width, b.Y, b.Height);

        double minX = Math.Min(Math.Min(t0.X, b2.X), Math.Min(t3.X, t1.X)) - 5;
        double maxX = Math.Max(Math.Max(t0.X, b2.X), Math.Max(t3.X, t1.X)) + 5;
        double minY = Math.Min(Math.Min(t0.Y, b2.Y), Math.Min(t3.Y, t1.Y)) - 5;
        double maxY = Math.Max(Math.Max(t0.Y, b2.Y), Math.Max(t3.Y, t1.Y)) + 5;

        return pt.X >= minX && pt.X <= maxX && pt.Y >= minY && pt.Y <= maxY;
    }

    private void RenderTooltip(DrawingContext context, Rect bounds, CityBuilding b)
    {
        var localizationService = LocalizationService.Instance;
        string title = $"{b.Name} ({b.Kind})";
        string sub = $"{localizationService.Get("City_Tooltip_District", b.DistrictName)}\n" +
                     $"{localizationService.Get("City_Tooltip_Loc", b.Loc)}\n" +
                     $"{localizationService.Get("City_Tooltip_CC", b.CyclomaticComplexity)}\n" +
                     $"{localizationService.Get("City_Tooltip_Members", b.MemberCount)}\n" +
                     $"{b.FilePath}";

        var ftTitle = new FormattedText(title, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, KpiFont, 12.0, Brushes.White);
        var ftSub = new FormattedText(sub, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, TooltipFont, 10.5, new SolidColorBrush(Color.Parse("#D0D0E0")));

        double cardW = Math.Max(ftTitle.Width, ftSub.Width) + 24.0;
        double cardH = ftTitle.Height + ftSub.Height + 18.0;

        double tipX = _mousePosition.X + 16.0;
        double tipY = _mousePosition.Y + 16.0;

        if (tipX + cardW > bounds.Width - 10) tipX = bounds.Width - cardW - 10;
        if (tipY + cardH > bounds.Height - 10) tipY = bounds.Height - cardH - 10;

        var cardRect = new Rect(tipX, tipY, cardW, cardH);
        var cardBg = new SolidColorBrush(Color.FromArgb(235, 18, 20, 28));
        var cardBorder = new Pen(new SolidColorBrush(Color.Parse(b.ColorHex)), 1.5);

        context.DrawRectangle(cardBg, cardBorder, new RoundedRect(cardRect, 6));
        context.DrawText(ftTitle, new Point(tipX + 12, tipY + 8));
        context.DrawText(ftSub, new Point(tipX + 12, tipY + 10 + ftTitle.Height));
    }

    private void RenderEmptyState(DrawingContext context, Rect bounds)
    {
        var localizationService = LocalizationService.Instance;
        var text = new FormattedText(
            localizationService["City_Placeholder"],
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            TooltipFont,
            13.0,
            _secondaryTextBrush);

        context.DrawText(text, new Point((bounds.Width - text.Width) / 2.0, (bounds.Height - text.Height) / 2.0));
    }

    public void ApplyTheme(ThemeDefinition theme)
    {
        _backgroundBrush = new SolidColorBrush(Color.Parse(theme.BackgroundPrimary));
        _textBrush = new SolidColorBrush(Color.Parse(theme.TextPrimary));
        _secondaryTextBrush = new SolidColorBrush(Color.Parse(theme.TextSecondary));
        InvalidateVisual();
    }
}
