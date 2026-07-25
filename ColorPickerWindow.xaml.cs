using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AutoClicker;

public partial class ColorPickerWindow : Window
{
    private readonly DispatcherTimer previewTimer = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private bool updating;
    public event Action<string>? PreviewColorChanged;
    public string SelectedColor => HexBox.Text;

    public ColorPickerWindow(string initialColor, string previewHint)
    {
        InitializeComponent();
        PreviewHint.Text = previewHint;
        previewTimer.Tick += (_, _) =>
        {
            previewTimer.Stop();
            PreviewColorChanged?.Invoke(SelectedColor);
        };
        SetColor(initialColor);
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove();
    }

    private void ChannelSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (updating) return;
        SetColor($"#{(byte)RedSlider.Value:X2}{(byte)GreenSlider.Value:X2}{(byte)BlueSlider.Value:X2}");
    }

    private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (updating || !OpenRgbHighlighter.TryNormalizeIndicatorColor(HexBox.Text, out var normalized)) return;
        SetColor(normalized);
    }

    private void SetColor(string value)
    {
        OpenRgbHighlighter.TryNormalizeIndicatorColor(value, out var hex);
        updating = true;
        try
        {
            var red = Convert.ToByte(hex[1..3], 16);
            var green = Convert.ToByte(hex[3..5], 16);
            var blue = Convert.ToByte(hex[5..7], 16);
            RedSlider.Value = red; GreenSlider.Value = green; BlueSlider.Value = blue;
            RedValue.Text = red.ToString(); GreenValue.Text = green.ToString(); BlueValue.Text = blue.ToString();
            HexBox.Text = hex;
            ColorSwatch.Background = new SolidColorBrush(Color.FromRgb(red, green, blue));
        }
        finally { updating = false; }
        previewTimer.Stop();
        previewTimer.Start();
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        previewTimer.Stop();
        PreviewColorChanged?.Invoke(SelectedColor);
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    protected override void OnClosed(EventArgs e) { previewTimer.Stop(); base.OnClosed(e); }
}
