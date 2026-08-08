using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Diagnostics;

namespace AutoClicker;

public partial class SettingsWindow : Window
{
    private const string NoIdleProfileOption = "(None)";
    private const int OpenRgbProfileRetryWindowMilliseconds = 10_000;
    private const int OpenRgbProfileRetryDelayMilliseconds = 300;
    public RgbSettings Settings { get; }
    public WorkerPriorityOption WorkerPriority { get; private set; }
    public bool CadenceDiagnosticsEnabled { get; private set; }
    public bool AdvancedMode { get; private set; }
    public bool KeyboardHotkeyModifiersEnabled { get; private set; }
    private readonly string hotkeyName;
    private readonly string? hotkeyKeyName;
    private readonly Func<ResetScope, bool> resetSettings;
    private readonly Func<BackupScope, string, string?> exportBackup;
    private readonly Func<BackupScope, string, string?> importBackup;
    private CancellationTokenSource? effectTestCancellation;
    private readonly System.Windows.Threading.DispatcherTimer effectPreviewRestartTimer;
    private bool restartEffectPreview;
    private bool isClosing;

    public SettingsWindow(RgbSettings current, WorkerPriorityOption workerPriority, bool cadenceDiagnosticsEnabled, bool advancedMode, bool keyboardHotkeyModifiersEnabled, string hotkeyName, string? hotkeyKeyName, Func<ResetScope, bool> resetSettings, Func<BackupScope, string, string?> exportBackup, Func<BackupScope, string, string?> importBackup)
    {
        InitializeComponent();
        this.hotkeyName = hotkeyName;
        this.hotkeyKeyName = hotkeyKeyName;
        this.resetSettings = resetSettings;
        this.exportBackup = exportBackup;
        this.importBackup = importBackup;
        effectPreviewRestartTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        effectPreviewRestartTimer.Tick += EffectPreviewRestartTimer_Tick;
        Settings = new RgbSettings { Enabled = current.Enabled, DeviceIndex = current.DeviceIndex, DeviceName = current.DeviceName, AutoStart = current.AutoStart, StopAutoStartedOnExit = current.StopAutoStartedOnExit, CrashRecoveryEnabled = current.CrashRecoveryEnabled, IdleProfileName = current.IdleProfileName, IndicatorColor = current.IndicatorColor, LightingEffect = current.LightingEffect, PulseSpeedMilliseconds = current.PulseSpeedMilliseconds };
        WorkerPriority = workerPriority;
        WorkerPriorityCombo.SelectedItem = WorkerPriorityCombo.Items.OfType<ComboBoxItem>().First(item => string.Equals(item.Tag?.ToString(), WorkerPriority.ToString(), StringComparison.OrdinalIgnoreCase));
        CadenceDiagnosticsEnabled = cadenceDiagnosticsEnabled;
        EnableCadenceDiagnostics.IsChecked = CadenceDiagnosticsEnabled;
        AdvancedMode = advancedMode;
        ModeCombo.SelectedIndex = AdvancedMode ? 1 : 0;
        KeyboardHotkeyModifiersEnabled = keyboardHotkeyModifiersEnabled;
        EnableKeyboardHotkeyModifiers.IsChecked = KeyboardHotkeyModifiersEnabled;
        EnableOpenRgb.IsChecked = Settings.Enabled;
        AutoStartOpenRgb.IsChecked = Settings.AutoStart;
        StopAutoStartedOpenRgb.IsChecked = Settings.StopAutoStartedOnExit;
        SetIdleProfileOptions([], clearMissingWhenKeyboardConnected: false);
        EnableCrashRecovery.IsChecked = Settings.CrashRecoveryEnabled;
        IndicatorColorBox.Text = Settings.IndicatorColor;
        UpdateColorPreview();
        SelectEffect(Settings.LightingEffect);
        EffectSpeedSlider.Value = SpeedToSlider(Settings.PulseSpeedMilliseconds, SelectedEffect());
        UpdatePulseSpeedEnabled();
        HotkeyLightingHint.Text = hotkeyKeyName is null
            ? "OpenRGB lighting applies to keyboard hotkeys. Select a keyboard hotkey to light one."
            : $"When AutoClicker is active, OpenRGB will light {hotkeyName}.";
        Loaded += (_, _) =>
        {
            RefreshKeyboards();
            RefreshProfiles();
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        // A preview owns a temporary LED snapshot, so always cancel it before the dialog goes away.
        isClosing = true;
        effectPreviewRestartTimer.Stop();
        effectPreviewRestartTimer.Tick -= EffectPreviewRestartTimer_Tick;
        effectTestCancellation?.Cancel();
        base.OnClosed(e);
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove();
    }

    private void SettingsScrollBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // The explicit settings template keeps scrolling reliable even with the themed scrollbar chrome.
        if (SettingsScroller is not null && Math.Abs(SettingsScroller.VerticalOffset - e.NewValue) > 0.1)
            SettingsScroller.ScrollToVerticalOffset(e.NewValue);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ConfirmationWindow("About", "Created by JBX7", "OK", showCancel: false) { Owner = this };
        dialog.ShowDialog();
    }

