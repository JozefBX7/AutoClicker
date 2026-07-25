using System.Windows;
using System.Windows.Media;

namespace AutoClicker;

public partial class SettingsWindow : Window
{
    public RgbSettings Settings { get; }
    private readonly string hotkeyName;
    private readonly string hotkeyKeyName;
    private readonly Func<bool> resetToDefaults;
    private readonly Func<string, string?> exportBackup;
    private readonly Func<string, string?> importBackup;

    public SettingsWindow(RgbSettings current, string hotkeyName, string hotkeyKeyName, Func<bool> resetToDefaults, Func<string, string?> exportBackup, Func<string, string?> importBackup)
    {
        InitializeComponent();
        this.hotkeyName = hotkeyName;
        this.hotkeyKeyName = hotkeyKeyName;
        this.resetToDefaults = resetToDefaults;
        this.exportBackup = exportBackup;
        this.importBackup = importBackup;
        Settings = new RgbSettings { Enabled = current.Enabled, DeviceIndex = current.DeviceIndex, DeviceName = current.DeviceName, AutoStart = current.AutoStart, StopAutoStartedOnExit = current.StopAutoStartedOnExit, CrashRecoveryEnabled = current.CrashRecoveryEnabled, IndicatorColor = current.IndicatorColor, LightingEffect = current.LightingEffect, PulseSpeedMilliseconds = current.PulseSpeedMilliseconds };
        EnableOpenRgb.IsChecked = Settings.Enabled;
        AutoStartOpenRgb.IsChecked = Settings.AutoStart;
        StopAutoStartedOpenRgb.IsChecked = Settings.StopAutoStartedOnExit;
        EnableCrashRecovery.IsChecked = Settings.CrashRecoveryEnabled;
        IndicatorColorBox.Text = Settings.IndicatorColor;
        UpdateColorPreview();
        SelectEffect(Settings.LightingEffect);
        PulseSpeedBox.Text = Settings.PulseSpeedMilliseconds.ToString();
        UpdatePulseSpeedEnabled();
        HotkeyLightingHint.Text = $"When AutoClicker is active, OpenRGB will light {hotkeyName}.";
        Loaded += (_, _) => RefreshKeyboards();
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ConfirmationWindow("About", "Created by JBX7", "OK", showCancel: false) { Owner = this };
        dialog.ShowDialog();
    }

    private void FindKeyboards_Click(object sender, RoutedEventArgs e) => RefreshKeyboards();

