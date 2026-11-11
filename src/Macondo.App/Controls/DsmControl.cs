using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Macondo.App.Models;
using Macondo.App.Services;
using Macondo.Semantic.Analytics.Dsm;

namespace Macondo.App.Controls;

/// <summary>
/// High performance Avalonia Skia custom control for rendering and interaction with the DSM (Dependency Structure Matrix).
/// It allows zoom, pan, row/column hover, cell selection and visual cycle detection.
/// </summary>
public sealed class DsmControl : Control
{
    public static readonly StyledProperty<DsmMatrix?> MatrixProperty =
        AvaloniaProperty.Register<DsmControl, DsmMatrix?>(nameof(Matrix));

    public DsmMatrix? Matrix
    {
        get => GetValue(MatrixProperty);
        set => SetValue(MatrixProperty, value);
    }

    public event EventHandler<DsmCell?>? CellSelected;
    public event EventHandler<DsmItem?>? ItemSelected;

    private double _scale = 1.0;
    private double _offsetX = 200.0;
    private double _offsetY = 140.0;

    private Point _pointerDownScreenPos;
    private bool _isPointerDown;
    private bool _isPanning;
    private const double PanThreshold = 4.0;

    private int _hoveredRow = -1;
    private int _hoveredCol = -1;
    private DsmCell? _selectedCell;

    private const double CellSize = 28.0;
    private const double HeaderWidth = 190.0;
    private const double HeaderHeight = 130.0;

    private static readonly Typeface LabelTypeface = new("Inter", FontStyle.Normal, FontWeight.Medium);
    private static readonly Typeface BoldTypeface = new("Inter", FontStyle.Normal, FontWeight.Bold);

    // Pennelli e penne tema pre-allocati
    private IBrush _backgroundBrush = new SolidColorBrush(Color.Parse("#14141E"));
    private IBrush _gridLineBrush = new SolidColorBrush(Color.Parse("#252535"));
    private IBrush _diagBrush = new SolidColorBrush(Color.Parse("#1E1E2C"));
    private IBrush _hoverHeaderBrush = new SolidColorBrush(Color.Parse("#2A2A40"));
    private IBrush _selectedCellBrush = new SolidColorBrush(Color.Parse("#FFD700"));
    private IBrush _textPrimaryBrush = Brushes.WhiteSmoke;
    private IBrush _textMutedBrush = new SolidColorBrush(Color.Parse("#888899"));
    private IBrush _cycleCellBrush = new SolidColorBrush(Color.Parse("#4A1520"));
    private IBrush _cycleBorderBrush = new SolidColorBrush(Color.Parse("#FF3355"));
    private IBrush _depCellBrush = new SolidColorBrush(Color.Parse("#1A3050"));
    private IBrush _depCellTextBrush = new SolidColorBrush(Color.Parse("#60B0FF"));
    private readonly IBrush _hoverRowColBrush = new SolidColorBrush(Color.FromArgb(40, 96, 176, 255));

    private IPen _gridLinePen = new Pen(new SolidColorBrush(Color.Parse("#252535")), 0.6);
    private IPen _diagLinePen = new Pen(new SolidColorBrush(Color.Parse("#252535")), 1);
    private IPen _cycleBorderPen = new Pen(new SolidColorBrush(Color.Parse("#FF3355")), 1.5);
    private IPen _selectedCellPen = new Pen(new SolidColorBrush(Color.Parse("#FFD700")), 2);

    static DsmControl()
    {
        AffectsRender<DsmControl>(MatrixProperty);
        FocusableProperty.OverrideDefaultValue<DsmControl>(true);
    }

    public DsmControl()
    {
        ClipToBounds = true;
    }

    /// <summary>
    /// Resets DSM matrix zoom and panning to default values.
    /// </summary>
    public void ResetView()
    {
        _scale = 1.0;
        _offsetX = 200.0;
        _offsetY = 140.0;
        InvalidateVisual();
    }

