using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Macondo.App.Services;

namespace Macondo.App.Views;

/// <summary>
/// Overlay a schermo intero con animazione di caricamento e barra di avanzamento per operazioni asincrone pesanti.
/// </summary>
public partial class LoadingOverlayView : UserControl
{
    /// <summary>
    /// Evento scatenato quando l'utente richiede di annullare l'operazione in corso.
    /// </summary>
    public event EventHandler? CancelRequested;

    /// <summary>
    /// Inizializza una nuova istanza di <see cref="LoadingOverlayView"/>.
    /// </summary>
    public LoadingOverlayView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Mostra l'overlay di caricamento con titolo e sottotitolo personalizzati.
    /// </summary>
    public void Show(string title, string subtitle, bool canCancel = true, string? timeText = null)
    {
        LoadingTitle.Text = title;
        LoadingSubtitle.Text = subtitle;
        LoadingCancelButton.IsVisible = canCancel;
        LoadingProgressBar.IsIndeterminate = true;
        LoadingProgressCount.IsVisible = false;
        LoadingProgressStats.IsVisible = false;
        LoadingProgressTime.Text = timeText ?? string.Empty;
        LoadingProgressTime.IsVisible = !string.IsNullOrEmpty(timeText);
        IsVisible = true;
    }

    /// <summary>
    /// Aggiorna i valori di avanzamento e i dettagli numerici della barra di caricamento.
    /// </summary>
    public void UpdateProgress(int current, int total, string countText, string statsText, string? timeText = null)
    {
        LoadingProgressBar.IsIndeterminate = false;
        LoadingProgressBar.Minimum = 0;
        LoadingProgressBar.Maximum = total > 0 ? total : 100;
        LoadingProgressBar.Value = current;

        LoadingProgressCount.Text = countText;
        LoadingProgressCount.IsVisible = !string.IsNullOrEmpty(countText);

        LoadingProgressStats.Text = statsText;
        LoadingProgressStats.IsVisible = !string.IsNullOrEmpty(statsText);

        LoadingProgressTime.Text = timeText ?? string.Empty;
        LoadingProgressTime.IsVisible = !string.IsNullOrEmpty(timeText);
    }

    /// <summary>
    /// Nasconde l'overlay di caricamento.
    /// </summary>
    public void Hide()
    {
        IsVisible = false;
    }

    /// <summary>
    /// Aggiorna le stringhe localizzate del pulsante di annullamento.
    /// </summary>
    public void UpdateLocalizedUI()
    {
        LoadingCancelButton.Content = LocalizationService.Instance["Loading_Cancel"];
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }
}
