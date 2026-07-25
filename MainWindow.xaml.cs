using System.Runtime.InteropServices;
using System.Text.Json;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AutoClicker;

public partial class MainWindow : Window
{
    private const int HotkeyId = 0xC11C;
    private const int WmHotkey = 0x0312;
    private static readonly string DefaultsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoClicker", "defaults.json");
    private static readonly string RgbSettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoClicker", "rgb-settings.json");
    private static readonly string UiPreferencesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoClicker", "ui-preferences.json");
    private static readonly string SequenceLibraryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoClicker", "sequence-library.json");
    private const double ExpandedWindowHeight = 558;
    private const double CompactWindowHeight = 166;
    private readonly DispatcherTimer resetTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer flashTimer = new() { Interval = TimeSpan.FromMilliseconds(85) };
    private readonly DispatcherTimer guiHeartbeatTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private CancellationTokenSource? clickCancellation;
    private Task? clickTask;
    private HwndSource? hwndSource;
    private nint hwnd;
    private bool hotkeyRegistered;
    private bool capturingHotkey;
    private bool capturingSpamKey;
    private bool updatingActionSelection;
    private ComboBoxItem? actionBeforeKeyCapture;
    private int customSpamVirtualKey;
    private List<SequenceStep> customSequence = [];
    private List<SequencePreset> sequenceLibrary = [];
    private bool settingsOpen;
    private int hotkey = System.Windows.Input.KeyInterop.VirtualKeyFromKey(System.Windows.Input.Key.F6);
    private uint hotkeyModifiers;
    private int liveClickCount;
    private DateTime lastLiveClick;
    private int liveKeyPressCount;
    private DateTime lastLiveKey;
    private volatile bool isClosing;
    private RgbSettings rgbSettings = new();
    private readonly object rgbLock = new();
    private RgbLightingSnapshot? rgbSnapshot;
    private CancellationTokenSource? rgbPulseCancellation;
    private Task? rgbPulseTask;
    private int rgbIndicatorGeneration;
    private long lastGuiHeartbeat;
    private int statusRevision;
    private bool compactMode;

    public MainWindow()
    {
        InitializeComponent();
        LoadSequenceLibrary();
        RefreshSequencePresetActions();
        LoadDefaults();
        LoadRgbSettings();
        LoadUiPreferences();
        UpdateHotkeyLabel();
        UpdateThemeButton();
        UpdateLiveInputMode();
        resetTimer.Tick += (_, _) => ResetCounterWhenIdle();
        flashTimer.Tick += (_, _) => RestoreLiveArea();
        guiHeartbeatTimer.Tick += (_, _) => Volatile.Write(ref lastGuiHeartbeat, Stopwatch.GetTimestamp());
        Volatile.Write(ref lastGuiHeartbeat, Stopwatch.GetTimestamp());
        resetTimer.Start();
        guiHeartbeatTimer.Start();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        hwnd = new WindowInteropHelper(this).Handle;
        hwndSource = HwndSource.FromHwnd(hwnd);
        hwndSource?.AddHook(WndProc);
        RegisterConfiguredHotkey();
    }

