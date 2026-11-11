using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Macondo.App.Services;

namespace Macondo.App.Views;

/// <summary>
/// Full screen overlay with loading animation and progress bar for heavy asynchronous operations.
/// </summary>
public partial class LoadingOverlayView : UserControl
{
    /// <summary>
    /// Event triggered when the user requests to cancel the current operation.
    /// </summary>
    public event EventHandler? CancelRequested;

    /// <summary>
    /// Initializes a new instance of <see cref="LoadingOverlayView"/>.
    /// </summary>
    public LoadingOverlayView()
    {
        InitializeComponent();
        LoadingCancelButton.Click += OnCancelClicked;
    }

    /// <summary>
    /// Show loading overlay with custom title and subtitle.
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
    /// Updates progress values and numerical details of the loading bar.
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
    /// Hides the loading overlay.
    /// </summary>
    public void Hide()
    {
        IsVisible = false;
    }

    /// <summary>
    /// Update localized cancel button strings.
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
