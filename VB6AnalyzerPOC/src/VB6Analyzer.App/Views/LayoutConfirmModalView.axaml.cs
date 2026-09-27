using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Macondo.App.Layout;
using Macondo.App.Services;

namespace Macondo.App.Views;

/// <summary>
/// Finestra modale di avviso e conferma prima del cambio algoritmo di layout del grafo quando l'utente ha spostato nodi manualmente.
/// </summary>
public partial class LayoutConfirmModalView : UserControl
{
    private GraphLayoutMode _pendingLayoutMode;

    /// <summary>
    /// Evento scatenato quando l'utente conferma il cambio di layout.
    /// </summary>
    public event EventHandler<GraphLayoutMode>? Confirmed;

    /// <summary>
    /// Evento scatenato quando l'utente annulla il cambio di layout.
    /// </summary>
    public event EventHandler? Cancelled;

    /// <summary>
    /// Inizializza una nuova istanza di <see cref="LayoutConfirmModalView"/>.
    /// </summary>
    public LayoutConfirmModalView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Mostra la modale di conferma layout salvando la modalità richiesta.
    /// </summary>
    public void Show(GraphLayoutMode mode)
    {
        _pendingLayoutMode = mode;
        UpdateLocalizedUI();
        IsVisible = true;
    }

    /// <summary>
    /// Nasconde la modale di conferma layout.
    /// </summary>
    public void Hide()
    {
        IsVisible = false;
    }

    /// <summary>
    /// Aggiorna i testi localizzati della modale.
    /// </summary>
    public void UpdateLocalizedUI()
    {
        var localizationService = LocalizationService.Instance;
        LayoutConfirmModalTitle.Text = localizationService["Layout_ConfirmTitle"];
        LayoutConfirmModalSubtitle.Text = localizationService["Layout_ConfirmMessage"];
        LayoutConfirmCancelButton.Content = localizationService["Layout_ConfirmCancel"];
        LayoutConfirmApplyButton.Content = localizationService["Layout_ConfirmApply"];
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Hide();
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    private void OnApplyClicked(object? sender, RoutedEventArgs e)
    {
        Hide();
        Confirmed?.Invoke(this, _pendingLayoutMode);
    }
}
