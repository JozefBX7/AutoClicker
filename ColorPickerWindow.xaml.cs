using System.Windows;
using System.Windows.Media;

namespace AutoClicker;

public partial class ColorPickerWindow : Window
{
    private readonly Func<string, Task<string?>> previewHotkeyAsync;
    public string SelectedColor => HexBox.Text;

    public ColorPickerWindow(string initialColor, Func<string, Task<string?>> previewHotkeyAsync)
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
        var current = ParseColor(SelectedColor);
        using var dialog = new System.Windows.Forms.ColorDialog { Color = current, FullOpen = true, AnyColor = true };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        var selected = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        SetColor(selected);
        PickColorButton.IsEnabled = false;
        PreviewStatus.Text = "Flashing the selected hotkey with this colour…";
        PreviewStatus.Foreground = ThemeManager.Brush("TextMutedBrush");
        try
        {
            var error = await previewHotkeyAsync(selected);
            PreviewStatus.Text = error is null ? "Preview complete; the previous key colour was restored." : error;
            PreviewStatus.Foreground = ThemeManager.Brush(error is null ? "SuccessBrush" : "ErrorBrush");
        }
        catch (Exception exception)
        {
            AppLog.Error("Indicator colour preview failed", exception);
            PreviewStatus.Text = $"Could not preview the key: {exception.Message}";
            PreviewStatus.Foreground = ThemeManager.Brush("ErrorBrush");
        }
        finally { PickColorButton.IsEnabled = true; }
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

    private void ConfirmButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
