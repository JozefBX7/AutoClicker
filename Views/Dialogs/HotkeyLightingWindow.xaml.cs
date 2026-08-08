using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AutoClicker;

public partial class HotkeyLightingWindow : Window
{
    public RgbSettings Settings { get; }

    public HotkeyLightingWindow(RgbSettings current, string hotkeyName)
    {
        InitializeComponent();
        Settings = new RgbSettings { Enabled = current.Enabled, DeviceIndex = current.DeviceIndex, DeviceName = current.DeviceName, AutoStart = current.AutoStart, StopAutoStartedOnExit = current.StopAutoStartedOnExit, IndicatorColor = current.IndicatorColor, LightingEffect = current.LightingEffect, PulseSpeedMilliseconds = current.PulseSpeedMilliseconds };
        HintLabel.Text = $"Override lighting for {hotkeyName}.";
        EnabledCheckBox.IsChecked = Settings.Enabled;
        SelectEffect(Settings.LightingEffect);
        SpeedSlider.Value = SpeedToSlider(Settings.PulseSpeedMilliseconds, SelectedEffect());
        UpdateColour(); UpdateSpeed();
    }

    private void ColourButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, Color = ParseColour(Settings.IndicatorColor) };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        Settings.IndicatorColor = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        UpdateColour();
    }
    private void EffectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSpeed();
    private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateSpeed();
    private void UpdateColour()
    {
        ColourLabel.Text = Settings.IndicatorColor.ToUpperInvariant();
        ColourPreview.Background = ColorConverter.ConvertFromString(Settings.IndicatorColor) is Color color ? new SolidColorBrush(color) : new SolidColorBrush(Colors.Transparent);
    }
    private void UpdateSpeed()
    {
        if (SpeedPanel is null) return;
        var effect = SelectedEffect(); var enabled = effect != "Constant";
        SpeedPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (!enabled) return;
        var milliseconds = ReadSpeed();
        SpeedLabel.Text = effect == "Fade" ? $"{milliseconds / 1000d:0.0} s" : $"{milliseconds} ms";
        SpeedHint.Text = effect == "Fade" ? "Pulse fades smoothly between off and the selected colour." : "Blink switches the key on and off.";
    }
    private void SaveButton_Click(object sender, RoutedEventArgs e) { Settings.Enabled = EnabledCheckBox.IsChecked == true; Settings.LightingEffect = SelectedEffect(); Settings.PulseSpeedMilliseconds = ReadSpeed(); DialogResult = true; }
    private void CloseButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove(); }
    private string SelectedEffect() => (EffectCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() switch { "Pulse" => "Fade", _ => (EffectCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Constant" };
    private void SelectEffect(string effect) { var display = effect == "Fade" ? "Pulse" : effect == "Pulse" ? "Blink" : effect; EffectCombo.SelectedItem = EffectCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Content?.ToString() == display) ?? EffectCombo.Items[0]; }
    private int ReadSpeed() { var p = SpeedSlider.Value / 100d; return SelectedEffect() == "Fade" ? (int)Math.Round(OpenRgbHighlighter.MaximumPulseCycleMilliseconds - (OpenRgbHighlighter.MaximumPulseCycleMilliseconds - OpenRgbHighlighter.MinimumPulseCycleMilliseconds) * p) : (int)Math.Round(2000d - 1880d * p); }
    private static double SpeedToSlider(int milliseconds, string effect) => effect == "Fade" ? (OpenRgbHighlighter.MaximumPulseCycleMilliseconds - Math.Clamp(milliseconds, OpenRgbHighlighter.MinimumPulseCycleMilliseconds, OpenRgbHighlighter.MaximumPulseCycleMilliseconds)) * 100d / (OpenRgbHighlighter.MaximumPulseCycleMilliseconds - OpenRgbHighlighter.MinimumPulseCycleMilliseconds) : (2000d - Math.Clamp(milliseconds, 120, 2000)) * 100d / 1880d;
    private static System.Drawing.Color ParseColour(string value) => OpenRgbHighlighter.TryNormalizeIndicatorColor(value, out var hex) ? System.Drawing.Color.FromArgb(Convert.ToInt32(hex[1..3], 16), Convert.ToInt32(hex[3..5], 16), Convert.ToInt32(hex[5..7], 16)) : System.Drawing.Color.White;
}
