// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AutoClicker;

public partial class HotkeyLightingWindow : Window
{
    public RgbSettings RgbSettings { get; }

    public HotkeyLightingWindow(RgbSettings currentRgbSettings, string hotkeyName)
    {
        InitializeComponent();
        RgbSettings = currentRgbSettings.Clone();
        HintLabel.Text = $"Override lighting for {hotkeyName}.";
        EnabledCheckBox.IsChecked = RgbSettings.Enabled;
        SelectEffect(RgbSettings.LightingEffect);
        SpeedSlider.Value = SpeedToSlider(RgbSettings.EffectSpeedMilliseconds, SelectedEffect());
        UpdateColour(); UpdateSpeed();
    }

    private void ColourButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, Color = ParseColour(RgbSettings.IndicatorColor) };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        RgbSettings.IndicatorColor = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        UpdateColour();
    }
    private void EffectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSpeed();
    private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateSpeed();
    private void UpdateColour()
    {
        ColourLabel.Text = RgbSettings.IndicatorColor.ToUpperInvariant();
        ColourPreview.Background = ColorConverter.ConvertFromString(RgbSettings.IndicatorColor) is Color color ? new SolidColorBrush(color) : new SolidColorBrush(Colors.Transparent);
    }
    private void UpdateSpeed()
    {
        if (SpeedPanel is null) return;
        var effect = SelectedEffect(); var enabled = effect != RgbLightingEffectIds.Constant;
        SpeedPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (!enabled) return;
        var milliseconds = ReadSpeed();
        SpeedLabel.Text = effect == RgbLightingEffectIds.Fade ? $"{milliseconds / 1000d:0.0} s" : $"{milliseconds} ms";
        SpeedHint.Text = effect == RgbLightingEffectIds.Fade ? "Pulse fades smoothly between off and the selected colour." : "Blink switches the key on and off.";
    }
    private void SaveButton_Click(object sender, RoutedEventArgs e) { RgbSettings.Enabled = EnabledCheckBox.IsChecked == true; RgbSettings.LightingEffect = SelectedEffect(); RgbSettings.EffectSpeedMilliseconds = ReadSpeed(); DialogResult = true; }
    private void CloseButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove(); }
    private string SelectedEffect() => (EffectCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() switch { RgbLightingEffectDisplayNames.Pulse => RgbLightingEffectIds.Fade, _ => (EffectCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? RgbLightingEffectIds.Constant };
    private void SelectEffect(string effect) { var display = effect == RgbLightingEffectIds.Fade ? RgbLightingEffectDisplayNames.Pulse : effect == RgbLightingEffectIds.LegacyBlink ? RgbLightingEffectIds.Blink : effect; EffectCombo.SelectedItem = EffectCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Content?.ToString() == display) ?? EffectCombo.Items[0]; }
    private int ReadSpeed() { var p = SpeedSlider.Value / 100d; return SelectedEffect() == RgbLightingEffectIds.Fade ? (int)Math.Round(OpenRgbHighlighter.MaximumFadeCycleMilliseconds - (OpenRgbHighlighter.MaximumFadeCycleMilliseconds - OpenRgbHighlighter.MinimumFadeCycleMilliseconds) * p) : (int)Math.Round(2000d - 1880d * p); }
    private static double SpeedToSlider(int milliseconds, string effect) => effect == RgbLightingEffectIds.Fade ? (OpenRgbHighlighter.MaximumFadeCycleMilliseconds - Math.Clamp(milliseconds, OpenRgbHighlighter.MinimumFadeCycleMilliseconds, OpenRgbHighlighter.MaximumFadeCycleMilliseconds)) * 100d / (OpenRgbHighlighter.MaximumFadeCycleMilliseconds - OpenRgbHighlighter.MinimumFadeCycleMilliseconds) : (2000d - Math.Clamp(milliseconds, 120, 2000)) * 100d / 1880d;
    private static System.Drawing.Color ParseColour(string value) => OpenRgbHighlighter.TryNormalizeIndicatorColor(value, out var hex) ? System.Drawing.Color.FromArgb(Convert.ToInt32(hex[1..3], 16), Convert.ToInt32(hex[3..5], 16), Convert.ToInt32(hex[5..7], 16)) : System.Drawing.Color.White;
}
