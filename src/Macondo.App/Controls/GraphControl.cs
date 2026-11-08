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
/// Controllo custom Avalonia/Skia ad alte prestazioni per il rendering e l'interazione con il CodeGraph (M5 + M6).
/// 
/// CARATTERISTICHE E INTERAZIONI:
/// 1. Zoom: zoom continuo tramite rotella del mouse con stabilità del punto sotto il cursore.
/// 2. Pan: trascinamento libero del canvas bidimensionale con il tasto sinistro/centrale del mouse.
/// 3. Select: selezione di un nodo tramite clic con evidenziazione del bordo, degli archi connessi e dei nodi vicini.
/// 4. Expand / Collapse: doppio clic su container (Class, Form, Module, Type, Enum) per progressive disclosure.
/// 5. Callers Mode: evidenziazione visiva gerarchica dei chiamanti della routine selezionata.
/// 6. Callees Mode: evidenziazione visiva gerarchica delle routine chiamate.
/// 7. Blast Radius Mode (M6): visualizzazione dell'impatto di modifica con gradiente di colore per profondità (Rosso -> Arancione -> Giallo) e Form UI toccate.
/// 8. Rendering Archi Direzionati con Frecce: CALLS, INSTANTIATES, USES_TYPE, CONTAINS.
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

    /// <summary>Sollevato quando cambia la selezione del nodo.</summary>
    public event EventHandler<GraphNode?>? SelectionChanged;

    /// <summary>Sollevato quando cambiano i filtri di visibilità dei simboli.</summary>
    public event EventHandler? FilterChanged;

    // =========================================================================
    // OTTIMIZZAZIONI DI PERFORMANCE & CACHING DEI NODI VISIBILI
    // =========================================================================
    // L'insieme dei nodi visibili viene memorizzato nella cache per evitare di rieseguire
    // la scansione O(N) e le allocazioni di HashSet<Guid> ad ogni frame di rendering o
    // movimento del mouse (HitTest). La cache viene invalidata in modo mirato solo quando
    // cambiano i filtri, la modalità di evidenziazione, il grafo o lo stato di espansione dei nodi.
    private HashSet<Guid>? _cachedVisibleNodeIds;
    private bool _isVisibilityCacheDirty = true;

    // =========================================================================
    // SPATIAL HASH GRID: INDICIZZAZIONE SPAZIALE 2D PER HIT TESTING O(1)
    // =========================================================================
    // Suddivide lo spazio euclideo del grafo in celle regolari di 250x250 unità.
    // Trasforma l'HitTest da una scansione lineare O(N) su 10.000+ nodi ad ogni frame
    // a una ricerca costante O(1) circoscritta alla sola cella sotto al cursore e alle
    // sue 8 celle adiacenti (5-15 nodi analizzati anziché 10.000, riducendo del 99.9% i controlli).
    private const double SpatialCellSize = 250.0;
    private readonly Dictionary<long, List<GraphNode>> _spatialGrid = new();
    private bool _isSpatialGridDirty = true;

    /// <summary>
    /// Calcola la chiave hash a 64-bit per una cella della griglia spaziale (cx, cy)
    /// impacchettando le coordinate intere nei 32 bit alti e bassi.
    /// </summary>
    private static long GetSpatialCellKey(int cx, int cy) => ((long)cx << 32) | (uint)cy;

    /// <summary>
    /// Invalida l'indice spaziale del grafo forzandone la ricostruzione al successivo HitTest o frame.
    /// </summary>
    public void InvalidateSpatialIndex()
    {
        _isSpatialGridDirty = true;
    }

    /// <summary>
    /// Invalida la cache dei nodi visibili, forzandone il ricalcolo al frame successivo
    /// e richiedendo un ridisegno immediato del controllo.
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
    /// <summary>Indica se la selezione e ispezione dei nodi avviene automaticamente al passaggio del puntatore del mouse (Hover).</summary>
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
    /// Imposta simultaneamente tutti i filtri di visibilità per tipologia di simbolo.
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
    /// Calcola il numero di nodi visibili e totali nel grafo corrente.
    /// </summary>
    public (int visibleNodes, int totalNodes) GetNodeCounts()
    {
        if (Graph is not { } graph) return (0, 0);
        var visible = ComputeVisibleNodeIds(graph);
        return (visible.Count, graph.Nodes.Count);
    }

    private readonly HashSet<Guid> _hiddenProjectIds = new();
    /// <summary>Insieme di ID dei progetti nascosti nel grafo.</summary>
    public IReadOnlySet<Guid> HiddenProjectIds => _hiddenProjectIds;

    /// <summary>Sollevato quando cambia la visibilità di un progetto nel grafo.</summary>
    public event EventHandler<Guid>? ProjectVisibilityChanged;

    /// <summary>Sollevato quando l'utente termina lo spostamento/trascinamento di un nodo con il mouse.</summary>
    public event EventHandler? NodeMoved;

    /// <summary>Verifica se un progetto è visibile nel grafo.</summary>
    public bool IsProjectVisible(Guid projectId) => !_hiddenProjectIds.Contains(projectId);

    /// <summary>Imposta la visibilità di un progetto nel grafo.</summary>
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

            // Se il nodo selezionato appartiene al progetto nascosto, deselezionalo
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

    /// <summary>Rende visibili tutti i progetti nel grafo.</summary>
    public void ClearHiddenProjects()
    {
        if (_hiddenProjectIds.Count == 0) return;
        _hiddenProjectIds.Clear();
        InvalidateVisibilityCache();
        ProjectVisibilityChanged?.Invoke(this, Guid.Empty);
    }

    /// <summary>Calcola il numero di progetti visibili e totali nel grafo corrente.</summary>
    public (int visibleProjects, int totalProjects) GetProjectCounts()
    {
        if (Graph is not { } graph) return (0, 0);
        var projectNodes = graph.Nodes.Where(n => n.Kind == SymbolKind.Project).ToList();
        var total = projectNodes.Count;
        if (total == 0) return (0, 0);
        var visible = projectNodes.Count(p => !_hiddenProjectIds.Contains(p.Id));
        return (visible, total);
    }

    // Trasformazioni di vista (Zoom & Pan)
    private double _scale = 1.0;
    private double _offsetX;
    private double _offsetY;

    // Stato di interazione puntatore e trascinamento nodi
    private Point _pointerDownScreenPos;
    private bool _isPointerDown;
    private bool _isPanning;
    private bool _isNodeDragging;
    private GraphNode? _draggedNode;
    private Point _lastDragGraphPos;
    private const double PanThreshold = 4.0;

    // Stato di selezione ed evidenziazione
    private GraphNode? _selectedNode;
    /// <summary>Nodo attualmente selezionato nel grafo.</summary>
    public GraphNode? SelectedNode => _selectedNode;
    private readonly HashSet<Guid> _highlightedNodeIds = new();
    private readonly HashSet<Guid> _callerNodeIds = new();
    private readonly HashSet<Guid> _calleeNodeIds = new();
    private readonly HashSet<Guid> _calleeContainerIds = new();
    private readonly HashSet<Guid> _callerContainerIds = new();
    private readonly HashSet<(Guid, Guid)> _calleeEdgeIds = new();
    private readonly HashSet<(Guid, Guid)> _callerEdgeIds = new();
    private readonly HashSet<(Guid, Guid)> _highlightedEdges = new();

    // Stato Blast Radius (M6)
    private readonly Dictionary<Guid, int> _blastRadiusDepths = new();
    private readonly HashSet<Guid> _affectedContainerIds = new();
    private ImpactReport? _activeBlastRadiusReport;

    // Modalità di evidenziazione
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

    // Palette statica di pennelli pre-allocati per eliminare le allocazioni GC a ogni frame di rendering
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
    // La generazione di FormattedText in Avalonia/Skia implica il layout del testo,
    // misurazione e font shaping via HarfBuzz. Creare istanze ad ogni frame provoca
    // centinaia di migliaia di allocazioni al secondo e cali drastici di framerate.
    // Questa cache Flyweight memorizza il testo formattato e lo riutilizza a costo zero.
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
    /// Restituisce un'istanza pre-formattata di <see cref="FormattedText"/> dalla cache interna,
    /// oppure ne crea una nuova inserendola in cache per i frame successivi (zero allocazioni a regime).
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

            // Svuota periodicamente la cache se supera la soglia di sicurezza (es. carichi dinamici enormi)
            if (_formattedTextCache.Count > 4000)
                _formattedTextCache.Clear();

            _formattedTextCache[key] = ft;
        }

        return ft;
    }

    private static readonly Typeface LabelTypeface = new("Inter", FontStyle.Normal, FontWeight.Medium);

    // =========================================================================
    // BATCHING DELLE GEOMETRIE DEGLI ARCHI (Draw Call Reduction da 16k a < 10)
    // =========================================================================
    // Invece di invocare DrawingContext.DrawLine singolarmente per ogni arco (che genererebbe
    // oltre 16.000 draw commands distinti per frame), le linee e le frecce visibili vengono
    // raggruppate per stile/penna in accumulatori continui pre-allocati.
    // Ciascun gruppo viene poi disegnato con un unico StreamGeometry multipart: la GPU
    // e Skia rasterizzano l'intero grafo in meno di 10 draw calls complessive.

    /// <summary>Rappresenta un segmento di linea non orientato tra due punti a schermo.</summary>
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

    /// <summary>Accumulatore di segmenti di linea associati a una specifica penna di rendering.</summary>
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

    // Timer di debouncing per l'Hover Selection: evita di scatenare decine di aggiornamenti
    // pesanti su ViewModel e pannelli laterali durante il transito rapido del puntatore sui nodi.
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

        // Verifica e aggiorna la cache dei nodi visibili solo se contrassegnata come dirty.
        // Evita di rieseguire ComputeVisibleNodeIds (scansione lineare di tutti i simboli con allocazione di un nuovo HashSet)
        // a ogni singolo frame di rendering (es. durante pan o zoom continui).
        if (_isVisibilityCacheDirty || _cachedVisibleNodeIds == null)
        {
            _cachedVisibleNodeIds = ComputeVisibleNodeIds(graph);
            _isVisibilityCacheDirty = false;
        }

        var visibleIds = _cachedVisibleNodeIds;
        var viewWidth = Bounds.Width;
        var viewHeight = Bounds.Height;

        // =========================================================================
        // VIEWPORT INVERSO (Coordinate Grafo): Culling O(1) prima delle conversioni
        // =========================================================================
        // Invece di calcolare GraphToScreen per tutti i 16.000 archi e 10.000 nodi ad ogni frame,
        // convertiamo il rettangolo dello schermo una sola volta nello spazio cartesiano del grafo.
        // Gli elementi fuori visuale vengono scartati con semplici confronti float, risparmiando
        // decine di migliaia di calcoli trigonometrici e conversioni di matrici a ogni frame.
        var pTopLeft = ScreenToGraph(new Point(-50, -50));
        var pBottomRight = ScreenToGraph(new Point(viewWidth + 50, viewHeight + 50));
        var viewMinX = Math.Min(pTopLeft.X, pBottomRight.X);
        var viewMaxX = Math.Max(pTopLeft.X, pBottomRight.X);
        var viewMinY = Math.Min(pTopLeft.Y, pBottomRight.Y);
        var viewMaxY = Math.Max(pTopLeft.Y, pBottomRight.Y);

        // =========================================================================
        // 1. RENDERING BATCHED DEGLI ARCHI (Sotto ai nodi)
        // =========================================================================
        // Svuota gli accumulatori pre-allocati (zero allocazioni GC)
        for (var i = 0; i < _allEdgeBatches.Length; i++)
            _allEdgeBatches[i].Clear();

        foreach (var edge in graph.Edges)
        {
            // Salta gli archi i cui nodi estremi non sono entrambi visibili nella gerarchia attiva
            if (!visibleIds.Contains(edge.SourceId) || !visibleIds.Contains(edge.TargetId))
                continue;

            var source = graph.GetNode(edge.SourceId);
            var target = graph.GetNode(edge.TargetId);
            if (source is null || target is null) continue;

            // Viewport edge culling in coordinate grafo: scarta l'arco prima delle conversioni a schermo
            var eMinX = Math.Min(source.X, target.X);
            var eMaxX = Math.Max(source.X, target.X);
            var eMinY = Math.Min(source.Y, target.Y);
            var eMaxY = Math.Max(source.Y, target.Y);

            if (eMaxX < viewMinX || eMinX > viewMaxX || eMaxY < viewMinY || eMinY > viewMaxY)
                continue;

            // Proietta a schermo solo i segmenti effettivamente intersecati dal viewport
            var p1 = GraphToScreen(source.X, source.Y);
            var p2 = GraphToScreen(target.X, target.Y);

            var isCalleeEdge = _currentHighlightMode == HighlightMode.Callees && _calleeEdgeIds.Contains((edge.SourceId, edge.TargetId));
            var isCallerEdge = _currentHighlightMode == HighlightMode.Callers && _callerEdgeIds.Contains((edge.SourceId, edge.TargetId));
            var isBlastRadiusEdge = _currentHighlightMode == HighlightMode.BlastRadius &&
                                    (_blastRadiusDepths.ContainsKey(edge.SourceId) || _selectedNode?.Id == edge.SourceId) &&
                                    (_blastRadiusDepths.ContainsKey(edge.TargetId) || _selectedNode?.Id == edge.TargetId);
            var isHighlighted = _highlightedEdges.Contains((edge.SourceId, edge.TargetId));

            // Classifica l'arco nell'accumulatore corrispondente allo stile visivo attivo
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

            // LOD Frecce: mostra le frecce solo se l'arco è attivo/evidenziato oppure se lo zoom è sufficiente (scale >= 0.45)
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

                    // Aggiunge le due cuspidi della freccia allo stesso batch della linea (zero allocazioni)
                    targetBatch.AddLine(tip, a1);
                    targetBatch.AddLine(tip, a2);
                }
            }
        }

        // Emette le geometrie batched degli archi: converte migliaia di segmenti in un unico StreamGeometry
        // per penna, eseguendo al massimo 11 draw calls GPU per l'intera rete relazionale.
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
        // 2. RENDERING DEI NODI (Sopra agli archi)
        // =========================================================================
        foreach (var node in graph.Nodes)
        {
            if (!visibleIds.Contains(node.Id)) continue;

            var nodeRadius = node.Radius;
            // Viewport culling in coordinate grafo: scarta il nodo prima delle conversioni a schermo
            if (node.X + nodeRadius < viewMinX || node.X - nodeRadius > viewMaxX ||
                node.Y + nodeRadius < viewMinY || node.Y - nodeRadius > viewMaxY)
                continue;

            var screenPos = GraphToScreen(node.X, node.Y);
            var radius = Math.Max(nodeRadius * Math.Min(_scale, 2.5), 1.0);

            // Viewport culling per performance
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

            // Indicatore visivo di container espanso/compresso
            if (node.IsContainer && radius >= 4)
            {
                IBrush markerColor = node.IsExpanded 
                    ? (_isDarkTheme ? Brushes.WhiteSmoke : new SolidColorBrush(Color.Parse("#4A4A5E"))) 
                    : Brushes.Orange;
                var markerRadius = Math.Min(3.0, radius * 0.4);
                context.DrawEllipse(markerColor, null, screenPos, markerRadius, markerRadius);
            }

            // Etichetta del nodo: Level of Detail (LOD) e FormattedText da cache a zero allocazioni
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
    /// Restituisce il batch di disegno per il tipo di relazione specificato.
    /// Distingue visivamente archi di chiamata (linea continua blu), gestione eventi (tratteggiata arancione),
    /// istanziazione (tratteggiata ambra), uso tipo (puntinata verde smeraldo) o contenimento (grigio scuro/chiaro).
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
    /// Determina il pennello di riempimento del nodo in base allo stato di interazione corrente:
    /// 1. Modalità Blast Radius: gradiente di rischio (Rosso = Target, Arancione/Ambra/Giallo = Chiamanti per livello di profondità, Ciano = Form UI impattate).
    /// 2. Modalità Callees: Oro per il nodo sorgente, Ciano per le routine chiamate, Slate per i relativi moduli.
    /// 3. Modalità Callers: Oro per il nodo target, Ambra per i chiamanti, Slate per i relativi moduli.
    /// 4. Modalità Normale: tavolozza semantica per SymbolKind (Form, Class, Module, Method, Property, Field, Constant, Type, Enum).
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
        // Se siamo in modalità Blast Radius: colora in base alla profondità di rischio
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

            // Nodi non impattati: attenuati
            return _isDarkTheme ? BrushDimmedDark : BrushDimmedLight;
        }

        // Modalità Callees: evidenzia il chiamante sorgente, le routine chiamate (Cyan) e i loro container
        if (_currentHighlightMode == HighlightMode.Callees)
        {
            if (isSelected) return BrushSelected;
            if (isCallee) return BrushCallee;

            if (_selectedNode is { IsContainer: true } && node.ParentId == _selectedNode.Id)
            {
                // Routine interna del container che effettua chiamate
                if (_calleeEdgeIds.Any(e => e.Item1 == node.Id))
                    return BrushRoutineInnerCall;
            }

            if (node.IsContainer && _calleeContainerIds.Contains(node.Id))

                return _isDarkTheme ? BrushContainerCalleeDark : BrushContainerCalleeLight;

            if (_selectedNode?.ParentId == node.Id)

                return _isDarkTheme ? BrushContainerCurrentDark : BrushContainerCurrentLight;

            return _isDarkTheme ? BrushDimmedAltDark : BrushDimmedLight;
        }

        // Modalità Callers: evidenzia la routine target, i chiamanti (Amber/Orange) e i loro container
        if (_currentHighlightMode == HighlightMode.Callers)
        {
            if (isSelected) return BrushSelected;
            if (isCaller) return BrushCaller;

            if (_selectedNode is { IsContainer: true } && node.ParentId == _selectedNode.Id)
            {
                // Routine interna del container chiamata dall'esterno
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
    /// Progressive disclosure e filtraggio per livelli/categorie:
    /// - I container principali (Project, Form, Class, Module, UserControl) sono sempre presenti nello scheletro architetturale.
    /// - I membri figli e le tipologie sono visibili solo se il relativo container è espanso E la rispettiva categoria è abilitata nel filtro.
    /// - Quando un container o un suo membro è selezionato o espanso, tutti i membri di quel container sono resi visibili indipendentemente dal filtro di dettaglio.
    /// - In modalità Blast Radius o Callees/Callers, i nodi impattati e il percorso di chiamata vengono forzati visibili.
    /// </summary>
    private HashSet<Guid> ComputeVisibleNodeIds(VisualCodeGraph graph)
    {
        var visible = new HashSet<Guid>();
        foreach (var node in graph.Nodes)
        {
            // Se il progetto a cui appartiene il nodo o il nodo radice di progetto è nascosto, ignoralo
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

            // Un membro è forzato visibile se:
            // 1. È nel raggio di blast radius o nei percorsi di chiamata calcolati
            // 2. È il nodo attualmente selezionato
            // 3. È un membro del container attualmente selezionato o condivide il container con il membro selezionato
            var isChildOfSelected = _selectedNode != null && (node.ParentId == _selectedNode.Id || (_selectedNode.ParentId.HasValue && node.ParentId == _selectedNode.ParentId));

            var isForcedVisible = _blastRadiusDepths.ContainsKey(node.Id) ||
                                  _calleeNodeIds.Contains(node.Id) ||
                                  _callerNodeIds.Contains(node.Id) ||
                                  _selectedNode?.Id == node.Id ||
                                  isChildOfSelected;

            // Verifica se la categoria del simbolo è abilitata nel filtro attivo
            var isCategoryEnabled = node.Kind switch
            {
                SymbolKind.Method => ShowMethods,
                SymbolKind.Property => ShowProperties,
                SymbolKind.Field => ShowFields,
                SymbolKind.Constant => ShowConstants,
                SymbolKind.Type or SymbolKind.Enum or SymbolKind.Event => ShowTypesAndEnums,
                _ => true
            };

            // Se la categoria non è abilitata nel filtro e il nodo non è forzato/selezionato, non mostrarlo
            if (!isCategoryEnabled && !isForcedVisible)

                continue;

            var parent = node.ParentId.HasValue ? graph.GetNode(node.ParentId.Value) : null;
            if (parent is { IsExpanded: true } || isForcedVisible || !node.ParentId.HasValue)

                visible.Add(node.Id);
        }
        return visible;
    }

    // =========================================================================
    // TRASFORMAZIONI DI COORDINATE (Pan & Zoom)
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

    /// <summary>Centra la vista del canvas su un nodo specifico.</summary>
    public void FocusOnNode(Guid nodeId)
    {
        var node = Graph?.GetNode(nodeId);
        if (node is null) return;

        _offsetX = -node.X * _scale;
        _offsetY = -node.Y * _scale;
        InvalidateVisual();
    }

    // =========================================================================
    // GESTIONE INPUT PUNTATORE (Pan, Zoom, Drag Nodi, Clic, Doppio Clic)
    // =========================================================================

    /// <summary>
    /// Sposta un nodo e ricorsivamente tutti i suoi nodi discendenti (figli diretti e indiretti) dello stesso vettore delta.
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
                // Aggiorna istantaneamente il cursore visivo (Hand) senza alcun ritardo percettibile
                Cursor = new Cursor(StandardCursorType.Hand);

                // Debounce di 45ms per la selezione effettiva: evita di scatenare decine di eventi
                // SelectionChanged e ricalcoli pesanti su chiamanti/callees quando il cursore attraversa rapidamente i nodi
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
    /// Gestisce l'evento di doppio clic per implementare il progressive disclosure:
    /// se il cursore si trova su un nodo container (Class, Form, Module, Type, Enum), ne alterna lo stato di espansione.
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
    /// Ricostruisce la griglia spaziale (Spatial Hash Grid) raggruppando i nodi in celle di 250x250 coordinate grafo.
    /// Consente ricerche e collision test O(1) con zero allocazioni a regime.
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
    /// Esegue il rilevamento del nodo sotto il cursore (Hit Testing) nello spazio coordinate del grafo.
    /// Utilizza la griglia spaziale (Spatial Hash Grid) O(1) per ispezionare solo i nodi delle celle limitrofe
    /// al cursore, garantendo risposta istantanea a 0 ritardi anche con decine di migliaia di nodi.
    /// </summary>
    /// <param name="graphPoint">Coordinate del puntatore convertite nello spazio cartesiano del grafo.</param>
    /// <returns>Il <see cref="GraphNode"/> più vicino entro il raggio di tolleranza, oppure null.</returns>
    public GraphNode? HitTest(Point graphPoint)
    {
        var graph = Graph;
        if (graph is null) return null;

        // Riutilizza l'insieme dei nodi visibili in cache: fondamentale per non allocare
        // un nuovo HashSet<Guid> ad ogni micro-movimento del cursore del mouse (OnPointerMoved).
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

        // Raggio di ricerca celle limitato in base alla scala di zoom per non scandire troppe celle su zoom estremi
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

                    // Raggio di selezione adattivo scalato con il fattore di zoom (minimo 6px di tolleranza)
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
    /// Rileva il nodo posizionato sotto le coordinate a schermo specificate.
    /// </summary>
    public GraphNode? GetNodeAtScreenPos(Point screenPoint) => HitTest(ScreenToGraph(screenPoint));

    // =========================================================================
    // SELEZIONE ED EVIDENZIAZIONE CALLERS / CALLEES / BLAST RADIUS
    // =========================================================================

    /// <summary>
    /// Ripristina lo stato interno delle strutture dati di evidenziazione (chiamanti, chiamati, percorsi e blast radius).
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
    /// Seleziona esplicitamente un nodo sul canvas, evidenziando i suoi archi e vicini diretti
    /// e notificando i listener tramite l'evento <see cref="SelectionChanged"/>.
    /// </summary>
    /// <param name="node">Il nodo da selezionare, oppure null per deselezionare.</param>
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
    /// Evidenzia sul grafo tutti i chiamanti (callers diretti) del nodo selezionato (routine o container).
    /// Se è selezionato un container (Classe, Modulo, Form), aggrega i chiamanti di tutte le routine membro.
    /// Espande automaticamente i container dei chiamanti per rendere i nodi visibili.
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

                    // Auto-espansione del container del caller
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

        // Espandi il container padre del nodo selezionato se presente
        if (_selectedNode.ParentId.HasValue && Graph.GetNode(_selectedNode.ParentId.Value) is { } selParent)
            selParent.IsExpanded = true;

        _currentHighlightMode = HighlightMode.Callers;
        InvalidateVisibilityCache();
        return resultList;
    }

    /// <summary>
    /// Evidenzia sul grafo tutti i chiamati (callees diretti) del nodo selezionato (routine o container).
    /// Se è selezionato un container (Classe, Modulo, Form), aggrega le chiamate di tutte le routine membro.
    /// Espande automaticamente i container di destinazione per rendere i nodi visibili.
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

                    // Auto-espansione del container del callee
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

        // Espandi il container padre del nodo selezionato se presente
        if (_selectedNode.ParentId.HasValue && Graph.GetNode(_selectedNode.ParentId.Value) is { } selParent)
            selParent.IsExpanded = true;

        _currentHighlightMode = HighlightMode.Callees;
        InvalidateVisibilityCache();
        return resultList;
    }

    /// <summary>
    /// Evidenzia sul grafo l'intero Blast Radius (M6): target in rosso, chiamanti per livello di profondità e Form UI toccate.
    /// Espande automaticamente i container dei nodi impattati.
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
    /// Ripristina l'evidenziazione normale.
    /// </summary>
    public void ClearHighlightMode()
    {
        ClearInternalHighlightState();
        _activeBlastRadiusReport = null;
        _currentHighlightMode = HighlightMode.Normal;
        InvalidateVisibilityCache();
    }

    /// <summary>
    /// Applica il tema al canvas del grafo (colore di sfondo ed etichette).
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