    private nint WndProc(nint handle, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (!capturingHotkey && !capturingSpamKey && msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            ToggleClicking();
            handled = true;
        }
        return 0;
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    private void StartButton_Click(object sender, RoutedEventArgs e) => StartClicking();
    private void StopButton_Click(object sender, RoutedEventArgs e) => StopClicking();
    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        ThemeManager.Apply(ThemeManager.Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);
        UpdateThemeButton();
        RestoreLiveArea();
    }
    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        UpdatePinUi();
        SaveUiPreferences();
    }

    private void CollapseButton_Click(object sender, RoutedEventArgs e)
    {
        compactMode = !compactMode;
        ApplyCompactMode();
        SaveUiPreferences();
    }

    private void UpdatePinUi()
    {
        PinButton.Tag = Topmost ? "Pinned" : null;
        PinButton.ToolTip = Topmost ? "Always on top — click to unpin" : "Keep on top";
    }
    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (clickCancellation is not null)
        {
            Status($"Stop {ActivityVerb().ToLowerInvariant()} before opening Settings.", ThemeManager.Brush("WarningBrush"));
            return;
        }
        if (settingsOpen) return;
        settingsOpen = true;
        var dialog = new SettingsWindow(rgbSettings, FormatHotkey(), HotkeyKeyName(), ResetToFactoryDefaults, ExportFullBackup, ImportFullBackup) { Owner = this };
        try
        {
            if (dialog.ShowDialog() == true)
            {
                rgbSettings = dialog.Settings;
                SaveRgbSettings();
                CrashRecovery.UpdateEnabled(rgbSettings.CrashRecoveryEnabled);
                Status(rgbSettings.Enabled ? "OpenRGB hotkey lighting enabled." : "OpenRGB hotkey lighting disabled.", rgbSettings.Enabled ? ThemeManager.Brush("SuccessBrush") : ThemeManager.Brush("TextMutedBrush"));
            }
        }
        finally { settingsOpen = false; }
    }

    private void HotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (capturingHotkey)
        {
            CancelHotkeyCapture();
            return;
        }
        if (hotkeyRegistered) { UnregisterHotKey(hwnd, HotkeyId); hotkeyRegistered = false; }
        capturingHotkey = true;
        HotkeyButton.Content = "Cancel";
        HotkeyButton.ContentTemplate = (DataTemplate)FindResource("HotkeyCancelIcon");
        HotkeyButton.Width = 31;
        HotkeyButton.Padding = new Thickness(0);
        HotkeyButton.ToolTip = "Keep the current hotkey";
        Status("Press a key combination, or click Cancel to keep the current hotkey.", ThemeManager.Brush("WarningBrush"));
        Focus();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (capturingSpamKey)
        {
            e.Handled = true;
            var selectedKey = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            if (selectedKey == System.Windows.Input.Key.Escape)
            {
                CancelSpamKeyCapture();
                return;
            }

            var virtualKey = System.Windows.Input.KeyInterop.VirtualKeyFromKey(selectedKey);
            if (virtualKey == 0) return;
            if (virtualKey == hotkey && hotkeyModifiers == 0)
            {
                Status($"{FormatInputKey(virtualKey)} is also the start/stop hotkey. Choose another key or change the hotkey first.", ThemeManager.Brush("WarningBrush"));
                return;
            }
            customSpamVirtualKey = virtualKey;
            CustomKeyItem.Content = $"Key: {FormatInputKey(virtualKey)}";
            capturingSpamKey = false;
            Select(ButtonCombo, "Custom");
            Status($"Ready — {FormatInputKey(virtualKey)} will be repeated.", ThemeManager.Brush("SuccessBrush"));
            return;
        }
        if (!capturingHotkey) return;
        e.Handled = true;
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        if (key == System.Windows.Input.Key.Escape) { CancelHotkeyCapture(); return; }
        var candidate = System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
        if (candidate == 0 || key is System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl or System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift or System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt) return;
        var modifiers = GetModifiers();
        if (RegisterHotKey(hwnd, HotkeyId, modifiers, (uint)candidate))
        {
            hotkey = candidate; hotkeyModifiers = modifiers; hotkeyRegistered = true;
            UpdateHotkeyLabel(); CancelHotkeyCapture(keepStatus: true);
            Status($"Ready — press {FormatHotkey()} to start or stop.", ThemeManager.Brush("SuccessBrush"));
            FlashSelectedHotkey();
        }
        else
        {
            RegisterConfiguredHotkey();
            CancelHotkeyCapture(keepStatus: true);
            Status($"{FormatHotkey(candidate, modifiers)} is already in use.", ThemeManager.Brush("ErrorBrush"));
        }
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var textBox = FindParent<TextBox>(e.OriginalSource as DependencyObject);
        if (textBox != HoursBox && textBox != MinutesBox && textBox != SecondsBox && textBox != MillisBox
            && textBox != CountBox && textBox != XBox && textBox != YBox) return;
        textBox.Focus();
        textBox.SelectAll();
        e.Handled = true;
    }

    private static T? FindParent<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match) return match;
            source = source switch
            {
                Visual or System.Windows.Media.Media3D.Visual3D => VisualTreeHelper.GetParent(source),
                FrameworkContentElement contentElement => contentElement.Parent,
                _ => LogicalTreeHelper.GetParent(source)
            };
        }
        return null;
    }

    private void CancelHotkeyCapture(bool keepStatus = false)
    {
        capturingHotkey = false;
        HotkeyButton.Content = "Edit";
        HotkeyButton.ContentTemplate = (DataTemplate)FindResource("HotkeyEditIcon");
        HotkeyButton.Width = 31;
        HotkeyButton.Padding = new Thickness(0);
        HotkeyButton.ToolTip = "Change hotkey";
        if (!hotkeyRegistered) RegisterConfiguredHotkey();
        if (!keepStatus) Status($"Ready — press {FormatHotkey()} to start or stop.", ThemeManager.Brush("SuccessBrush"));
    }

    private void ActionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingActionSelection || ButtonCombo is null || PickKeyItem is null) return;
        var selectedAction = Selected(ButtonCombo);
        if (selectedAction.StartsWith("Preset:", StringComparison.Ordinal))
        {
            var preset = sequenceLibrary.FirstOrDefault(item => item.Id == selectedAction[7..]);
            if (preset is not null)
            {
                customSequence = preset.Steps.Select(step => step.Clone()).ToList();
                SequenceItem.Content = $"Custom sequence — {preset.Name}";
                updatingActionSelection = true; ButtonCombo.SelectedItem = SequenceItem; updatingActionSelection = false;
                UpdateLiveInputMode();
                Status($"Ready — {preset.Name} will be repeated.", ThemeManager.Brush("SuccessBrush"));
                return;
            }
        }
        if (Selected(ButtonCombo) is "Sequence" or "EditSequence")
        {
            var previous = e.RemovedItems.OfType<ComboBoxItem>().FirstOrDefault();
            if (Selected(ButtonCombo) == "EditSequence" || customSequence.Count == 0)
            {
                var editor = new SequenceEditorWindow(customSequence, sequenceLibrary) { Owner = this };
                var accepted = editor.ShowDialog() == true;
                if (accepted) customSequence = editor.Steps.Select(step => step.Clone()).ToList();
                if (editor.LibraryChanged)
                {
                    sequenceLibrary = editor.Library.Select(preset => preset.Clone()).ToList();
                    SaveSequenceLibrary();
                    RefreshSequencePresetActions();
                }
                if (!accepted && previous is not null) { updatingActionSelection = true; ButtonCombo.SelectedItem = previous; updatingActionSelection = false; UpdateLiveInputMode(); return; }
            }
            if (customSequence.Count >= 2)
            {
                SequenceItem.Content = $"Custom sequence ({customSequence.Count} actions)";
                updatingActionSelection = true; ButtonCombo.SelectedItem = SequenceItem; updatingActionSelection = false;
            }
            else if (previous is not null) { updatingActionSelection = true; ButtonCombo.SelectedItem = previous; updatingActionSelection = false; }
            UpdateLiveInputMode();
            ShowReadyActionStatus();
            return;
        }
        if (Selected(ButtonCombo) != "Pick")
        {
            UpdateLiveInputMode();
            ShowReadyActionStatus();
            return;
        }

        actionBeforeKeyCapture = e.RemovedItems.OfType<ComboBoxItem>().FirstOrDefault();
        capturingSpamKey = true;
        Status("Press the key to repeat, or Escape to cancel.", ThemeManager.Brush("WarningBrush"));
        Focus();
    }

    private void CancelSpamKeyCapture()
    {
        capturingSpamKey = false;
        updatingActionSelection = true;
        ButtonCombo.SelectedItem = actionBeforeKeyCapture ?? ButtonCombo.Items.OfType<ComboBoxItem>().First();
        updatingActionSelection = false;
        Status("Key selection cancelled.", ThemeManager.Brush("TextMutedBrush"));
    }

    private void RegisterConfiguredHotkey()
    {
        if (hwnd == 0) return;
        hotkeyRegistered = RegisterHotKey(hwnd, HotkeyId, hotkeyModifiers, (uint)hotkey);
        if (!hotkeyRegistered) Status($"{FormatHotkey()} is in use — choose another key.", ThemeManager.Brush("ErrorBrush"));
    }

    private void ToggleClicking()
    {
        if (settingsOpen)
        {
            Status($"Close Settings before {ActivityVerb().ToLowerInvariant()}.", ThemeManager.Brush("WarningBrush"));
            return;
        }
        if (clickCancellation is null) StartClicking(); else StopClicking();
    }
    private void StartClicking()
    {
        if (settingsOpen)
        {
            Status($"Close Settings before {ActivityVerb().ToLowerInvariant()}.", ThemeManager.Brush("WarningBrush"));
            return;
        }
        if (clickCancellation is not null) return;
        if (capturingSpamKey)
        {
            Status("Finish choosing the key to repeat first.", ThemeManager.Brush("WarningBrush"));
            return;
        }
        var input = Selected(ButtonCombo);
        if (input == "Pick")
        {
            Status("Choose a key to repeat first.", ThemeManager.Brush("WarningBrush"));
            return;
        }
        if (input == "Custom" && customSpamVirtualKey == 0)
        {
            Status("Choose a custom key to repeat first.", ThemeManager.Brush("WarningBrush"));
            return;
        }
        if (input == "Sequence" && customSequence.Count < 2)
        {
            Status("Add at least two actions to the custom sequence first.", ThemeManager.Brush("WarningBrush"));
            return;
        }
        var keyboardVirtualKey = input switch { "Space" => 0x20, "Enter" => 0x0D, "Custom" => customSpamVirtualKey, _ => 0 };
        if (keyboardVirtualKey == hotkey && hotkeyModifiers == 0)
        {
            Status($"{FormatInputKey(keyboardVirtualKey)} is also the start/stop hotkey. Choose another key or change the hotkey first.", ThemeManager.Brush("WarningBrush"));
            return;
        }
        var delay = InputRules.CreateInterval(Read(HoursBox, 0, 999), Read(MinutesBox, 0, 59), Read(SecondsBox, 0, 59), Read(MillisBox, 1, 999));
        var cancellation = new CancellationTokenSource();
        clickCancellation = cancellation;
        Volatile.Write(ref lastGuiHeartbeat, Stopwatch.GetTimestamp());
        var settings = new ClickSettings(FixedPositionRadio.IsChecked == true, Read(XBox, -32768, 32767), Read(YBox, -32768, 32767), input, keyboardVirtualKey == 0 ? null : keyboardVirtualKey, Selected(TypeCombo) == "Double", CountRadio.IsChecked == true ? Read(CountBox, 1, 999999) : null, input == "Sequence" ? BuildSequence(customSequence) : null);
        AppLog.Info($"Starting {ActivityVerb().ToLowerInvariant()} | Input={input} | IntervalMs={delay.TotalMilliseconds:0.###} | Repeat={(settings.MaximumClicks?.ToString() ?? "until stopped")}");
        StartButton.IsEnabled = false; StopButton.IsEnabled = true;
        LiveArea.Background = ThemeManager.Brush("AccentBrush");
        LiveArea.BorderBrush = ThemeManager.Brush("AccentHoverBrush");
        LiveCountLabel.Text = liveClickCount == 0 ? "0 clicks" : $"{liveClickCount:N0} clicks";
        UpdateLiveInputMode();
        Status($"{ActivityVerb()} — press {FormatHotkey()} to stop.", ThemeManager.Brush("ErrorBrush"));
        SetTaskbarIcon(running: true);
        StartRgbIndicator();
        clickTask = Task.Run(() => ClickLoop(delay, settings, cancellation));
    }

    private void ClickLoop(TimeSpan delay, ClickSettings settings, CancellationTokenSource cancellation)
    {
        var sent = 0;
        var watchdogExpired = false;
        Exception? failure = null;
        var originalPriority = Thread.CurrentThread.Priority;
        try
        {
            // This is deliberately not Highest/Realtime: AboveNormal helps the click
            // worker under load without taking time away from the foreground game.
            Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
            using var timer = new PrecisionTimer();
            var intervalTicks = delay.TotalSeconds * Stopwatch.Frequency;
            var nextClickAt = (double)Stopwatch.GetTimestamp();
            var actionInputs = settings.KeyboardVirtualKey is int virtualKey ? CreateKeyInputs(virtualKey) : CreateClickInputs(settings.Button);
            while (!cancellation.IsCancellationRequested && (!settings.MaximumClicks.HasValue || sent < settings.MaximumClicks.Value))
            {
                if (Stopwatch.GetTimestamp() - Volatile.Read(ref lastGuiHeartbeat) > Stopwatch.Frequency * 5)
                {
                    watchdogExpired = true;
                    cancellation.Cancel();
                    break;
                }
                timer.WaitUntil(nextClickAt, cancellation.Token);
                var now = Stopwatch.GetTimestamp();
                // Do not burst a backlog after a long scheduler stall. Resume the
                // fixed cadence from the current instant instead.
                if (now - nextClickAt > intervalTicks) nextClickAt = now;
                if (settings.Sequence is { Length: > 0 })
                {
                    foreach (var step in settings.Sequence)
                    {
                        if (settings.FixedPosition && step.IsMouse) SetCursorPos(settings.X, settings.Y);
                        SendAction(step.Inputs, false);
                        if (step.DelayAfterMilliseconds > 0 && step != settings.Sequence[^1])
                            timer.WaitUntil(Stopwatch.GetTimestamp() + step.DelayAfterMilliseconds * Stopwatch.Frequency / 1000d, cancellation.Token);
                    }
                }
                else
                {
                    if (settings.FixedPosition && settings.KeyboardVirtualKey is null) SetCursorPos(settings.X, settings.Y);
                    SendAction(actionInputs, settings.DoubleClick);
                }
                sent++;
                nextClickAt = settings.Sequence is { Length: > 0 }
                    ? Stopwatch.GetTimestamp() + intervalTicks
                    : nextClickAt + intervalTicks;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            failure = exception;
            AppLog.Error("Click/spam worker failed", exception);
        }
        finally
        {
            try { Thread.CurrentThread.Priority = originalPriority; } catch { }
            if (!isClosing && !Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        if (ReferenceEquals(clickCancellation, cancellation))
                        {
                            StopClicking();
                            if (watchdogExpired) Status("Stopped — the GUI heartbeat timed out.", ThemeManager.Brush("WarningBrush"));
                            else if (failure is not null) Status("Stopped — details were written to AutoClicker.log.", ThemeManager.Brush("ErrorBrush"));
                        }
                    }
                    finally
                    {
                        // The UI can still need to cancel this source while it is
                        // processing the completion notification. Dispose only after
                        // that notification has run, rather than on the worker thread.
                        cancellation.Dispose();
                    }
                });
            else
                cancellation.Dispose();
        }
    }

    private void StopClicking()
    {
        var cancellation = clickCancellation;
        clickCancellation = null;
        cancellation?.Cancel();
        if (cancellation is not null) AppLog.Info("Click/spam worker stop requested.");
        StartButton.IsEnabled = true; StopButton.IsEnabled = false;
        LiveArea.Background = ThemeManager.Brush("ControlBrush");
        LiveArea.BorderBrush = ThemeManager.Brush("LiveBorderBrush");
        if (liveClickCount == 0) LiveCountLabel.Text = "Start to test";
        UpdateLiveInputMode();
        Status($"Ready — press {FormatHotkey()} to start or stop.", ThemeManager.Brush("SuccessBrush"));
        SetTaskbarIcon(running: false);
        StopRgbIndicator();
    }

    internal bool IsClicking => clickCancellation is not null;
    internal void EmergencyStop()
    {
        clickCancellation?.Cancel();
        if (rgbSettings.StopAutoStartedOnExit) OpenRgbHighlighter.StopAutoStartedServer();
    }

    internal void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
        var wasPinned = Topmost;
        Topmost = true;
        if (!wasPinned) Topmost = false;
        Focus();
    }

    private void LiveArea_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (clickCancellation is null || IsKeyboardInputSelected()) return;
        var now = DateTime.UtcNow;
        var interval = liveClickCount > 0 ? now - lastLiveClick : (TimeSpan?)null;
        liveClickCount++; lastLiveClick = now;
        LiveCountLabel.Text = $"{liveClickCount:N0} clicks";
        LiveIntervalLabel.Text = interval is null ? "Waiting for next click" : $"Last interval: ~{FormatInterval(interval.Value)}";
        // A darker pulse retains contrast with the light labels; the outline makes
        // the feedback clear without changing the text or the wider app theme.
        LiveArea.Background = ThemeManager.Brush("LiveFlashBrush");
        LiveArea.BorderBrush = ThemeManager.Brush("LiveFlashBorderBrush");
        if (ThemeManager.Current == AppTheme.Light)
        {
            var flashText = ThemeManager.Brush("TextSecondaryBrush");
            LiveTitleLabel.Foreground = flashText;
            LiveIntervalLabel.Foreground = flashText;
            LiveMouseHint.Foreground = flashText;
        }
        flashTimer.Stop(); flashTimer.Start();
    }

    private void KeyTestBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (clickCancellation is null) return;
        e.Handled = true;
        var now = DateTime.UtcNow;
        var interval = lastLiveKey == default ? (TimeSpan?)null : now - lastLiveKey;
        lastLiveKey = now;
        liveKeyPressCount++;
        KeyTestBox.Text = "Receiving key presses";
        KeyTestPlaceholder.Visibility = Visibility.Collapsed;
        LiveCountLabel.Text = FormatKeyPressCount(liveKeyPressCount);
        LiveIntervalLabel.Text = interval is null ? "Waiting for next key" : $"Last interval: ~{FormatInterval(interval.Value)}";
    }

    private void KeyTestBox_GotKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (clickCancellation is null || !IsKeyboardInputSelected()) return;
        liveKeyPressCount = 0;
        lastLiveKey = default;
        KeyTestBox.Clear();
        KeyTestPlaceholder.Visibility = Visibility.Visible;
        LiveCountLabel.Text = FormatKeyPressCount(0);
        LiveIntervalLabel.Text = "Waiting for key presses";
    }

    private static string FormatInterval(TimeSpan interval) => interval.TotalMilliseconds < 1000
        ? $"{Math.Max(1, Math.Round(interval.TotalMilliseconds)):0} ms"
        : $"{interval.TotalSeconds:0.##} s";

    private void RestoreLiveArea()
    {
        flashTimer.Stop();
        var running = clickCancellation is not null;
        LiveArea.Background = ThemeManager.Brush(running ? "AccentBrush" : "ControlBrush");
        LiveArea.BorderBrush = ThemeManager.Brush(running ? "AccentHoverBrush" : "LiveBorderBrush");
        UpdateLiveAreaTextContrast();
    }
    private void ResetCounterWhenIdle()
    {
        var now = DateTime.UtcNow;
        if (liveClickCount > 0 && now - lastLiveClick >= TimeSpan.FromSeconds(3))
        {
            liveClickCount = 0; LiveCountLabel.Text = clickCancellation is null ? "Start to test" : "0 clicks";
            LiveIntervalLabel.Text = "Waiting for clicks";
        }
        if (lastLiveKey != default && now - lastLiveKey >= TimeSpan.FromSeconds(3))
        {
            lastLiveKey = default;
            liveKeyPressCount = 0;
            KeyTestBox.Clear();
            KeyTestPlaceholder.Visibility = Visibility.Visible;
            LiveIntervalLabel.Text = "Waiting for key presses";
            LiveCountLabel.Text = KeyTestBox.IsKeyboardFocusWithin ? FormatKeyPressCount(0) : "Focus the field to test";
        }
    }

    private void UpdateLiveInputMode()
    {
        if (ButtonCombo?.SelectedItem is not ComboBoxItem
            || LiveMouseHint is null || LiveKeyFocusBox is null || LiveTitleLabel is null
            || IntervalHint is null || LiveCountLabel is null || LiveIntervalLabel is null || TypeCombo is null) return;
        var sequenceInput = Selected(ButtonCombo) == "Sequence";
        var keyboardInput = IsKeyboardInputSelected();
        TypeCombo.IsEnabled = !sequenceInput;
        LiveArea.IsHitTestVisible = !sequenceInput;
        LiveArea.Opacity = sequenceInput ? 0.7 : 1;
        if (sequenceInput)
        {
            LiveMouseHint.Visibility = Visibility.Visible;
            LiveKeyFocusBox.Visibility = Visibility.Collapsed;
            LiveTitleLabel.Text = "SEQUENCE MODE";
            LiveMouseHint.Text = "Test area disabled";
            LiveCountLabel.Text = "Custom sequence";
            LiveIntervalLabel.Text = "Configure steps from the Input menu";
            IntervalHint.Text = "Time between sequences";
            UpdateLiveAreaTextContrast();
            return;
        }
        LiveMouseHint.Text = "Hover here while running";
        LiveMouseHint.Visibility = keyboardInput ? Visibility.Collapsed : Visibility.Visible;
        LiveKeyFocusBox.Visibility = keyboardInput ? Visibility.Visible : Visibility.Collapsed;
        LiveTitleLabel.Text = keyboardInput ? "LIVE SPAM AREA" : "LIVE CLICK AREA";
        IntervalHint.Text = keyboardInput ? "Time between key presses" : "Time between clicks";
        if (keyboardInput)
        {
            LiveCountLabel.Text = clickCancellation is null ? "Start to test" : KeyTestBox.IsKeyboardFocusWithin ? FormatKeyPressCount(liveKeyPressCount) : "Focus the field to test";
            if (lastLiveKey == default) LiveIntervalLabel.Text = "Waiting for key presses";
        }
        if (!keyboardInput)
        {
            lastLiveKey = default;
            liveKeyPressCount = 0;
            KeyTestBox?.Clear();
            if (KeyTestPlaceholder is not null) KeyTestPlaceholder.Visibility = Visibility.Visible;
        }
        UpdateLiveAreaTextContrast();
    }

    private bool IsKeyboardInputSelected() => ButtonCombo?.SelectedItem is ComboBoxItem && InputRules.IsKeyboardAction(Selected(ButtonCombo));

    private void ShowReadyActionStatus()
    {
        if (clickCancellation is not null || StatusLabel is null) return;
        Status($"Ready — {SelectedActionDescription()} will be repeated.", ThemeManager.Brush("SuccessBrush"));
    }

    private string SelectedActionDescription() => InputRules.DescribeAction(Selected(ButtonCombo), customSpamVirtualKey);

    private string ActivityVerb() => Selected(ButtonCombo) == "Sequence"
        ? "Running sequence"
        : IsKeyboardInputSelected() ? "Spamming" : "Clicking";

    private void UpdateLiveAreaTextContrast()
    {
        if (LiveTitleLabel is null) return;
        var brush = ThemeManager.Brush(ThemeManager.Current == AppTheme.Light && clickCancellation is not null ? "LiveAccentTextBrush" : "TextMutedBrush");
        LiveTitleLabel.Foreground = brush;
        LiveIntervalLabel.Foreground = brush;
        LiveMouseHint.Foreground = brush;
        KeyTestPlaceholder.Foreground = brush;
    }

    private static string FormatKeyPressCount(int count) => count == 1 ? "1 key press" : $"{count:N0} key presses";

    private void StartRgbIndicator()
    {
        if (!rgbSettings.Enabled) return;
        var generation = Interlocked.Increment(ref rgbIndicatorGeneration);
        var settings = new RgbSettings { Enabled = true, DeviceIndex = rgbSettings.DeviceIndex, DeviceName = rgbSettings.DeviceName, AutoStart = rgbSettings.AutoStart, StopAutoStartedOnExit = rgbSettings.StopAutoStartedOnExit, IndicatorColor = rgbSettings.IndicatorColor, LightingEffect = rgbSettings.LightingEffect, PulseSpeedMilliseconds = rgbSettings.PulseSpeedMilliseconds };
        var keyName = HotkeyKeyName();
        _ = Task.Run(() =>
        {
            try
            {
                var availability = OpenRgbHighlighter.EnsureSdkAsync(settings).GetAwaiter().GetResult();
                if (!availability.IsAvailable)
                {
                    if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(() => Status(availability.Message ?? "OpenRGB's SDK server is unavailable.", ThemeManager.Brush("ErrorBrush")));
                    return;
                }
                if (availability.Message is not null && !Dispatcher.HasShutdownStarted)
                    _ = Dispatcher.BeginInvoke(() => ShowOpenRgbStartedStatus(generation));
                var keyboard = OpenRgbHighlighter.ResolveKeyboard(settings);
                if (keyboard is null)
                {
                    if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(() => Status("No matching OpenRGB keyboard found. Open Settings to choose one.", ThemeManager.Brush("ErrorBrush")));
                    return;
                }
                settings.DeviceIndex = keyboard.Index;
                settings.DeviceName = keyboard.Name;
                var snapshot = OpenRgbHighlighter.EnableKeyIndicator(settings, keyName, out var error);
                if (snapshot is not null)
                {
                    var restoreImmediately = false;
                    lock (rgbLock)
                    {
                        if (generation == rgbIndicatorGeneration)
                        {
                            rgbSnapshot = snapshot;
                            if (settings.IsPulse)
                            {
                                var pulseCancellation = new CancellationTokenSource();
                                rgbPulseCancellation = pulseCancellation;
                                rgbPulseTask = Task.Run(() => OpenRgbHighlighter.PulseIndicatorAsync(snapshot, settings.PulseSpeedMilliseconds, pulseCancellation.Token));
                            }
                        }
                        else restoreImmediately = true;
                    }
                    if (restoreImmediately) OpenRgbHighlighter.RestoreIndicator(snapshot);
                    if (!Dispatcher.HasShutdownStarted && (settings.DeviceIndex != rgbSettings.DeviceIndex || !string.Equals(settings.DeviceName, rgbSettings.DeviceName, StringComparison.Ordinal)))
                        Dispatcher.BeginInvoke(() => { rgbSettings = settings; SaveRgbSettings(); });
                }
                if (error is not null && !Dispatcher.HasShutdownStarted)
                    Dispatcher.BeginInvoke(() => Status(error, ThemeManager.Brush("ErrorBrush")));
            }
            catch (Exception exception) when (!Dispatcher.HasShutdownStarted)
            {
                AppLog.Error("OpenRGB hotkey indicator failed", exception);
                Dispatcher.BeginInvoke(() => Status($"OpenRGB unavailable: {exception.Message}", ThemeManager.Brush("ErrorBrush")));
            }
        });
    }

    private void FlashSelectedHotkey()
    {
        if (!rgbSettings.Enabled) return;
        var settings = new RgbSettings { Enabled = true, DeviceIndex = rgbSettings.DeviceIndex, DeviceName = rgbSettings.DeviceName, AutoStart = rgbSettings.AutoStart, StopAutoStartedOnExit = rgbSettings.StopAutoStartedOnExit, IndicatorColor = rgbSettings.IndicatorColor, LightingEffect = rgbSettings.LightingEffect, PulseSpeedMilliseconds = rgbSettings.PulseSpeedMilliseconds };
        var keyName = HotkeyKeyName();
        _ = Task.Run(async () =>
        {
            try
            {
                var availability = await OpenRgbHighlighter.EnsureSdkAsync(settings);
                if (!availability.IsAvailable)
                {
                    AppLog.Info($"Could not flash newly selected hotkey: {availability.Message}");
                    return;
                }
                var keyboard = OpenRgbHighlighter.ResolveKeyboard(settings);
                if (keyboard is null)
                {
                    AppLog.Info("Could not flash newly selected hotkey: no matching OpenRGB keyboard.");
                    return;
                }
                settings.DeviceIndex = keyboard.Index;
                settings.DeviceName = keyboard.Name;
                var error = await OpenRgbHighlighter.FlashKeyAsync(settings, keyName);
                if (error is not null) AppLog.Info($"Could not flash newly selected hotkey: {error}");
                else if (settings.DeviceIndex != rgbSettings.DeviceIndex || !string.Equals(settings.DeviceName, rgbSettings.DeviceName, StringComparison.Ordinal))
                    _ = Dispatcher.BeginInvoke(() => { rgbSettings = settings; SaveRgbSettings(); });
            }
            catch (Exception exception)
            {
                AppLog.Error("Could not flash newly selected hotkey", exception);
            }
        });
    }

    private void StopRgbIndicator()
    {
        Interlocked.Increment(ref rgbIndicatorGeneration);
        RgbLightingSnapshot? snapshot;
        CancellationTokenSource? pulseCancellation;
        Task? pulseTask;
        lock (rgbLock)
        {
            snapshot = rgbSnapshot;
            rgbSnapshot = null;
            pulseCancellation = rgbPulseCancellation;
            rgbPulseCancellation = null;
            pulseTask = rgbPulseTask;
            rgbPulseTask = null;
        }
        pulseCancellation?.Cancel();
        if (snapshot is null) return;
        _ = Task.Run(async () =>
        {
            try { if (pulseTask is not null) await pulseTask; }
            catch (Exception exception) { AppLog.Error("OpenRGB pulse cleanup failed", exception); }
            finally
            {
                pulseCancellation?.Dispose();
                OpenRgbHighlighter.RestoreIndicator(snapshot);
            }
        });
    }

    private void RepeatMode_Changed(object sender, RoutedEventArgs e)
    {
        if (CountBox is not null && CountRadio is not null) CountBox.IsEnabled = CountRadio.IsChecked == true;
    }
    private void PositionMode_Changed(object sender, RoutedEventArgs e)
    {
        if (XBox is null || YBox is null || FixedPositionRadio is null) return;
        var enabled = FixedPositionRadio.IsChecked == true; XBox.IsEnabled = enabled; YBox.IsEnabled = enabled;
    }
    private void SaveDefaultButton_Click(object sender, RoutedEventArgs e)
    {
        var confirmation = new ConfirmationWindow(
            "Set as default",
            "Save every current option as the startup default? You can reset the app back to its original defaults from Settings.",
            "Set as default") { Owner = this };
        if (confirmation.ShowDialog() == true) SaveDefaults();
    }

    private bool ResetToFactoryDefaults()
    {
        if (clickCancellation is not null)
        {
            Status($"Stop {ActivityVerb().ToLowerInvariant()} before resetting defaults.", ThemeManager.Brush("WarningBrush"));
            return false;
        }

        if (hotkeyRegistered) { UnregisterHotKey(hwnd, HotkeyId); hotkeyRegistered = false; }
        ApplyDefaults(new AppDefaults());
        ThemeManager.Apply(AppTheme.Dark);
        UpdateThemeButton();
        RestoreLiveArea();
        RegisterConfiguredHotkey();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DefaultsPath)!);
            File.WriteAllText(DefaultsPath, JsonSerializer.Serialize(new AppDefaults()));
        }
        catch { }
        SaveRgbSettings();
        CrashRecovery.UpdateEnabled(rgbSettings.CrashRecoveryEnabled);
        Topmost = false;
        compactMode = false;
        UpdatePinUi();
        ApplyCompactMode();
        SaveUiPreferences();
        Status("Factory default values restored.", ThemeManager.Brush("SuccessBrush"));
        return true;
    }

    private void SaveDefaults()
    {
        try
        {
            var settings = CreateCurrentDefaults();
            Directory.CreateDirectory(Path.GetDirectoryName(DefaultsPath)!);
            File.WriteAllText(DefaultsPath, JsonSerializer.Serialize(settings));
            Status("Current settings saved as the default.", ThemeManager.Brush("SuccessBrush"));
        }
        catch { Status("Could not save the default settings.", ThemeManager.Brush("ErrorBrush")); }
    }

    private AppDefaults CreateCurrentDefaults() => new() { Hours = Read(HoursBox, 0, 999), Minutes = Read(MinutesBox, 0, 59), Seconds = Read(SecondsBox, 0, 59), Milliseconds = Read(MillisBox, 1, 999), MouseButton = Selected(ButtonCombo), Input = Selected(ButtonCombo), CustomKey = customSpamVirtualKey, CustomSequence = customSequence.Select(step => step.Clone()).ToList(), ClickType = Selected(TypeCombo), RepeatUntilStopped = UntilStoppedRadio.IsChecked == true, RepeatCount = Read(CountBox, 1, 999999), FixedPosition = FixedPositionRadio.IsChecked == true, X = Read(XBox, -32768, 32767), Y = Read(YBox, -32768, 32767), Hotkey = hotkey, HotkeyModifiers = hotkeyModifiers, Rgb = rgbSettings };

    private string? ExportFullBackup(string path)
    {
        try
        {
            ConfigBackupStore.Write(path, new ConfigBackupDocument
            {
                DefaultsJson = JsonSerializer.Serialize(CreateCurrentDefaults()),
                RgbJson = JsonSerializer.Serialize(rgbSettings),
                UiPreferencesJson = JsonSerializer.Serialize(new UiPreferences { Pinned = Topmost, CompactMode = compactMode }),
                AppearanceJson = ThemeManager.ExportConfiguration(),
                SequenceLibraryJson = JsonSerializer.Serialize(new SequenceLibraryDocument { Presets = sequenceLibrary.Select(preset => preset.Clone()).ToList() })
            });
            return null;
        }
        catch (Exception exception) { AppLog.Error("Could not export full backup", exception); return $"Could not export backup: {exception.Message}"; }
    }

    private string? ImportFullBackup(string path)
    {
        if (clickCancellation is not null) return "Stop AutoClicker before importing a backup.";
        try
        {
            var backup = ConfigBackupStore.Read(path);
            var defaults = JsonSerializer.Deserialize<AppDefaults>(backup.DefaultsJson) ?? throw new InvalidDataException("Backup settings are invalid.");
            var rgb = string.IsNullOrWhiteSpace(backup.RgbJson) ? defaults.Rgb ?? new RgbSettings() : JsonSerializer.Deserialize<RgbSettings>(backup.RgbJson) ?? new RgbSettings();
            var ui = string.IsNullOrWhiteSpace(backup.UiPreferencesJson) ? new UiPreferences() : JsonSerializer.Deserialize<UiPreferences>(backup.UiPreferencesJson) ?? new UiPreferences();
            var library = string.IsNullOrWhiteSpace(backup.SequenceLibraryJson) ? new SequenceLibraryDocument() : JsonSerializer.Deserialize<SequenceLibraryDocument>(backup.SequenceLibraryJson) ?? new SequenceLibraryDocument();
            if (!string.IsNullOrWhiteSpace(backup.AppearanceJson) && !ThemeManager.TryImportConfiguration(backup.AppearanceJson)) throw new InvalidDataException("Backup appearance settings are invalid.");

            if (hotkeyRegistered) { UnregisterHotKey(hwnd, HotkeyId); hotkeyRegistered = false; }
            ApplyDefaults(defaults);
            rgbSettings = rgb; SaveRgbSettings(); CrashRecovery.UpdateEnabled(rgb.CrashRecoveryEnabled);
            sequenceLibrary = library.Presets.Where(preset => preset.Steps.Count >= 2).Select(preset => preset.Clone()).ToList(); SaveSequenceLibrary(); RefreshSequencePresetActions();
            Topmost = ui.Pinned; compactMode = ui.CompactMode; UpdatePinUi(); ApplyCompactMode(); SaveUiPreferences();
            UpdateThemeButton(); RestoreLiveArea(); RegisterConfiguredHotkey();
            SaveDefaults();
            return null;
        }
        catch (Exception exception) { AppLog.Error("Could not import full backup", exception); return $"Could not import backup: {exception.Message}"; }
    }

    private bool LoadDefaults()
    {
        try
        {
            if (!File.Exists(DefaultsPath)) return false;
            var s = JsonSerializer.Deserialize<AppDefaults>(File.ReadAllText(DefaultsPath)); if (s is null) return false;
            ApplyDefaults(s);
            return true;
        }
        catch { return false; }
    }

    private void ApplyDefaults(AppDefaults s)
    {
        HoursBox.Text = s.Hours.ToString(); MinutesBox.Text = s.Minutes.ToString(); SecondsBox.Text = s.Seconds.ToString(); MillisBox.Text = s.Milliseconds.ToString();
        customSpamVirtualKey = s.CustomKey;
        customSequence = s.CustomSequence?.Select(step => step.Clone()).ToList() ?? [];
        SequenceItem.Content = customSequence.Count >= 2 ? $"Custom sequence ({customSequence.Count} actions)" : "Custom sequence";
        CustomKeyItem.Content = customSpamVirtualKey != 0 ? $"Key: {FormatInputKey(customSpamVirtualKey)}" : "Custom key";
        Select(ButtonCombo, string.IsNullOrWhiteSpace(s.Input) ? s.MouseButton : s.Input); Select(TypeCombo, s.ClickType); UntilStoppedRadio.IsChecked = s.RepeatUntilStopped; CountRadio.IsChecked = !s.RepeatUntilStopped; CountBox.Text = s.RepeatCount.ToString();
        CurrentPositionRadio.IsChecked = !s.FixedPosition; FixedPositionRadio.IsChecked = s.FixedPosition; XBox.Text = s.X.ToString(); YBox.Text = s.Y.ToString();
        hotkey = s.Hotkey > 0 ? s.Hotkey : System.Windows.Input.KeyInterop.VirtualKeyFromKey(System.Windows.Input.Key.F6); hotkeyModifiers = s.HotkeyModifiers;
        rgbSettings = s.Rgb ?? new RgbSettings();
        RepeatMode_Changed(this, new RoutedEventArgs());
        PositionMode_Changed(this, new RoutedEventArgs());
        UpdateHotkeyLabel();
    }

    private void SaveRgbSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RgbSettingsPath)!);
            File.WriteAllText(RgbSettingsPath, JsonSerializer.Serialize(rgbSettings));
        }
        catch { }
    }

    private void LoadSequenceLibrary() => sequenceLibrary = SequenceLibraryStore.Load(SequenceLibraryPath);
    private void SaveSequenceLibrary()
    {
        try { SequenceLibraryStore.Save(SequenceLibraryPath, sequenceLibrary); }
        catch { }
    }

    private void RefreshSequencePresetActions()
    {
        if (ButtonCombo is null || EditSequenceItem is null) return;
        foreach (var item in ButtonCombo.Items.OfType<ComboBoxItem>().Where(item => item.Tag?.ToString()?.StartsWith("Preset:", StringComparison.Ordinal) == true).ToList()) ButtonCombo.Items.Remove(item);
        var insertAt = ButtonCombo.Items.IndexOf(EditSequenceItem) + 1;
        foreach (var preset in sequenceLibrary.OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase))
            ButtonCombo.Items.Insert(insertAt++, new ComboBoxItem { Content = $"Sequence: {preset.Name}", Tag = $"Preset:{preset.Id}" });
    }

    private void LoadRgbSettings()
    {
        try
        {
            if (File.Exists(RgbSettingsPath)) rgbSettings = JsonSerializer.Deserialize<RgbSettings>(File.ReadAllText(RgbSettingsPath)) ?? rgbSettings;
        }
        catch { }
    }

    private void LoadUiPreferences()
    {
        var preferences = UiPreferencesStore.Load(UiPreferencesPath);
        Topmost = preferences.Pinned;
        compactMode = preferences.CompactMode;
        UpdatePinUi();
        ApplyCompactMode();
    }

    private void SaveUiPreferences()
    {
        try { UiPreferencesStore.Save(UiPreferencesPath, new UiPreferences { Pinned = Topmost, CompactMode = compactMode }); }
        catch { }
    }

    private void ApplyCompactMode()
    {
        if (SettingsContent is null || SetDefaultButton is null || CollapseButton is null) return;
        SettingsContent.Visibility = compactMode ? Visibility.Collapsed : Visibility.Visible;
        SetDefaultButton.Visibility = compactMode ? Visibility.Collapsed : Visibility.Visible;
        Height = compactMode ? CompactWindowHeight : ExpandedWindowHeight;
        CollapseButton.ContentTemplate = (DataTemplate)FindResource(compactMode ? "ExpandIcon" : "CollapseIcon");
        CollapseButton.ToolTip = compactMode ? "Show settings" : "Hide settings";
    }

    private static int Read(TextBox box, int min, int max) => InputRules.ParseClamped(box.Text, min, max);
    private static string Selected(ComboBox combo)
    {
        var item = (ComboBoxItem)combo.SelectedItem;
        return item.Tag?.ToString() ?? item.Content.ToString()!;
    }
    private static void Select(ComboBox combo, string value)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
            if (string.Equals(item.Tag?.ToString() ?? item.Content?.ToString(), value, StringComparison.Ordinal))
            {
                combo.SelectedItem = item;
                return;
            }
    }
    private void UpdateHotkeyLabel() => HotkeyLabel.Text = FormatHotkey();
    private void UpdateThemeButton()
    {
        var light = ThemeManager.Current == AppTheme.Light;
        ThemeButton.Content = "Theme";
        ThemeButton.ContentTemplate = (DataTemplate)FindResource(light ? "SunIcon" : "MoonIcon");
        ThemeButton.ToolTip = light ? "Switch to dark mode" : "Switch to light mode";
    }
    private void SetTaskbarIcon(bool running)
    {
        var asset = running ? "AutoClickerRunningIcon.ico" : "AutoClickerIcon.ico";
        Icon = new BitmapImage(new Uri($"pack://application:,,,/Assets/{asset}", UriKind.Absolute));
    }
    private string HotkeyKeyName() => System.Windows.Input.KeyInterop.KeyFromVirtualKey(hotkey).ToString();
    private string FormatHotkey() => FormatHotkey(hotkey, hotkeyModifiers);
    private static string FormatHotkey(int key, uint modifiers) => HotkeyFormatter.Format(key, modifiers);
    private static string FormatInputKey(int virtualKey)
    {
        var key = System.Windows.Input.KeyInterop.KeyFromVirtualKey(virtualKey);
        return key switch { System.Windows.Input.Key.Return => "Enter", System.Windows.Input.Key.Space => "Space", _ => key.ToString() };
    }
    private static uint GetModifiers() { uint m = 0; var mods = System.Windows.Input.Keyboard.Modifiers; if (mods.HasFlag(System.Windows.Input.ModifierKeys.Control)) m |= 2; if (mods.HasFlag(System.Windows.Input.ModifierKeys.Alt)) m |= 1; if (mods.HasFlag(System.Windows.Input.ModifierKeys.Shift)) m |= 4; return m; }
    private int Status(string text, Brush color)
    {
        var revision = ++statusRevision;
        if (StatusLabel is null) return revision;
        StatusLabel.Text = text;
        StatusLabel.Foreground = color;
        return revision;
    }

    private void ShowOpenRgbStartedStatus(int generation)
    {
        if (generation != rgbIndicatorGeneration || clickCancellation is null) return;
        var revision = Status("OpenRGB started automatically.", ThemeManager.Brush("SuccessBrush"));
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            if (!Dispatcher.HasShutdownStarted)
                _ = Dispatcher.BeginInvoke(() =>
                {
                    if (generation == rgbIndicatorGeneration && clickCancellation is not null && statusRevision == revision)
                        Status($"{ActivityVerb()} — press {FormatHotkey()} to stop.", ThemeManager.Brush("ErrorBrush"));
                });
        });
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        isClosing = true;
        var activeTask = clickTask;
        StopClicking();
        try { activeTask?.Wait(TimeSpan.FromSeconds(2)); } catch (Exception exception) { AppLog.Error("Error while waiting for worker shutdown", exception); }
        if (rgbSettings.StopAutoStartedOnExit) OpenRgbHighlighter.StopAutoStartedServer();
        resetTimer.Stop(); flashTimer.Stop(); guiHeartbeatTimer.Stop(); if (hotkeyRegistered) UnregisterHotKey(hwnd, HotkeyId); if (hwndSource is not null) hwndSource.RemoveHook(WndProc);
    }

    private static Input[] CreateClickInputs(string button)
    {
        var flags = button switch { "Right" => (MouseFlags.RightDown, MouseFlags.RightUp), "Middle" => (MouseFlags.MiddleDown, MouseFlags.MiddleUp), _ => (MouseFlags.LeftDown, MouseFlags.LeftUp) };
        return [new() { Type = 0, Data = new InputUnion { Mouse = new MouseInput { Flags = flags.Item1 } } }, new() { Type = 0, Data = new InputUnion { Mouse = new MouseInput { Flags = flags.Item2 } } }];
    }
    private static Input[] CreateKeyInputs(int virtualKey)
    {
        var flags = IsExtendedKey(virtualKey) ? KeyboardFlags.ExtendedKey : KeyboardFlags.None;
        return
        [
            new() { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = (ushort)virtualKey, Flags = flags } } },
            new() { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = (ushort)virtualKey, Flags = flags | KeyboardFlags.KeyUp } } }
        ];
    }
    private static SequenceAction[] BuildSequence(IEnumerable<SequenceStep> sequence) => sequence.Select(step =>
    {
        var key = step.Input switch { "Space" => 0x20, "Enter" => 0x0D, "Custom" => step.CustomKey, _ => 0 };
        return new SequenceAction(key == 0 ? CreateClickInputs(step.Input) : CreateKeyInputs(key), key == 0, Math.Clamp(step.DelayAfterMilliseconds, 0, 600000));
    }).ToArray();
    private static bool IsExtendedKey(int virtualKey) => virtualKey is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E or 0x5B or 0x5C or 0x5D or 0xA3 or 0xA5 or 0x6F;
    private static void SendAction(Input[] inputs, bool doubleClick)
    {
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (doubleClick) SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    private sealed record ClickSettings(bool FixedPosition, int X, int Y, string Button, int? KeyboardVirtualKey, bool DoubleClick, int? MaximumClicks, SequenceAction[]? Sequence);
    private sealed record SequenceAction(Input[] Inputs, bool IsMouse, int DelayAfterMilliseconds);
    private sealed class PrecisionTimer : IDisposable
    {
        private const uint TimerAllAccess = 0x001F0003;
        private const uint CreateHighResolution = 0x00000002;
        private const uint WaitObject0 = 0;
        private readonly nint handle;

        public PrecisionTimer()
        {
            handle = CreateWaitableTimerEx(nint.Zero, null, CreateHighResolution, TimerAllAccess);
            if (handle == 0) handle = CreateWaitableTimer(nint.Zero, false, null);
            if (handle == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        public void WaitUntil(double targetTimestamp, CancellationToken token)
        {
            var remainingTicks = targetTimestamp - Stopwatch.GetTimestamp();
            if (remainingTicks <= 0) return;
            var dueTime = -Math.Max(1L, (long)Math.Ceiling(remainingTicks * 10_000_000 / Stopwatch.Frequency));
            if (!SetWaitableTimer(handle, ref dueTime, 0, nint.Zero, nint.Zero, false)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var handles = new[] { handle, token.WaitHandle.SafeWaitHandle.DangerousGetHandle() };
            if (WaitForMultipleObjects(2, handles, false, uint.MaxValue) == WaitObject0 + 1) throw new OperationCanceledException(token);
        }

        public void Dispose() => CloseHandle(handle);
    }

    private sealed class AppDefaults { public int Hours { get; set; } public int Minutes { get; set; } public int Seconds { get; set; } public int Milliseconds { get; set; } = 100; public string MouseButton { get; set; } = "Left"; public string? Input { get; set; } public int CustomKey { get; set; } public List<SequenceStep>? CustomSequence { get; set; } public string ClickType { get; set; } = "Single"; public bool RepeatUntilStopped { get; set; } = true; public int RepeatCount { get; set; } = 10; public bool FixedPosition { get; set; } public int X { get; set; } public int Y { get; set; } public int Hotkey { get; set; } = 117; public uint HotkeyModifiers { get; set; } public RgbSettings? Rgb { get; set; } }
    [Flags] private enum MouseFlags : uint { LeftDown = 2, LeftUp = 4, RightDown = 8, RightUp = 16, MiddleDown = 32, MiddleUp = 64 }
    [Flags] private enum KeyboardFlags : uint { None = 0, ExtendedKey = 1, KeyUp = 2 }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int Dx, Dy; public uint MouseData; public MouseFlags Flags; public uint Time; public nint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort VirtualKey; public ushort ScanCode; public KeyboardFlags Flags; public uint Time; public nint ExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hWnd, int id);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWaitableTimerEx(nint attributes, string? name, uint flags, uint desiredAccess);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWaitableTimer(nint attributes, bool manualReset, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetWaitableTimer(nint timer, ref long dueTime, int period, nint completionRoutine, nint argument, bool resume);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForMultipleObjects(uint count, nint[] handles, bool waitAll, uint milliseconds);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
}