    private void QuickStartButton_Click(object sender, RoutedEventArgs e) => new QuickStartWindow { Owner = this }.ShowDialog();

    private void FindKeyboards_Click(object sender, RoutedEventArgs e) => RefreshKeyboards();
    private void RefreshProfiles_Click(object sender, RoutedEventArgs e) => RefreshProfiles();

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        // Update checks are manual; this only runs from the Settings button.
        CheckUpdatesButton.IsEnabled = false;
        DownloadUpdateButton.Visibility = Visibility.Collapsed;
        ReleaseHistoryPanel.Visibility = Visibility.Collapsed;
        ReleaseHistoryList.ItemsSource = null;
        UpdateStatus.Text = "Checking GitHub Releases…";
        UpdateStatus.Foreground = ThemeManager.Brush("TextMutedBrush");
        try
        {
            var update = await UpdateService.CheckForUpdateAsync(AppPaths.IsPortable, CancellationToken.None);
            UpdateStatus.Text = update.Message;
            UpdateStatus.Foreground = ThemeManager.Brush(update.IsUpdateAvailable ? "SuccessBrush" : "TextMutedBrush");
            if (update.RecentReleases is { Count: > 0 })
            {
                ReleaseHistoryList.ItemsSource = update.RecentReleases;
                ReleaseHistoryPanel.Visibility = Visibility.Visible;
            }
            if (update.IsUpdateAvailable && update.DownloadUri is not null)
            {
                DownloadUpdateButton.Tag = update;
                DownloadUpdateButton.Content = AppPaths.IsPortable ? "Download portable update" : "Download & install";
                DownloadUpdateButton.Visibility = Visibility.Visible;
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("GitHub update check failed", exception);
            UpdateStatus.Text = "Could not check GitHub Releases. Open Releases to download an update manually.";
            UpdateStatus.Foreground = ThemeManager.Brush("WarningBrush");
        }
        finally { CheckUpdatesButton.IsEnabled = true; }
    }

    private void OpenReleases_Click(object sender, RoutedEventArgs e) => OpenUrl(UpdateService.ReleasesUrl);
    private async void DownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (DownloadUpdateButton.Tag is not UpdateCheckResult { DownloadUri: { } downloadUri } update) return;
        if (AppPaths.IsPortable)
        {
            // Portable files are replaced by the user so their folder remains under their control.
            OpenUrl(downloadUri);
            UpdateStatus.Text = "Download opened. Extract it over the portable copy after AutoClicker closes; its Data folder is preserved.";
            UpdateStatus.Foreground = ThemeManager.Brush("TextMutedBrush");
            return;
        }

        var confirmation = new ConfirmationWindow(
            "Install update",
            $"Download and run AutoClicker {update.LatestTag} from the official GitHub Release? AutoClicker will close before setup starts.",
            "Download and install") { Owner = this };
        if (confirmation.ShowDialog() != true) return;

