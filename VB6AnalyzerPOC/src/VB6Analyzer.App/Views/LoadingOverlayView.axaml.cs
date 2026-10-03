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
        LoadingCancelButton.Click += OnCancelClicked;
    }

    /// <summary>
    /// Mostra l'overlay di caricamento con titolo e sottotitolo personalizzati.
    /// </summary>
    public void Show(string title, string subtitle, bool canCancel = true, string? timeText = null)
    {
        LoadingTitle.Text = title;
        LoadingSubtitle.Text = subtitle;
        LoadingCancelButton.IsVisible = canCancel;
        LoadingCancelButton.IsEnabled = true;
        UpdateLocalizedUI();
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
    public void UpdateProgress(int current, int total, string countText, string statsText, string? timeText = null, string? subtitle = null)
    {
        if (!IsVisible || !LoadingCancelButton.IsEnabled)
            return;

        if (subtitle != null && LoadingSubtitle.Text != subtitle)
            LoadingSubtitle.Text = subtitle;

        if (LoadingProgressBar.IsIndeterminate)
            LoadingProgressBar.IsIndeterminate = false;

        var max = total > 0 ? total : 100;
        if (LoadingProgressBar.Maximum != max)
            LoadingProgressBar.Maximum = max;

        LoadingProgressBar.Minimum = 0;
        LoadingProgressBar.Value = current;

        if (LoadingProgressCount.Text != countText)
            LoadingProgressCount.Text = countText;

        var showCount = !string.IsNullOrEmpty(countText);
        if (LoadingProgressCount.IsVisible != showCount)
            LoadingProgressCount.IsVisible = showCount;

        if (LoadingProgressStats.Text != statsText)
            LoadingProgressStats.Text = statsText;

        var showStats = !string.IsNullOrEmpty(statsText);
        if (LoadingProgressStats.IsVisible != showStats)
            LoadingProgressStats.IsVisible = showStats;

        var timeStr = timeText ?? string.Empty;
        if (LoadingProgressTime.Text != timeStr)
            LoadingProgressTime.Text = timeStr;

        var showTime = !string.IsNullOrEmpty(timeStr);
        if (LoadingProgressTime.IsVisible != showTime)
            LoadingProgressTime.IsVisible = showTime;
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
        LoadingCancelButton.IsEnabled = false;
        LoadingSubtitle.Text = LocalizationService.Instance["Loading_Cancelling"];
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }
}
