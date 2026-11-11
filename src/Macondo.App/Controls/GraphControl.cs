using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Macondo.App.Models;
using Macondo.App.Services;
using Macondo.Semantic.Impact;
using Macondo.Semantic.Model;

namespace Macondo.App.Controls;

/// <summary>
/// High performance Avalonia/Skia custom control for rendering and interaction with CodeGraph (M5 + M6).
/// 
/// FEATURES AND INTERACTIONS:
/// 1. Zoom: Continuous zoom via mouse wheel with stability of the point under the cursor.
/// 2. Pan: free dragging of the two-dimensional canvas with the left/central mouse button.
/// 3. Select: selection of a node by clicking with highlighting of the edge, connected arcs and neighboring nodes.
/// 4. Expand/Collapse: Double click on containers (Class, Form, Module, Type, Enum) for progressive disclosure.
/// 5. Callers Mode: hierarchical visual highlighting of the callers of the selected routine.
/// 6. Callees Mode: hierarchical visual highlighting of called routines.
/// 7. Blast Radius Mode (M6): Visualization of change impact with color gradient for depth (Red -> Orange -> Yellow) and Form UI touch.
/// 8. Rendering Directed Arcs with Arrows: CALLS, INSTANTIATES, USES_TYPE, CONTAINS.
/// </summary>
public sealed class GraphControl : Control
{
    public static readonly StyledProperty<VisualCodeGraph?> GraphProperty =
        AvaloniaProperty.Register<GraphControl, VisualCodeGraph?>(nameof(Graph));

    public VisualCodeGraph? Graph
    {
        get => GetValue(GraphProperty);
        set => SetValue(GraphProperty, value);
    }

    /// <summary>Raised when node selection changes.</summary>
    public event EventHandler<GraphNode?>? SelectionChanged;

    /// <summary>Raised when symbol visibility filters change.</summary>
    public event EventHandler? FilterChanged;

    // =========================================================================
    // PERFORMANCE OPTIMIZATIONS & CACHING OF VISIBLE NODES
    // =========================================================================
    // The set of visible nodes is cached to avoid re-execution
    // O(N) scan and HashSet<Guid> allocations at each render frame o
    // mouse movement (HitTest). The cache is only specifically invalidated when
    // the filters, highlighting mode, graph or node expansion status change.
    private HashSet<Guid>? _cachedVisibleNodeIds;
    private bool _isVisibilityCacheDirty = true;

    // =========================================================================
    // SPATIAL HASH GRID: 2D SPATIAL INDEXING FOR HIT TESTING O(1)
    // =========================================================================
    // Subdivides the Euclidean space of the graph into regular cells of 250x250 units.
    // Transform the HitTest from an O(N) linear scan across 10,000+ nodes per frame
    // to a constant O(1) search limited to just the cell under the cursor and the
    // its 8 adjacent cells (5-15 nodes analyzed instead of 10,000, reducing checks by 99.9%).
    private const double SpatialCellSize = 250.0;
    private readonly Dictionary<long, List<GraphNode>> _spatialGrid = new();
    private bool _isSpatialGridDirty = true;

    /// <summary>
    /// Compute the 64-bit hash key for a spatial grid cell (cx, cy)
    /// packing the integer coordinates into the high and low 32 bits.
    /// </summary>
    private static long GetSpatialCellKey(int cx, int cy) => ((long)cx << 32) | (uint)cy;

    /// <summary>
    /// Invalidates the spatial index of the graph, forcing its reconstruction at the next HitTest or frame.
    /// </summary>
    public void InvalidateSpatialIndex()
    {
        _isSpatialGridDirty = true;
    }

    /// <summary>
    /// Invalidates the visible node cache, forcing it to be recalculated at the next frame
    /// and requiring an immediate redesign of the control.
    /// </summary>
    public void InvalidateVisibilityCache()
    {
        _isVisibilityCacheDirty = true;
        InvalidateVisual();
    }