        // Download first; launch the normal installer only after a complete file is available.
        DownloadUpdateButton.IsEnabled = false;
        UpdateStatus.Text = "Downloading the installer from GitHub Releases…";
        UpdateStatus.Foreground = ThemeManager.Brush("TextMutedBrush");
        try
        {
            var installerPath = await UpdateService.DownloadInstallerAsync(downloadUri, update.LatestTag ?? "latest", CancellationToken.None);
            Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception exception)
        {
            AppLog.Error("GitHub update installer download failed", exception);
            UpdateStatus.Text = "Could not download the installer. Open Releases to download the update manually.";
            UpdateStatus.Foreground = ThemeManager.Brush("WarningBrush");
            DownloadUpdateButton.IsEnabled = true;
        }
    }
    private static void OpenUrl(Uri url) => Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });

    private void ExportBackup(BackupScope scope)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = $"Export {BackupScopeInfo.DisplayName(scope)}",
            Filter = BackupScopeInfo.ExportFilter(scope),
            FileName = BackupScopeInfo.DefaultFileName(scope),
            DefaultExt = BackupScopeInfo.FileExtension(scope),
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;
        var error = exportBackup(scope, dialog.FileName);
        ConnectionStatus.Text = error is null ? $"{BackupScopeInfo.DisplayName(scope)} exported." : error;
        ConnectionStatus.Foreground = ThemeManager.Brush(error is null ? "SuccessBrush" : "ErrorBrush");
    }

    private void ImportBackup(BackupScope scope)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Restore {BackupScopeInfo.DisplayName(scope)}",
            Filter = BackupScopeInfo.ImportFilter(scope)
        };
        if (dialog.ShowDialog(this) != true) return;
        var error = importBackup(scope, dialog.FileName);
        ConnectionStatus.Text = error is null ? $"{BackupScopeInfo.DisplayName(scope)} restored. Close Settings to use it." : error;
        ConnectionStatus.Foreground = ThemeManager.Brush(error is null ? "SuccessBrush" : "ErrorBrush");
    }

    private void ExportEverything_Click(object sender, RoutedEventArgs e) => ExportBackup(BackupScope.Everything);
    private void ExportSimple_Click(object sender, RoutedEventArgs e) => ExportBackup(BackupScope.SimpleMode);
    private void ExportAdvanced_Click(object sender, RoutedEventArgs e) => ExportBackup(BackupScope.AdvancedMode);
    private void ExportSequences_Click(object sender, RoutedEventArgs e) => ExportBackup(BackupScope.CustomSequences);
    private void ImportEverything_Click(object sender, RoutedEventArgs e) => ImportBackup(BackupScope.Everything);
    private void ImportSimple_Click(object sender, RoutedEventArgs e) => ImportBackup(BackupScope.SimpleMode);
    private void ImportAdvanced_Click(object sender, RoutedEventArgs e) => ImportBackup(BackupScope.AdvancedMode);
    private void ImportSequences_Click(object sender, RoutedEventArgs e) => ImportBackup(BackupScope.CustomSequences);

    private void LightingEffect_Changed(object sender, RoutedEventArgs e) => UpdatePulseSpeedEnabled();
    private void EffectSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdatePulseSpeedEnabled();
        if (effectTestCancellation is null) return;

        effectPreviewRestartTimer.Stop();
        effectPreviewRestartTimer.Start();
    }

    private void EffectPreviewRestartTimer_Tick(object? sender, EventArgs e)
    {
        effectPreviewRestartTimer.Stop();
        if (effectTestCancellation is null || isClosing) return;

        restartEffectPreview = true;
        TestEffectButton.IsEnabled = false;
        ConnectionStatus.Text = "Applying effect speed…";
        ConnectionStatus.Foreground = ThemeManager.Brush("TextMutedBrush");
        effectTestCancellation.Cancel();
    }

    private void IndicatorColorBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdateColorPreview();

    private void PickIndicatorColor_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ColorPickerWindow(IndicatorColorBox.Text, PreviewPickedColourAsync) { Owner = this };
        if (dialog.ShowDialog() == true) IndicatorColorBox.Text = dialog.SelectedColor;
    }

    private async Task<string?> PreviewPickedColourAsync(string color, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (hotkeyKeyName is null) return "OpenRGB can only preview keyboard hotkeys.";
        if (EnableOpenRgb.IsChecked != true || KeyboardCombo.SelectedItem is not KeyboardDevice keyboard)
            return "Enable OpenRGB lighting and select a keyboard to preview this colour.";

        var settings = new RgbSettings
        {
            Enabled = true,
            DeviceIndex = keyboard.Index,
            DeviceName = keyboard.Name,
            AutoStart = AutoStartOpenRgb.IsChecked == true,
            StopAutoStartedOnExit = StopAutoStartedOpenRgb.IsChecked == true,
            IdleProfileName = SelectedIdleProfileName(),
            IndicatorColor = color
        };
        var availability = await OpenRgbHighlighter.EnsureSdkAsync(settings);
        cancellation.ThrowIfCancellationRequested();
        if (!availability.IsAvailable) return availability.Message ?? "OpenRGB's SDK server is unavailable.";
        return await OpenRgbHighlighter.ShowKeySolidAsync(settings, hotkeyKeyName, cancellation);
    }

    private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ResetOptionsWindow(resetSettings) { Owner = this };
        if (dialog.ShowDialog() == true) Close();
    }

    private async void TestRgbButton_Click(object sender, RoutedEventArgs e)
    {
        if (hotkeyKeyName is null)
        {
            ConnectionStatus.Text = "OpenRGB can only light a keyboard hotkey.";
            ConnectionStatus.Foreground = ThemeManager.Brush("WarningBrush");
            return;
        }
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

        SetLightingTestControlsEnabled(false);
        ConnectionStatus.Text = "Flashing the selected keyboard three times…";
        ConnectionStatus.Foreground = ThemeManager.Brush("SuccessBrush");
        try
        {
            if (!OpenRgbHighlighter.TryNormalizeIndicatorColor(IndicatorColorBox.Text, out var color))
            {
                ConnectionStatus.Text = "Enter a colour as a hex value, for example #22D3EE.";
                ConnectionStatus.Foreground = ThemeManager.Brush("ErrorBrush");
                return;
            }
            var settings = new RgbSettings { Enabled = true, DeviceIndex = keyboard.Index, DeviceName = keyboard.Name, AutoStart = AutoStartOpenRgb.IsChecked == true, StopAutoStartedOnExit = StopAutoStartedOpenRgb.IsChecked == true, IdleProfileName = SelectedIdleProfileName(), IndicatorColor = color, LightingEffect = SelectedEffect(), PulseSpeedMilliseconds = ReadPulseSpeed() };
            var availability = await OpenRgbHighlighter.EnsureSdkAsync(settings);
            if (!availability.IsAvailable)
            {
                ConnectionStatus.Text = availability.Message ?? "OpenRGB's SDK server is unavailable.";
                ConnectionStatus.Foreground = ThemeManager.Brush("ErrorBrush");
                return;
            }
            var error = await OpenRgbHighlighter.FlashKeyboardAsync(settings);
            ConnectionStatus.Text = error is null ? "Finished testing the keyboard; its previous colours were restored." : error;
            ConnectionStatus.Foreground = error is null ? ThemeManager.Brush("SuccessBrush") : ThemeManager.Brush("ErrorBrush");
        }
        finally
        {
            SetLightingTestControlsEnabled(true);
        }
    }

    private async void TestEffectButton_Click(object sender, RoutedEventArgs e)
    {
        if (effectTestCancellation is not null)
        {
            effectPreviewRestartTimer.Stop();
            restartEffectPreview = false;
            effectTestCancellation.Cancel();
            TestEffectButton.IsEnabled = false;
            ConnectionStatus.Text = "Stopping keyboard effect test…";
            ConnectionStatus.Foreground = ThemeManager.Brush("TextMutedBrush");
            return;
        }

        if (Owner is MainWindow { IsClicking: true })
        {
            ConnectionStatus.Text = "Stop AutoClicker before testing RGB lighting.";
            ConnectionStatus.Foreground = ThemeManager.Brush("WarningBrush");
            return;
        }
        if (!TryCreateLightingSettings(out var settings, out var validationError))
        {
            ConnectionStatus.Text = validationError;
            ConnectionStatus.Foreground = ThemeManager.Brush("WarningBrush");
            return;
        }

        TestRgbButton.IsEnabled = false;
        ClearStuckLightingButton.IsEnabled = false;
        effectTestCancellation = new CancellationTokenSource();
        TestEffectButton.Content = "Stop effect test";
        TestEffectButton.ToolTip = "Stop the current keyboard effect test and restore its previous colours.";
        ConnectionStatus.Text = $"Testing {SelectedEffect().ToLowerInvariant()} across the keyboard…";
        ConnectionStatus.Foreground = ThemeManager.Brush("SuccessBrush");

        RgbKeyboardSnapshot? snapshot = null;
        try
        {
            var availability = await OpenRgbHighlighter.EnsureSdkAsync(settings);
            if (!availability.IsAvailable)
            {
                ConnectionStatus.Text = availability.Message ?? "OpenRGB's SDK server is unavailable.";
                ConnectionStatus.Foreground = ThemeManager.Brush("ErrorBrush");
                return;
            }

            snapshot = OpenRgbHighlighter.EnableKeyboardIndicator(settings, out var error, lightImmediately: !settings.IsPulse);
            if (snapshot is null)
            {
                ConnectionStatus.Text = error ?? "OpenRGB could not start the effect test.";
                ConnectionStatus.Foreground = ThemeManager.Brush("ErrorBrush");
                return;
            }

            using var duration = CancellationTokenSource.CreateLinkedTokenSource(effectTestCancellation.Token);
            if (settings.IsBlink)
            {
                duration.CancelAfter(TimeSpan.FromMilliseconds(Math.Clamp(settings.PulseSpeedMilliseconds, 120, 2000) * 6));
                await OpenRgbHighlighter.BlinkKeyboardAsync(snapshot, settings.PulseSpeedMilliseconds, duration.Token);
            }
            else if (settings.IsPulse)
            {
                duration.CancelAfter(TimeSpan.FromMilliseconds(Math.Clamp(settings.PulseSpeedMilliseconds, OpenRgbHighlighter.MinimumPulseCycleMilliseconds, OpenRgbHighlighter.MaximumPulseCycleMilliseconds) * 3));
                await OpenRgbHighlighter.FadePulseKeyboardAsync(snapshot, settings.PulseSpeedMilliseconds, duration.Token);
            }
            else
            {
                await Task.Delay(TimeSpan.FromSeconds(3), duration.Token);
            }

            if (effectTestCancellation.IsCancellationRequested)
            {
                if (!isClosing)
                {
                    ConnectionStatus.Text = "Keyboard effect test stopped; the previous colours were restored.";
                    ConnectionStatus.Foreground = ThemeManager.Brush("TextMutedBrush");
                }
            }
            else
            {
                ConnectionStatus.Text = $"Finished testing {SelectedEffect().ToLowerInvariant()}; the previous colours were restored.";
                ConnectionStatus.Foreground = ThemeManager.Brush("SuccessBrush");
            }
        }
        catch (OperationCanceledException)
        {
            if (!isClosing)
            {
                ConnectionStatus.Text = "Keyboard effect test stopped; the previous colours were restored.";
                ConnectionStatus.Foreground = ThemeManager.Brush("TextMutedBrush");
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("OpenRGB effect test failed", exception);
            if (!isClosing)
            {
                ConnectionStatus.Text = $"Keyboard effect test failed: {exception.Message}";
                ConnectionStatus.Foreground = ThemeManager.Brush("ErrorBrush");
            }
        }
        finally
        {
            if (snapshot is not null) OpenRgbHighlighter.RestoreKeyboard(snapshot);
            var restart = restartEffectPreview && !isClosing;
            restartEffectPreview = false;
            effectTestCancellation?.Dispose();
            effectTestCancellation = null;
            if (!isClosing)
            {
                SetLightingTestControlsEnabled(true);
                TestEffectButton.Content = "Test keyboard effect";
                TestEffectButton.ToolTip = "Shows the selected lighting effect across the keyboard, then restores its previous colours. Click again to stop it early.";
            }
            if (restart) TestEffectButton_Click(this, new RoutedEventArgs());
        }
    }

    private async void ClearStuckLightingButton_Click(object sender, RoutedEventArgs e)
    {
        if (Owner is MainWindow { IsClicking: true })
        {
            ConnectionStatus.Text = "Stop AutoClicker before clearing keyboard lighting.";
            ConnectionStatus.Foreground = ThemeManager.Brush("WarningBrush");
            return;
        }
        if (effectTestCancellation is not null || !TestRgbButton.IsEnabled)
        {
            ConnectionStatus.Text = "Finish the current keyboard lighting test before clearing lighting.";
            ConnectionStatus.Foreground = ThemeManager.Brush("WarningBrush");
            return;
        }
        if (KeyboardCombo.SelectedItem is not KeyboardDevice keyboard)
        {
            ConnectionStatus.Text = "Select a keyboard before clearing stuck lighting.";
            ConnectionStatus.Foreground = ThemeManager.Brush("WarningBrush");
            return;
        }

        SetLightingTestControlsEnabled(false);
        ConnectionStatus.Text = "Refreshing the selected keyboard's colours…";
        ConnectionStatus.Foreground = ThemeManager.Brush("TextMutedBrush");
        try
        {
            var settings = new RgbSettings
            {
                Enabled = true,
                DeviceIndex = keyboard.Index,
                DeviceName = keyboard.Name,
                AutoStart = AutoStartOpenRgb.IsChecked == true,
                StopAutoStartedOnExit = StopAutoStartedOpenRgb.IsChecked == true,
                IdleProfileName = SelectedIdleProfileName()
            };
            var availability = await OpenRgbHighlighter.EnsureSdkAsync(settings);
            if (!availability.IsAvailable)
            {
                ConnectionStatus.Text = availability.Message ?? "OpenRGB's SDK server is unavailable.";
                ConnectionStatus.Foreground = ThemeManager.Brush("ErrorBrush");
                return;
            }

            var error = await Task.Run(() => OpenRgbHighlighter.ClearStuckKeyboardLighting(settings));
            ConnectionStatus.Text = error is null
                ? "Refreshed the selected keyboard colours to clear any stuck lighting."
                : error;
            ConnectionStatus.Foreground = error is null ? ThemeManager.Brush("SuccessBrush") : ThemeManager.Brush("ErrorBrush");
        }
        finally
        {
            if (!isClosing) SetLightingTestControlsEnabled(true);
        }
    }

    private void SetLightingTestControlsEnabled(bool enabled)
    {
        TestRgbButton.IsEnabled = enabled;
        TestEffectButton.IsEnabled = enabled;
        ClearStuckLightingButton.IsEnabled = enabled;
        TestProfileButton.IsEnabled = enabled;
    }

    private bool TryCreateLightingSettings(out RgbSettings settings, out string error)
    {
        settings = new RgbSettings();
        error = string.Empty;
        if (EnableOpenRgb.IsChecked != true || KeyboardCombo.SelectedItem is not KeyboardDevice keyboard)
        {
            error = "Enable OpenRGB lighting and select a keyboard before testing.";
            return false;
        }
        if (!OpenRgbHighlighter.TryNormalizeIndicatorColor(IndicatorColorBox.Text, out var color))
        {
            error = "Enter a colour as a hex value, for example #22D3EE.";
            return false;
        }
        settings = new RgbSettings { Enabled = true, DeviceIndex = keyboard.Index, DeviceName = keyboard.Name, AutoStart = AutoStartOpenRgb.IsChecked == true, StopAutoStartedOnExit = StopAutoStartedOpenRgb.IsChecked == true, IdleProfileName = SelectedIdleProfileName(), IndicatorColor = color, LightingEffect = SelectedEffect(), PulseSpeedMilliseconds = ReadPulseSpeed() };
        return true;
    }

    private async void RefreshProfiles()
    {
        try
        {
            var autoStart = AutoStartOpenRgb.IsChecked == true;
            var retryDeadline = Stopwatch.GetTimestamp() + OpenRgbProfileRetryWindowMilliseconds * Stopwatch.Frequency / 1000d;
            while (!isClosing)
            {
                Settings.AutoStart = autoStart;
                var availability = await OpenRgbHighlighter.EnsureSdkAsync(Settings);
                if (!availability.IsAvailable)
                {
                    if (!autoStart || Stopwatch.GetTimestamp() >= retryDeadline)
                    {
                        SetIdleProfileOptions([], clearMissingWhenKeyboardConnected: false);
                        return;
                    }
                    await Task.Delay(OpenRgbProfileRetryDelayMilliseconds);
                    continue;
                }

                var profiles = OpenRgbHighlighter.GetProfiles();
                SetIdleProfileOptions(profiles, clearMissingWhenKeyboardConnected: KeyboardCombo.SelectedItem is KeyboardDevice);
                return;
            }
        }
        catch { }
    }

    private void SetIdleProfileOptions(IEnumerable<string> discoveredProfiles, bool clearMissingWhenKeyboardConnected)
    {
        var remembered = (Settings.IdleProfileName ?? string.Empty).Trim();
        var rememberedKey = NormalizeProfileNameForCompare(remembered);
        var profiles = discoveredProfiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(profile => profile, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var options = new List<string> { NoIdleProfileOption };
        options.AddRange(profiles);
        if (remembered.Length > 0 && !profiles.Any(profile => string.Equals(NormalizeProfileNameForCompare(profile), rememberedKey, StringComparison.OrdinalIgnoreCase))) options.Add(remembered);

        OpenRgbProfileCombo.ItemsSource = options;
        if (remembered.Length == 0)
        {
            OpenRgbProfileCombo.SelectedItem = NoIdleProfileOption;
            return;
        }

        var discovered = profiles.FirstOrDefault(profile => string.Equals(NormalizeProfileNameForCompare(profile), rememberedKey, StringComparison.OrdinalIgnoreCase));
        if (discovered is not null)
        {
            OpenRgbProfileCombo.SelectedItem = discovered;
            return;
        }

        if (clearMissingWhenKeyboardConnected && profiles.Count > 0)
        {
            Settings.IdleProfileName = string.Empty;
            OpenRgbProfileCombo.SelectedItem = NoIdleProfileOption;
            ConnectionStatus.Text = $"Saved idle profile '{remembered}' was not found. Idle profile was set to none.";
            ConnectionStatus.Foreground = ThemeManager.Brush("WarningBrush");
            return;
        }

        OpenRgbProfileCombo.SelectedItem = remembered;
    }

    private static string NormalizeProfileNameForCompare(string profileName)
    {
        var value = profileName.Trim();
        return value.EndsWith(".orp", StringComparison.OrdinalIgnoreCase)
            ? value[..^4]
            : value;
    }

    private async void TestProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var profileName = SelectedIdleProfileName();
        if (string.IsNullOrWhiteSpace(profileName))
        {
            ConnectionStatus.Text = "Choose an OpenRGB profile before testing.";
            ConnectionStatus.Foreground = ThemeManager.Brush("WarningBrush");
            return;
        }

        SetLightingTestControlsEnabled(false);
        ConnectionStatus.Text = $"Applying OpenRGB profile '{profileName}'…";
        ConnectionStatus.Foreground = ThemeManager.Brush("TextMutedBrush");
        try
        {
            var settings = new RgbSettings { Enabled = true, AutoStart = AutoStartOpenRgb.IsChecked == true };
            var availability = await OpenRgbHighlighter.EnsureSdkAsync(settings);
            if (!availability.IsAvailable)
            {
                ConnectionStatus.Text = availability.Message ?? "OpenRGB's SDK server is unavailable.";
                ConnectionStatus.Foreground = ThemeManager.Brush("ErrorBrush");
                return;
            }
            if (!OpenRgbHighlighter.TryLoadProfile(profileName, out var error))
            {
                ConnectionStatus.Text = error ?? "OpenRGB profile load failed.";
                ConnectionStatus.Foreground = ThemeManager.Brush("ErrorBrush");
                return;
            }

            ConnectionStatus.Text = $"Applied OpenRGB profile '{profileName}'.";
            ConnectionStatus.Foreground = ThemeManager.Brush("SuccessBrush");
        }
        finally
        {
            if (!isClosing) SetLightingTestControlsEnabled(true);
        }
    }

    private string SelectedIdleProfileName()
    {
        var selected = (OpenRgbProfileCombo.SelectedItem as string ?? OpenRgbProfileCombo.Text ?? string.Empty).Trim();
        return string.Equals(selected, NoIdleProfileOption, StringComparison.Ordinal) ? string.Empty : selected;
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
            if (EnableOpenRgb.IsChecked == true && selected is not null && hotkeyKeyName is not null)
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
        Settings.IdleProfileName = SelectedIdleProfileName();
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
        WorkerPriority = WorkerPriorityRules.Normalize((WorkerPriorityCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString());
        CadenceDiagnosticsEnabled = EnableCadenceDiagnostics.IsChecked == true;
        AdvancedMode = string.Equals((ModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(), "Advanced", StringComparison.Ordinal);
        KeyboardHotkeyModifiersEnabled = EnableKeyboardHotkeyModifiers.IsChecked == true;
        DialogResult = true;
    }

    private int ReadPulseSpeed()
    {
        var progress = Math.Clamp(EffectSpeedSlider?.Value ?? 50d, 0d, 100d) / 100d;
        return string.Equals(SelectedEffect(), "Fade", StringComparison.OrdinalIgnoreCase)
            ? (int)Math.Round(OpenRgbHighlighter.MaximumPulseCycleMilliseconds - ((OpenRgbHighlighter.MaximumPulseCycleMilliseconds - OpenRgbHighlighter.MinimumPulseCycleMilliseconds) * progress))
            : (int)Math.Round(2000d - (1880d * progress));
    }

    private static double SpeedToSlider(int milliseconds, string effect)
    {
        if (string.Equals(effect, "Fade", StringComparison.OrdinalIgnoreCase))
            return (OpenRgbHighlighter.MaximumPulseCycleMilliseconds - Math.Clamp(milliseconds, OpenRgbHighlighter.MinimumPulseCycleMilliseconds, OpenRgbHighlighter.MaximumPulseCycleMilliseconds)) * 100d / (OpenRgbHighlighter.MaximumPulseCycleMilliseconds - OpenRgbHighlighter.MinimumPulseCycleMilliseconds);
        return (2000d - Math.Clamp(milliseconds, 120, 2000)) * 100d / 1880d;
    }

    private void UpdateColorPreview()
    {
        if (ColorPreview is null) return;
        var color = ParseColor(IndicatorColorBox.Text);
        ColorPreview.Background = color is null
            ? ThemeManager.Brush("DisabledBrush")
            : new SolidColorBrush(Color.FromRgb(color.Value.R, color.Value.G, color.Value.B));
        ColorPickerButton.ToolTip = color is null
            ? "Choose indicator colour — enter a valid hex value"
            : $"Choose indicator colour (currently {IndicatorColorBox.Text.ToUpperInvariant()})";
    }

    private static System.Drawing.Color? ParseColor(string value)
    {
        if (!OpenRgbHighlighter.TryNormalizeIndicatorColor(value, out var normalized)) return null;
        return System.Drawing.Color.FromArgb(
            Convert.ToInt32(normalized[1..3], 16),
            Convert.ToInt32(normalized[3..5], 16),
            Convert.ToInt32(normalized[5..7], 16));
    }

    private string SelectedEffect() => ((LightingEffectCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Constant") switch { "Pulse" => "Fade", _ => (LightingEffectCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Constant" };
    private void SelectEffect(string effect)
    {
        var displayEffect = string.Equals(effect, "Fade", StringComparison.OrdinalIgnoreCase) ? "Pulse"
            : string.Equals(effect, "Pulse", StringComparison.OrdinalIgnoreCase) ? "Blink" : effect;
        LightingEffectCombo.SelectedItem = LightingEffectCombo.Items.OfType<System.Windows.Controls.ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Content?.ToString(), displayEffect, StringComparison.OrdinalIgnoreCase)) ?? LightingEffectCombo.Items[0];
    }
    private void UpdatePulseSpeedEnabled()
    {
        if (EffectSpeedSlider is null || EffectSpeedHint is null || EffectSpeedValueLabel is null || EffectSpeedPanel is null) return;
        var effect = SelectedEffect();
        var hasSpeed = !string.Equals(effect, "Constant", StringComparison.OrdinalIgnoreCase);
        EffectSpeedPanel.Visibility = hasSpeed ? Visibility.Visible : Visibility.Collapsed;
        EffectSpeedHint.Visibility = hasSpeed ? Visibility.Visible : Visibility.Collapsed;
        EffectSpeedSlider.IsEnabled = hasSpeed;
        if (string.Equals(effect, "Fade", StringComparison.OrdinalIgnoreCase))
        {
            EffectSpeedValueLabel.Text = $"{ReadPulseSpeed() / 1000d:0.0} s per cycle";
            EffectSpeedHint.Text = "Pulse fades smoothly; use the slider to choose a slower or faster cycle.";
        }
        else if (string.Equals(effect, "Blink", StringComparison.OrdinalIgnoreCase))
        {
            EffectSpeedValueLabel.Text = $"{ReadPulseSpeed():N0} ms per state";
            EffectSpeedHint.Text = "Blink switches the key on and off at the selected speed.";
        }
        else { EffectSpeedValueLabel.Text = string.Empty; EffectSpeedHint.Text = string.Empty; }
    }
}