    /// <summary>
    /// Apply brushes and colors of the current theme (background, grid, cells with circular or normal dependencies, labels).
    /// </summary>
    public void ApplyTheme(ThemeDefinition theme)
    {
        if (Color.TryParse(theme.GraphBackground, out var bgCol))
            _backgroundBrush = new SolidColorBrush(bgCol);

        if (Color.TryParse(theme.BorderSubtle, out var borderSubtleCol))
        {
            _gridLineBrush = new SolidColorBrush(borderSubtleCol);
            _gridLinePen = new Pen(_gridLineBrush, 0.6);
            _diagLinePen = new Pen(_gridLineBrush, 1);
        }

        if (Color.TryParse(theme.BackgroundSurface, out var bgSurfaceCol))
            _diagBrush = new SolidColorBrush(bgSurfaceCol);

        if (Color.TryParse(theme.BackgroundElevated, out var bgElevatedCol))
            _hoverHeaderBrush = new SolidColorBrush(bgElevatedCol);

        if (Color.TryParse(theme.TextPrimary, out var textPrimaryCol))
            _textPrimaryBrush = new SolidColorBrush(textPrimaryCol);

        if (Color.TryParse(theme.TextMuted, out var textMutedCol))
            _textMutedBrush = new SolidColorBrush(textMutedCol);

        if (theme.IsDark)
        {
            _cycleCellBrush = new SolidColorBrush(Color.Parse("#4A1520"));
            _cycleBorderBrush = new SolidColorBrush(Color.Parse("#FF3355"));
            _depCellBrush = new SolidColorBrush(Color.Parse("#1A3050"));
            _depCellTextBrush = new SolidColorBrush(Color.Parse("#60B0FF"));
        }
        else
        {
            _cycleCellBrush = new SolidColorBrush(Color.FromArgb(60, 255, 50, 80));
            _cycleBorderBrush = new SolidColorBrush(Color.Parse("#D32F2F"));
            _depCellBrush = new SolidColorBrush(Color.FromArgb(40, 0, 122, 204));
            _depCellTextBrush = new SolidColorBrush(Color.Parse("#005FB8"));
        }

        _cycleBorderPen = new Pen(_cycleBorderBrush, 1.5);
        _selectedCellPen = new Pen(_selectedCellBrush, 2);

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(_backgroundBrush, new Rect(Bounds.Size));

        var localizationService = LocalizationService.Instance;
        var matrix = Matrix;
        if (matrix is null || matrix.Size == 0)
        {
            var placeholder = new FormattedText(
                localizationService["Dsm_Placeholder"],
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                14,
                Brushes.Gray);
            context.DrawText(placeholder, new Point(20, 20));
            return;
        }

        int n = matrix.Size;

        // Viewport culling: calculates the row/column intervals visible on the screen to avoid O(N^2) draw calls
        double viewportW = Bounds.Width;
        double viewportH = Bounds.Height;

        double minLocalX = (0 - _offsetX) / _scale;
        double maxLocalX = (viewportW - _offsetX) / _scale;
        double minLocalY = (0 - _offsetY) / _scale;
        double maxLocalY = (viewportH - _offsetY) / _scale;

        int minRow = Math.Max(0, (int)Math.Floor(minLocalY / CellSize));
        int maxRow = Math.Min(n - 1, (int)Math.Ceiling(maxLocalY / CellSize));

        int minCol = Math.Max(0, (int)Math.Floor(minLocalX / CellSize));
        int maxCol = Math.Min(n - 1, (int)Math.Ceiling(maxLocalX / CellSize));

        bool showRowHeaders = maxLocalX >= -HeaderWidth && minLocalX <= 0;
        bool showColHeaders = maxLocalY >= -HeaderHeight && minLocalY <= 0;

        using (context.PushTransform(Avalonia.Matrix.CreateTranslation(_offsetX, _offsetY)))
        using (context.PushTransform(Avalonia.Matrix.CreateScale(_scale, _scale)))
        {
            // 1. Hovered Row/Column Highlighting
            if (_hoveredRow >= 0 && _hoveredRow < n)
            {
                var rowRect = new Rect(0, _hoveredRow * CellSize, n * CellSize, CellSize);
                context.FillRectangle(_hoverRowColBrush, rowRect);
            }
            if (_hoveredCol >= 0 && _hoveredCol < n)
            {
                var colRect = new Rect(_hoveredCol * CellSize, 0, CellSize, n * CellSize);
                context.FillRectangle(_hoverRowColBrush, colRect);
            }

            // 2. Grid and Matrix Cells (Culling active for maximum fluidity at 60+ FPS)
            for (int r = minRow; r <= maxRow; r++)
            {
                for (int c = minCol; c <= maxCol; c++)
                {
                    var cellRect = new Rect(c * CellSize, r * CellSize, CellSize, CellSize);
                    var cell = matrix.GetCell(r, c);

                    if (r == c)
                    {
                        // Main diagonal (self-reference)
                        context.FillRectangle(_diagBrush, cellRect);
                        context.DrawLine(_diagLinePen, cellRect.TopLeft, cellRect.BottomRight);
                    }
                    else if (cell.HasDependency)
                    {
                        if (cell.IsInCycle)
                        {
                            context.FillRectangle(_cycleCellBrush, cellRect);
                            context.DrawRectangle(_cycleBorderPen, cellRect);
                        }
                        else
                        {
                            context.FillRectangle(_depCellBrush, cellRect);
                        }

                        // Text of the weight/number of calls
                        string countText = cell.Weight > 99 ? "99+" : cell.Weight.ToString();
                        var numFmt = new FormattedText(
                            countText,
                            CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight,
                            BoldTypeface,
                            10,
                            cell.IsInCycle ? _cycleBorderBrush : _depCellTextBrush);

                        double tx = cellRect.X + (CellSize - numFmt.Width) / 2.0;
                        double ty = cellRect.Y + (CellSize - numFmt.Height) / 2.0;
                        context.DrawText(numFmt, new Point(tx, ty));
                    }

                    // Active selection
                    if (_selectedCell != null && _selectedCell.RowIndex == r && _selectedCell.ColumnIndex == c)

                        context.DrawRectangle(_selectedCellPen, cellRect);

                    context.DrawRectangle(_gridLinePen, cellRect);
                }
            }

            // 3. Row Headers (Left - visible only)
            if (showRowHeaders)
            {
                for (int r = minRow; r <= maxRow; r++)
                {
                    var item = matrix.Items[r];
                    var headerRect = new Rect(-HeaderWidth, r * CellSize, HeaderWidth - 4, CellSize);
                    
                    if (r == _hoveredRow)

                        context.FillRectangle(_hoverHeaderBrush, headerRect);

                    // Badge numerico indice riga
                    var idxFmt = new FormattedText(
                        (r + 1).ToString("D2"),
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        BoldTypeface,
                        9,
                        item.HasCircularDependencies ? _cycleBorderBrush : _textMutedBrush);
                    context.DrawText(idxFmt, new Point(-HeaderWidth + 4, r * CellSize + 7));

                    // Nome modulo
                    string displayName = item.Name.Length > 20 ? item.Name[..18] + "…" : item.Name;
                    var textFmt = new FormattedText(
                        displayName,
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        LabelTypeface,
                        11,
                        item.HasCircularDependencies ? _cycleBorderBrush : _textPrimaryBrush);
                    context.DrawText(textFmt, new Point(-HeaderWidth + 26, r * CellSize + 6));
                }
            }

            // 4. Column Headings (Top with 45° rotation - visible only)
            if (showColHeaders)
            {
                for (int c = minCol; c <= maxCol; c++)
                {
                    var item = matrix.Items[c];
                    double cx = (c * CellSize) + (CellSize / 2.0);

                    var idxFmt = new FormattedText(
                        (c + 1).ToString("D2"),
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        BoldTypeface,
                        9,
                        item.HasCircularDependencies ? _cycleBorderBrush : _textMutedBrush);
                    context.DrawText(idxFmt, new Point(c * CellSize + (CellSize - idxFmt.Width) / 2.0, -16));

                    using (context.PushTransform(Avalonia.Matrix.CreateTranslation(cx, -22)))
                    using (context.PushTransform(Avalonia.Matrix.CreateRotation(-Math.PI / 4.0))) // Ruota a -45°
                    {
                        string colName = item.Name.Length > 16 ? item.Name[..14] + "…" : item.Name;
                        var colFmt = new FormattedText(
                            colName,
                            CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight,
                            LabelTypeface,
                            10,
                            c == _hoveredCol ? Brushes.Yellow : (item.HasCircularDependencies ? _cycleBorderBrush : _textMutedBrush));
                        context.DrawText(colFmt, new Point(0, -colFmt.Height / 2.0));
                    }
                }
            }
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var pos = e.GetPosition(this);

        double zoomFactor = e.Delta.Y > 0 ? 1.15 : 1.0 / 1.15;
        double newScale = Math.Clamp(_scale * zoomFactor, 0.2, 3.5);

        _offsetX = pos.X - (pos.X - _offsetX) * (newScale / _scale);
        _offsetY = pos.Y - (pos.Y - _offsetY) * (newScale / _scale);
        _scale = newScale;

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var currentPoint = e.GetCurrentPoint(this);
        if (currentPoint.Properties.IsLeftButtonPressed || currentPoint.Properties.IsMiddleButtonPressed)
        {
            _pointerDownScreenPos = e.GetPosition(this);
            _isPointerDown = true;
            _isPanning = false;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);

        if (_isPointerDown)
        {
            var delta = pos - _pointerDownScreenPos;
            if (!_isPanning && (Math.Abs(delta.X) > PanThreshold || Math.Abs(delta.Y) > PanThreshold))

                _isPanning = true;

            if (_isPanning)
            {
                _offsetX += delta.X;
                _offsetY += delta.Y;
                _pointerDownScreenPos = pos;
                InvalidateVisual();
                return;
            }
        }

        // Calcola indici hovered
        var localX = (pos.X - _offsetX) / _scale;
        var localY = (pos.Y - _offsetY) / _scale;

        int oldRow = _hoveredRow;
        int oldCol = _hoveredCol;

        if (Matrix != null && localX >= 0 && localY >= 0 &&
            localX < Matrix.Size * CellSize && localY < Matrix.Size * CellSize)
        {
            _hoveredCol = (int)(localX / CellSize);
            _hoveredRow = (int)(localY / CellSize);
        }
        else
        {
            _hoveredRow = -1;
            _hoveredCol = -1;
        }

        if (oldRow != _hoveredRow || oldCol != _hoveredCol)

            InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isPointerDown && !_isPanning && Matrix != null)
        {
            var pos = e.GetPosition(this);
            var localX = (pos.X - _offsetX) / _scale;
            var localY = (pos.Y - _offsetY) / _scale;

            if (localX >= 0 && localY >= 0 && localX < Matrix.Size * CellSize && localY < Matrix.Size * CellSize)
            {
                int col = (int)(localX / CellSize);
                int row = (int)(localY / CellSize);
                _selectedCell = Matrix.GetCell(row, col);
                CellSelected?.Invoke(this, _selectedCell);
                InvalidateVisual();
            }
            else if (localX >= -HeaderWidth && localX < 0 && localY >= 0 && localY < Matrix.Size * CellSize)
            {
                int row = (int)(localY / CellSize);
                if (row >= 0 && row < Matrix.Size)

                    ItemSelected?.Invoke(this, Matrix.Items[row]);
            }
        }

        _isPointerDown = false;
        _isPanning = false;
    }
}
