using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Macondo.App.Models;
using Macondo.App.Services;

namespace Macondo.App.Views;

/// <summary>
/// Modal window for notification and download of new software updates.
/// </summary>
public partial class UpdateModalView : UserControl
{
    private AppUpdateInfo? _availableUpdate;

    /// <summary>
    /// Event triggered when user chooses to download/install update.
    /// </summary>
    public event EventHandler<AppUpdateInfo>? UpdateNowRequested;

    /// <summary>
    /// Event triggered when user chooses to postpone update.
    /// </summary>
    public event EventHandler? UpdateLaterRequested;

    /// <summary>
    /// Initializes a new instance of <see cref="UpdateModalView"/>.
    /// </summary>
    public UpdateModalView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Show the modal by populating it with the available release information.
    /// </summary>
    public void Show(AppUpdateInfo updateInfo)
    {
        _availableUpdate = updateInfo;
        UpdateLocalizedUI();
        IsVisible = true;
    }

    /// <summary>
    /// Hides the update modal.
    /// </summary>
    public void Hide()
    {
        IsVisible = false;
        _availableUpdate = null;
    }

    /// <summary>
    /// Update localized modal texts.
    /// </summary>
    public void UpdateLocalizedUI()
    {
        if (_availableUpdate == null) return;

        var localizationService = LocalizationService.Instance;
        UpdateModalTitle.Text = localizationService["Update_AvailableTitle"];
        UpdateModalSubtitle.Text = localizationService.Get("Update_AvailableSubtitle", _availableUpdate.VersionString, UpdateService.CurrentVersionString);
        UpdateModalNotesLabel.Text = localizationService["Update_NotesLabel"];
        UpdateModalNotes.Text = _availableUpdate.GetReleaseNotes(localizationService.CurrentLanguage == AppLanguage.Italian ? "it" : "en");
        UpdateLaterButton.Content = localizationService["Update_ButtonLater"];
        UpdateNowButton.Content = localizationService["Update_ButtonNow"];
    }

    private void OnUpdateNowClicked(object? sender, RoutedEventArgs e)
    {
        if (_availableUpdate != null)

            UpdateNowRequested?.Invoke(this, _availableUpdate);
    }

    private void OnUpdateLaterClicked(object? sender, RoutedEventArgs e)
    {
        Hide();
        UpdateLaterRequested?.Invoke(this, EventArgs.Empty);
    }
}
