// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace AutoClicker;

public partial class ColorPickerWindow : Window
{
    private readonly Func<string, CancellationToken, Task<string?>> previewHotkeyAsync;
    private CancellationTokenSource? previewCancellation;
    private Task? previewTask;
    public string SelectedColor => HexBox.Text;

    public ColorPickerWindow(string initialColor, Func<string, CancellationToken, Task<string?>> previewHotkeyAsync)
    {
        InitializeComponent();
        this.previewHotkeyAsync = previewHotkeyAsync;
        SetColor(initialColor);
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove();
    }

    private async void PickColorButton_Click(object sender, RoutedEventArgs e)
    {
        var activePreview = previewTask;
        previewCancellation?.Cancel();

        var current = ParseColor(SelectedColor);
        using var dialog = new System.Windows.Forms.ColorDialog { Color = current, FullOpen = true, AnyColor = true };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        var selected = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        SetColor(selected);
        if (activePreview is not null) await activePreview;
        await StartPreviewSelectedColorAsync();
    }

    private async void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (previewTask is not null)
        {
            previewCancellation?.Cancel();
            return;
        }

        await StartPreviewSelectedColorAsync();
    }

    private async Task StartPreviewSelectedColorAsync()
    {
        if (previewTask is not null) return;

        previewTask = PreviewSelectedColorAsync();
        try { await previewTask; }
        finally { previewTask = null; }
    }

    private async Task PreviewSelectedColorAsync()
    {
        if (previewCancellation is not null) return;

        using var cancellation = new CancellationTokenSource();
        previewCancellation = cancellation;
        PreviewButton.Content = "■ Cancel preview";
        PreviewStatus.Text = "Preview in progress: the selected hotkey is lit with this colour for 5 seconds…";
        PreviewStatus.Foreground = ThemeManager.Brush("TextMutedBrush");
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        try
        {
            var error = await previewHotkeyAsync(SelectedColor, cancellation.Token);
            PreviewStatus.Text = error is null ? "Preview complete; the previous key colour was restored." : error;
            PreviewStatus.Foreground = ThemeManager.Brush(error is null ? "SuccessBrush" : "ErrorBrush");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            PreviewStatus.Text = "Preview cancelled; the previous key colour was restored.";
            PreviewStatus.Foreground = ThemeManager.Brush("TextMutedBrush");
        }
        catch (Exception exception)
        {
            AppLog.Error("Indicator colour preview failed", exception);
            PreviewStatus.Text = $"Could not preview the key: {exception.Message}";
            PreviewStatus.Foreground = ThemeManager.Brush("ErrorBrush");
        }
        finally
        {
            if (ReferenceEquals(previewCancellation, cancellation)) previewCancellation = null;
            PreviewButton.Content = "▶ Preview";
            PreviewButton.IsEnabled = true;
        }
    }

    private void SetColor(string value)
    {
        OpenRgbHighlighter.TryNormalizeIndicatorColor(value, out var hex);
        HexBox.Text = hex;
        ColorSwatch.Background = new SolidColorBrush(Color.FromRgb(
            Convert.ToByte(hex[1..3], 16),
            Convert.ToByte(hex[3..5], 16),
            Convert.ToByte(hex[5..7], 16)));
    }

    private static System.Drawing.Color ParseColor(string value)
    {
        OpenRgbHighlighter.TryNormalizeIndicatorColor(value, out var hex);
        return System.Drawing.Color.FromArgb(
            Convert.ToInt32(hex[1..3], 16),
            Convert.ToInt32(hex[3..5], 16),
            Convert.ToInt32(hex[5..7], 16));
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e) => previewCancellation?.Cancel();

    private void ConfirmButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
