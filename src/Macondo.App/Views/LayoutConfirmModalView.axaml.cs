using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Macondo.App.Layout;
using Macondo.App.Services;

namespace Macondo.App.Views;

/// <summary>
/// Warning and confirmation modal window before graph layout algorithm change when the user has moved nodes manually.
/// </summary>
public partial class LayoutConfirmModalView : UserControl
{
    private GraphLayoutMode _pendingLayoutMode;

    /// <summary>
    /// Event triggered when the user confirms the layout change.
    /// </summary>
    public event EventHandler<GraphLayoutMode>? Confirmed;

    /// <summary>
    /// Event triggered when user cancels layout change.
    /// </summary>
    public event EventHandler? Cancelled;

    /// <summary>
    /// Initializes a new instance of <see cref="LayoutConfirmModalView"/>.
    /// </summary>
    public LayoutConfirmModalView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Show layout confirmation modal by saving the requested mode.
    /// </summary>
    public void Show(GraphLayoutMode mode)
    {
        _pendingLayoutMode = mode;
        UpdateLocalizedUI();
        IsVisible = true;
    }

    /// <summary>
    /// Hides the layout confirmation modal.
    /// </summary>
    public void Hide()
    {
        IsVisible = false;
    }

    /// <summary>
    /// Update localized modal texts.
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