    private void ExportBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "AutoClicker backup (*.json)|*.json", FileName = "AutoClicker-backup.json", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        var error = exportBackup(dialog.FileName);
        ConnectionStatus.Text = error is null ? "Full configuration backup exported." : error;
        ConnectionStatus.Foreground = ThemeManager.Brush(error is null ? "SuccessBrush" : "ErrorBrush");
    }

    private void ImportBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "AutoClicker backup (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        var error = importBackup(dialog.FileName);
        ConnectionStatus.Text = error is null ? "Full backup imported. Close Settings to use it." : error;
        ConnectionStatus.Foreground = ThemeManager.Brush(error is null ? "SuccessBrush" : "ErrorBrush");
    }

    private void LightingEffect_Changed(object sender, RoutedEventArgs e) => UpdatePulseSpeedEnabled();

    private void IndicatorColorBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdateColorPreview();

    private void PickIndicatorColor_Click(object sender, RoutedEventArgs e)
    {
        var current = ParseColor(IndicatorColorBox.Text) ?? System.Drawing.Color.FromArgb(34, 211, 238);
        using var dialog = new System.Windows.Forms.ColorDialog
        {
            Color = current,
            FullOpen = true,
            AnyColor = true
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        IndicatorColorBox.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
    }

    private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
    {
        var confirmation = new ConfirmationWindow(
            "Reset to defaults",
            "Reset every option to AutoClicker's original defaults? This includes the 100 ms interval, F6 hotkey, disabled RGB lighting, and enabled crash recovery.",
            "Reset everything",
            destructive: true) { Owner = this };
        if (confirmation.ShowDialog() != true) return;
        if (resetToDefaults()) Close();
        else
        {
            ConnectionStatus.Text = "Defaults could not be reset while AutoClicker is active. Stop it first, then try again.";
            ConnectionStatus.Foreground = ThemeManager.Brush("WarningBrush");
        }
    }

    private async void TestRgbButton_Click(object sender, RoutedEventArgs e)
    {
        if (Owner is MainWindow { IsClicking: true })
        {
            ConnectionStatus.Text = "Stop AutoClicker before testing RGB lighting.";
            ConnectionStatus.Foreground = ThemeManager.Brush("WarningBrush");
            return;
        }
        if (EnableOpenRgb.IsChecked != true || KeyboardCombo.SelectedItem is not KeyboardDevice keyboard)
        {
            ConnectionStatus.Text = "Enable OpenRGB lighting and select a keyboard before testing.";
            ConnectionStatus.Foreground = ThemeManager.Brush("WarningBrush");
            return;
        }

        TestRgbButton.IsEnabled = false;
        ConnectionStatus.Text = $"Flashing {hotkeyName} three times…";
        ConnectionStatus.Foreground = ThemeManager.Brush("SuccessBrush");
        try
        {
            if (!OpenRgbHighlighter.TryNormalizeIndicatorColor(IndicatorColorBox.Text, out var color))
            {
                ConnectionStatus.Text = "Enter a colour as a hex value, for example #22D3EE.";
                ConnectionStatus.Foreground = ThemeManager.Brush("ErrorBrush");
                return;
            }
            var settings = new RgbSettings { Enabled = true, DeviceIndex = keyboard.Index, DeviceName = keyboard.Name, AutoStart = AutoStartOpenRgb.IsChecked == true, StopAutoStartedOnExit = StopAutoStartedOpenRgb.IsChecked == true, IndicatorColor = color, LightingEffect = SelectedEffect(), PulseSpeedMilliseconds = ReadPulseSpeed() };
            var availability = await OpenRgbHighlighter.EnsureSdkAsync(settings);
            if (!availability.IsAvailable)
            {
                ConnectionStatus.Text = availability.Message ?? "OpenRGB's SDK server is unavailable.";
                ConnectionStatus.Foreground = ThemeManager.Brush("ErrorBrush");
                return;
            }
            var error = await OpenRgbHighlighter.FlashKeyAsync(settings, hotkeyKeyName);
            ConnectionStatus.Text = error is null ? $"Finished testing {hotkeyName}; its previous colour was restored." : error;
            ConnectionStatus.Foreground = error is null ? ThemeManager.Brush("SuccessBrush") : ThemeManager.Brush("ErrorBrush");
        }
        finally
        {
            TestRgbButton.IsEnabled = true;
        }
    }

    private async void RefreshKeyboards()
    {
        try
        {
            Settings.AutoStart = AutoStartOpenRgb.IsChecked == true;
            var availability = await OpenRgbHighlighter.EnsureSdkAsync(Settings);
            if (!availability.IsAvailable)
            {
                KeyboardCombo.ItemsSource = Array.Empty<KeyboardDevice>();
                ConnectionStatus.Text = availability.Message ?? "OpenRGB's SDK server is unavailable.";
                ConnectionStatus.Foreground = ThemeManager.Brush("ErrorBrush");
                return;
            }
            var keyboards = OpenRgbHighlighter.FindKeyboards();
            KeyboardCombo.ItemsSource = keyboards;
            KeyboardCombo.SelectedItem = keyboards.FirstOrDefault(item => item.Index == Settings.DeviceIndex) ?? (keyboards.Length == 1 ? keyboards[0] : null);
            ConnectionStatus.Text = keyboards.Length == 0 ? "Connected, but OpenRGB did not expose your keyboard. In OpenRGB, rescan devices and try running it as administrator." : $"Found {keyboards.Length} keyboard{(keyboards.Length == 1 ? string.Empty : "s")}.";
            ConnectionStatus.Foreground = keyboards.Length == 0 ? ThemeManager.Brush("WarningBrush") : ThemeManager.Brush("SuccessBrush");
            var selected = KeyboardCombo.SelectedItem as KeyboardDevice;
            if (EnableOpenRgb.IsChecked == true && selected is not null)
            {
                var candidate = new RgbSettings { Enabled = true, DeviceIndex = selected.Index, DeviceName = selected.Name };
                var canLight = OpenRgbHighlighter.CanHighlightKey(candidate, hotkeyKeyName, out var error);
                HotkeyLightingHint.Text = canLight
                    ? $"OpenRGB can light {hotkeyName} while AutoClicker is active."
                    : error ?? $"OpenRGB cannot light {hotkeyName}.";
                HotkeyLightingHint.Foreground = canLight ? ThemeManager.Brush("SuccessBrush") : ThemeManager.Brush("ErrorBrush");
            }
        }
        catch (Exception exception)
        {
            ConnectionStatus.Text = $"Could not connect to OpenRGB. Install and start OpenRGB with its SDK server enabled, then try again. ({exception.Message})";
            ConnectionStatus.Foreground = ThemeManager.Brush("ErrorBrush");
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var keyboard = KeyboardCombo.SelectedItem as KeyboardDevice;
        Settings.Enabled = EnableOpenRgb.IsChecked == true;
        Settings.AutoStart = AutoStartOpenRgb.IsChecked == true;
        Settings.StopAutoStartedOnExit = StopAutoStartedOpenRgb.IsChecked == true;
        Settings.CrashRecoveryEnabled = EnableCrashRecovery.IsChecked == true;
        if (!OpenRgbHighlighter.TryNormalizeIndicatorColor(IndicatorColorBox.Text, out var color))
        {
            ConnectionStatus.Text = "Enter a colour as a hex value, for example #22D3EE.";
            ConnectionStatus.Foreground = ThemeManager.Brush("ErrorBrush");
            return;
        }
        Settings.IndicatorColor = color;
        Settings.LightingEffect = SelectedEffect();
        Settings.PulseSpeedMilliseconds = ReadPulseSpeed();
        Settings.DeviceIndex = keyboard?.Index ?? -1;
        Settings.DeviceName = keyboard?.Name ?? string.Empty;
        DialogResult = true;
    }

    private int ReadPulseSpeed() => int.TryParse(PulseSpeedBox.Text, out var value) ? Math.Clamp(value, 120, 2000) : 450;

    private void UpdateColorPreview()
    {
        if (ColorPreview is null) return;
        var color = ParseColor(IndicatorColorBox.Text);
        ColorPreview.Background = color is null
            ? ThemeManager.Brush("DisabledBrush")
            : new SolidColorBrush(Color.FromRgb(color.Value.R, color.Value.G, color.Value.B));
        ColorPreview.ToolTip = color is null ? "Enter a valid hex colour" : $"Selected colour: {IndicatorColorBox.Text.ToUpperInvariant()}";
    }

    private static System.Drawing.Color? ParseColor(string value)
    {
        if (!OpenRgbHighlighter.TryNormalizeIndicatorColor(value, out var normalized)) return null;
        return System.Drawing.Color.FromArgb(
            Convert.ToInt32(normalized[1..3], 16),
            Convert.ToInt32(normalized[3..5], 16),
            Convert.ToInt32(normalized[5..7], 16));
    }

    private string SelectedEffect() => (LightingEffectCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Constant";
    private void SelectEffect(string effect) => LightingEffectCombo.SelectedItem = LightingEffectCombo.Items.OfType<System.Windows.Controls.ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Content?.ToString(), effect, StringComparison.OrdinalIgnoreCase)) ?? LightingEffectCombo.Items[0];
    private void UpdatePulseSpeedEnabled()
    {
        if (PulseSpeedBox is null) return;
        PulseSpeedBox.IsEnabled = string.Equals(SelectedEffect(), "Pulse", StringComparison.OrdinalIgnoreCase);
    }
}
