using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Macondo.App.Services;

namespace Macondo.App.Views;

/// <summary>
/// Modal notification window following successful file exports to disk.
/// </summary>
public partial class ExportSuccessModalView : UserControl
{
    private string? _fullPath;

    /// <summary>
    /// Event triggered when the user requests to open the folder containing the exported file.
    /// </summary>
    public event EventHandler<string>? OpenFolderRequested;

    /// <summary>
    /// Event triggered when the modal is closed by the user.
    /// </summary>
    public event EventHandler? Closed;

    /// <summary>
    /// Initializes a new instance of <see cref="ExportSuccessModalView"/>.
    /// </summary>
    public ExportSuccessModalView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Show the modal by valorising the file name and the complete path.
    /// </summary>
    public void Show(string fileName, string fullPath)
    {
        _fullPath = fullPath;
        ExportSuccessFileName.Text = fileName;
        ExportSuccessFilePath.Text = fullPath;
        UpdateLocalizedUI();
        IsVisible = true;
    }

    /// <summary>
    /// Hides the modal.
    /// </summary>
    public void Hide()
    {
        IsVisible = false;
        _fullPath = null;
    }

    /// <summary>
    /// Update localized modal texts.
    /// </summary>
    public void UpdateLocalizedUI()
    {
        var localizationService = LocalizationService.Instance;
        ExportSuccessModalTitle.Text = localizationService["Export_ModalTitle"];
        ExportSuccessModalSubtitle.Text = localizationService["Export_ModalSubtitle"];
        ExportSuccessFileLabel.Text = localizationService["Export_ModalFileLabel"];
        ExportSuccessPathLabel.Text = localizationService["Export_ModalPathLabel"];
        ExportSuccessOpenFolderButton.Content = localizationService["Export_ModalOpenFolder"];
        ExportSuccessCloseButton.Content = localizationService["Export_ModalOk"];
    }

    private void OnOpenFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_fullPath))

            OpenFolderRequested?.Invoke(this, _fullPath);
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Hide();
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
