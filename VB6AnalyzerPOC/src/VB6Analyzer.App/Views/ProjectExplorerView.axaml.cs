using System;
using System.Collections;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Macondo.App.Models;
using Macondo.App.Services;

namespace Macondo.App.Views;

/// <summary>
/// Controllo visuale per il pannello laterale Project Explorer.
/// Gestisce l'albero gerarchico di progetti, moduli, file e simboli, con filtri di visibilità e menu contestuale.
/// </summary>
public partial class ProjectExplorerView : UserControl
{
    /// <summary>
    /// Evento scatenato quando cambia l'elemento selezionato nell'albero.
    /// </summary>
    public event EventHandler<ExplorerTreeItem?>? SelectionChanged;

    /// <summary>
    /// Evento scatenato quando si fa doppio clic su un elemento dell'albero.
    /// </summary>
    public event EventHandler<ExplorerTreeItem>? ItemDoubleTapped;

    /// <summary>
    /// Evento scatenato quando viene richiesta l'apertura del file sorgente originale.
    /// </summary>
    public event EventHandler<ExplorerTreeItem>? OpenFileRequested;

    /// <summary>
    /// Evento scatenato quando viene richiesta l'apertura della cartella contenitrice.
    /// </summary>
    public event EventHandler<ExplorerTreeItem>? OpenFolderRequested;

    /// <summary>
    /// Evento scatenato quando viene richiesta la copia del percorso del file negli appunti.
    /// </summary>
    public event EventHandler<ExplorerTreeItem>? CopyPathRequested;

    /// <summary>
    /// Evento scatenato quando cambia lo stato del checkbox di visibilità di un progetto nel workspace multi-progetto.
    /// </summary>
    public event EventHandler<ExplorerTreeItem>? ProjectVisibilityChanged;

    /// <summary>
    /// Evento scatenato quando si clicca sul pulsante "Tutti" nella barra di filtro progetti.
    /// </summary>
    public event EventHandler? SelectAllProjectsRequested;

    public ProjectExplorerView()
    {
        InitializeComponent();
        ExplorerTree.AddHandler(PointerPressedEvent, OnExplorerTreePointerPressed, RoutingStrategies.Tunnel);
        ExplorerTree.ContextRequested += OnExplorerTreeContextRequested;
    }

    /// <summary>
    /// Ottiene o imposta la sorgente dati per l'albero del Project Explorer.
    /// </summary>
    public IEnumerable? ItemsSource
    {
        get => ExplorerTree.ItemsSource;
        set => ExplorerTree.ItemsSource = value;
    }

    /// <summary>
    /// Ottiene o imposta l'elemento attualmente selezionato nel TreeView.
    /// </summary>
    public ExplorerTreeItem? SelectedItem
    {
        get => ExplorerTree.SelectedItem as ExplorerTreeItem;
        set => ExplorerTree.SelectedItem = value;
    }

    /// <summary>
    /// Aggiorna la visibilità e il testo della barra di filtro rapido dei progetti in caso di workspace multi-progetto.
    /// </summary>
    /// <param name="isVisible">True se la barra di filtro progetti deve essere visibile.</param>
    /// <param name="countText">Testo formattato con il conteggio dei progetti visibili (es. "2/3 Progetti visibili").</param>
    public void UpdateProjectFilterBar(bool isVisible, string countText)
    {
        ExplorerProjectFilterBar.IsVisible = isVisible;
        ExplorerProjectsCountText.Text = countText;
    }

    /// <summary>
    /// Aggiorna i testi localizzati del titolo del pannello, della barra filtri e del menu contestuale.
    /// </summary>
    public void UpdateLocalizedUI()
    {
        var loc = LocalizationService.Instance;
        ExplorerTitleText.Text = loc["Explorer_Title"];
        ExplorerProjectsSelectAllButton.Content = loc["Explorer_All"];
        CtxExplorerOpenFile.Header = loc["Ctx_OpenFile"];
        CtxExplorerOpenFolder.Header = loc["Ctx_OpenFolder"];
        CtxExplorerCopyPath.Header = loc["Ctx_CopyPath"];
    }