    private bool _showMethods = true;
    public bool ShowMethods
    {
        get => _showMethods;
        set
        {
            if (_showMethods != value)
            {
                _showMethods = value;
                InvalidateVisibilityCache();
                FilterChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private bool _showProperties = true;
    public bool ShowProperties
    {
        get => _showProperties;
        set
        {
            if (_showProperties != value)
            {
                _showProperties = value;
                InvalidateVisibilityCache();
                FilterChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private bool _showFields = true;
    public bool ShowFields
    {
        get => _showFields;
        set
        {
            if (_showFields != value)
            {
                _showFields = value;
                InvalidateVisibilityCache();
                FilterChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private bool _showConstants = true;
    public bool ShowConstants
    {
        get => _showConstants;
        set
        {
            if (_showConstants != value)
            {
                _showConstants = value;
                InvalidateVisibilityCache();
                FilterChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private bool _showTypesAndEnums = true;
    public bool ShowTypesAndEnums
    {
        get => _showTypesAndEnums;
        set
        {
            if (_showTypesAndEnums != value)
            {
                _showTypesAndEnums = value;
                InvalidateVisibilityCache();
                FilterChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private bool _enableHoverSelection = true;
    /// <summary>Indicates whether node selection and inspection occurs automatically when the mouse pointer passes over (Hover).</summary>
    public bool EnableHoverSelection
    {
        get => _enableHoverSelection;
        set => _enableHoverSelection = value;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == GraphProperty)
        {
            InvalidateVisibilityCache();
            _isSpatialGridDirty = true;
            _formattedTextCache.Clear();
        }
    }

    /// <summary>
    /// Simultaneously set all visibility filters by symbol type.
    /// </summary>
    public void SetFilters(
        bool methods,
        bool properties,
        bool fields,
        bool constants,
        bool typesAndEnums)
    {
        _showMethods = methods;
        _showProperties = properties;
        _showFields = fields;
        _showConstants = constants;
        _showTypesAndEnums = typesAndEnums;
        InvalidateVisibilityCache();
        FilterChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Calculates the number of visible and total nodes in the current graph.
    /// </summary>
    public (int visibleNodes, int totalNodes) GetNodeCounts()
    {
        if (Graph is not { } graph) return (0, 0);
        var visible = ComputeVisibleNodeIds(graph);
        return (visible.Count, graph.Nodes.Count);
    }

    private readonly HashSet<Guid> _hiddenProjectIds = new();
    /// <summary>Set of project IDs hidden in the graph.</summary>
    public IReadOnlySet<Guid> HiddenProjectIds => _hiddenProjectIds;

    /// <summary>Raised when the visibility of a project in the graph changes.</summary>
    public event EventHandler<Guid>? ProjectVisibilityChanged;

    /// <summary>Raised when the user finishes moving/dragging a node with the mouse.</summary>
    public event EventHandler? NodeMoved;

    /// <summary>Checks if a project is visible in the graph.</summary>
    public bool IsProjectVisible(Guid projectId) => !_hiddenProjectIds.Contains(projectId);

    /// <summary>Set the visibility of a project in the graph.</summary>
    public void SetProjectVisibility(Guid projectId, bool isVisible)
    {
        if (isVisible)
        {
            if (!_hiddenProjectIds.Remove(projectId))
                return;
        }
        else
        {
            if (!_hiddenProjectIds.Add(projectId))
                return;

            // If the selected node belongs to the hidden project, deselect it
            if (_selectedNode != null)
            {
                var isSelectedInProject = (_selectedNode.Kind == SymbolKind.Project && _selectedNode.Id == projectId) ||
                                          (_selectedNode.ProjectId == projectId);
                if (isSelectedInProject)
                    SelectNode(null);
            }
        }

        InvalidateVisibilityCache();
        ProjectVisibilityChanged?.Invoke(this, projectId);
    }

    /// <summary>Makes all projects in the graph visible.</summary>
    public void ClearHiddenProjects()
    {
        if (_hiddenProjectIds.Count == 0) return;
        _hiddenProjectIds.Clear();
        InvalidateVisibilityCache();
        ProjectVisibilityChanged?.Invoke(this, Guid.Empty);
    }

    /// <summary>Calculates the number of visible and total projects in the current graph.</summary>
    public (int visibleProjects, int totalProjects) GetProjectCounts()
    {
        if (Graph is not { } graph) return (0, 0);
        var projectNodes = graph.Nodes.Where(n => n.Kind == SymbolKind.Project).ToList();
        var total = projectNodes.Count;
        if (total == 0) return (0, 0);
        var visible = projectNodes.Count(p => !_hiddenProjectIds.Contains(p.Id));
        return (visible, total);
    }

    // View Transformations (Zoom & Pan)
    private double _scale = 1.0;
    private double _offsetX;
    private double _offsetY;

    // State of pointer interaction and dragging nodes
    private Point _pointerDownScreenPos;
    private bool _isPointerDown;
    private bool _isPanning;
    private bool _isNodeDragging;
    private GraphNode? _draggedNode;
    private Point _lastDragGraphPos;
    private const double PanThreshold = 4.0;

    // Selection and highlighting status
    private GraphNode? _selectedNode;
    /// <summary>Node currently selected in the graph.</summary>
    public GraphNode? SelectedNode => _selectedNode;
    private readonly HashSet<Guid> _highlightedNodeIds = new();
    private readonly HashSet<Guid> _callerNodeIds = new();
    private readonly HashSet<Guid> _calleeNodeIds = new();
    private readonly HashSet<Guid> _calleeContainerIds = new();
    private readonly HashSet<Guid> _callerContainerIds = new();
    private readonly HashSet<(Guid, Guid)> _calleeEdgeIds = new();
    private readonly HashSet<(Guid, Guid)> _callerEdgeIds = new();
    private readonly HashSet<(Guid, Guid)> _highlightedEdges = new();

    // Blast Radius Status (M6)
    private readonly Dictionary<Guid, int> _blastRadiusDepths = new();
    private readonly HashSet<Guid> _affectedContainerIds = new();
    private ImpactReport? _activeBlastRadiusReport;

    // Highlight mode
    private HighlightMode _currentHighlightMode = HighlightMode.Normal;
    public HighlightMode CurrentHighlightMode => _currentHighlightMode;

    // Pennelli e Stili
    private bool _isDarkTheme = true;
    private IBrush _backgroundBrush = new SolidColorBrush(Color.Parse("#181822"));
    private IBrush _defaultLabelBrush = Brushes.WhiteSmoke;
    private IBrush _dimmedLabelBrush = new SolidColorBrush(Color.Parse("#555566"));
    private IPen _edgeContainsPen = new Pen(new SolidColorBrush(Color.Parse("#30303E")), 1);
    private static readonly IPen EdgeCallsPen = new Pen(new SolidColorBrush(Color.Parse("#4A78B8")), 1.5);
    private static readonly IPen EdgeHandlesEventPen = new Pen(new SolidColorBrush(Color.Parse("#F08C4A")), 1.5, DashStyle.Dash);
    private static readonly IPen EdgeInstantiatesPen = new Pen(new SolidColorBrush(Color.Parse("#D4883B")), 1.5, DashStyle.Dash);
    private static readonly IPen EdgeUsesTypePen = new Pen(new SolidColorBrush(Color.Parse("#3BAE8C")), 1, DashStyle.Dot);
    private static readonly IPen EdgeHighlightPen = new Pen(new SolidColorBrush(Color.Parse("#FFD700")), 2.5);
    private static readonly IPen EdgeCallerPen = new Pen(new SolidColorBrush(Color.Parse("#FF9933")), 2.5);
    private static readonly IPen EdgeCalleePen = new Pen(new SolidColorBrush(Color.Parse("#33CCFF")), 2.5);
    private static readonly IPen EdgeBlastRadiusPen = new Pen(new SolidColorBrush(Color.Parse("#FF6600")), 2.5);
    private static readonly IPen EdgeCrossProjectPen = new Pen(new SolidColorBrush(Color.Parse("#E056FD")), 2.2, DashStyle.Dash);
    private IPen _edgeDimmedPen = new Pen(new SolidColorBrush(Color.Parse("#252532")), 0.8);
    private static readonly IPen SelectionOutlinePen = new Pen(new SolidColorBrush(Color.Parse("#FFD700")), 3);
    private static readonly IPen TargetBlastOutlinePen = new Pen(new SolidColorBrush(Color.Parse("#FF3344")), 3.5);
    private static readonly IPen FormBlastOutlinePen = new Pen(new SolidColorBrush(Color.Parse("#00D0FF")), 2.5);

    // Static palette of pre-allocated brushes to eliminate GC allocations at each render frame
    private static readonly IBrush BrushProject = new SolidColorBrush(Color.Parse("#E85D75"));
    private static readonly IBrush BrushForm = new SolidColorBrush(Color.Parse("#4FA8E8"));
    private static readonly IBrush BrushClass = new SolidColorBrush(Color.Parse("#54C08C"));
    private static readonly IBrush BrushModule = new SolidColorBrush(Color.Parse("#C084F0"));
    private static readonly IBrush BrushUserControl = new SolidColorBrush(Color.Parse("#F08C4A"));
    private static readonly IBrush BrushMethod = new SolidColorBrush(Color.Parse("#7D7D8E"));
    private static readonly IBrush BrushProperty = new SolidColorBrush(Color.Parse("#E8B838"));
    private static readonly IBrush BrushField = new SolidColorBrush(Color.Parse("#60A5FA"));
    private static readonly IBrush BrushConstant = new SolidColorBrush(Color.Parse("#F87171"));
    private static readonly IBrush BrushType = new SolidColorBrush(Color.Parse("#34D399"));
    private static readonly IBrush BrushEnum = new SolidColorBrush(Color.Parse("#A7F3D0"));
    private static readonly IBrush BrushSelected = new SolidColorBrush(Color.Parse("#FFD700"));
    private static readonly IBrush BrushHighlighted = new SolidColorBrush(Color.Parse("#90CAF9"));
    private static readonly IBrush BrushBlastTarget = new SolidColorBrush(Color.Parse("#FF3344"));
    private static readonly IBrush BrushBlastDepth1 = new SolidColorBrush(Color.Parse("#FF8800"));
    private static readonly IBrush BrushBlastDepth2 = new SolidColorBrush(Color.Parse("#FFBB00"));
    private static readonly IBrush BrushBlastDepth3 = new SolidColorBrush(Color.Parse("#FFE066"));
    private static readonly IBrush BrushAffectedForm = new SolidColorBrush(Color.Parse("#00D0FF"));
    private static readonly IBrush BrushAffectedContainer = new SolidColorBrush(Color.Parse("#54C08C"));
    private static readonly IBrush BrushDimmedDark = new SolidColorBrush(Color.Parse("#282834"));
    private static readonly IBrush BrushDimmedLight = new SolidColorBrush(Color.Parse("#E0E0EA"));
    private static readonly IBrush BrushCallee = new SolidColorBrush(Color.Parse("#00D0FF"));
    private static readonly IBrush BrushCaller = new SolidColorBrush(Color.Parse("#FF9933"));
    private static readonly IBrush BrushContainerCalleeDark = new SolidColorBrush(Color.Parse("#3A6073"));
    private static readonly IBrush BrushContainerCalleeLight = new SolidColorBrush(Color.Parse("#90C2DE"));
    private static readonly IBrush BrushContainerCallerDark = new SolidColorBrush(Color.Parse("#5C4335"));
    private static readonly IBrush BrushContainerCallerLight = new SolidColorBrush(Color.Parse("#DEB598"));
    private static readonly IBrush BrushContainerCurrentDark = new SolidColorBrush(Color.Parse("#5C5535"));
    private static readonly IBrush BrushContainerCurrentLight = new SolidColorBrush(Color.Parse("#D4C388"));
    private static readonly IBrush BrushRoutineInnerCall = new SolidColorBrush(Color.Parse("#FFE57F"));
    private static readonly IBrush BrushDimmedAltDark = new SolidColorBrush(Color.Parse("#22222E"));

    // =========================================================================
    // CACHING FLYWEIGHT FORMATTED TEXT & PENNELLI ETICHETTE
    // =========================================================================
    // Generating FormattedText in Avalonia/Skia involves text layout,
    // measurement and font shaping via HarfBuzz. Creating instances every frame causes
    // hundreds of thousands of allocations per second and drastic framerate drops.
    // This Flyweight cache stores formatted text and reuses it at no cost.
    private static readonly IBrush BrushLabelSelectedDark = Brushes.Gold;
    private static readonly IBrush BrushLabelSelectedLight = new SolidColorBrush(Color.Parse("#B36200"));
    private static readonly IBrush BrushLabelCalleeDark = new SolidColorBrush(Color.Parse("#00D0FF"));
    private static readonly IBrush BrushLabelCalleeLight = new SolidColorBrush(Color.Parse("#0088CC"));
    private static readonly IBrush BrushLabelCallerDark = new SolidColorBrush(Color.Parse("#FF9933"));
    private static readonly IBrush BrushLabelCallerLight = new SolidColorBrush(Color.Parse("#E65C00"));
    private static readonly IBrush BrushLabelBlastDark = Brushes.Gold;
    private static readonly IBrush BrushLabelBlastLight = new SolidColorBrush(Color.Parse("#C06000"));

    private readonly Dictionary<(string text, int fontSize, IBrush brush), FormattedText> _formattedTextCache = new();

    /// <summary>
    /// Returns a pre-formatted instance of <see cref="FormattedText"/> from the internal cache,
    /// or it creates a new one by inserting it into the cache for subsequent frames (zero allocations when fully operational).
    /// </summary>
    private FormattedText GetOrCreateFormattedText(string text, int fontSize, IBrush brush)
    {
        var key = (text, fontSize, brush);
        if (!_formattedTextCache.TryGetValue(key, out var ft))
        {
            ft = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                fontSize,
                brush);

            // Periodically clear the cache if it exceeds the safety threshold (e.g. huge dynamic loads)
            if (_formattedTextCache.Count > 4000)
                _formattedTextCache.Clear();

            _formattedTextCache[key] = ft;
        }

        return ft;
    }

    private static readonly Typeface LabelTypeface = new("Inter", FontStyle.Normal, FontWeight.Medium);

    // =========================================================================
    // BATCHING OF ARC GEOMETRIES (Draw Call Reduction from 16k to < 10)
    // =========================================================================
    // Instead of invoking DrawingContext.DrawLine individually for each arc (which would generate
    // over 16,000 distinct draw commands per frame), visible lines and arrows are
    // grouped by style/pen in pre-allocated continuous accumulators.
    // Each group is then drawn with a single multipart StreamGeometry: the GPU
    // and Skia rasterize the entire graph in less than 10 draw calls overall.

    /// <summary>Represents an unoriented line segment between two points on the screen.</summary>
    private readonly struct LineSegment
    {
        public readonly Point P1;
        public readonly Point P2;

        public LineSegment(Point p1, Point p2)
        {
            P1 = p1;
            P2 = p2;
        }
    }

    /// <summary>Accumulator of line segments associated with a specific rendering pen.</summary>
    private sealed class EdgeBatch
    {
        public IPen Pen { get; set; }
        public readonly List<LineSegment> Segments = new(2048);

        public EdgeBatch(IPen pen) => Pen = pen;

        public void AddLine(Point p1, Point p2) => Segments.Add(new LineSegment(p1, p2));

        public void Clear() => Segments.Clear();
    }

    private readonly EdgeBatch _batchContains;
    private readonly EdgeBatch _batchCalls;
    private readonly EdgeBatch _batchHandlesEvent;
    private readonly EdgeBatch _batchInstantiates;
    private readonly EdgeBatch _batchUsesType;
    private readonly EdgeBatch _batchCrossProject;
    private readonly EdgeBatch _batchCallee;
    private readonly EdgeBatch _batchCaller;
    private readonly EdgeBatch _batchBlastRadius;
    private readonly EdgeBatch _batchHighlight;
    private readonly EdgeBatch _batchDimmed;
    private readonly EdgeBatch[] _allEdgeBatches;

    // Debouncing timer for Hover Selection: avoid unleashing dozens of updates
    // heavy on ViewModel and side panels during rapid pointer transit over nodes.
    private readonly DispatcherTimer _hoverDebounceTimer;
    private GraphNode? _pendingHoverNode;

    static GraphControl()
    {
        AffectsRender<GraphControl>(GraphProperty);
        FocusableProperty.OverrideDefaultValue<GraphControl>(true);
    }

    public GraphControl()
    {
        ClipToBounds = true;
        DoubleTapped += OnDoubleTapped;

        _hoverDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(45)
        };
        _hoverDebounceTimer.Tick += OnHoverDebounceTimerTick;

        _batchContains = new EdgeBatch(_edgeContainsPen);
        _batchCalls = new EdgeBatch(EdgeCallsPen);
        _batchHandlesEvent = new EdgeBatch(EdgeHandlesEventPen);
        _batchInstantiates = new EdgeBatch(EdgeInstantiatesPen);
        _batchUsesType = new EdgeBatch(EdgeUsesTypePen);
        _batchCrossProject = new EdgeBatch(EdgeCrossProjectPen);
        _batchCallee = new EdgeBatch(EdgeCalleePen);
        _batchCaller = new EdgeBatch(EdgeCallerPen);
        _batchBlastRadius = new EdgeBatch(EdgeBlastRadiusPen);
        _batchHighlight = new EdgeBatch(EdgeHighlightPen);
        _batchDimmed = new EdgeBatch(_edgeDimmedPen);

        _allEdgeBatches = new[]
        {
            _batchContains,
            _batchCalls,
            _batchHandlesEvent,
            _batchInstantiates,
            _batchUsesType,
            _batchCrossProject,
            _batchDimmed,
            _batchCallee,
            _batchCaller,
            _batchBlastRadius,
            _batchHighlight
        };
    }

    private void OnHoverDebounceTimerTick(object? sender, EventArgs e)
    {
        _hoverDebounceTimer.Stop();
        if (_pendingHoverNode != null && _pendingHoverNode.Id != _selectedNode?.Id)
            SelectNode(_pendingHoverNode);
    }

    // =========================================================================
    // RENDERING SKIA / AVALONIA
    // =========================================================================

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(_backgroundBrush, new Rect(Bounds.Size));

        var graph = Graph;
        if (graph is null || graph.Nodes.Count == 0)
        {
            var placeholder = new FormattedText(
                LocalizationService.Instance["Graph_Placeholder"],
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                14,
                Brushes.Gray);
            context.DrawText(placeholder, new Point(16, 16));
            return;
        }

        // Check and update the visible node cache only if marked dirty.
        // Avoid rerunning ComputeVisibleNodeIds (linear scan of all symbols with allocation of a new HashSet)
        // to each single rendering frame (e.g. during continuous panning or zooming).
        if (_isVisibilityCacheDirty || _cachedVisibleNodeIds == null)
        {
            _cachedVisibleNodeIds = ComputeVisibleNodeIds(graph);
            _isVisibilityCacheDirty = false;
        }

        var visibleIds = _cachedVisibleNodeIds;
        var viewWidth = Bounds.Width;
        var viewHeight = Bounds.Height;

        // =========================================================================
        // REVERSE VIEWPORT (Coordinate Graph): Culling O(1) before conversions
        // =========================================================================
        // Instead of calculating GraphToScreen for all 16,000 edges and 10,000 nodes at each frame,
        // we convert the screen rectangle only once into the Cartesian space of the graph.
        // Out-of-view elements are discarded with simple float comparisons, saving money
        // tens of thousands of trigonometric calculations and matrix conversions every frame.
        var pTopLeft = ScreenToGraph(new Point(-50, -50));
        var pBottomRight = ScreenToGraph(new Point(viewWidth + 50, viewHeight + 50));
        var viewMinX = Math.Min(pTopLeft.X, pBottomRight.X);
        var viewMaxX = Math.Max(pTopLeft.X, pBottomRight.X);
        var viewMinY = Math.Min(pTopLeft.Y, pBottomRight.Y);
        var viewMaxY = Math.Max(pTopLeft.Y, pBottomRight.Y);

        // =========================================================================
        // 1. BATCHED RENDERING OF THE ARCHES (Under the nodes)
        // =========================================================================
        // Empty pre-allocated accumulators (zero GC allocations)
        for (var i = 0; i < _allEdgeBatches.Length; i++)
            _allEdgeBatches[i].Clear();

        foreach (var edge in graph.Edges)
        {
            // Skips edges whose end nodes are not both visible in the active hierarchy
            if (!visibleIds.Contains(edge.SourceId) || !visibleIds.Contains(edge.TargetId))
                continue;

            var source = graph.GetNode(edge.SourceId);
            var target = graph.GetNode(edge.TargetId);
            if (source is null || target is null) continue;

            // Viewport edge culling in graph coordinates: discard the edge before screen conversions
            var eMinX = Math.Min(source.X, target.X);
            var eMaxX = Math.Max(source.X, target.X);
            var eMinY = Math.Min(source.Y, target.Y);
            var eMaxY = Math.Max(source.Y, target.Y);

            if (eMaxX < viewMinX || eMinX > viewMaxX || eMaxY < viewMinY || eMinY > viewMaxY)
                continue;

            // Projects onto the screen only the segments actually intersected by the viewport
            var p1 = GraphToScreen(source.X, source.Y);
            var p2 = GraphToScreen(target.X, target.Y);

            var isCalleeEdge = _currentHighlightMode == HighlightMode.Callees && _calleeEdgeIds.Contains((edge.SourceId, edge.TargetId));
            var isCallerEdge = _currentHighlightMode == HighlightMode.Callers && _callerEdgeIds.Contains((edge.SourceId, edge.TargetId));
            var isBlastRadiusEdge = _currentHighlightMode == HighlightMode.BlastRadius &&
                                    (_blastRadiusDepths.ContainsKey(edge.SourceId) || _selectedNode?.Id == edge.SourceId) &&
                                    (_blastRadiusDepths.ContainsKey(edge.TargetId) || _selectedNode?.Id == edge.TargetId);
            var isHighlighted = _highlightedEdges.Contains((edge.SourceId, edge.TargetId));

            // Classifies the arc in the accumulator corresponding to the active visual style
            EdgeBatch targetBatch;
            if (isCalleeEdge)
                targetBatch = _batchCallee;
            else if (isCallerEdge)
                targetBatch = _batchCaller;
            else if (isBlastRadiusEdge)
                targetBatch = _batchBlastRadius;
            else if (isHighlighted)
                targetBatch = _batchHighlight;
            else if (_currentHighlightMode is HighlightMode.Callees or HighlightMode.Callers or HighlightMode.BlastRadius)
                targetBatch = _batchDimmed;
            else if (edge.IsCrossProject)
                targetBatch = _batchCrossProject;
            else
                targetBatch = GetBatchForEdgeKind(edge.Kind);

            targetBatch.AddLine(p1, p2);

            // LOD Arrows: shows the arrows only if the arc is active/highlighted or if the zoom is sufficient (scale >= 0.45)
            var shouldDrawArrow = (isCalleeEdge || isCallerEdge || isBlastRadiusEdge || isHighlighted) ||
                (_scale >= 0.45 && (edge.IsCrossProject || edge.Kind is RelationshipKind.Calls or RelationshipKind.Instantiates or RelationshipKind.HandlesEvent));

            if (shouldDrawArrow)
            {
                var targetRadius = target.Radius * Math.Clamp(_scale, 0.2, 2.5);
                var dx = p2.X - p1.X;
                var dy = p2.Y - p1.Y;
                var len = Math.Sqrt(dx * dx + dy * dy);
                if (len >= 1)
                {
                    var ux = dx / len;
                    var uy = dy / len;
                    var tip = new Point(p2.X - ux * (targetRadius + 2), p2.Y - uy * (targetRadius + 2));
                    var arrowSize = 6.0;
                    var a1 = new Point(tip.X - ux * arrowSize + uy * (arrowSize * 0.6), tip.Y - uy * arrowSize - ux * (arrowSize * 0.6));
                    var a2 = new Point(tip.X - ux * arrowSize - uy * (arrowSize * 0.6), tip.Y - uy * arrowSize + ux * (arrowSize * 0.6));

                    // Adds the two arrow cusps to the same line batch (zero allocations)
                    targetBatch.AddLine(tip, a1);
                    targetBatch.AddLine(tip, a2);
                }
            }
        }

        // Outputs batched arc geometries: converts thousands of segments into a single StreamGeometry
        // per pen, executing at most 11 GPU draw calls for the entire relational network.
        for (var i = 0; i < _allEdgeBatches.Length; i++)
        {
            var batch = _allEdgeBatches[i];
            if (batch.Segments.Count == 0) continue;

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                for (var s = 0; s < batch.Segments.Count; s++)
                {
                    var seg = batch.Segments[s];
                    ctx.BeginFigure(seg.P1, false);
                    ctx.LineTo(seg.P2);
                    ctx.EndFigure(false);
                }
            }

            context.DrawGeometry(null, batch.Pen, geometry);
        }

        // =========================================================================
        // 2. RENDERING OF THE NODES (Above the arcs)
        // =========================================================================
        foreach (var node in graph.Nodes)
        {
            if (!visibleIds.Contains(node.Id)) continue;

            var nodeRadius = node.Radius;
            // Viewport culling in graph coordinates: discard the node before screen conversions
            if (node.X + nodeRadius < viewMinX || node.X - nodeRadius > viewMaxX ||
                node.Y + nodeRadius < viewMinY || node.Y - nodeRadius > viewMaxY)
                continue;

            var screenPos = GraphToScreen(node.X, node.Y);
            var radius = Math.Max(nodeRadius * Math.Min(_scale, 2.5), 1.0);

            // Viewport culling for performance
            if (screenPos.X < -radius - 50 || screenPos.Y < -radius - 50 ||
                screenPos.X - radius > Bounds.Width + 50 || screenPos.Y - radius > Bounds.Height + 50)
                continue;

            var isSelected = node.Id == _selectedNode?.Id;
            var isHighlighted = _highlightedNodeIds.Contains(node.Id);
            var isCaller = _callerNodeIds.Contains(node.Id);
            var isCallee = _calleeNodeIds.Contains(node.Id);

            // Blast Radius info
            var isInBlastRadius = _currentHighlightMode == HighlightMode.BlastRadius;
            var blastDepth = _blastRadiusDepths.TryGetValue(node.Id, out var d) ? d : (int?)null;
            var isAffectedContainer = _affectedContainerIds.Contains(node.Id);

            var fill = GetNodeBrush(node, isSelected, isHighlighted, isCaller, isCallee, isInBlastRadius, blastDepth, isAffectedContainer);
            var outlinePen = isSelected ? (isInBlastRadius ? TargetBlastOutlinePen : SelectionOutlinePen) :
                            (isAffectedContainer && node.Kind == SymbolKind.Form ? FormBlastOutlinePen : null);

            context.DrawEllipse(fill, outlinePen, screenPos, radius, radius);

            // Expanded/compressed container visual indicator
            if (node.IsContainer && radius >= 4)
            {
                IBrush markerColor = node.IsExpanded 
                    ? (_isDarkTheme ? Brushes.WhiteSmoke : new SolidColorBrush(Color.Parse("#4A4A5E"))) 
                    : Brushes.Orange;
                var markerRadius = Math.Min(3.0, radius * 0.4);
                context.DrawEllipse(markerColor, null, screenPos, markerRadius, markerRadius);
            }

            // Node Label: Level of Detail (LOD) and FormattedText from cache to zero allocations
            var isProminent = isSelected || isHighlighted || isCaller || isCallee || (isInBlastRadius && blastDepth.HasValue);
            var showLabel = isProminent ? radius >= 4.0 : (node.IsContainer ? radius >= 7.0 : radius >= 11.0);

            if (showLabel)
            {
                var fontSize = node.Kind is SymbolKind.Method or SymbolKind.Property ? 10 : 12;
                IBrush labelBrush;
                if (isSelected)
                    labelBrush = _isDarkTheme ? BrushLabelSelectedDark : BrushLabelSelectedLight;
                else if (_currentHighlightMode == HighlightMode.Callees)
                    labelBrush = isCallee ? (_isDarkTheme ? BrushLabelCalleeDark : BrushLabelCalleeLight) :
                                 (_calleeContainerIds.Contains(node.Id) ? _defaultLabelBrush : _dimmedLabelBrush);
                else if (_currentHighlightMode == HighlightMode.Callers)
                    labelBrush = isCaller ? (_isDarkTheme ? BrushLabelCallerDark : BrushLabelCallerLight) :
                                 (_callerContainerIds.Contains(node.Id) ? _defaultLabelBrush : _dimmedLabelBrush);
                else if (_currentHighlightMode == HighlightMode.BlastRadius)
                    labelBrush = blastDepth.HasValue ? (_isDarkTheme ? BrushLabelBlastDark : BrushLabelBlastLight) :
                                 (isAffectedContainer ? _defaultLabelBrush : _dimmedLabelBrush);
                else
                    labelBrush = _defaultLabelBrush;

                var label = GetOrCreateFormattedText(node.Name, fontSize, labelBrush);
                context.DrawText(label, screenPos + new Point(radius + 4, -label.Height / 2));
            }
        }
    }

    /// <summary>
    /// Returns the drawing batch for the specified relationship type.
    /// Visually distinguishes call arcs (solid blue line), event management (dashed orange),
    /// instantiation (dotted amber), typical use (dotted emerald green) or containment (dark/light grey).
    /// </summary>
    private EdgeBatch GetBatchForEdgeKind(RelationshipKind kind) => kind switch
    {
        RelationshipKind.Calls => _batchCalls,
        RelationshipKind.HandlesEvent => _batchHandlesEvent,
        RelationshipKind.Instantiates => _batchInstantiates,
        RelationshipKind.UsesType => _batchUsesType,
        _ => _batchContains
    };

    /// <summary>
    /// Determines the node's fill brush based on the current interaction state:
    /// 1. Blast Radius Mode: Risk Gradient (Red = Target, Orange/Amber/Yellow = Callers by Depth Level, Cyan = Impacted UI Forms).
    /// 2. Callees mode: Gold for source node, Cyan for called routines, Slate for related modules.
    /// 3. Callers Mode: Gold for target node, Amber for callers, Slate for related modules.
    /// 4. Normal Mode: Semantic palette for SymbolKind (Form, Class, Module, Method, Property, Field, Constant, Type, Enum).
    /// </summary>
    private IBrush GetNodeBrush(
        GraphNode node,
        bool isSelected,
        bool isHighlighted,
        bool isCaller,
        bool isCallee,
        bool isInBlastRadius,
        int? blastDepth,
        bool isAffectedContainer)
    {
        // If we are in Blast Radius mode: color based on risk depth
        if (isInBlastRadius)
        {
            if (isSelected)
                return BrushBlastTarget; // Rosso acceso = Target di modifica

            if (blastDepth.HasValue)

                return blastDepth.Value switch
                {
                    1 => BrushBlastDepth1, // Arancione = Chiamante diretto
                    2 => BrushBlastDepth2, // Ambra = Chiamante 2° livello
                    _ => BrushBlastDepth3  // Giallo chiaro = Chiamante 3°+ livello
                };

            if (isAffectedContainer)

                return node.Kind == SymbolKind.Form
                    ? BrushAffectedForm // Form UI toccata
                    : BrushAffectedContainer; // Classe/Modulo toccato

            // Unimpacted nodes: attenuated
            return _isDarkTheme ? BrushDimmedDark : BrushDimmedLight;
        }

        // Callees mode: Highlights the source caller, the called routines (Cyan), and their containers
        if (_currentHighlightMode == HighlightMode.Callees)
        {
            if (isSelected) return BrushSelected;
            if (isCallee) return BrushCallee;

            if (_selectedNode is { IsContainer: true } && node.ParentId == _selectedNode.Id)
            {
                // Internal routine of the container that makes calls
                if (_calleeEdgeIds.Any(e => e.Item1 == node.Id))
                    return BrushRoutineInnerCall;
            }

            if (node.IsContainer && _calleeContainerIds.Contains(node.Id))

                return _isDarkTheme ? BrushContainerCalleeDark : BrushContainerCalleeLight;

            if (_selectedNode?.ParentId == node.Id)

                return _isDarkTheme ? BrushContainerCurrentDark : BrushContainerCurrentLight;

            return _isDarkTheme ? BrushDimmedAltDark : BrushDimmedLight;
        }

        // Callers Mode: Highlights the target routine, callers (Amber/Orange) and their containers
        if (_currentHighlightMode == HighlightMode.Callers)
        {
            if (isSelected) return BrushSelected;
            if (isCaller) return BrushCaller;

            if (_selectedNode is { IsContainer: true } && node.ParentId == _selectedNode.Id)
            {
                // Internal container routine called from outside
                if (_callerEdgeIds.Any(e => e.Item2 == node.Id))
                    return BrushRoutineInnerCall;
            }

            if (node.IsContainer && _callerContainerIds.Contains(node.Id))

                return _isDarkTheme ? BrushContainerCallerDark : BrushContainerCallerLight;

            if (_selectedNode?.ParentId == node.Id)

                return _isDarkTheme ? BrushContainerCurrentDark : BrushContainerCurrentLight;

            return _isDarkTheme ? BrushDimmedAltDark : BrushDimmedLight;
        }

        // Modalità normale
        if (isSelected) return BrushSelected;
        if (isHighlighted) return BrushHighlighted;

        return node.Kind switch
        {
            SymbolKind.Project => BrushProject,
            SymbolKind.Form => BrushForm,
            SymbolKind.Class => BrushClass,
            SymbolKind.Module => BrushModule,
            SymbolKind.UserControl => BrushUserControl,
            SymbolKind.Method => BrushMethod,
            SymbolKind.Property => BrushProperty,
            SymbolKind.Field => BrushField,
            SymbolKind.Constant => BrushConstant,
            SymbolKind.Type => BrushType,
            SymbolKind.Enum => BrushEnum,
            _ => Brushes.Gray
        };
    }

    /// <summary>
    /// Progressive disclosure and filtering by levels/categories:
    /// - The main containers (Project, Form, Class, Module, UserControl) are always present in the architectural skeleton.
    /// - Child members and types are only visible if the relevant container is expanded AND the respective category is enabled in the filter.
    /// - When a container or its member is selected or expanded, all members of that container are made visible regardless of the detail filter.
    /// - In Blast Radius or Callees/Callers mode, impacted nodes and call path are forced visible.
    /// </summary>
    private HashSet<Guid> ComputeVisibleNodeIds(VisualCodeGraph graph)
    {
        var visible = new HashSet<Guid>();
        foreach (var node in graph.Nodes)
        {
            // If the project to which the node belongs or the project root node is hidden, ignore it
            if (node.ProjectId.HasValue && _hiddenProjectIds.Contains(node.ProjectId.Value))
                continue;
            if (node.Kind == SymbolKind.Project && _hiddenProjectIds.Contains(node.Id))
                continue;

            var isPrimaryContainer = node.Kind is SymbolKind.Project or SymbolKind.Class or SymbolKind.Form
                                              or SymbolKind.Module or SymbolKind.UserControl;

            if (isPrimaryContainer)
            {
                visible.Add(node.Id);
                continue;
            }

            // A member is forced visible if:
            // 1. Is within blast radius or calculated call paths
            // 2. This is the currently selected node
            // 3. Is a member of the currently selected container or shares the container with the selected member
            var isChildOfSelected = _selectedNode != null && (node.ParentId == _selectedNode.Id || (_selectedNode.ParentId.HasValue && node.ParentId == _selectedNode.ParentId));

            var isForcedVisible = _blastRadiusDepths.ContainsKey(node.Id) ||
                                  _calleeNodeIds.Contains(node.Id) ||
                                  _callerNodeIds.Contains(node.Id) ||
                                  _selectedNode?.Id == node.Id ||
                                  isChildOfSelected;

            // Check if the symbol category is enabled in the active filter
            var isCategoryEnabled = node.Kind switch
            {
                SymbolKind.Method => ShowMethods,
                SymbolKind.Property => ShowProperties,
                SymbolKind.Field => ShowFields,
                SymbolKind.Constant => ShowConstants,
                SymbolKind.Type or SymbolKind.Enum or SymbolKind.Event => ShowTypesAndEnums,
                _ => true
            };

            // If the category is not enabled in the filter and the node is not forced/selected, do not show it
            if (!isCategoryEnabled && !isForcedVisible)

                continue;

            var parent = node.ParentId.HasValue ? graph.GetNode(node.ParentId.Value) : null;
            if (parent is { IsExpanded: true } || isForcedVisible || !node.ParentId.HasValue)

                visible.Add(node.Id);
        }
        return visible;
    }

    // =========================================================================
    // COORDINATE TRANSFORMATIONS (Pan & Zoom)
    // =========================================================================

    public Point GraphToScreen(double x, double y) =>
        new(Bounds.Width / 2 + x * _scale + _offsetX, Bounds.Height / 2 + y * _scale + _offsetY);

    public Point ScreenToGraph(Point screen) => new(
        (screen.X - Bounds.Width / 2 - _offsetX) / _scale,
        (screen.Y - Bounds.Height / 2 - _offsetY) / _scale);

    public void ResetView()
    {
        _scale = 1.0;
        _offsetX = 0;
        _offsetY = 0;
        InvalidateVisual();
    }

    /// <summary>Centers the canvas view on a specific node.</summary>
    public void FocusOnNode(Guid nodeId)
    {
        var node = Graph?.GetNode(nodeId);
        if (node is null) return;

        _offsetX = -node.X * _scale;
        _offsetY = -node.Y * _scale;
        InvalidateVisual();
    }

    // =========================================================================
    // POINTER INPUT MANAGEMENT (Pan, Zoom, Drag Nodes, Click, Double Click)
    // =========================================================================

    /// <summary>
    /// Move a node and recursively all its descendant nodes (direct and indirect children) of the same delta vector.
    /// </summary>
    private void MoveNodeAndDescendants(GraphNode rootNode, double deltaX, double deltaY)
    {
        if (Graph is null || (deltaX == 0 && deltaY == 0)) return;

        _isSpatialGridDirty = true;
        rootNode.X += deltaX;
        rootNode.Y += deltaY;

        var visited = new HashSet<Guid> { rootNode.Id };
        var queue = new Queue<Guid>();
        queue.Enqueue(rootNode.Id);

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            foreach (var child in Graph.GetChildren(currentId))
            {
                if (visited.Add(child.Id))
                {
                    child.X += deltaX;
                    child.Y += deltaY;
                    queue.Enqueue(child.Id);
                }
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed && !point.Properties.IsMiddleButtonPressed) return;

        _isPointerDown = true;
        _isPanning = false;
        _isNodeDragging = false;
        _draggedNode = null;
        _pointerDownScreenPos = e.GetPosition(this);

        if (point.Properties.IsLeftButtonPressed)
        {
            var graphPoint = ScreenToGraph(_pointerDownScreenPos);
            var hit = HitTest(graphPoint);
            if (hit != null)
            {
                _draggedNode = hit;
                _lastDragGraphPos = graphPoint;
            }
        }

        e.Pointer.Capture(this);
        Focus();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_isPointerDown)
        {
            var currentScreen = e.GetPosition(this);
            var screenDx = currentScreen.X - _pointerDownScreenPos.X;
            var screenDy = currentScreen.Y - _pointerDownScreenPos.Y;
            var screenDistSq = screenDx * screenDx + screenDy * screenDy;

            if (_draggedNode != null)
            {
                if (!_isNodeDragging && screenDistSq > PanThreshold * PanThreshold)

                    _isNodeDragging = true;

                if (_isNodeDragging)
                {
                    var currentGraphPos = ScreenToGraph(currentScreen);
                    var deltaX = currentGraphPos.X - _lastDragGraphPos.X;
                    var deltaY = currentGraphPos.Y - _lastDragGraphPos.Y;

                    MoveNodeAndDescendants(_draggedNode, deltaX, deltaY);
                    _lastDragGraphPos = currentGraphPos;
                    InvalidateVisual();
                }
            }
            else
            {
                if (!_isPanning && screenDistSq > PanThreshold * PanThreshold)

                    _isPanning = true;

                if (_isPanning)
                {
                    var dx = currentScreen.X - _pointerDownScreenPos.X;
                    var dy = currentScreen.Y - _pointerDownScreenPos.Y;
                    _offsetX += dx;
                    _offsetY += dy;
                    _pointerDownScreenPos = currentScreen;
                    InvalidateVisual();
                }
            }
        }
        else if (EnableHoverSelection)
        {
            var graphPoint = ScreenToGraph(e.GetPosition(this));
            var hit = HitTest(graphPoint);

            if (hit != null)
            {
                // Instantly updates the visual cursor (Hand) without any perceptible delay
                Cursor = new Cursor(StandardCursorType.Hand);

                // 45ms debounce for actual selection: avoid triggering dozens of events
                // SelectionChanged and heavy recalculations on callers/callees when cursor rapidly crosses nodes
                if (hit.Id != _selectedNode?.Id && hit.Id != _pendingHoverNode?.Id)
                {
                    _pendingHoverNode = hit;
                    _hoverDebounceTimer.Stop();
                    _hoverDebounceTimer.Start();
                }
            }
            else
            {
                Cursor = Cursor.Default;
                if (_pendingHoverNode != null)
                {
                    _hoverDebounceTimer.Stop();
                    _pendingHoverNode = null;
                }
            }
        }
        else
        {
            var graphPoint = ScreenToGraph(e.GetPosition(this));
            var hit = HitTest(graphPoint);
            Cursor = hit != null ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoverDebounceTimer.Stop();
        _pendingHoverNode = null;
        if (!_isPointerDown)
            Cursor = Cursor.Default;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
        _hoverDebounceTimer.Stop();
        _pendingHoverNode = null;

        if (_isPointerDown)
        {
            if (_draggedNode != null)
            {
                if (!_isNodeDragging)

                    SelectNode(_draggedNode);

                else
                {
                    if (_selectedNode?.Id != _draggedNode.Id)

                        SelectNode(_draggedNode);
                    NodeMoved?.Invoke(this, EventArgs.Empty);
                }
            }
            else if (!_isPanning)
            {
                var graphPoint = ScreenToGraph(e.GetPosition(this));
                var hit = HitTest(graphPoint);
                SelectNode(hit);
            }
        }

        _isPointerDown = false;
        _isPanning = false;
        _isNodeDragging = false;
        _draggedNode = null;
    }

    /// <summary>
    /// Handles the double-click event to implement progressive disclosure:
    /// if the cursor is on a container node (Class, Form, Module, Type, Enum), it toggles its expansion state.
    /// </summary>
    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        var graphPoint = ScreenToGraph(e.GetPosition(this));
        var hit = HitTest(graphPoint);

        if (hit is { IsContainer: true })
        {
            hit.IsExpanded = !hit.IsExpanded;
            InvalidateVisibilityCache();
        }

        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var pointerScreenPos = e.GetPosition(this);
        var graphPointUnderPointer = ScreenToGraph(pointerScreenPos);

        var zoomFactor = e.Delta.Y > 0 ? 1.15 : 1 / 1.15;
        var newScale = Math.Clamp(_scale * zoomFactor, 0.002, 8.0);

        _offsetX = pointerScreenPos.X - Bounds.Width / 2 - graphPointUnderPointer.X * newScale;
        _offsetY = pointerScreenPos.Y - Bounds.Height / 2 - graphPointUnderPointer.Y * newScale;
        _scale = newScale;

        InvalidateVisual();
        e.Handled = true;
    }

    /// <summary>
    /// It reconstructs the spatial grid (Spatial Hash Grid) by grouping the nodes into cells of 250x250 graph coordinates.
    /// Allows O(1) searches and collision tests with zero allocations at steady state.
    /// </summary>
    private void RebuildSpatialGrid(VisualCodeGraph graph)
    {
        _spatialGrid.Clear();
        foreach (var node in graph.Nodes)
        {
            var cx = (int)Math.Floor(node.X / SpatialCellSize);
            var cy = (int)Math.Floor(node.Y / SpatialCellSize);
            var key = GetSpatialCellKey(cx, cy);
            if (!_spatialGrid.TryGetValue(key, out var cellList))
            {
                cellList = new List<GraphNode>();
                _spatialGrid[key] = cellList;
            }
            cellList.Add(node);
        }
        _isSpatialGridDirty = false;
    }

    /// <summary>
    /// Performs detection of the node under the cursor (Hit Testing) in the coordinate space of the graph.
    /// Use the O(1) Spatial Hash Grid to inspect only the nodes of neighboring cells
    /// to the cursor, ensuring instant response with 0 delays even with tens of thousands of nodes.
    /// </summary>
    /// <param name="graphPoint">Pointer coordinates converted to the Cartesian space of the graph.</param>
    /// <returns>The closest <see cref="GraphNode"/> within the tolerance radius, or null.</returns>
    public GraphNode? HitTest(Point graphPoint)
    {
        var graph = Graph;
        if (graph is null) return null;

        // Reuse the set of nodes visible in cache: essential to avoid allocating
        // a new HashSet<Guid> with each micro-move of the mouse cursor (OnPointerMoved).
        if (_isVisibilityCacheDirty || _cachedVisibleNodeIds == null)
        {
            _cachedVisibleNodeIds = ComputeVisibleNodeIds(graph);
            _isVisibilityCacheDirty = false;
        }

        if (_isSpatialGridDirty)
            RebuildSpatialGrid(graph);

        var visibleIds = _cachedVisibleNodeIds;
        GraphNode? best = null;
        var bestDistSq = double.MaxValue;

        var centerCx = (int)Math.Floor(graphPoint.X / SpatialCellSize);
        var centerCy = (int)Math.Floor(graphPoint.Y / SpatialCellSize);

        // Cell search radius limited based on zoom scale to avoid scanning too many cells at extreme zooms
        var maxTolerance = 50.0 / Math.Max(_scale, 0.05) + 6.0;
        var cellRadius = Math.Clamp((int)Math.Ceiling(maxTolerance / SpatialCellSize), 1, 3);

        for (var cx = centerCx - cellRadius; cx <= centerCx + cellRadius; cx++)
        {
            for (var cy = centerCy - cellRadius; cy <= centerCy + cellRadius; cy++)
            {
                var key = GetSpatialCellKey(cx, cy);
                if (!_spatialGrid.TryGetValue(key, out var cellNodes))
                    continue;

                foreach (var node in cellNodes)
                {
                    if (!visibleIds.Contains(node.Id))
                        continue;

                    var dx = node.X - graphPoint.X;
                    var dy = node.Y - graphPoint.Y;
                    var distSq = dx * dx + dy * dy;

                    // Adaptive selection radius scaled with zoom factor (minimum 6px tolerance)
                    var hitRadius = node.Radius / Math.Max(_scale, 0.002) + 6.0;
                    if (distSq <= hitRadius * hitRadius && distSq < bestDistSq)
                    {
                        best = node;
                        bestDistSq = distSq;
                    }
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Detects the node positioned below the specified screen coordinates.
    /// </summary>
    public GraphNode? GetNodeAtScreenPos(Point screenPoint) => HitTest(ScreenToGraph(screenPoint));

    // =========================================================================
    // SELECTION AND HIGHLIGHTING CALLERS / CALLEES / BLAST RADIUS
    // =========================================================================

    /// <summary>
    /// Restores the internal state of highlight data structures (callers, callees, paths, and blast radius).
    /// </summary>
    private void ClearInternalHighlightState()
    {
        _callerNodeIds.Clear();
        _calleeNodeIds.Clear();
        _calleeContainerIds.Clear();
        _callerContainerIds.Clear();
        _calleeEdgeIds.Clear();
        _callerEdgeIds.Clear();
        _blastRadiusDepths.Clear();
        _affectedContainerIds.Clear();
        _highlightedEdges.Clear();
    }

    /// <summary>
    /// Explicitly select a node on the canvas, highlighting its edges and direct neighbors
    /// and notifying listeners via the <see cref="SelectionChanged"/> event.
    /// </summary>
    /// <param name="node">The node to select, or null to deselect.</param>
    public void SelectNode(GraphNode? node)
    {
        if (_selectedNode == node && _currentHighlightMode == HighlightMode.Normal)
            return;

        _selectedNode = node;
        _highlightedNodeIds.Clear();
        ClearInternalHighlightState();
        _currentHighlightMode = HighlightMode.Normal;

        if (node is not null && Graph is { } graph)
        {
            _highlightedNodeIds.Add(node.Id);

            if (node.IsContainer)
                node.IsExpanded = true;
            else if (node.ParentId.HasValue)
            {
                var parent = graph.GetNode(node.ParentId.Value);
                if (parent is not null)
                    parent.IsExpanded = true;
            }

            foreach (var edge in graph.GetIncidentEdges(node.Id))
            {
                if (edge.SourceId == node.Id)
                {
                    _highlightedNodeIds.Add(edge.TargetId);
                    _highlightedEdges.Add((edge.SourceId, edge.TargetId));
                }
                else if (edge.TargetId == node.Id)
                {
                    _highlightedNodeIds.Add(edge.SourceId);
                    _highlightedEdges.Add((edge.SourceId, edge.TargetId));
                }
            }
        }

        InvalidateVisibilityCache();
        SelectionChanged?.Invoke(this, node);
    }

    /// <summary>
    /// Highlights on the graph all the callers (direct callers) of the selected node (routine or container).
    /// If a container (Class, Module, Form) is selected, aggregate the callers of all member procedures.
    /// Automatically expands caller containers to make nodes visible.
    /// </summary>
    public IReadOnlyList<Symbol> HighlightCallersForSelected()
    {
        ClearInternalHighlightState();

        if (_selectedNode is null || Graph?.Queries is null)
            return Array.Empty<Symbol>();

        var symbolsToQuery = new List<Symbol>();

        if (_selectedNode.IsContainer)
        {
            _selectedNode.IsExpanded = true;
            foreach (var childNode in Graph.GetChildren(_selectedNode.Id))
            {
                if (childNode.SemanticSymbol is not null && childNode.Kind is SymbolKind.Method or SymbolKind.Property)
                    symbolsToQuery.Add(childNode.SemanticSymbol);
            }
        }
        else if (_selectedNode.SemanticSymbol is not null)
            symbolsToQuery.Add(_selectedNode.SemanticSymbol);

        if (symbolsToQuery.Count == 0)
            return Array.Empty<Symbol>();

        var callersSet = new HashSet<Guid>();
        var resultList = new List<Symbol>();

        foreach (var targetSym in symbolsToQuery)
        {
            var directCallers = Graph.Queries.GetDirectCallers(targetSym);
            foreach (var caller in directCallers)
            {
                _callerEdgeIds.Add((caller.Id, targetSym.Id));

                if (callersSet.Add(caller.Id))
                {
                    resultList.Add(caller);
                    _callerNodeIds.Add(caller.Id);

                    // Auto-expanding of the caller container
                    var callerNode = Graph.GetNode(caller.Id);
                    if (callerNode?.ParentId.HasValue == true)
                    {
                        _callerContainerIds.Add(callerNode.ParentId.Value);
                        if (Graph.GetNode(callerNode.ParentId.Value) is { } parentNode)
                            parentNode.IsExpanded = true;
                    }
                }
            }
        }

        // Expand the parent container of the selected node if present
        if (_selectedNode.ParentId.HasValue && Graph.GetNode(_selectedNode.ParentId.Value) is { } selParent)
            selParent.IsExpanded = true;

        _currentHighlightMode = HighlightMode.Callers;
        InvalidateVisibilityCache();
        return resultList;
    }

    /// <summary>
    /// Highlights on the graph all the calls (direct callees) of the selected node (routine or container).
    /// If a container (Class, Module, Form) is selected, aggregate calls of all member routines.
    /// Automatically expands target containers to make nodes visible.
    /// </summary>
    public IReadOnlyList<Symbol> HighlightCalleesForSelected()
    {
        ClearInternalHighlightState();

        if (_selectedNode is null || Graph?.Queries is null)
            return Array.Empty<Symbol>();

        var symbolsToQuery = new List<Symbol>();

        if (_selectedNode.IsContainer)
        {
            _selectedNode.IsExpanded = true;
            foreach (var childNode in Graph.GetChildren(_selectedNode.Id))
            {
                if (childNode.SemanticSymbol is not null && childNode.Kind is SymbolKind.Method or SymbolKind.Property)
                    symbolsToQuery.Add(childNode.SemanticSymbol);
            }
        }
        else if (_selectedNode.SemanticSymbol is not null)
            symbolsToQuery.Add(_selectedNode.SemanticSymbol);

        if (symbolsToQuery.Count == 0)
            return Array.Empty<Symbol>();

        var calleesSet = new HashSet<Guid>();
        var resultList = new List<Symbol>();

        foreach (var callerSym in symbolsToQuery)
        {
            var directCallees = Graph.Queries.GetDirectCallees(callerSym);
            foreach (var callee in directCallees)
            {
                _calleeEdgeIds.Add((callerSym.Id, callee.Id));

                if (calleesSet.Add(callee.Id))
                {
                    resultList.Add(callee);
                    _calleeNodeIds.Add(callee.Id);

                    // Self-expanding of the callee container
                    var calleeNode = Graph.GetNode(callee.Id);
                    if (calleeNode?.ParentId.HasValue == true)
                    {
                        _calleeContainerIds.Add(calleeNode.ParentId.Value);
                        if (Graph.GetNode(calleeNode.ParentId.Value) is { } parentNode)
                            parentNode.IsExpanded = true;
                    }
                }
            }
        }

        // Expand the parent container of the selected node if present
        if (_selectedNode.ParentId.HasValue && Graph.GetNode(_selectedNode.ParentId.Value) is { } selParent)
            selParent.IsExpanded = true;

        _currentHighlightMode = HighlightMode.Callees;
        InvalidateVisibilityCache();
        return resultList;
    }

    /// <summary>
    /// Highlight the entire Blast Radius (M6) on the graph: targets in red, callers by depth level and Form UI touched.
    /// Automatically expands the containers of impacted nodes.
    /// </summary>
    public void HighlightBlastRadius(ImpactReport report)
    {
        ClearInternalHighlightState();
        _activeBlastRadiusReport = report;

        // Chiamanti diretti (Depth 1)
        foreach (var dc in report.DirectCallers)
        {
            _blastRadiusDepths[dc.Id] = 1;
            if (Graph?.GetNode(dc.Id) is { ParentId: { } pId } && Graph.GetNode(pId) is { } pNode)
                pNode.IsExpanded = true;
        }

        // Chiamanti indiretti (Depth >= 2)
        foreach (var ic in report.IndirectCallers)
        {
            _blastRadiusDepths[ic.Symbol.Id] = ic.Depth;
            if (Graph?.GetNode(ic.Symbol.Id) is { ParentId: { } pId } && Graph.GetNode(pId) is { } pNode)
                pNode.IsExpanded = true;
        }

        // Container impattati (Form, Classi, Moduli)
        foreach (var f in report.AffectedForms) _affectedContainerIds.Add(f.Id);
        foreach (var c in report.AffectedClasses) _affectedContainerIds.Add(c.Id);
        foreach (var m in report.AffectedModules) _affectedContainerIds.Add(m.Id);

        _currentHighlightMode = HighlightMode.BlastRadius;
        InvalidateVisibilityCache();
    }

    /// <summary>
    /// Restore normal highlighting.
    /// </summary>
    public void ClearHighlightMode()
    {
        ClearInternalHighlightState();
        _activeBlastRadiusReport = null;
        _currentHighlightMode = HighlightMode.Normal;
        InvalidateVisibilityCache();
    }

    /// <summary>
    /// Apply the theme to the graph canvas (background color and labels).
    /// </summary>
    public void ApplyTheme(ThemeDefinition theme)
    {
        _isDarkTheme = theme.IsDark;

        if (Color.TryParse(theme.GraphBackground, out var bgCol))
            _backgroundBrush = new SolidColorBrush(bgCol);

        if (theme.IsDark)
        {
            _defaultLabelBrush = Brushes.WhiteSmoke;
            _dimmedLabelBrush = new SolidColorBrush(Color.Parse("#555566"));
            _edgeContainsPen = new Pen(new SolidColorBrush(Color.Parse("#30303E")), 1);
            _edgeDimmedPen = new Pen(new SolidColorBrush(Color.Parse("#252532")), 0.8);
        }
        else
        {
            _defaultLabelBrush = new SolidColorBrush(Color.Parse("#1A1A24"));
            _dimmedLabelBrush = new SolidColorBrush(Color.Parse("#9E9EB0"));
            _edgeContainsPen = new Pen(new SolidColorBrush(Color.Parse("#D0D0DC")), 1);
            _edgeDimmedPen = new Pen(new SolidColorBrush(Color.Parse("#E4E4EE")), 0.8);
        }

        _batchContains.Pen = _edgeContainsPen;
        _batchDimmed.Pen = _edgeDimmedPen;

        _formattedTextCache.Clear();
        InvalidateVisual();
    }
}
