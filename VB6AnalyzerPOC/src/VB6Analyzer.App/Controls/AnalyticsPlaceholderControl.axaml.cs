using System;
using Avalonia.Controls;
using Macondo.App.Models;
using Macondo.App.Services;

namespace Macondo.App.Controls;

/// <summary>
/// Controllo placeholder visualizzato all'interno delle schede analitiche quando la
/// pre-elaborazione automatica in background è disabilitata e l'analisi non è ancora stata eseguita.
/// Permette all'utente di avviare l'analisi on-demand per la singola scheda o per tutte le schede rimanenti.
/// </summary>
public partial class AnalyticsPlaceholderControl : UserControl
{
    public event EventHandler? StartAnalysisRequested;
    public event EventHandler? StartAllRequested;

    public AnalyticsPlaceholderControl()
    {
        InitializeComponent();

        var startBtn = this.FindControl<Button>("StartButton");
        if (startBtn != null)
        {
            startBtn.Click += (_, _) => StartAnalysisRequested?.Invoke(this, EventArgs.Empty);
        }

        var startAllBtn = this.FindControl<Button>("StartAllButton");
        if (startAllBtn != null)
        {
            startAllBtn.Click += (_, _) => StartAllRequested?.Invoke(this, EventArgs.Empty);
        }

        UpdateLocalizedUI();
    }

    public void SetState(bool isPending, bool isComputing)
    {
        if (!isPending && !isComputing)
        {
            IsVisible = false;
            return;
        }

        IsVisible = true;
        var cardActionPanel = this.FindControl<StackPanel>("CardActionPanel");
        var computingContainer = this.FindControl<StackPanel>("ComputingContainer");

        if (cardActionPanel != null)
        {
            cardActionPanel.IsVisible = !isComputing;
        }
        if (computingContainer != null)
        {
            computingContainer.IsVisible = isComputing;
        }
    }

    public void UpdateLocalizedUI()
    {
        var loc = LocalizationService.Instance;
        var titleText = this.FindControl<TextBlock>("TitleText");
        var descText = this.FindControl<TextBlock>("DescText");
        var startBtnText = this.FindControl<TextBlock>("StartBtnText");
        var startAllBtnText = this.FindControl<TextBlock>("StartAllBtnText");
        var computingText = this.FindControl<TextBlock>("ComputingText");

        if (titleText != null) titleText.Text = loc["Analytics_NotComputedTitle"];
        if (descText != null) descText.Text = loc["Analytics_NotComputedDesc"];
        if (startBtnText != null) startBtnText.Text = loc["Analytics_StartButton"];
        if (startAllBtnText != null) startAllBtnText.Text = loc["Analytics_StartAllButton"];
        if (computingText != null) computingText.Text = loc["Analytics_Computing"];
    }

    public void ApplyTheme(AppTheme theme)
    {
        // I pennelli del tema sono collegati dinamicamente nel markup AXAML tramite DynamicResource
    }
}