    private void OnExplorerSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var item = ExplorerTree.SelectedItem as ExplorerTreeItem;
        SelectionChanged?.Invoke(this, item);
    }

    private void OnExplorerDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ExplorerTree.SelectedItem is ExplorerTreeItem item)

            ItemDoubleTapped?.Invoke(this, item);
    }

    private void OnExplorerOpenItemButtonClick(object? sender, RoutedEventArgs e)
    {
        var item = GetExplorerItemFromSender(sender);
        if (item != null)

            OpenFileRequested?.Invoke(this, item);
    }

    private void OnProjectsSelectAllClicked(object? sender, RoutedEventArgs e)
    {
        SelectAllProjectsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnExplorerProjectCheckBoxClick(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.CommandParameter is ExplorerTreeItem item)

            ProjectVisibilityChanged?.Invoke(this, item);
    }

    private void OnExplorerContextMenuOpenFile(object? sender, RoutedEventArgs e)
    {
        var item = GetExplorerItemFromSender(sender);
        if (item != null && !item.IsGroup && item.HasFile)

            OpenFileRequested?.Invoke(this, item);
    }

    private void OnExplorerContextMenuOpenFolder(object? sender, RoutedEventArgs e)
    {
        var item = GetExplorerItemFromSender(sender);
        if (item != null && !item.IsGroup && item.HasFile)

            OpenFolderRequested?.Invoke(this, item);
    }

    private void OnExplorerContextMenuCopyPath(object? sender, RoutedEventArgs e)
    {
        var item = GetExplorerItemFromSender(sender);
        if (item != null && !item.IsGroup && item.HasFile)

            CopyPathRequested?.Invoke(this, item);
    }

    private void OnExplorerTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(ExplorerTree);
        if (point.Properties.IsRightButtonPressed)
        {
            var visual = e.Source as Visual;
            var tvi = visual?.FindAncestorOfType<TreeViewItem>();
            if (tvi?.DataContext is ExplorerTreeItem item)

                ExplorerTree.SelectedItem = item;
        }
    }

    private void OnExplorerTreeContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        var visual = e.Source as Visual;
        var tvi = visual?.FindAncestorOfType<TreeViewItem>();
        var item = tvi?.DataContext as ExplorerTreeItem ?? ExplorerTree.SelectedItem as ExplorerTreeItem;

        if (item == null || item.IsGroup || !item.HasFile)
        {
            e.Handled = true;
            return;
        }

        if (Resources.TryGetResource("ExplorerFileContextMenu", null, out var res) && res is ContextMenu menu)
        {
            menu.DataContext = item;
            menu.PlacementTarget = tvi ?? (Control)e.Source!;
            menu.Open(tvi ?? (Control)e.Source!);
            e.Handled = true;
        }
    }

    private ExplorerTreeItem? GetExplorerItemFromSender(object? sender)
    {
        if (sender is Button btn && btn.CommandParameter is ExplorerTreeItem itemParam)
            return itemParam;

        if (sender is MenuItem menuItem)
        {
            if (menuItem.DataContext is ExplorerTreeItem item1)
                return item1;
            if (menuItem.Parent is ContextMenu ctx && ctx.DataContext is ExplorerTreeItem itemCtx)
                return itemCtx;
            if (menuItem.Parent is ContextMenu ctx2 && ctx2.PlacementTarget?.DataContext is ExplorerTreeItem itemTarget)
                return itemTarget;
            if (menuItem.Parent is ContextMenu ctxTvi && ctxTvi.PlacementTarget is TreeViewItem tvi && tvi.DataContext is ExplorerTreeItem itemTvi)
                return itemTvi;
        }

        if (sender is ContextMenu ctxMenu)
        {
            if (ctxMenu.DataContext is ExplorerTreeItem itemCtx)
                return itemCtx;
            if (ctxMenu.PlacementTarget?.DataContext is ExplorerTreeItem itemTarget)
                return itemTarget;
            if (ctxMenu.PlacementTarget is TreeViewItem tvi2 && tvi2.DataContext is ExplorerTreeItem itemTvi2)
                return itemTvi2;
        }

        if (sender is Control ctrl && ctrl.DataContext is ExplorerTreeItem item3)
            return item3;

        return ExplorerTree.SelectedItem as ExplorerTreeItem;
    }
}
