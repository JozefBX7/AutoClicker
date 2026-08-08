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
    private static readonly string DefaultsPath = AppPaths.ConfigFile("defaults.json");
    private static readonly string GlobalDefaultsPath = AppPaths.ConfigFile("global-defaults.json");
    private static readonly string RgbSettingsPath = AppPaths.ConfigFile("rgb-settings.json");
    private static readonly string UiPreferencesPath = AppPaths.ConfigFile("ui-preferences.json");
    private static readonly string SequenceLibraryPath = AppPaths.ConfigFile("sequence-library.json");
    private static readonly string ProfilesPath = AppPaths.ConfigFile("automation-profiles.json");
    private const double ExpandedWindowHeight = 580;
    private const double AdvancedExpandedWindowHeight = 600;
    private const double CompactWindowHeight = 166;
    // Windows input is global; serialize each native packet batch so concurrent workers cannot interleave one dispatch call.
    private static readonly object nativeInputLock = new();
    private readonly DispatcherTimer resetTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer flashTimer = new() { Interval = TimeSpan.FromMilliseconds(85) };
    private readonly DispatcherTimer guiHeartbeatTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private CancellationTokenSource? clickCancellation;
    private Task? clickTask;
    private HwndSource? hwndSource;
    private nint hwnd;
    private bool hotkeyRegistered;
    private readonly Dictionary<int, AutomationAction> additionalHotkeys = [];
    private readonly Dictionary<MouseHotkey, AutomationAction?> mouseHotkeys = [];
    private AutomationAction? pendingActionDrag;
    private Point actionDragStart;
    private Border? actionDragTarget;
    private readonly LowLevelMouseProc mouseHookCallback;
    private nint mouseHook;
    private readonly Dictionary<string, CancellationTokenSource> profileRuns = [];
    private readonly Dictionary<string, Task> profileTasks = [];
    private bool capturingHotkey;
    private string? pendingNewActionId;
    private bool capturingSpamKey;
    private bool updatingActionSelection;
    private ComboBoxItem? actionBeforeKeyCapture;
    private int customSpamVirtualKey;
    private List<SequenceStep> customSequence = [];
    private List<SequencePreset> sequenceLibrary = [];
    private readonly List<ComboBoxItem> sequencePresetItems = [];
    private bool settingsOpen;
    private int hotkey = System.Windows.Input.KeyInterop.VirtualKeyFromKey(System.Windows.Input.Key.F6);
    private uint hotkeyModifiers;
    private HotkeyTrigger hotkeyTrigger = HotkeyTrigger.Keyboard;
    private int liveClickCount;
    private DateTime lastLiveClick;
    private int lastLiveClickTimestamp;
    private int liveKeyPressCount;
    private DateTime lastLiveKey;
    private volatile bool isClosing;
    private RgbSettings rgbSettings = new();
    private readonly object rgbLock = new();
    private readonly Dictionary<string, RgbIndicatorSession> rgbIndicators = [];
    private const string SimpleRgbIndicatorId = "simple";
    private long lastGuiHeartbeat;
    private int statusRevision;
    private string statusBrushKey = "SuccessBrush";
    private bool compactMode;
    private bool quickStartSeen;
    private string? targetWindowTitle;
    private int inputPulseMilliseconds = InputRules.DefaultInputPulseMilliseconds;
    private long inputJitterMaximumMilliseconds;
    private bool customSequenceUsesGlobalInputPulse = true;
    private bool profilesDirty;
    private string? editingProfileDefaultsId;
    private bool profileDefaultsEditingDirty;
    private bool suppressProfileDefaultTracking;
    private bool applyingDefaults;
    private string savedProfileConfiguration = string.Empty;
    private string? unsavedProfileId;
    private string? pendingRemovalActionId;
    private WorkerPriorityOption workerPriority = WorkerPriorityOption.Normal;
    private bool cadenceDiagnosticsEnabled;
    private AutomationProfileDocument automationProfiles = new();
    private bool advancedMode;
    // The active action still owns global hotkey registration; this set only controls what the editor is showing.
    private readonly HashSet<string> selectedAdvancedActionIds = new(StringComparer.Ordinal);

    public MainWindow()
    {
        // Keep the delegate alive for the entire window lifetime; Windows calls it outside normal WPF input routing.
        mouseHookCallback = MouseHookProc;
        InitializeComponent();
        LoadSequenceLibrary();
        RefreshSequencePresetActions();
        LoadDefaults();
        LoadAutomationProfiles();
        UpdateInputPulseButton();
        UpdateInputJitterButton();
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
        Loaded += (_, _) => _ = Dispatcher.BeginInvoke(ShowQuickStart, DispatcherPriority.ContextIdle);
        Loaded += (_, _) => StartConfiguredOpenRgb();
    }
    private void FindWindowsButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new TargetWindowPickerWindow(WindowTargeting.GetVisibleWindows()) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedWindow is not { } window) return;
        TargetExecutableBox.Text = window.ExecutableName;
        targetWindowTitle = window.Title;
        EnableTargetWindowCheckBox.IsChecked = true;
        UpdateTargetWindowUi();
        CommitBehaviorChange(AutomationBehaviorOverride.TargetWindow);
    }

    private void ClearTargetWindowButton_Click(object sender, RoutedEventArgs e)
    {
        targetWindowTitle = null;
        TargetExecutableBox.Text = string.Empty;
        EnableTargetWindowCheckBox.IsChecked = false;
        UpdateTargetWindowUi();
    }

    private void EnableTargetWindowCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateTargetWindowUi();
        UpdateLiveInputMode();
        CommitBehaviorChange(AutomationBehaviorOverride.TargetWindow);
    }

    private void InputPulseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new InputPulseWindow(inputPulseMilliseconds, InputTimingScopeDescription()) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        inputPulseMilliseconds = dialog.PulseMilliseconds;
        UpdateInputPulseButton();
        CommitInputTimingChange("Pulse");
    }

    private void InputJitterButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new InputJitterWindow(inputJitterMaximumMilliseconds, InputTimingScopeDescription()) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        inputJitterMaximumMilliseconds = dialog.MaximumJitterMilliseconds;
        UpdateInputJitterButton();
        CommitInputTimingChange("Jitter");
    }

    private enum InputTimingScope { SimpleMode, GlobalDefaults, ProfileDefaults, HotkeyOverride }

    private InputTimingScope CurrentInputTimingScope() => !advancedMode
        ? InputTimingScope.SimpleMode
        : editingProfileDefaultsId == ActiveProfile()?.Id
            ? InputTimingScope.ProfileDefaults
            : IsEditingAdvancedAction()
                ? InputTimingScope.HotkeyOverride
                : InputTimingScope.GlobalDefaults;

    private string InputTimingScopeDescription() => CurrentInputTimingScope() switch
    {
        InputTimingScope.GlobalDefaults => "global Advanced defaults",
        InputTimingScope.ProfileDefaults => $"{ActiveProfile()?.Name ?? "current"} profile defaults",
        InputTimingScope.HotkeyOverride => $"{FormatHotkey()} hotkey override",
        _ => "Simple mode settings"
    };

    private void CommitInputTimingChange(string settingName)
    {
        switch (CurrentInputTimingScope())
        {
            case InputTimingScope.GlobalDefaults:
            {
                var defaults = LoadSavedDefaults();
                defaults.InputPulseMilliseconds = inputPulseMilliseconds;
                defaults.InputJitterMaximumMilliseconds = inputJitterMaximumMilliseconds;
                if (WriteDefaults(GlobalDefaultsPath, defaults))
                    Status($"{settingName} saved to global Advanced defaults.", ThemeManager.Brush("SuccessBrush"));
                else
                    Status($"Could not save the global {settingName.ToLowerInvariant()} default.", ThemeManager.Brush("ErrorBrush"));
                break;
            }
            case InputTimingScope.ProfileDefaults:
                MarkProfileDefaultsEdited();
                Status($"{settingName} updated for {ActiveProfile()?.Name ?? "this"} profile defaults — save the profile when ready.", ThemeManager.Brush("SuccessBrush"));
                break;
            case InputTimingScope.HotkeyOverride:
                CaptureCurrentActionToProfile();
                Status($"{settingName} updated for the {FormatHotkey()} hotkey override.", ThemeManager.Brush("SuccessBrush"));
                break;
            default:
                Status($"{settingName} updated for Simple mode.", ThemeManager.Brush("SuccessBrush"));
                break;
        }
    }

    private void UpdateInputPulseButton()
    {
        if (InputPulseButton is null) return;
        InputPulseButton.Content = inputPulseMilliseconds == 0 ? "Pulse: Off" : $"Pulse: {inputPulseMilliseconds} ms";
    }

    private void UpdateInputJitterButton()
    {
        if (InputJitterButton is null) return;
        InputJitterButton.Content = inputJitterMaximumMilliseconds == 0
            ? "Jitter Off"
            : inputJitterMaximumMilliseconds < 1_000
                ? $"Jitter {inputJitterMaximumMilliseconds} ms"
                : $"Jitter {inputJitterMaximumMilliseconds / 1_000d:0.###} s";
        InputJitterButton.Tag = inputJitterMaximumMilliseconds == 0 ? null : "Active";
    }

    private void TargetExecutableBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TargetExecutableBox is null || TargetWindowHint is null) return;
        targetWindowTitle = null;
        UpdateTargetWindowUi();
        UpdateLiveInputMode();
        CommitBehaviorChange(AutomationBehaviorOverride.TargetWindow);
    }

    private void UpdateTargetWindowUi()
    {
        if (TargetExecutableBox is null || TargetWindowHint is null || EnableTargetWindowCheckBox is null || ClearTargetWindowButton is null) return;
        var hasTarget = !string.IsNullOrWhiteSpace(TargetExecutableBox.Text);
        var enabled = hasTarget && EnableTargetWindowCheckBox.IsChecked == true;
        EnableTargetWindowCheckBox.IsEnabled = hasTarget;
        ClearTargetWindowButton.IsEnabled = hasTarget;
        TargetWindowHint.Text = enabled
            ? targetWindowTitle is null ? "Executable target enabled." : "Window target enabled."
            : hasTarget ? "Target disabled. Global input enabled." : "Global input enabled.";
        TargetWindowHint.ToolTip = enabled
            ? targetWindowTitle is null ? "Input runs only while any active window from this executable is focused." : $"{targetWindowTitle} — {TargetExecutableBox.Text}"
            : "Input is sent to whichever window is active.";
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
        // Ignore the global hotkey during key capture.
        if (!capturingHotkey && !capturingSpamKey && msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            if (advancedMode && ActiveProfileAction() is { } action) ToggleProfileAction(action);
            else ToggleClicking();
            handled = true;
        }
        else if (!capturingHotkey && !capturingSpamKey && msg == WmHotkey && additionalHotkeys.TryGetValue(wParam.ToInt32(), out var action))
        {
            ToggleProfileAction(action);
            handled = true;
        }
        return 0;
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (advancedMode && !IsClicking) ShowAdvancedSharedDefaults(announce: true);
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove();
    }

    private void Header_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (IsClicking) return;
        if (FindParent<Button>(e.OriginalSource as DependencyObject) is not null) return;
        var menu = new ContextMenu { PlacementTarget = sender as UIElement };
        var resetOptions = new MenuItem { Header = "Reset options…" };
        resetOptions.Click += (_, _) => new ResetOptionsWindow(ResetSettings) { Owner = this }.ShowDialog();
        menu.Items.Add(resetOptions);
        menu.Items.Add(new Separator());
        var quickReset = new MenuItem
        {
            Header = advancedMode ? "Reset global Advanced defaults to app defaults…" : "Reset Simple mode defaults to app defaults…"
        };
        quickReset.Click += (_, _) => QuickResetCurrentModeDefaults();
        menu.Items.Add(quickReset);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void QuickResetCurrentModeDefaults()
    {
        var scope = advancedMode ? ResetScope.SharedDefaults : ResetScope.SimpleMode;
        var title = advancedMode ? "Reset global Advanced defaults" : "Reset Simple mode defaults";
        var message = advancedMode
            ? "Restore the global Advanced defaults to the app defaults? Profiles and hotkey overrides will be kept."
            : "Restore the Simple mode defaults to the app defaults?";
        var confirmation = new ConfirmationWindow(title, message, "Reset defaults") { Owner = this };
        if (confirmation.ShowDialog() == true) ResetSettings(scope);
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

    private void AdvancedHelpButton_Click(object sender, RoutedEventArgs e)
    {
        if (!advancedMode || IsClicking) return;
        new AdvancedModeHelpWindow { Owner = this }.ShowDialog();
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

    private void ShowQuickStart()
    {
        if (quickStartSeen || isClosing) return;
        var dialog = new QuickStartWindow { Owner = this };
        dialog.ShowDialog();
        quickStartSeen = true;
        SaveUiPreferences();
    }
    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (clickCancellation is not null || profileRuns.Count > 0)
        {
            Status($"Stop {ActivityVerb().ToLowerInvariant()} before opening Settings.", ThemeManager.Brush("WarningBrush"));
            return;
        }
        if (settingsOpen) return;
        settingsOpen = true;
        var dialog = new SettingsWindow(rgbSettings, workerPriority, cadenceDiagnosticsEnabled, advancedMode, FormatHotkey(), HotkeyKeyName(), ResetSettings, ExportFullBackup, ImportFullBackup) { Owner = this };
        try
        {
            if (dialog.ShowDialog() == true)
            {
                rgbSettings = dialog.Settings;
                workerPriority = dialog.WorkerPriority;
                cadenceDiagnosticsEnabled = dialog.CadenceDiagnosticsEnabled;
                if (dialog.AdvancedMode != advancedMode) SetAdvancedMode(dialog.AdvancedMode);
                SaveRgbSettings();
                SaveUiPreferences();
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
        BeginHotkeyCapture();
    }

    private void BeginHotkeyCapture()
    {
        if (capturingHotkey) return;
        var button = ActiveHotkeyButton();
        if (hotkeyRegistered) { UnregisterHotKey(hwnd, HotkeyId); hotkeyRegistered = false; }
        if (advancedMode)
        {
            foreach (var registeredId in additionalHotkeys.Keys.ToList()) UnregisterHotKey(hwnd, registeredId);
            additionalHotkeys.Clear();
        }
        mouseHotkeys.Clear();
        capturingHotkey = true;
        UpdateMouseHook();
        if (!advancedMode)
        {
            button.Content = "Cancel";
            button.ContentTemplate = (DataTemplate)FindResource("HotkeyCancelIcon");
            button.Width = 31;
            button.Padding = new Thickness(0);
        }
        button.ToolTip = "Keep the current hotkey";
        Status(advancedMode ? "Press a key combination or supported mouse input, or Escape to keep the current hotkey." : "Press a key combination or supported mouse input, or click Cancel to keep the current hotkey.", ThemeManager.Brush("WarningBrush"));
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
            if (!CommitSelectedActionChange()) ShowReadyActionStatus();
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
        if (IsProfileHotkeyAlreadyAssigned(candidate, modifiers, HotkeyTrigger.Keyboard))
        {
            CancelHotkeyCapture(keepStatus: true);
            Status($"{FormatHotkey(candidate, modifiers)} is already assigned in this profile.", ThemeManager.Brush("WarningBrush"));
            return;
        }
        if (RegisterHotKey(hwnd, HotkeyId, modifiers, (uint)candidate))
        {
            hotkeyRegistered = true;
            CompleteCapturedHotkey(candidate, modifiers, HotkeyTrigger.Keyboard);
        }
        else
        {
            RegisterConfiguredHotkey();
            CancelHotkeyCapture(keepStatus: true);
            Status($"{FormatHotkey(candidate, modifiers, HotkeyTrigger.Keyboard)} is already in use.", ThemeManager.Brush("ErrorBrush"));
        }
    }

    private void CompleteCapturedHotkey(int virtualKey, uint modifiers, HotkeyTrigger trigger)
    {
        if (IsProfileHotkeyAlreadyAssigned(virtualKey, modifiers, trigger))
        {
            CancelHotkeyCapture(keepStatus: true);
            Status($"{FormatHotkey(virtualKey, modifiers, trigger)} is already assigned in this profile.", ThemeManager.Brush("WarningBrush"));
            return;
        }
        hotkey = virtualKey;
        hotkeyModifiers = modifiers;
        hotkeyTrigger = trigger;
        CaptureCurrentActionToProfile();
        pendingNewActionId = null;
        RefreshAdvancedFooterUi();
        UpdateHotkeyLabel();
        CancelHotkeyCapture(keepStatus: true);
        Status($"Ready — press {FormatHotkey()} to start or stop.", ThemeManager.Brush("SuccessBrush"));
        FlashSelectedHotkey();
    }

    private bool IsProfileHotkeyAlreadyAssigned(int candidate, uint modifiers, HotkeyTrigger trigger)
    {
        if (!advancedMode || ActiveProfile() is not { } profile) return false;
        return profile.Actions.Any(action => action.Id != automationProfiles.ActiveActionId && action.MatchesHotkey(candidate, modifiers, trigger));
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        CancelPendingNewActionWhenClickingElsewhere(source);
        CancelPendingRemovalWhenClickingElsewhere(source);
        // Rebuilding the footer on mouse-down would remove a profile button before its Click event.
        // Let that button finish first so it can deliberately enter profile-default editing.
        var clickedProfileButton = FindParent<Button>(source)?.Tag is AutomationProfile;
        var withinFooter = IsWithin(source, AdvancedFooter);
        var editorDeadSpace = IsAdvancedEditorDeadSpace(source);
        if (!IsClicking && !clickedProfileButton && (ShouldReturnToSharedDefaults(advancedMode, IsWithinAdvancedActionTile(source), withinFooter)
            || ShouldReturnFromEditorDeadSpace(advancedMode, editorDeadSpace)))
            ShowAdvancedSharedDefaults(announce: true);

        var textBox = FindParent<TextBox>(source);
        if (textBox != HoursBox && textBox != MinutesBox && textBox != SecondsBox && textBox != MillisBox
            && textBox != CountBox && textBox != XBox && textBox != YBox) return;
        textBox.Focus();
        textBox.SelectAll();
        e.Handled = true;
    }

    private static bool IsWithinAdvancedActionTile(DependencyObject? source)
    {
        for (var current = source; current is not null;)
        {
            if (current is FrameworkElement { Name: "ActionTile" }) return true;
            current = current switch
            {
                Visual or System.Windows.Media.Media3D.Visual3D => VisualTreeHelper.GetParent(current),
                FrameworkContentElement contentElement => contentElement.Parent,
                _ => LogicalTreeHelper.GetParent(current)
            };
        }
        return false;
    }

    // Dropdown popups live outside the settings visual tree, so only actual footer space is treated as a click away.
    // The header has its own handler and also returns to shared defaults.
    internal static bool ShouldReturnToSharedDefaults(bool advancedMode, bool isWithinActionTile, bool isWithinFooter) =>
        advancedMode && !isWithinActionTile && isWithinFooter;

    internal static bool ShouldReturnFromEditorDeadSpace(bool advancedMode, bool isEditorDeadSpace) => advancedMode && isEditorDeadSpace;

    private bool IsAdvancedEditorDeadSpace(DependencyObject? source)
    {
        if (!IsWithin(source, SettingsContent) || IsWithin(source, ActionCard) || IsWithin(source, IntervalCard) || IsWithinSharedBehaviorSurface(source)) return false;
        for (var current = source; current is not null && !ReferenceEquals(current, SettingsContent);)
        {
            if (current is Button or TextBox or ComboBox or RadioButton or CheckBox or Slider or System.Windows.Controls.Primitives.ScrollBar) return false;
            current = current switch
            {
                Visual or System.Windows.Media.Media3D.Visual3D => VisualTreeHelper.GetParent(current),
                FrameworkContentElement contentElement => contentElement.Parent,
                _ => LogicalTreeHelper.GetParent(current)
            };
        }
        return true;
    }

    private static bool IsWithinSharedBehaviorSurface(DependencyObject? source)
    {
        for (var current = source; current is not null;)
        {
            if (current is FrameworkElement { Tag: string tag } && Enum.TryParse<AutomationBehaviorOverride>(tag, out _)) return true;
            current = current switch
            {
                Visual or System.Windows.Media.Media3D.Visual3D => VisualTreeHelper.GetParent(current),
                FrameworkContentElement contentElement => contentElement.Parent,
                _ => LogicalTreeHelper.GetParent(current)
            };
        }
        return false;
    }

    private static bool IsWithin(DependencyObject? source, DependencyObject? ancestor)
    {
        for (var current = source; current is not null;)
        {
            if (ReferenceEquals(current, ancestor)) return true;
            current = current switch
            {
                Visual or System.Windows.Media.Media3D.Visual3D => VisualTreeHelper.GetParent(current),
                FrameworkContentElement contentElement => contentElement.Parent,
                _ => LogicalTreeHelper.GetParent(current)
            };
        }
        return false;
    }

    private void CancelPendingNewActionWhenClickingElsewhere(DependencyObject? source)
    {
        if (!capturingHotkey || pendingNewActionId is not { } pendingActionId) return;
        if (FindAdvancedActionAt(source)?.Id == pendingActionId) return;
        if (ReferenceEquals(FindParent<Button>(source), AddAdvancedActionButton)) return;
        CancelHotkeyCapture();
    }

    private static AutomationAction? FindAdvancedActionAt(DependencyObject? source)
    {
        for (var current = source; current is not null;)
        {
            if (current is FrameworkElement { DataContext: AdvancedActionTile tile }) return tile.Action;
            current = current switch
            {
                Visual or System.Windows.Media.Media3D.Visual3D => VisualTreeHelper.GetParent(current),
                FrameworkContentElement contentElement => contentElement.Parent,
                _ => LogicalTreeHelper.GetParent(current)
            };
        }
        return null;
    }

    // Deletion confirmation is deliberately transient: interacting elsewhere keeps the assignment.
    private void CancelPendingRemovalWhenClickingElsewhere(DependencyObject? source)
    {
        if (pendingRemovalActionId is null) return;
        var button = FindParent<Button>(source);
        if (button?.ToolTip is "Confirm removal" or "Keep this hotkey") return;
        pendingRemovalActionId = null;
        RefreshAdvancedFooterUi();
    }

    private void IntervalBox_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        NormalizeIntervalBoxes();
        if (!applyingDefaults) CommitIntervalChange();
    }

    private void CommitIntervalChange()
    {
        var interval = CreateCurrentDefaults();
        if (!advancedMode)
        {
            SaveDefaults();
            return;
        }
        if (editingProfileDefaultsId == ActiveProfile()?.Id && ActiveProfile() is { } profile)
        {
            if (HasSameInterval(interval, AutomationBehaviorSettingsResolver.ResolveProfileDefaults(LoadSavedDefaults(), profile))) return;
            var local = profile.BehaviorDefaults?.Clone() ?? LoadSavedDefaults();
            CopyBehaviorOverride(interval, local, AutomationBehaviorOverride.Interval);
            profile.BehaviorDefaults = local;
            profile.UsesSharedBehaviorDefaults = true;
            profile.BehaviorOverrides |= AutomationBehaviorOverride.Interval;
            MarkProfilesDirty();
            return;
        }
        if (IsEditingAdvancedAction() && ActiveProfileAction() is { } action)
        {
            if (HasSameInterval(interval, ResolveActionSettings(action))) return;
            CopyBehaviorOverride(interval, action.Settings, AutomationBehaviorOverride.Interval);
            action.UsesSharedBehaviorDefaults = true;
            action.BehaviorOverrides |= AutomationBehaviorOverride.Interval;
            MarkProfilesDirty();
            return;
        }
        var defaults = LoadSavedDefaults();
        if (HasSameInterval(interval, defaults)) return;
        CopyBehaviorOverride(interval, defaults, AutomationBehaviorOverride.Interval);
        WriteDefaults(GlobalDefaultsPath, defaults);
    }

    private static bool HasSameInterval(AppDefaults left, AppDefaults right) =>
        left.Hours == right.Hours && left.Minutes == right.Minutes && left.Seconds == right.Seconds && left.Milliseconds == right.Milliseconds;

    private InputRules.IntervalParts NormalizeIntervalBoxes()
    {
        var parts = InputRules.NormalizeInterval(ParseIntervalPart(HoursBox.Text), ParseIntervalPart(MinutesBox.Text), ParseIntervalPart(SecondsBox.Text), ParseIntervalPart(MillisBox.Text));
        HoursBox.Text = parts.Hours.ToString();
        MinutesBox.Text = parts.Minutes.ToString();
        SecondsBox.Text = parts.Seconds.ToString();
        MillisBox.Text = parts.Milliseconds.ToString();
        return parts;
    }

    private static long ParseIntervalPart(string? value) => long.TryParse(value, out var parsed) ? Math.Max(0, parsed) : 0;

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
        var button = ActiveHotkeyButton();
        capturingHotkey = false;
        button.Content = "Edit";
        button.ContentTemplate = (DataTemplate)FindResource("HotkeyEditIcon");
        button.Width = 31;
        button.Padding = new Thickness(0);
        button.ToolTip = "Change hotkey";
        if (pendingNewActionId is { } pendingActionId)
        {
            pendingNewActionId = null;
            AbandonPendingNewAction(pendingActionId);
            if (!keepStatus) Status("New hotkey was not added.", ThemeManager.Brush("TextMutedBrush"));
            return;
        }
        if (advancedMode || !hotkeyRegistered) RegisterConfiguredHotkey();
        if (!keepStatus) Status($"Ready — press {FormatHotkey()} to start or stop.", ThemeManager.Brush("SuccessBrush"));
    }

    private void AbandonPendingNewAction(string actionId)
    {
        var profile = ActiveProfile();
        var action = profile?.Actions.FirstOrDefault(item => item.Id == actionId);
        if (profile is not null && action is not null)
        {
            profile.Actions.Remove(action);
            automationProfiles.ActiveActionId = profile.Actions.FirstOrDefault()?.Id ?? string.Empty;
            selectedAdvancedActionIds.Clear();
            ShowAdvancedSharedDefaults(clearSelection: false);
            MarkProfilesDirty();
        }
        RegisterConfiguredHotkey();
        UpdateLiveInputMode();
    }

    private void ActionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (applyingDefaults || updatingActionSelection || ButtonCombo is null) return;
        var selectedAction = Selected(ButtonCombo);
        UpdateActionPlaceholder();
        if (selectedAction.StartsWith("SequencePreset:", StringComparison.Ordinal))
        {
            var preset = sequenceLibrary.FirstOrDefault(item => item.Id == selectedAction["SequencePreset:".Length..]);
            if (preset is not null) ApplySequencePreset(preset);
            return;
        }
        if (selectedAction == "Sequence")
        {
            if (Selected(TypeCombo) == "Hold") Select(TypeCombo, "Single");
            SequenceItem.Content = "Custom sequence";
            UpdateLiveInputMode();
            if (!CommitSelectedActionChange()) ShowReadyActionStatus();
            return;
        }
        if (selectedAction == "EditSequence")
        {
            var previous = e.RemovedItems.OfType<ComboBoxItem>().FirstOrDefault();
            var editor = new SequenceEditorWindow(customSequence, customSequenceUsesGlobalInputPulse, sequenceLibrary) { Owner = this };
            var accepted = editor.ShowDialog() == true;
            if (accepted)
            {
                customSequence = editor.Steps.Select(step => step.Clone()).ToList();
                customSequenceUsesGlobalInputPulse = editor.UseGlobalInputPulse;
            }
            if (editor.LibraryChanged)
            {
                sequenceLibrary = editor.Library.Select(preset => preset.Clone()).ToList();
                SaveSequenceLibrary();
                RefreshSequencePresetActions();
            }
            if (!accepted && previous is not null) { updatingActionSelection = true; ButtonCombo.SelectedItem = previous; updatingActionSelection = false; UpdateLiveInputMode(); return; }
            if (customSequence.Count >= 2) { SequenceItem.Content = "Custom sequence"; updatingActionSelection = true; ButtonCombo.SelectedItem = SequenceItem; updatingActionSelection = false; }
            else if (previous is not null) { updatingActionSelection = true; ButtonCombo.SelectedItem = previous; updatingActionSelection = false; }
            UpdateLiveInputMode();
            if (!accepted || !CommitSelectedActionChange()) ShowReadyActionStatus();
            return;
        }
        if (Selected(ButtonCombo) != "Custom")
        {
            UpdateLiveInputMode();
            if (!CommitSelectedActionChange()) ShowReadyActionStatus();
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

    // Input selection is the one part of a hotkey that always belongs to the tile, even when it uses shared behavior defaults.
    private bool CommitSelectedActionChange()
    {
        if (!advancedMode || !IsEditingAdvancedAction() || ActiveProfileAction() is not { } action) return false;
        action.Settings = CreateCurrentDefaults();
        MarkProfilesDirty();
        UpdateActionEditorHint();
        Status($"Updated {FormatHotkey()} — {action.ActionDescription}.", ThemeManager.Brush("SuccessBrush"));
        return true;
    }

    private void RegisterConfiguredHotkey()
    {
        if (hwnd == 0) return;
        if (hotkeyRegistered) UnregisterHotKey(hwnd, HotkeyId);
        foreach (var registeredId in additionalHotkeys.Keys.ToList()) UnregisterHotKey(hwnd, registeredId);
        additionalHotkeys.Clear();
        mouseHotkeys.Clear();
        var activeAction = ActiveProfileAction();
        if (advancedMode)
        {
            hotkey = activeAction?.Settings.Hotkey ?? 0;
            hotkeyModifiers = activeAction?.Settings.HotkeyModifiers ?? 0;
            hotkeyTrigger = activeAction?.Settings.HotkeyTrigger ?? HotkeyTrigger.Keyboard;
        }
        var registerActiveHotkey = !advancedMode || activeAction?.HotkeyEnabled == true;
        hotkeyRegistered = registerActiveHotkey && hotkeyTrigger == HotkeyTrigger.Keyboard && hotkey > 0 && RegisterHotKey(hwnd, HotkeyId, hotkeyModifiers, (uint)hotkey);
        if (registerActiveHotkey && hotkeyTrigger == HotkeyTrigger.Keyboard && hotkey > 0 && !hotkeyRegistered) Status($"{FormatHotkey()} is in use — choose another key.", ThemeManager.Brush("ErrorBrush"));
        if (registerActiveHotkey && hotkeyTrigger != HotkeyTrigger.Keyboard) RegisterMouseHotkey(new MouseHotkey(hotkeyTrigger, hotkeyModifiers), advancedMode ? activeAction : null);
        if (!advancedMode) { UpdateMouseHook(); return; }
        var profile = ActiveProfile();
        if (profile is null) { UpdateMouseHook(); return; }
        var additionalId = HotkeyId + 1;
        foreach (var action in profile.Actions.Where(action => action.HotkeyEnabled && action.Id != automationProfiles.ActiveActionId && HotkeyFormatter.IsConfigured(action.Settings.Hotkey, action.Settings.HotkeyTrigger)))
        {
            if (action.Settings.HotkeyTrigger == hotkeyTrigger && action.Settings.Hotkey == hotkey && action.Settings.HotkeyModifiers == hotkeyModifiers) continue;
            if (action.Settings.HotkeyTrigger != HotkeyTrigger.Keyboard)
            {
                RegisterMouseHotkey(new MouseHotkey(action.Settings.HotkeyTrigger, action.Settings.HotkeyModifiers), action);
                continue;
            }
            if (RegisterHotKey(hwnd, additionalId, action.Settings.HotkeyModifiers, (uint)action.Settings.Hotkey)) additionalHotkeys[additionalId] = action;
            else AppLog.Info($"Could not register profile hotkey {action.DisplayName}.");
            additionalId++;
        }
        UpdateMouseHook();
    }

    private void RegisterMouseHotkey(MouseHotkey hotkeyBinding, AutomationAction? action)
    {
        if (!mouseHotkeys.TryAdd(hotkeyBinding, action))
            AppLog.Info($"Could not register duplicate mouse hotkey {HotkeyFormatter.Format(0, hotkeyBinding.Modifiers, hotkeyBinding.Trigger)}.");
    }

    // A low-level hook is only kept while a mouse binding exists or the capture prompt is open.
    // Its callback only matches gestures and returns immediately; all UI and worker work is queued to WPF.
    private void UpdateMouseHook()
    {
        var needed = !isClosing && (capturingHotkey || mouseHotkeys.Count > 0);
        if (needed && mouseHook == 0)
        {
            mouseHook = SetWindowsHookEx(MouseHookId, mouseHookCallback, GetModuleHandle(null), 0);
            if (mouseHook == 0) AppLog.Error("Could not install the mouse hotkey hook", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
        }
        else if (!needed && mouseHook != 0)
        {
            UnhookWindowsHookEx(mouseHook);
            mouseHook = 0;
        }
    }

    private nint MouseHookProc(int code, nint wParam, nint lParam)
    {
        if (code < 0 || isClosing)
            return CallNextHookEx(mouseHook, code, wParam, lParam);

        var data = Marshal.PtrToStructure<LowLevelMouseData>(lParam);
        if (!TryGetMouseTrigger(wParam.ToInt32(), data, out var trigger))
            return CallNextHookEx(mouseHook, code, wParam, lParam);
        // SendInput-generated middle clicks must never turn a configured mouse binding back off.
        if ((data.Flags & LowLevelMouseInjected) != 0)
            return CallNextHookEx(mouseHook, code, wParam, lParam);

        var binding = new MouseHotkey(trigger, GetMouseModifiers());
        if (capturingHotkey)
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                if (capturingHotkey) CompleteCapturedHotkey(0, binding.Modifiers, binding.Trigger);
            });
            return 1;
        }
        if (capturingSpamKey || !mouseHotkeys.TryGetValue(binding, out var action))
            return CallNextHookEx(mouseHook, code, wParam, lParam);

        _ = Dispatcher.BeginInvoke(() =>
        {
            if (capturingHotkey || capturingSpamKey) return;
            if (advancedMode && action is not null) ToggleProfileAction(action);
            else ToggleClicking();
        });
        // A mouse hotkey behaves like a keyboard hotkey: it is reserved for AutoClicker, not forwarded to the target app.
        return 1;
    }

    private static bool TryGetMouseTrigger(int message, LowLevelMouseData data, out HotkeyTrigger trigger)
    {
        trigger = message switch
        {
            WmMiddleButtonDown => HotkeyTrigger.MiddleMouse,
            WmMouseWheel => GetWheelTrigger(data, horizontal: false),
            WmMouseHorizontalWheel => GetWheelTrigger(data, horizontal: true),
            WmXButtonDown => GetXButtonTrigger(data),
            _ => HotkeyTrigger.Keyboard
        };
        return trigger != HotkeyTrigger.Keyboard;
    }

    private static HotkeyTrigger GetXButtonTrigger(LowLevelMouseData data) =>
        ((data.MouseData >> 16) & 0xffff) == 2
            ? HotkeyTrigger.Mouse5
            : HotkeyTrigger.Mouse4;

    private static HotkeyTrigger GetWheelTrigger(LowLevelMouseData data, bool horizontal)
    {
        var delta = unchecked((short)(data.MouseData >> 16));
        return horizontal
            ? delta < 0 ? HotkeyTrigger.WheelLeft : HotkeyTrigger.WheelRight
            : delta < 0 ? HotkeyTrigger.WheelDown : HotkeyTrigger.WheelUp;
    }

    private static uint GetMouseModifiers()
    {
        uint modifiers = 0;
        if (IsKeyPressed(0x11)) modifiers |= 2; // Ctrl
        if (IsKeyPressed(0x12)) modifiers |= 1; // Alt
        if (IsKeyPressed(0x10)) modifiers |= 4; // Shift
        return modifiers;
    }

    private static bool IsKeyPressed(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    private AutomationProfile? ActiveProfile() => automationProfiles.Profiles.FirstOrDefault(profile => profile.Id == automationProfiles.ActiveProfileId);
    private AutomationAction? ActiveProfileAction() => ActiveProfile()?.Actions.FirstOrDefault(action => action.Id == automationProfiles.ActiveActionId);

    private void LoadAutomationProfiles()
    {
        // Advanced profiles start from their own global defaults, never from Simple mode's saved values.
        automationProfiles = AutomationProfileStore.Load(ProfilesPath, LoadSavedDefaults());
        var profile = ActiveProfile() ?? automationProfiles.Profiles.First();
        var action = profile.Actions.FirstOrDefault(action => action.Id == automationProfiles.ActiveActionId) ?? profile.Actions.FirstOrDefault();
        automationProfiles.ActiveProfileId = profile.Id;
        automationProfiles.ActiveActionId = action?.Id ?? string.Empty;
        TouchRecentProfile(profile.Id);
        ApplyDefaults(action is null ? LoadSavedDefaults() : ResolveActionSettings(action));
        PersistAutomationProfiles();
        RefreshAdvancedFooterUi();
    }

    private void PersistAutomationProfiles()
    {
        try
        {
            AutomationProfileStore.Save(ProfilesPath, automationProfiles);
            savedProfileConfiguration = AutomationProfileConfiguration.Fingerprint(automationProfiles);
            profilesDirty = false;
        }
        catch (Exception exception) { AppLog.Error("Could not save automation profiles", exception); }
    }

    // Active/recent profile navigation is useful state, but changing it must not silently save an edited profile.
    private void PersistProfileNavigation()
    {
        if (profilesDirty) return;
        try { AutomationProfileStore.Save(ProfilesPath, automationProfiles); }
        catch (Exception exception) { AppLog.Error("Could not save selected automation profile", exception); }
    }

    private void SaveAutomationProfiles() => MarkProfilesDirty();

    private void MarkProfilesDirty()
    {
        profilesDirty = profileDefaultsEditingDirty
            || ActiveProfile()?.Id == unsavedProfileId
            || !string.Equals(savedProfileConfiguration, AutomationProfileConfiguration.Fingerprint(automationProfiles), StringComparison.Ordinal);
        RefreshAdvancedFooterUi();
    }

    private void CaptureCurrentActionToProfile()
    {
        if (advancedMode && !IsEditingAdvancedAction()) { CaptureProfileDefaults(); return; }
        var action = ActiveProfileAction();
        if (action is null) return;
        var settings = CreateCurrentDefaults();
        var current = advancedMode ? ResolveActionSettings(action) : action.Settings;
        if (JsonSerializer.Serialize(current) == JsonSerializer.Serialize(settings)) return;
        action.Settings = settings;
        if (advancedMode) MarkProfilesDirty();
    }

    private void CaptureProfileDefaults()
    {
        if (!advancedMode || suppressProfileDefaultTracking || editingProfileDefaultsId != ActiveProfile()?.Id || ActiveProfile() is not { } profile) return;
        var overrides = profile.ActiveBehaviorOverrides;
        if (overrides == AutomationBehaviorOverride.None)
        {
            profile.BehaviorDefaults = null;
            return;
        }
        var current = profile.BehaviorDefaults?.Clone() ?? LoadSavedDefaults();
        var updated = current.Clone();
        CopyBehaviorOverride(CreateCurrentDefaults(), updated, overrides);
        if (JsonSerializer.Serialize(current) != JsonSerializer.Serialize(updated)) profile.BehaviorDefaults = updated;
        profileDefaultsEditingDirty = false;
    }

    private void MarkProfileDefaultsEdited()
    {
        if (suppressProfileDefaultTracking || editingProfileDefaultsId != ActiveProfile()?.Id) return;
        // Keep the profile model current as the editor changes, so the save cue reflects real differences only.
        profileDefaultsEditingDirty = true;
        CaptureProfileDefaults();
        MarkProfilesDirty();
    }

    private bool IsEditingAdvancedAction() => selectedAdvancedActionIds.Count == 1
        && selectedAdvancedActionIds.Contains(automationProfiles.ActiveActionId);

    private IReadOnlyList<AutomationAction> SelectedAdvancedActions()
    {
        var profile = ActiveProfile();
        return profile?.Actions.Where(action => selectedAdvancedActionIds.Contains(action.Id)).ToList() ?? [];
    }

    // Shared defaults deliberately leave the registered hotkey alone: they configure behavior, not an assignment.
    private void ShowAdvancedSharedDefaults(bool clearSelection = true, bool announce = false)
    {
        if (!advancedMode) return;
        CaptureProfileDefaults();
        editingProfileDefaultsId = null;
        profileDefaultsEditingDirty = false;
        if (clearSelection) selectedAdvancedActionIds.Clear();
        var active = ActiveProfileAction();
        ApplyDefaults(LoadSavedDefaults());
        if (active is not null)
        {
            hotkey = active.Settings.Hotkey;
            hotkeyModifiers = active.Settings.HotkeyModifiers;
            UpdateHotkeyLabel();
        }
        UpdateSharedBehaviorDefaultsUi();
        RefreshAdvancedFooterUi();
        if (announce) Status("Ready — editing shared defaults.", ThemeManager.Brush("SuccessBrush"));
    }

    private AppDefaults ResolveActionSettings(AutomationAction action)
    {
        return AutomationBehaviorSettingsResolver.Resolve(LoadSavedDefaults(), ActiveProfile(), action);
    }

    private static AppDefaults LoadSavedDefaults()
    {
        try
        {
            return File.Exists(GlobalDefaultsPath)
                ? JsonSerializer.Deserialize<AppDefaults>(File.ReadAllText(GlobalDefaultsPath)) ?? new AppDefaults()
                : new AppDefaults();
        }
        catch { return new AppDefaults(); }
    }

    private static RgbSettings CloneLighting(RgbSettings source) => source.Clone();
    private RgbSettings ResolveProfileLighting(AutomationProfile? profile) => profile?.LightingDefaults ?? rgbSettings;
    private RgbSettings ResolveLighting(AutomationAction action) => AutomationLightingSettingsResolver.Resolve(rgbSettings, ActiveProfile(), action);

    private void UseSharedBehaviorDefaultsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AutomationAction action || sender is not MenuItem item) return;
        ApplySharedBehaviorDefaults([action], item.IsChecked);
    }

    private void UseSharedLightingSettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AutomationAction action || sender is not MenuItem item) return;
        ApplySharedLightingSettings([action], item.IsChecked);
    }

    private void ConfigureLightingOverride_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AutomationAction action) return;
        ConfigureLightingOverride([action]);
    }

    private void ApplySharedBehaviorDefaults(IEnumerable<AutomationAction> actions, bool useSharedDefaults)
    {
        var targets = actions.DistinctBy(action => action.Id).ToList();
        if (targets.Count == 0) return;

        if (useSharedDefaults)
        {
            var overridden = targets.Aggregate(AutomationBehaviorOverride.None, (current, action) => current | action.ActiveBehaviorOverrides);
            var dialog = new SharedBehaviorDefaultsWindow(overridden, targets.Count) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            var reverted = dialog.RevertAll ? AutomationBehaviorOverride.All : dialog.SelectedOverrides;
            if (reverted == AutomationBehaviorOverride.None) return;
            foreach (var action in targets)
            {
                var existingOverrides = action.ActiveBehaviorOverrides;
                action.UsesSharedBehaviorDefaults = true;
                action.BehaviorOverrides = existingOverrides & ~reverted;
            }
            RefreshAdvancedEditorAfterActionChange();
            MarkProfilesDirty();
            var detail = reverted == AutomationBehaviorOverride.All ? "all behavior settings" : DescribeBehaviorOverrides(reverted);
            Status($"Shared defaults restored for {detail}.", ThemeManager.Brush("SuccessBrush"));
            return;
        }

        foreach (var action in targets)
        {
            // Preserve the effective values when turning every aspect into a local override.
            action.Settings = ResolveActionSettings(action);
            action.UsesSharedBehaviorDefaults = false;
            action.BehaviorOverrides = AutomationBehaviorOverride.None;
        }
        RefreshAdvancedEditorAfterActionChange();
        MarkProfilesDirty();
        Status(targets.Count == 1 ? "This hotkey now has its own behavior settings." : $"{targets.Count} hotkeys now have their own behavior settings.", ThemeManager.Brush("SuccessBrush"));
    }

    private void SharedBehaviorSurface_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag } || !Enum.TryParse<AutomationBehaviorOverride>(tag, out var aspect)) return;
        if (IsClicking || !advancedMode) return;
        if (aspect == AutomationBehaviorOverride.Position && IsKeyboardInputSelected())
        {
            Status("Position settings apply to mouse actions only.", ThemeManager.Brush("TextMutedBrush"));
            e.Handled = true;
            return;
        }

        if (editingProfileDefaultsId == ActiveProfile()?.Id && ActiveProfile() is { } profile && profile.UsesSharedBehavior(aspect))
        {
            var local = profile.BehaviorDefaults?.Clone() ?? LoadSavedDefaults();
            CopyBehaviorOverride(CreateCurrentDefaults(), local, aspect);
            profile.BehaviorDefaults = local;
            profile.UsesSharedBehaviorDefaults = true;
            profile.BehaviorOverrides |= aspect;
            RefreshAdvancedFooterUi();
            UpdateSharedBehaviorDefaultsUi();
            MarkProfilesDirty();
            Status($"This profile now uses its own {DescribeBehaviorOverrides(aspect)} settings.", ThemeManager.Brush("SuccessBrush"));
            e.Handled = true;
            return;
        }

        if (!IsEditingAdvancedAction() || ActiveProfileAction() is not { } action || !action.UsesSharedBehavior(aspect)) return;

        CopyBehaviorOverride(CreateCurrentDefaults(), action.Settings, aspect);
        action.UsesSharedBehaviorDefaults = true;
        action.BehaviorOverrides |= aspect;
        RefreshAdvancedEditorAfterActionChange();
        MarkProfilesDirty();
        Status($"This hotkey now uses its own {DescribeBehaviorOverrides(aspect)} settings.", ThemeManager.Brush("SuccessBrush"));
        e.Handled = true;
    }

    private void SharedBehaviorSurface_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) => SetSharedBehaviorOverlay(sender as FrameworkElement, visible: true);
    // Shared-section prompts stay visible while that aspect is inherited. This avoids a disabled child
    // control swallowing mouse transitions and leaving the user with no discoverable override affordance.
    private void SharedBehaviorSurface_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) { }

    private void SetSharedBehaviorOverlay(FrameworkElement? surface, bool visible)
    {
        if (surface?.Tag is not string tag || !Enum.TryParse<AutomationBehaviorOverride>(tag, out var aspect)) return;
        var profile = ActiveProfile();
        var editingProfileDefaults = editingProfileDefaultsId == profile?.Id;
        var action = ActiveProfileAction();
        var shared = editingProfileDefaults
            ? profile?.UsesSharedBehavior(aspect) == true
            : IsEditingAdvancedAction() && action?.UsesSharedBehavior(aspect) == true;
        if (!visible || IsClicking || !advancedMode || !shared)
        {
            SharedBehaviorOverlay(aspect).Visibility = Visibility.Collapsed;
            return;
        }
        SharedBehaviorOverlay(aspect).Visibility = Visibility.Visible;
    }

    private Border SharedBehaviorOverlay(AutomationBehaviorOverride aspect) => aspect switch
    {
        AutomationBehaviorOverride.Repeat => RepeatSharedOverlay,
        AutomationBehaviorOverride.Position => PositionSharedOverlay,
        AutomationBehaviorOverride.TargetWindow => TargetWindowSharedOverlay,
        AutomationBehaviorOverride.InputJitter => InputJitterSharedOverlay,
        AutomationBehaviorOverride.InputPulse => InputPulseSharedOverlay,
        _ => throw new ArgumentOutOfRangeException(nameof(aspect))
    };

    private static void CopyBehaviorOverride(AppDefaults source, AppDefaults destination, AutomationBehaviorOverride aspect)
    {
        if (aspect.HasFlag(AutomationBehaviorOverride.Interval))
        {
            destination.Hours = source.Hours;
            destination.Minutes = source.Minutes;
            destination.Seconds = source.Seconds;
            destination.Milliseconds = source.Milliseconds;
        }
        if (aspect.HasFlag(AutomationBehaviorOverride.Repeat))
        {
            destination.RepeatUntilStopped = source.RepeatUntilStopped;
            destination.RepeatCount = source.RepeatCount;
        }
        if (aspect.HasFlag(AutomationBehaviorOverride.Position))
        {
            destination.FixedPosition = source.FixedPosition;
            destination.X = source.X;
            destination.Y = source.Y;
        }
        if (aspect.HasFlag(AutomationBehaviorOverride.TargetWindow))
        {
            destination.TargetExecutable = source.TargetExecutable;
            destination.TargetWindowTitle = source.TargetWindowTitle;
            destination.TargetWindowEnabled = source.TargetWindowEnabled;
        }
        if (aspect.HasFlag(AutomationBehaviorOverride.InputJitter)) destination.InputJitterMaximumMilliseconds = source.InputJitterMaximumMilliseconds;
        if (aspect.HasFlag(AutomationBehaviorOverride.InputPulse)) destination.InputPulseMilliseconds = source.InputPulseMilliseconds;
    }

    private static string DescribeBehaviorOverrides(AutomationBehaviorOverride aspects)
    {
        var labels = new List<string>();
        if (aspects.HasFlag(AutomationBehaviorOverride.Repeat)) labels.Add("repeat");
        if (aspects.HasFlag(AutomationBehaviorOverride.Interval)) labels.Add("interval");
        if (aspects.HasFlag(AutomationBehaviorOverride.Position)) labels.Add("position");
        if (aspects.HasFlag(AutomationBehaviorOverride.TargetWindow)) labels.Add("target window");
        if (aspects.HasFlag(AutomationBehaviorOverride.InputJitter)) labels.Add("input jitter");
        if (aspects.HasFlag(AutomationBehaviorOverride.InputPulse)) labels.Add("input pulse");
        return labels.Count switch { 0 => "settings", 1 => labels[0], 2 => $"{labels[0]} and {labels[1]}", _ => string.Join(", ", labels[..^1]) + $", and {labels[^1]}" };
    }

    private void ApplySharedLightingSettings(IEnumerable<AutomationAction> actions, bool useSharedSettings)
    {
        var targets = actions.DistinctBy(action => action.Id).ToList();
        if (targets.Count == 0) return;
        if (useSharedSettings && targets.Count > 1)
        {
            var confirmation = new ConfirmationWindow(
                "Use shared lighting settings?",
                $"Use the shared lighting settings for {targets.Count} selected hotkeys? Hotkeys already using the shared settings are unchanged.",
                "Use shared settings") { Owner = this };
            if (confirmation.ShowDialog() != true) return;
        }
        foreach (var action in targets)
        {
            action.UsesSharedLightingSettings = useSharedSettings;
            if (useSharedSettings) action.LightingOverride = null;
            else action.LightingOverride ??= CloneLighting(ResolveProfileLighting(ActiveProfile()));
        }
        MarkProfilesDirty();
        RefreshAdvancedEditorAfterActionChange();
        var message = useSharedSettings
            ? targets.Count == 1 ? "Inherited lighting settings enabled for this hotkey." : $"Inherited lighting settings enabled for {targets.Count} hotkeys."
            : targets.Count == 1 ? "This hotkey now has its own lighting settings." : $"{targets.Count} hotkeys now have their own lighting settings.";
        Status(message, ThemeManager.Brush("SuccessBrush"));
    }

    private void ConfigureLightingOverride(IEnumerable<AutomationAction> actions)
    {
        var targets = actions.DistinctBy(action => action.Id).ToList();
        if (targets.Count == 0) return;
        var label = targets.Count == 1
            ? FormatHotkey(targets[0].Settings.Hotkey, targets[0].Settings.HotkeyModifiers, targets[0].Settings.HotkeyTrigger)
            : $"{targets.Count} selected hotkeys";
        var dialog = new HotkeyLightingWindow(targets[0].LightingOverride ?? ResolveLighting(targets[0]), label) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        foreach (var action in targets)
        {
            action.UsesSharedLightingSettings = false;
            action.LightingOverride = CloneLighting(dialog.Settings);
        }
        MarkProfilesDirty();
        RefreshAdvancedEditorAfterActionChange();
        Status(targets.Count == 1 ? "Lighting override saved for this hotkey." : "Lighting override saved for the selected hotkeys.", ThemeManager.Brush("SuccessBrush"));
    }

    private void RefreshAdvancedEditorAfterActionChange()
    {
        if (IsEditingAdvancedAction() && ActiveProfileAction() is { } action) ApplyDefaults(ResolveActionSettings(action));
        else ShowAdvancedSharedDefaults(clearSelection: false);
    }

    private void NewProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (clickCancellation is not null || profileRuns.Count > 0) { Status("Stop active hotkeys before creating a profile.", ThemeManager.Brush("WarningBrush")); return; }
        var replacingDraft = ActiveProfile()?.Id == unsavedProfileId;
        if (replacingDraft) DiscardActiveDraft();
        else if (!ResolveUnsavedProfileChanges("creating a new profile")) return;
        var profile = new AutomationProfile { Name = "Unsaved" };
        automationProfiles.Profiles.Add(profile);
        automationProfiles.ActiveProfileId = profile.Id;
        automationProfiles.ActiveActionId = string.Empty;
        unsavedProfileId = profile.Id;
        TouchRecentProfile(profile.Id);
        selectedAdvancedActionIds.Clear();
        ShowAdvancedSharedDefaults(clearSelection: false);
        MarkProfilesDirty();
        RegisterConfiguredHotkey();
        Status("New profile — add a hotkey when you are ready.", ThemeManager.Brush("SuccessBrush"));
    }

    private void DiscardActiveDraft()
    {
        if (ActiveProfile()?.Id != unsavedProfileId || unsavedProfileId is null) return;
        automationProfiles.Profiles.RemoveAll(profile => profile.Id == unsavedProfileId);
        automationProfiles.RecentProfileIds.Remove(unsavedProfileId);
        unsavedProfileId = null;
        profilesDirty = false;
    }

    private void SaveProfileButton_Click(object sender, RoutedEventArgs e)
    {
        SaveActiveProfile();
    }

    private bool SaveActiveProfile(string? savedProfileName = null)
    {
        CaptureCurrentActionToProfile();
        var profile = ActiveProfile();
        if (profile is null) return false;
        if (profile.Id == unsavedProfileId)
        {
            var name = savedProfileName;
            if (name is null)
            {
                var dialog = new ProfileNameWindow("Save profile", "Give this profile a name before saving it.", "New profile") { Owner = this };
                if (dialog.ShowDialog() != true) return false;
                name = dialog.ProfileName;
            }
            if (string.IsNullOrWhiteSpace(name)) return false;
            profile.Name = UniqueProfileName(name, profile.Id);
            unsavedProfileId = null;
        }
        PersistAutomationProfiles();
        profilesDirty = false;
        RefreshAdvancedFooterUi();
        Status($"{profile.Name} saved.", ThemeManager.Brush("SuccessBrush"));
        return true;
    }

    private bool ResolveUnsavedProfileChanges(string nextStep)
    {
        if (ActiveProfile()?.Id == unsavedProfileId && ActiveProfile() is { } draft && !AutomationProfileDraftRules.HasContent(draft))
        {
            DiscardActiveDraft();
            return true;
        }
        CaptureCurrentActionToProfile();
        if (!profilesDirty) return true;
        var currentDraft = ActiveProfile()?.Id == unsavedProfileId ? ActiveProfile() : null;
        var dialog = new UnsavedProfileChangesWindow(nextStep, currentDraft is not null, currentDraft?.Name) { Owner = this };
        if (dialog.ShowDialog() != true) return false;
        return dialog.Decision switch
        {
            ProfileChangeDecision.Save => SaveActiveProfile(dialog.SavedProfileName),
            ProfileChangeDecision.Discard => DiscardUnsavedProfileChanges(),
            _ => false
        };
    }

    private bool DiscardUnsavedProfileChanges()
    {
        if (ActiveProfile()?.Id == unsavedProfileId)
        {
            DiscardActiveDraft();
            return true;
        }

        // Reloading from the atomic store restores the current profile exactly as it was last saved.
        automationProfiles = AutomationProfileStore.Load(ProfilesPath, CreateCurrentDefaults());
        unsavedProfileId = null;
        savedProfileConfiguration = AutomationProfileConfiguration.Fingerprint(automationProfiles);
        profilesDirty = false;
        return true;
    }

    private void UpdateSharedBehaviorDefaultsUi()
    {
        if (RepeatCard is null || RepeatContent is null || PositionCard is null || PositionContent is null || TargetWindowCard is null || TargetWindowContent is null) return;
        var locked = IsClicking;
        var editingProfileDefaults = advancedMode && editingProfileDefaultsId == ActiveProfile()?.Id;
        var editingSharedDefaults = advancedMode && !IsEditingAdvancedAction();
        var profile = ActiveProfile();
        var action = advancedMode && IsEditingAdvancedAction() ? ActiveProfileAction() : null;
        var sharedRepeat = editingProfileDefaults ? profile?.UsesSharedBehavior(AutomationBehaviorOverride.Repeat) == true : action?.UsesSharedBehavior(AutomationBehaviorOverride.Repeat) == true;
        var sharedPosition = editingProfileDefaults ? profile?.UsesSharedBehavior(AutomationBehaviorOverride.Position) == true : action?.UsesSharedBehavior(AutomationBehaviorOverride.Position) == true;
        var sharedTarget = editingProfileDefaults ? profile?.UsesSharedBehavior(AutomationBehaviorOverride.TargetWindow) == true : action?.UsesSharedBehavior(AutomationBehaviorOverride.TargetWindow) == true;
        var sharedJitter = editingProfileDefaults ? profile?.UsesSharedBehavior(AutomationBehaviorOverride.InputJitter) == true : action?.UsesSharedBehavior(AutomationBehaviorOverride.InputJitter) == true;
        var sharedPulse = editingProfileDefaults ? profile?.UsesSharedBehavior(AutomationBehaviorOverride.InputPulse) == true : action?.UsesSharedBehavior(AutomationBehaviorOverride.InputPulse) == true;
        var holdingHotkey = action is not null && InputRules.IsHoldAction(Selected(TypeCombo));
        var positionAvailable = editingSharedDefaults || (!IsKeyboardInputSelected() && Selected(ButtonCombo) != "Sequence");
        IntervalCard.IsEnabled = !locked && !holdingHotkey;
        ActionCard.IsEnabled = !locked;
        ButtonCombo.IsEnabled = !locked && !editingSharedDefaults;
        TypeCombo.IsEnabled = !locked && !editingSharedDefaults;
        RepeatCard.IsEnabled = !locked;
        PositionCard.IsEnabled = !locked && positionAvailable;
        TargetWindowCard.IsEnabled = !locked;
        RepeatContent.IsEnabled = !locked && !sharedRepeat;
        PositionContent.IsEnabled = !locked && positionAvailable && !sharedPosition;
        TargetWindowContent.IsEnabled = !locked && !sharedTarget;
        InputJitterButton.IsEnabled = !locked && !sharedJitter;
        InputPulseButton.IsEnabled = !locked && !sharedPulse;
        var overrideScope = editingProfileDefaults ? "profile" : "hotkey";
        UpdateSharedBehaviorSurface(RepeatCard, sharedRepeat, "repeat", overrideScope);
        UpdateSharedBehaviorSurface(PositionCard, sharedPosition, "position", overrideScope);
        UpdateSharedBehaviorSurface(TargetWindowCard, sharedTarget, "target window", overrideScope);
        RepeatSharedOverlayLabel.Text = $"Click to override repeat for this {overrideScope}";
        PositionSharedOverlayLabel.Text = $"Click to override position for this {overrideScope}";
        TargetWindowSharedOverlayLabel.Text = $"Click to override target window for this {overrideScope}";
        UpdateSharedBehaviorSurface(InputJitterOverrideHost, sharedJitter, "input jitter", overrideScope);
        UpdateSharedBehaviorSurface(InputPulseOverrideHost, sharedPulse, "input pulse", overrideScope);
        RepeatSharedOverlay.Visibility = sharedRepeat ? Visibility.Visible : Visibility.Collapsed;
        PositionSharedOverlay.Visibility = sharedPosition ? Visibility.Visible : Visibility.Collapsed;
        TargetWindowSharedOverlay.Visibility = sharedTarget ? Visibility.Visible : Visibility.Collapsed;
        InputJitterSharedOverlay.Visibility = sharedJitter ? Visibility.Visible : Visibility.Collapsed;
        InputPulseSharedOverlay.Visibility = sharedPulse ? Visibility.Visible : Visibility.Collapsed;
        HotkeyButton.IsEnabled = !locked;
        ModeButton.IsEnabled = !locked;
        SettingsButton.IsEnabled = !locked;
        AdvancedHelpButton.IsEnabled = !locked;
        SetDefaultButton.IsEnabled = !locked;

        var profileManagementLocked = advancedMode && profileRuns.Count > 0;
        RecentProfilesList.IsEnabled = !profileManagementLocked;
        NewProfileButton.IsEnabled = !profileManagementLocked;
        AdvancedSaveProfileButton.IsEnabled = !profileManagementLocked;
        ManageProfilesButton.IsEnabled = !profileManagementLocked;
        AddAdvancedActionButton.IsEnabled = !profileManagementLocked && (ActiveProfile()?.Actions.Count ?? 0) < AutomationProfileLimits.MaximumHotkeys;
        UpdateActionEditorHint();
    }

    private static void UpdateSharedBehaviorSurface(FrameworkElement surface, bool shared, string label, string scope)
    {
        surface.ToolTip = shared ? $"Uses the shared {label} default. Click to override {label} for this {scope}." : null;
        surface.Cursor = shared ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow;
    }

    private void UpdateActionEditorHint()
    {
        if (ActionEditorHint is null) return;
        if (advancedMode && editingProfileDefaultsId == ActiveProfile()?.Id)
        {
            ActionEditorHint.Text = "Editing profile behavior defaults";
            ActionEditorHint.ToolTip = "These defaults apply to hotkeys in this profile unless that hotkey overrides an aspect.";
        }
        else if (advancedMode && IsEditingAdvancedAction())
        {
            ActionEditorHint.Text = $"Editing {FormatHotkey()} action";
            ActionEditorHint.ToolTip = "Changes in this card apply to the selected hotkey.";
        }
        else if (advancedMode)
        {
            ActionEditorHint.Text = "Editing global behavior defaults";
            ActionEditorHint.ToolTip = "These defaults apply when a profile and hotkey both inherit an aspect.";
        }
        else
        {
            ActionEditorHint.Text = "Mouse or keyboard input";
            ActionEditorHint.ToolTip = "Select a hotkey tile to edit its action.";
        }
    }

    private void ManageProfilesButton_Click(object sender, RoutedEventArgs e)
    {
        if (clickCancellation is not null || profileRuns.Count > 0)
        {
            Status("Stop all active hotkeys before changing profiles.", ThemeManager.Brush("WarningBrush"));
            return;
        }
        var menu = new ContextMenu { PlacementTarget = ManageProfilesButton };
        var import = new MenuItem { Header = "Import profile…" };
        import.Click += ImportProfile_Click;
        menu.Items.Add(import);
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Right-click a profile to export it", IsEnabled = false });
        menu.IsOpen = true;
    }

    private void ExportProfile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AutomationProfile profile) return;
        if (profile.Id == ActiveProfile()?.Id) CaptureCurrentActionToProfile();
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export AutoClicker profile",
            Filter = "AutoClicker profile (*.autoclicker-profile.json)|*.autoclicker-profile.json",
            FileName = SafeProfileFileName(profile.Name) + ".autoclicker-profile.json",
            DefaultExt = ".autoclicker-profile.json",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            ProfileTransferStore.Save(dialog.FileName, profile);
            Status($"{profile.Name} exported.", ThemeManager.Brush("SuccessBrush"));
        }
        catch (Exception exception)
        {
            AppLog.Error($"Could not export profile '{profile.Name}'", exception);
            Status("Could not export that profile. See the log for details.", ThemeManager.Brush("ErrorBrush"));
        }
    }

    private void ImportProfile_Click(object? sender, RoutedEventArgs e)
    {
        if (profilesDirty)
        {
            Status("Save or discard current profile changes before importing a profile.", ThemeManager.Brush("WarningBrush"));
            return;
        }
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import AutoClicker profile",
            Filter = "AutoClicker profiles (*.autoclicker-profile.json)|*.autoclicker-profile.json|Other JSON files (*.json)|*.json",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var profile = ProfileTransferStore.Load(dialog.FileName);
            profile.Name = UniqueProfileName(profile.Name);
            automationProfiles.Profiles.Add(profile);
            automationProfiles.ActiveProfileId = profile.Id;
            automationProfiles.ActiveActionId = profile.Actions.FirstOrDefault()?.Id ?? string.Empty;
            TouchRecentProfile(profile.Id);
            selectedAdvancedActionIds.Clear();
            if (profile.Actions.FirstOrDefault() is { } action) ApplyDefaults(ResolveActionSettings(action));
            else ShowAdvancedSharedDefaults(clearSelection: false);
            PersistAutomationProfiles();
            RegisterConfiguredHotkey();
            RefreshAdvancedFooterUi();
            Status($"{profile.Name} imported.", ThemeManager.Brush("SuccessBrush"));
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not import profile", exception);
            Status("Could not import that profile. See the log for details.", ThemeManager.Brush("ErrorBrush"));
        }
    }

    private string UniqueProfileName(string preferredName, string? excludedProfileId = null) =>
        AutomationProfileNameRules.MakeUnique(preferredName, automationProfiles.Profiles, excludedProfileId);

    private static string SafeProfileFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "AutoClicker-profile" : safe;
    }

    private Button ActiveHotkeyButton() => HotkeyButton;

    private void ModeButton_Click(object sender, RoutedEventArgs e) => SetAdvancedMode(!advancedMode);

    private void SetAdvancedMode(bool enabled)
    {
        if (advancedMode == enabled) return;
        if (clickCancellation is not null || profileRuns.Count > 0)
        {
            Status("Stop all active hotkeys before changing modes.", ThemeManager.Brush("WarningBrush"));
            return;
        }
        if (hotkeyRegistered) { UnregisterHotKey(hwnd, HotkeyId); hotkeyRegistered = false; }
        if (!enabled)
        {
            CaptureCurrentActionToProfile();
            LoadDefaults();
        }
        advancedMode = enabled;
        ApplyModeUi();
        RegisterConfiguredHotkey();
        SaveUiPreferences();
        Status(enabled ? "Advanced mode enabled — use the footer to manage hotkeys." : "Simple mode enabled.", ThemeManager.Brush("SuccessBrush"));
    }

    private void ApplyModeUi()
    {
        if (SimpleHotkeyHeader is null || AdvancedFooter is null || SimpleFooter is null || StatusLabel is null || ModeButton is null) return;
        SimpleHotkeyHeader.Visibility = advancedMode ? Visibility.Collapsed : Visibility.Visible;
        AdvancedHelpButton.Visibility = advancedMode ? Visibility.Visible : Visibility.Collapsed;
        SimpleFooter.Visibility = advancedMode ? Visibility.Collapsed : Visibility.Visible;
        StatusLabel.Visibility = advancedMode ? Visibility.Collapsed : Visibility.Visible;
        AdvancedFooter.Visibility = advancedMode ? Visibility.Visible : Visibility.Collapsed;
        FooterRow.Height = advancedMode ? new GridLength(102) : new GridLength(82);
        if (!compactMode) Height = advancedMode ? AdvancedExpandedWindowHeight : ExpandedWindowHeight;
        ModeButton.Content = advancedMode ? "Advanced" : "S";
        ModeButton.ToolTip = advancedMode ? "Switch to Simple mode" : "Switch to Advanced profiles";
        if (advancedMode && selectedAdvancedActionIds.Count == 0) ShowAdvancedSharedDefaults(clearSelection: false);
        else UpdateSharedBehaviorDefaultsUi();
        if (advancedMode) RefreshAdvancedFooterUi();
    }

    private void RefreshAdvancedFooterUi()
    {
        if (!advancedMode || RecentProfilesList is null || AdvancedActionsFooterList is null) return;
        var profile = ActiveProfile() ?? automationProfiles.Profiles.FirstOrDefault();
        FooterRow.Height = new GridLength(102);
        if (compactMode) ApplyCompactMode();
        RecentProfilesList.ItemsSource = automationProfiles.RecentProfileIds
            .Select(id => automationProfiles.Profiles.FirstOrDefault(item => item.Id == id))
            .Where(item => item is not null).Cast<AutomationProfile>().Take(3)
            .Select(item => new AdvancedProfileTile(item, item.Id == automationProfiles.ActiveProfileId, item.Id == automationProfiles.ActiveProfileId && profilesDirty)).ToList();
        // Multiple selections still edit shared defaults, but every selected tile remains visibly highlighted.
        var multiSelection = selectedAdvancedActionIds.Count > 1;
        var showInlineActionControls = (profile?.Actions.Count ?? 0) < AutomationProfileLimits.HideInlineActionControlsAt;
        AdvancedActionsFooterList.ItemsSource = profile?.Actions.Select(action => new AdvancedActionTile(action, profileRuns.ContainsKey(action.Id), action.Id == pendingRemovalActionId, selectedAdvancedActionIds.Contains(action.Id), profileRuns.Count > 0, multiSelection, showInlineActionControls, hotkeyCapturePending: action.Id == pendingNewActionId)).ToList();
        if (EmptyAdvancedActionsLabel is not null)
            EmptyAdvancedActionsLabel.Visibility = profile?.Actions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (AdvancedSaveProfileButton is not null) AdvancedSaveProfileButton.Visibility = profilesDirty || ActiveProfile()?.Id == unsavedProfileId ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TouchRecentProfile(string profileId)
    {
        automationProfiles.RecentProfileIds.Remove(profileId);
        automationProfiles.RecentProfileIds.Insert(0, profileId);
        automationProfiles.RecentProfileIds = automationProfiles.RecentProfileIds
            .Where(id => automationProfiles.Profiles.Any(profile => profile.Id == id)).Distinct().ToList();
    }

    private void SelectAdvancedProfile(AutomationProfile profile, bool editProfileDefaults = false)
    {
        var targetProfileId = profile.Id;
        if (targetProfileId == automationProfiles.ActiveProfileId)
        {
            if (editProfileDefaults) BeginProfileDefaultsEdit(profile);
            return;
        }
        if (!ResolveUnsavedProfileChanges($"switching to {profile.Name}")) return;
        var resolvedProfile = automationProfiles.Profiles.FirstOrDefault(item => item.Id == targetProfileId);
        if (resolvedProfile is null) return;
        profile = resolvedProfile;
        automationProfiles.ActiveProfileId = profile.Id;
        var action = profile.Actions.FirstOrDefault();
        automationProfiles.ActiveActionId = action?.Id ?? string.Empty;
        TouchRecentProfile(profile.Id);
        PersistProfileNavigation();
        selectedAdvancedActionIds.Clear();
        if (editProfileDefaults) BeginProfileDefaultsEdit(profile);
        else ShowAdvancedSharedDefaults(clearSelection: false);
        if (hotkeyRegistered) { UnregisterHotKey(hwnd, HotkeyId); hotkeyRegistered = false; }
        RegisterConfiguredHotkey();
        RefreshAdvancedFooterUi();
        if (!editProfileDefaults) Status($"{profile.Name} selected — shared defaults are ready.", ThemeManager.Brush("SuccessBrush"));
    }

    private void SelectAdvancedAction(AutomationAction action, bool startHotkeyCapture = false)
    {
        if (editingProfileDefaultsId == ActiveProfile()?.Id)
        {
            CaptureProfileDefaults();
            editingProfileDefaultsId = null;
            profileDefaultsEditingDirty = false;
        }
        if (action.Id != automationProfiles.ActiveActionId || !IsEditingAdvancedAction())
        {
            CaptureCurrentActionToProfile();
            automationProfiles.ActiveActionId = action.Id;
            ApplyDefaults(ResolveActionSettings(action));
            if (hotkeyRegistered) { UnregisterHotKey(hwnd, HotkeyId); hotkeyRegistered = false; }
            RegisterConfiguredHotkey();
        }
        selectedAdvancedActionIds.Clear();
        selectedAdvancedActionIds.Add(action.Id);
        RefreshAdvancedFooterUi();
        UpdateSharedBehaviorDefaultsUi();
        UpdateActionEditorHint();
        Status($"Editing {action.DisplayName}.", ThemeManager.Brush("SuccessBrush"));
        if (startHotkeyCapture) BeginHotkeyCapture();
    }

    private void RecentProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is AutomationProfile profile) SelectAdvancedProfile(profile, editProfileDefaults: true);
    }

    private void ConfigureProfileDefaults_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AutomationProfile profile || profileRuns.Count > 0) return;
        if (profile.Id != automationProfiles.ActiveProfileId)
        {
            SelectAdvancedProfile(profile, editProfileDefaults: true);
            return;
        }
        BeginProfileDefaultsEdit(profile);
    }

    private void BeginProfileDefaultsEdit(AutomationProfile profile)
    {
        CaptureCurrentActionToProfile();
        selectedAdvancedActionIds.Clear();
        editingProfileDefaultsId = profile.Id;
        profileDefaultsEditingDirty = false;
        suppressProfileDefaultTracking = true;
        try { ApplyDefaults(AutomationBehaviorSettingsResolver.ResolveProfileDefaults(LoadSavedDefaults(), profile)); }
        finally { suppressProfileDefaultTracking = false; }
        RefreshAdvancedFooterUi();
        UpdateSharedBehaviorDefaultsUi();
        Status($"Editing {profile.Name} profile defaults — save the profile when ready.", ThemeManager.Brush("SuccessBrush"));
    }

    private void UseSharedProfileBehaviorDefaults_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AutomationProfile profile || profileRuns.Count > 0) return;
        if (profile.Id != ActiveProfile()?.Id)
        {
            SelectAdvancedProfile(profile, editProfileDefaults: true);
            if (profile.Id != ActiveProfile()?.Id) return;
            profile = ActiveProfile()!;
        }
        var overridden = profile.ActiveBehaviorOverrides;
        if (overridden == AutomationBehaviorOverride.None)
        {
            Status("This profile already uses the global Advanced defaults.", ThemeManager.Brush("TextMutedBrush"));
            return;
        }
        var dialog = new SharedBehaviorDefaultsWindow(overridden, scopeLabel: "this profile") { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var reverted = dialog.RevertAll ? AutomationBehaviorOverride.All : dialog.SelectedOverrides;
        if (reverted == AutomationBehaviorOverride.None) return;
        RevertProfileBehaviorToAppDefaults(profile, reverted);
    }

    private void UseAppDefaultsForProfile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AutomationProfile profile || profileRuns.Count > 0) return;
        if (profile.Id != ActiveProfile()?.Id)
        {
            SelectAdvancedProfile(profile, editProfileDefaults: true);
            if (profile.Id != ActiveProfile()?.Id) return;
            profile = ActiveProfile()!;
        }
        if (profile.ActiveBehaviorOverrides == AutomationBehaviorOverride.None)
        {
            Status("This profile already uses the app defaults.", ThemeManager.Brush("TextMutedBrush"));
            return;
        }
        RevertProfileBehaviorToAppDefaults(profile, AutomationBehaviorOverride.All);
    }

    private void RevertProfileBehaviorToAppDefaults(AutomationProfile profile, AutomationBehaviorOverride reverted)
    {
        var existingOverrides = profile.ActiveBehaviorOverrides;
        profile.UsesSharedBehaviorDefaults = true;
        profile.BehaviorOverrides = existingOverrides & ~reverted;
        if (profile.BehaviorOverrides == AutomationBehaviorOverride.None) profile.BehaviorDefaults = null;
        BeginProfileDefaultsEdit(profile);
        MarkProfilesDirty();
        var detail = reverted == AutomationBehaviorOverride.All ? "all behavior settings" : DescribeBehaviorOverrides(reverted);
        Status($"Profile now uses app defaults for {detail}.", ThemeManager.Brush("SuccessBrush"));
    }

    private void ConfigureProfileLighting_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AutomationProfile profile || profileRuns.Count > 0) return;
        if (profile.Id != ActiveProfile()?.Id)
        {
            SelectAdvancedProfile(profile, editProfileDefaults: true);
            if (profile.Id != ActiveProfile()?.Id) return;
            profile = ActiveProfile()!;
        }
        var dialog = new HotkeyLightingWindow(profile.LightingDefaults ?? rgbSettings, profile.Name) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        profile.LightingDefaults = CloneLighting(dialog.Settings);
        MarkProfilesDirty();
        Status($"Profile lighting saved for {profile.Name}.", ThemeManager.Brush("SuccessBrush"));
    }

    private void UseAppLightingDefaultsForProfile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AutomationProfile profile || profileRuns.Count > 0) return;
        if (profile.Id != ActiveProfile()?.Id)
        {
            SelectAdvancedProfile(profile, editProfileDefaults: true);
            if (profile.Id != ActiveProfile()?.Id) return;
            profile = ActiveProfile()!;
        }
        if (profile.LightingDefaults is null)
        {
            Status("This profile already uses the app lighting defaults.", ThemeManager.Brush("TextMutedBrush"));
            return;
        }
        profile.LightingDefaults = null;
        MarkProfilesDirty();
        Status($"Profile lighting reset to app defaults for {profile.Name}.", ThemeManager.Brush("SuccessBrush"));
    }

    private void RenameProfile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AutomationProfile profile) return;
        var dialog = new ProfileNameWindow("Rename profile", "Choose a new name for this profile.", profile.Name) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        profile.Name = UniqueProfileName(dialog.ProfileName, profile.Id);
        MarkProfilesDirty();
    }

    // Behavior values are stored at the level currently being edited. Global Advanced values persist independently
    // from Simple-mode defaults; profile and hotkey edits continue to use their existing inheritance models.
    private void CommitBehaviorChange(AutomationBehaviorOverride aspect)
    {
        if (applyingDefaults) return;
        if (!advancedMode)
        {
            MarkProfileDefaultsEdited();
            return;
        }

        if (editingProfileDefaultsId == ActiveProfile()?.Id)
        {
            MarkProfileDefaultsEdited();
            return;
        }

        if (IsEditingAdvancedAction())
        {
            CaptureCurrentActionToProfile();
            return;
        }

        var defaults = LoadSavedDefaults();
        var updated = defaults.Clone();
        CopyBehaviorOverride(CreateCurrentDefaults(), updated, aspect);
        if (JsonSerializer.Serialize(defaults) == JsonSerializer.Serialize(updated)) return;
        if (!WriteDefaults(GlobalDefaultsPath, updated))
            Status("Could not save the global Advanced default.", ThemeManager.Brush("ErrorBrush"));
    }

    private void ProfileContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu || menu.PlacementTarget is not FrameworkElement { Tag: AutomationProfile profile }) return;
        var canDelete = profile.Id != ActiveProfile()?.Id;
        foreach (var item in menu.Items.OfType<FrameworkElement>())
        {
            if (item.Name is "DeleteProfileMenuItem" or "DeleteProfileSeparator")
                item.Visibility = canDelete ? Visibility.Visible : Visibility.Collapsed;
            if (item.Name == "DiscardProfileChangesMenuItem")
                item.Visibility = profile.Id == ActiveProfile()?.Id && profilesDirty ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void DiscardProfileChanges_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AutomationProfile profile || profile.Id != ActiveProfile()?.Id || !profilesDirty) return;
        if (!DiscardUnsavedProfileChanges()) return;
        ShowAdvancedSharedDefaults(clearSelection: false);
        RegisterConfiguredHotkey();
        RefreshAdvancedFooterUi();
        Status($"{profile.Name} restored to its last saved state.", ThemeManager.Brush("SuccessBrush"));
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AutomationProfile profile || profile.Id == ActiveProfile()?.Id) return;
        if (profilesDirty)
        {
            Status("Save or discard current profile changes before deleting another profile.", ThemeManager.Brush("WarningBrush"));
            return;
        }

        var confirmation = new ConfirmationWindow("Delete profile", $"Delete {profile.Name}? This cannot be undone.", "Delete", destructive: true) { Owner = this };
        if (confirmation.ShowDialog() != true) return;
        automationProfiles.Profiles.RemoveAll(item => item.Id == profile.Id);
        automationProfiles.RecentProfileIds.Remove(profile.Id);
        PersistAutomationProfiles();
        RefreshAdvancedFooterUi();
        Status($"{profile.Name} deleted.", ThemeManager.Brush("SuccessBrush"));
    }

    private void DuplicateProfile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AutomationProfile profile) return;
        var sourceProfileId = profile.Id;
        if (!ResolveUnsavedProfileChanges("duplicating a profile")) return;
        var sourceProfile = automationProfiles.Profiles.FirstOrDefault(item => item.Id == sourceProfileId);
        if (sourceProfile is null) return;
        profile = sourceProfile;
        var copy = profile.Clone();
        copy.Id = Guid.NewGuid().ToString("N");
        foreach (var action in copy.Actions) action.Id = Guid.NewGuid().ToString("N");
        copy.Name = "Unsaved";
        automationProfiles.Profiles.Add(copy);
        automationProfiles.ActiveProfileId = copy.Id;
        automationProfiles.ActiveActionId = copy.Actions.FirstOrDefault()?.Id ?? string.Empty;
        unsavedProfileId = copy.Id;
        TouchRecentProfile(copy.Id);
        if (copy.Actions.FirstOrDefault() is { } copiedAction) ApplyDefaults(ResolveActionSettings(copiedAction));
        else { selectedAdvancedActionIds.Clear(); ShowAdvancedSharedDefaults(clearSelection: false); }
        MarkProfilesDirty();
        RegisterConfiguredHotkey();
    }

    private void AdvancedActionEdit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AutomationAction action) return;
        BeginAdvancedActionEdit(action);
    }

    private void BeginAdvancedActionEdit(AutomationAction action)
    {
        if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0)
        {
            ToggleAdvancedActionSelection(action);
            return;
        }
        if (IsEditingAdvancedAction() && action.Id == automationProfiles.ActiveActionId)
        {
            return;
        }
        SelectAdvancedAction(action);
    }

    private void ToggleAdvancedActionSelection(AutomationAction action)
    {
        CaptureCurrentActionToProfile();
        if (!selectedAdvancedActionIds.Add(action.Id)) selectedAdvancedActionIds.Remove(action.Id);

        var selected = SelectedAdvancedActions();
        if (selected.Count == 1)
        {
            SelectAdvancedAction(selected[0]);
            return;
        }

        ShowAdvancedSharedDefaults(clearSelection: false);
        Status(selected.Count == 0 ? "Shared defaults selected." : $"{selected.Count} hotkeys selected — use their menu for shared options.", ThemeManager.Brush("SuccessBrush"));
    }

    private void AdvancedActionRemove_Click(object sender, RoutedEventArgs e)
    {
        if (profileRuns.Count > 0) return;
        if ((sender as FrameworkElement)?.Tag is not AutomationAction action) return;
        if (pendingRemovalActionId != action.Id)
        {
            pendingRemovalActionId = action.Id;
            RefreshAdvancedFooterUi();
            return;
        }
        pendingRemovalActionId = null;
        DeleteAdvancedAction(action);
    }

    private void CancelAdvancedActionRemove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AutomationAction action || pendingRemovalActionId != action.Id) return;
        pendingRemovalActionId = null;
        RefreshAdvancedFooterUi();
    }

    private void ChangeAdvancedActionHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (profileRuns.Count > 0) return;
        if ((sender as FrameworkElement)?.Tag is AutomationAction action) SelectAdvancedAction(action, startHotkeyCapture: true);
    }

    private void AdvancedActionHeader_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // The body button supplies its own event; this keeps the header and empty tile space equally useful.
        if (profileRuns.Count > 0) return;
        if (FindParent<Button>(e.OriginalSource as DependencyObject) is not null) return;
        if ((sender as FrameworkElement)?.DataContext is not AdvancedActionTile tile) return;
        OpenAdvancedActionContextMenu(sender as UIElement, tile.Action);
        e.Handled = true;
    }

    private void AdvancedActionBody_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (profileRuns.Count > 0) return;
        if ((sender as FrameworkElement)?.Tag is not AutomationAction action) return;
        OpenAdvancedActionContextMenu(sender as UIElement, action);
        e.Handled = true;
    }

    private void AdvancedActionTile_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (profileRuns.Count > 0 || sender is not Border { DataContext: AdvancedActionTile tile }) return;
        // Buttons retain their normal edit/start/stop behavior; the compact header is the drag handle.
        if (FindParent<Button>(e.OriginalSource as DependencyObject) is not null) return;
        pendingActionDrag = tile.Action;
        actionDragStart = e.GetPosition(this);
    }

    private void AdvancedActionTile_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (pendingActionDrag is null || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed || sender is not Border tile) return;
        var point = e.GetPosition(this);
        if (Math.Abs(point.X - actionDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(point.Y - actionDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var action = pendingActionDrag;
        pendingActionDrag = null;
        DragDrop.DoDragDrop(tile, new DataObject("AutoClicker.ActionId", action.Id), DragDropEffects.Move);
        ClearActionDragTarget();
    }

    private void AdvancedActionTile_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var action = pendingActionDrag;
        pendingActionDrag = null;
        // A click on the header is an edit selection; a drag clears this state before mouse-up.
        if (action is not null && sender is Border && FindParent<Button>(e.OriginalSource as DependencyObject) is null)
            BeginAdvancedActionEdit(action);
    }

    private void AdvancedActionTile_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("AutoClicker.ActionId") && sender is Border tile) SetActionDragTarget(tile);
    }

    private void AdvancedActionTile_DragLeave(object sender, DragEventArgs e)
    {
        if (ReferenceEquals(actionDragTarget, sender)) ClearActionDragTarget();
    }

    private void AdvancedActionTile_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("AutoClicker.ActionId")) return;
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void AdvancedActionTile_Drop(object sender, DragEventArgs e)
    {
        ClearActionDragTarget();
        if (profileRuns.Count > 0 || sender is not Border { DataContext: AdvancedActionTile target } || ActiveProfile() is not { } profile
            || e.Data.GetData("AutoClicker.ActionId") is not string actionId) return;
        var placeAfter = e.GetPosition((Border)sender).X > ((Border)sender).ActualWidth / 2;
        if (!AutomationProfileActionOrder.Move(profile, actionId, target.Action.Id, placeAfter)) return;
        MarkProfilesDirty();
        RefreshAdvancedFooterUi();
        Status("Hotkey order changed — save the profile when ready.", ThemeManager.Brush("SuccessBrush"));
        e.Handled = true;
    }

    private void SetActionDragTarget(Border target)
    {
        if (ReferenceEquals(actionDragTarget, target)) return;
        ClearActionDragTarget();
        actionDragTarget = target;
        target.BorderBrush = ThemeManager.Brush("AccentFocusBrush");
        target.BorderThickness = new Thickness(2);
    }

    private void ClearActionDragTarget()
    {
        if (actionDragTarget is null) return;
        actionDragTarget.ClearValue(Border.BorderBrushProperty);
        actionDragTarget.ClearValue(Border.BorderThicknessProperty);
        actionDragTarget = null;
    }

    private void OpenAdvancedActionContextMenu(UIElement? placementTarget, AutomationAction action)
    {
        var selected = SelectedAdvancedActions();
        IReadOnlyList<AutomationAction> targets = selected.Count > 1 && selected.Any(item => item.Id == action.Id) ? selected : [action];
        var menu = new ContextMenu();
        if (targets.Count == 1)
        {
            var changeHotkey = new MenuItem { Header = "Change hotkey", Tag = action };
            changeHotkey.Click += ChangeAdvancedActionHotkey_Click;
            menu.Items.Add(changeHotkey);
        }
        else menu.Items.Add(new MenuItem { Header = $"{targets.Count} selected hotkeys", IsEnabled = false });

        var hotkeyEnabledState = SharedMenuState(targets.Select(item => (bool?)item.HotkeyEnabled));
        var hotkeyEnabled = new MenuItem
        {
            Header = $"{hotkeyEnabledState}Hotkey enabled",
            IsCheckable = true,
            IsChecked = targets.All(item => item.HotkeyEnabled),
            StaysOpenOnClick = false
        };
        hotkeyEnabled.Click += (_, _) => SetHotkeysEnabled(targets, hotkeyEnabled.IsChecked);
        menu.Items.Add(hotkeyEnabled);
        menu.Items.Add(new Separator());

        var allUseSharedBehavior = targets.All(item => item.ActiveBehaviorOverrides == AutomationBehaviorOverride.None);
        var behaviorState = SharedBehaviorMenuState(targets);
        var sharedBehavior = new MenuItem
        {
            Header = $"{behaviorState}Use shared behavior defaults"
        };
        sharedBehavior.Click += (_, _) => ApplySharedBehaviorDefaults(targets, !allUseSharedBehavior);
        menu.Items.Add(sharedBehavior);
        menu.Items.Add(new Separator());

        var allUseSharedLighting = targets.All(item => item.UsesSharedLightingSettings);
        var lightingState = SharedMenuState(targets.Select(item => (bool?)item.UsesSharedLightingSettings));
        var sharedLighting = new MenuItem
        {
            Header = $"{lightingState}Use inherited lighting settings"
        };
        sharedLighting.Click += (_, _) => ApplySharedLightingSettings(targets, !allUseSharedLighting);
        menu.Items.Add(sharedLighting);
        var configureLighting = new MenuItem { Header = "Configure lighting override…" };
        configureLighting.Click += (_, _) => ConfigureLightingOverride(targets);
        menu.Items.Add(configureLighting);
        menu.Items.Add(new Separator());
        var copy = new MenuItem { Header = targets.Count == 1 ? "Copy hotkey to profile…" : "Copy selected hotkeys to profile…" };
        copy.Click += (_, _) => CopyHotkeysToProfile(targets);
        menu.Items.Add(copy);
        if (targets.Count > 1)
        {
            menu.Items.Add(new Separator());
            var deleteSelected = new MenuItem { Header = "Delete selected hotkeys…" };
            deleteSelected.Click += (_, _) => DeleteAdvancedActions(targets);
            menu.Items.Add(deleteSelected);
        }
        menu.PlacementTarget = placementTarget;
        menu.IsOpen = true;
    }

    private void CopyHotkeysToProfile(IReadOnlyList<AutomationAction> actions)
    {
        if (actions.Count == 0 || ActiveProfile() is not { } source) return;
        if (profilesDirty)
        {
            // This operation can create a new draft; keep the one-draft model unambiguous.
            Status("Save or discard current profile changes before copying hotkeys.", ThemeManager.Brush("WarningBrush"));
            return;
        }

        var destinations = automationProfiles.Profiles.Where(profile => profile.Id != source.Id).ToList();
        var dialog = new CopyHotkeysWindow(destinations, actions.Count) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        AutomationProfile destination;
        ProfileCopyResult result;
        if (dialog.DestinationProfile is null)
        {
            destination = AutomationProfileCopy.CreateNewProfile(UniqueProfileName(dialog.NewProfileName), source, actions);
            automationProfiles.Profiles.Add(destination);
            automationProfiles.ActiveProfileId = destination.Id;
            automationProfiles.ActiveActionId = destination.Actions.FirstOrDefault()?.Id ?? string.Empty;
            TouchRecentProfile(destination.Id);
            selectedAdvancedActionIds.Clear();
            if (destination.Actions.FirstOrDefault() is { } copiedAction) ApplyDefaults(ResolveActionSettings(copiedAction));
            else ShowAdvancedSharedDefaults(clearSelection: false);
            result = new ProfileCopyResult(destination.Actions.Count, 0, 0);
        }
        else
        {
            destination = dialog.DestinationProfile;
            result = AutomationProfileCopy.CopyTo(destination, actions, dialog.ConflictResolution);
        }

        MarkProfilesDirty();
        RefreshAdvancedFooterUi();
        var details = result.ReplacedCount > 0 ? $" Replaced {result.ReplacedCount}." : string.Empty;
        details += result.SkippedCount > 0 ? $" Skipped {result.SkippedCount}." : string.Empty;
        Status($"Copied {result.CopiedCount} hotkey{(result.CopiedCount == 1 ? string.Empty : "s")} to {destination.Name}.{details} Save to keep the change.", ThemeManager.Brush("SuccessBrush"));
    }

    private static string SharedBehaviorMenuState(IEnumerable<AutomationAction> actions)
    {
        var states = actions.Select(action => action.ActiveBehaviorOverrides == AutomationBehaviorOverride.None
            ? (bool?)true
            : action.ActiveBehaviorOverrides != AutomationBehaviorOverride.All ? null : false);
        return SharedMenuState(states);
    }

    private static string SharedMenuState(IEnumerable<bool?> states)
    {
        var values = states.Distinct().ToList();
        return values.Count == 1 && values[0] == true ? "✓  " : values.Any(value => value is true or null) ? "~  " : string.Empty;
    }

    private void SetHotkeysEnabled(IEnumerable<AutomationAction> actions, bool enabled)
    {
        var targets = actions.DistinctBy(action => action.Id).ToList();
        if (targets.Count == 0) return;
        foreach (var action in targets) action.HotkeyEnabled = enabled;
        RegisterConfiguredHotkey();
        MarkProfilesDirty();
        RefreshAdvancedFooterUi();
        var noun = targets.Count == 1 ? "hotkey" : "hotkeys";
        Status(enabled ? $"{targets.Count} {noun} enabled." : $"{targets.Count} {noun} disabled.", ThemeManager.Brush(enabled ? "SuccessBrush" : "TextMutedBrush"));
    }

    private void AdvancedActionStart_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is AutomationAction action) StartProfileAction(action);
    }

    private void AdvancedActionStop_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is AutomationAction action) StopProfileAction(action.Id);
    }

    private void DeleteAdvancedAction_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AutomationAction action || ActiveProfile() is not { } profile) return;
        var confirmation = new ConfirmationWindow("Delete hotkey", $"Remove {action.DisplayName} from {profile.Name}?", "Delete") { Owner = this };
        if (confirmation.ShowDialog() != true) return;
        DeleteAdvancedAction(action);
    }

    private void DeleteAdvancedAction(AutomationAction action)
    {
        if (ActiveProfile() is not { } profile) return;
        StopProfileAction(action.Id);
        profile.Actions.Remove(action);
        selectedAdvancedActionIds.Remove(action.Id);
        if (automationProfiles.ActiveActionId == action.Id)
        {
            var next = profile.Actions.FirstOrDefault();
            automationProfiles.ActiveActionId = next?.Id ?? string.Empty;
            if (next is not null) ApplyDefaults(ResolveActionSettings(next));
        }
        if (advancedMode) ShowAdvancedSharedDefaults();
        SaveAutomationProfiles();
        RegisterConfiguredHotkey();
        RefreshAdvancedFooterUi();
        UpdateLiveInputMode();
        Status("Hotkey removed.", ThemeManager.Brush("SuccessBrush"));
    }

    private void DeleteAdvancedActions(IEnumerable<AutomationAction> actions)
    {
        if (ActiveProfile() is not { } profile) return;
        var targets = actions.DistinctBy(action => action.Id).Where(action => profile.Actions.Any(item => item.Id == action.Id)).ToList();
        if (targets.Count < 2) return;
        var confirmation = new ConfirmationWindow(
            "Delete selected hotkeys",
            $"Remove {targets.Count} selected hotkeys from {profile.Name}? This cannot be undone.",
            "Delete hotkeys",
            destructive: true) { Owner = this };
        if (confirmation.ShowDialog() != true) return;

        foreach (var action in targets) StopProfileAction(action.Id);
        foreach (var action in targets) profile.Actions.Remove(action);
        pendingRemovalActionId = null;
        selectedAdvancedActionIds.Clear();
        if (targets.Any(action => action.Id == automationProfiles.ActiveActionId))
            automationProfiles.ActiveActionId = profile.Actions.FirstOrDefault()?.Id ?? string.Empty;
        if (advancedMode) ShowAdvancedSharedDefaults(clearSelection: false);
        SaveAutomationProfiles();
        RegisterConfiguredHotkey();
        RefreshAdvancedFooterUi();
        UpdateLiveInputMode();
        Status($"{targets.Count} hotkeys removed.", ThemeManager.Brush("SuccessBrush"));
    }

    private void AddAdvancedAction_Click(object sender, RoutedEventArgs e)
    {
        if (profileRuns.Count > 0)
        {
            Status("Stop active hotkeys before adding another.", ThemeManager.Brush("WarningBrush"));
            return;
        }
        if (capturingHotkey)
        {
            // Treat another Add click as a restart when the outstanding tile never received a key.
            // This keeps one unbound tile at most, without requiring Escape before trying again.
            if (pendingNewActionId is not null)
            {
                CancelHotkeyCapture(keepStatus: true);
            }
            else
            {
                Status("Finish choosing the hotkey, or press Escape to cancel it.", ThemeManager.Brush("WarningBrush"));
                return;
            }
        }
        var profile = ActiveProfile();
        if (profile is null) return;
        if (profile.Actions.Count >= AutomationProfileLimits.MaximumHotkeys)
        {
            Status($"A profile can have up to {AutomationProfileLimits.MaximumHotkeys} hotkeys.", ThemeManager.Brush("WarningBrush"));
            return;
        }
        CaptureCurrentActionToProfile();
        var action = new AutomationAction { Settings = CreateUnconfiguredActionDefaults(), UsesSharedBehaviorDefaults = true };
        // A new assignment is intentionally unbound until the capture prompt receives a key.
        action.Settings.Hotkey = 0;
        action.Settings.HotkeyModifiers = 0;
        action.Settings.HotkeyTrigger = HotkeyTrigger.Keyboard;
        profile.Actions.Add(action);
        automationProfiles.ActiveActionId = action.Id;
        pendingNewActionId = action.Id;
        selectedAdvancedActionIds.Clear();
        selectedAdvancedActionIds.Add(action.Id);
        SaveAutomationProfiles();
        if (hotkeyRegistered) { UnregisterHotKey(hwnd, HotkeyId); hotkeyRegistered = false; }
        ApplyDefaults(ResolveActionSettings(action));
        RefreshAdvancedFooterUi();
        BeginHotkeyCapture();
    }

    private void ToggleProfileAction(AutomationAction action)
    {
        if (!action.HotkeyEnabled) return;
        if (settingsOpen) { Status("Close Settings before starting another hotkey.", ThemeManager.Brush("WarningBrush")); return; }
        if (profileRuns.ContainsKey(action.Id)) StopProfileAction(action.Id); else StartProfileAction(action);
    }

    private void StartProfileAction(AutomationAction action)
    {
        if (!HotkeyFormatter.IsConfigured(action.Settings.Hotkey, action.Settings.HotkeyTrigger) || profileRuns.ContainsKey(action.Id)) return;
        var effectiveSettings = ResolveActionSettings(action);
        var input = string.IsNullOrWhiteSpace(effectiveSettings.Input) ? effectiveSettings.MouseButton : effectiveSettings.Input;
        if (!InputRules.IsConfiguredAction(input, effectiveSettings.CustomKey, effectiveSettings.CustomSequence?.Count ?? 0))
        {
            Status($"Set an action for {HotkeyFormatter.Format(action.Settings.Hotkey, action.Settings.HotkeyModifiers, action.Settings.HotkeyTrigger)} before starting it.", ThemeManager.Brush("WarningBrush"));
            return;
        }
        if (InputRules.IsHoldAction(effectiveSettings.ClickType) && effectiveSettings.TargetWindowEnabled && !string.IsNullOrWhiteSpace(effectiveSettings.TargetExecutable))
        {
            Status("Target-window mode does not support held input. Override target window on the hotkey if desired.", ThemeManager.Brush("WarningBrush"));
            return;
        }
        var repeatedKey = input switch { "Space" => 0x20, "Enter" => 0x0D, "Custom" => effectiveSettings.CustomKey, _ => 0 };
        if (action.Settings.HotkeyTrigger == HotkeyTrigger.Keyboard && repeatedKey == action.Settings.Hotkey && action.Settings.HotkeyModifiers == 0)
        {
            Status("Choose a different hotkey from the key being repeated.", ThemeManager.Brush("WarningBrush"));
            return;
        }
        var cancellation = new CancellationTokenSource();
        profileRuns[action.Id] = cancellation;
        var settings = CreateClickSettings(effectiveSettings);
        var interval = InputRules.CreateInterval(effectiveSettings.Hours, effectiveSettings.Minutes, effectiveSettings.Seconds, effectiveSettings.Milliseconds);
        AppLog.Info($"Starting profile action {action.DisplayName} | IntervalMs={interval.TotalMilliseconds:0.###} | PulseMs={settings.InputPulseMilliseconds} | JitterMaxMs={settings.JitterMaximumMilliseconds} | WorkerPriority={settings.WorkerPriority} | Repeat={(settings.MaximumClicks?.ToString() ?? "until stopped")}");
        profileTasks[action.Id] = AutomationWorkerScheduler.Start(() => ProfileClickLoop(action.Id, interval, settings, cancellation));
        CollapseButton.IsEnabled = false;
        Status($"{action.DisplayName} active.", ThemeManager.Brush("ErrorBrush"));
        SetTaskbarIcon(running: true);
        RefreshAdvancedFooterUi();
        UpdateSharedBehaviorDefaultsUi();
        StartRgbIndicator(action.Id, ResolveLighting(action), LightingKeyName(action.Settings));
        RestoreLiveArea();
        UpdateLiveInputMode();
    }

    private void StopProfileAction(string actionId)
    {
        if (!profileRuns.Remove(actionId, out var cancellation)) return;
        cancellation.Cancel();
        profileTasks.Remove(actionId);
        Status("Profile hotkey stopped.", ThemeManager.Brush("SuccessBrush"));
        if (clickCancellation is null && profileRuns.Count == 0) { SetTaskbarIcon(running: false); CollapseButton.IsEnabled = true; }
        RefreshAdvancedFooterUi();
        UpdateSharedBehaviorDefaultsUi();
        StopRgbIndicator(actionId);
        RestoreLiveArea();
        UpdateLiveInputMode();
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
        // Reject states that cannot produce a valid worker configuration.
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
        if (!InputRules.IsConfiguredAction(input, customSpamVirtualKey, customSequence.Count))
        {
            Status("Set an action before starting.", ThemeManager.Brush("WarningBrush"));
            return;
        }
        if (InputRules.IsHoldAction(Selected(TypeCombo)) && EnableTargetWindowCheckBox.IsChecked == true && !string.IsNullOrWhiteSpace(TargetExecutableBox.Text))
        {
            Status("Target-window mode does not support held input.", ThemeManager.Brush("WarningBrush"));
            return;
        }
        // Resolve keyboard shortcuts before handing work to the background thread.
        var keyboardVirtualKey = input switch { "Space" => 0x20, "Enter" => 0x0D, "Custom" => customSpamVirtualKey, _ => 0 };
        if (keyboardVirtualKey == hotkey && hotkeyModifiers == 0)
        {
            Status($"{FormatInputKey(keyboardVirtualKey)} is also the start/stop hotkey. Choose another key or change the hotkey first.", ThemeManager.Brush("WarningBrush"));
            return;
        }
        // Take a snapshot of the UI; the worker must not read WPF controls.
        var interval = NormalizeIntervalBoxes();
        var delay = InputRules.CreateInterval(interval.Hours, interval.Minutes, interval.Seconds, interval.Milliseconds);
        // Owns cancellation for this run.
        var cancellation = new CancellationTokenSource();
        clickCancellation = cancellation;
        Volatile.Write(ref lastGuiHeartbeat, Stopwatch.GetTimestamp());
        var hold = InputRules.IsHoldAction(Selected(TypeCombo));
        var target = EnableTargetWindowCheckBox.IsChecked == true
            ? new TargetWindowRule(TargetExecutableBox.Text, targetWindowTitle)
            : new TargetWindowRule(string.Empty, null);
        var sequencePulseMilliseconds = customSequenceUsesGlobalInputPulse ? inputPulseMilliseconds : 0;
        var settings = new ClickSettings(FixedPositionRadio.IsChecked == true, Read(XBox, -32768, 32767), Read(YBox, -32768, 32767), input, keyboardVirtualKey == 0 ? null : keyboardVirtualKey, Selected(TypeCombo) == "Double", hold, hold ? null : CountRadio.IsChecked == true ? Read(CountBox, 1, 999999) : null, input == "Sequence" ? BuildSequence(customSequence) : null, InputRules.NormalizeInputPulseMilliseconds(input == "Sequence" ? sequencePulseMilliseconds : inputPulseMilliseconds), inputJitterMaximumMilliseconds, workerPriority, cadenceDiagnosticsEnabled, target);
        // Reflect the running state before the worker can send its first input.
        CaptureCurrentActionToProfile();
        AppLog.Info($"Starting {ActivityVerb().ToLowerInvariant()} | Input={input} | IntervalMs={delay.TotalMilliseconds:0.###} | PulseMs={settings.InputPulseMilliseconds} | JitterMaxMs={settings.JitterMaximumMilliseconds} | WorkerPriority={settings.WorkerPriority} | Repeat={(settings.MaximumClicks?.ToString() ?? "until stopped")}");
        StartButton.IsEnabled = false; StopButton.IsEnabled = true;
        CollapseButton.IsEnabled = false;
        UpdateSharedBehaviorDefaultsUi();
        LiveArea.Background = ThemeManager.Brush("AccentBrush");
        LiveArea.BorderBrush = ThemeManager.Brush("AccentHoverBrush");
        LiveCountLabel.Text = liveClickCount == 0 ? "0 clicks" : $"{liveClickCount:N0} clicks";
        UpdateLiveInputMode();
        Status($"{ActivityVerb()} — press {FormatHotkey()} to stop.", ThemeManager.Brush("ErrorBrush"));
        SetTaskbarIcon(running: true);
        StartRgbIndicator();
        clickTask = AutomationWorkerScheduler.Start(() => ClickLoop(delay, settings, cancellation));
    }

    private void ClickLoop(TimeSpan delay, ClickSettings settings, CancellationTokenSource cancellation)
    {
        var sent = 0;
        var watchdogExpired = false;
        Exception? failure = null;
        var originalPriority = Thread.CurrentThread.Priority;
        Input[]? heldRelease = null;
        CadenceDiagnostics? cadence = null;
        try
        {
            Thread.CurrentThread.Priority = settings.WorkerPriority == WorkerPriorityOption.AboveNormal
                ? ThreadPriority.AboveNormal
                : ThreadPriority.Normal;
            // Set up a fixed cadence rather than sleeping after each action.
            using var timer = new PrecisionTimer();
            var intervalTicks = delay.TotalSeconds * Stopwatch.Frequency;
            Random? jitter = settings.JitterMaximumMilliseconds > 0 ? new Random() : null;
            var nextClickAt = (double)Stopwatch.GetTimestamp();
            if (settings.CadenceDiagnosticsEnabled) cadence = new CadenceDiagnostics(intervalTicks, settings.InputPulseMilliseconds);
            var actionInputs = settings.KeyboardVirtualKey is int virtualKey ? CreateKeyInputs(virtualKey) : CreateClickInputs(settings.Button);
            if (settings.Hold)
            {
                if (settings.FixedPosition && settings.KeyboardVirtualKey is null) SetCursorPos(settings.X, settings.Y);
                // Send the down packet once; finally always sends the matching up packet.
                heldRelease = [actionInputs[1]];
                SendNativeInput(1, [actionInputs[0]]);
                if (settings.KeyboardVirtualKey is not null)
                {
                    var repeatAt = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 2;
                    while (!cancellation.IsCancellationRequested)
                    {
                        if (!WaitUntilGuiIsHealthy(timer, repeatAt, cancellation, ref watchdogExpired)) break;
                        SendNativeInput(1, [actionInputs[0]]);
                        repeatAt = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 30;
                    }
                    return;
                }
                while (!cancellation.IsCancellationRequested)
                    if (!WaitUntilGuiIsHealthy(timer, Stopwatch.GetTimestamp() + Stopwatch.Frequency, cancellation, ref watchdogExpired)) break;
                return;
            }
            while (!cancellation.IsCancellationRequested && (!settings.MaximumClicks.HasValue || sent < settings.MaximumClicks.Value))
            {
                if (!WaitUntilGuiIsHealthy(timer, nextClickAt, cancellation, ref watchdogExpired)) break;
                var now = Stopwatch.GetTimestamp();
                // Resume from the current time instead of catching up in a burst.
                if (now - nextClickAt > intervalTicks) nextClickAt = now;
                if (settings.Sequence is { Length: > 0 })
                {
                    // A sequence has its own ordered actions and optional waits.
                    var sentSequenceAction = false;
                    foreach (var step in settings.Sequence)
                    {
                        if (step.IsDelay)
                        {
                            if (!WaitUntilGuiIsHealthy(timer, Stopwatch.GetTimestamp() + step.DelayAfterMilliseconds * Stopwatch.Frequency / 1000d, cancellation, ref watchdogExpired)) break;
                            continue;
                        }
                        if (CanSendAction(settings, step.IsMouse))
                        {
                            if (settings.FixedPosition && step.IsMouse) SetCursorPos(settings.X, settings.Y);
                            if (!SendAction(step.Inputs, false, settings.InputPulseMilliseconds, timer, cancellation, ref watchdogExpired)) break;
                            sentSequenceAction = true;
                        }
                        if (step.DelayAfterMilliseconds > 0)
                            if (!WaitUntilGuiIsHealthy(timer, Stopwatch.GetTimestamp() + step.DelayAfterMilliseconds * Stopwatch.Frequency / 1000d, cancellation, ref watchdogExpired)) break;
                    }
                    if (watchdogExpired || cancellation.IsCancellationRequested) break;
                    if (sentSequenceAction) sent++;
                }
                else
                {
                    // Single mouse/key actions share the main interval.
                    if (CanSendAction(settings, settings.KeyboardVirtualKey is null))
                    {
                        if (settings.FixedPosition && settings.KeyboardVirtualKey is null) SetCursorPos(settings.X, settings.Y);
                        var sentAction = cadence is null
                            ? SendAction(actionInputs, settings.DoubleClick, settings.InputPulseMilliseconds, timer, cancellation, ref watchdogExpired)
                            : SendActionWithDiagnostics(actionInputs, settings.DoubleClick, settings.InputPulseMilliseconds, timer, cancellation, ref watchdogExpired, cadence, nextClickAt);
                        if (!sentAction) break;
                        sent++;
                    }
                }
                var effectiveIntervalTicks = jitter is null
                    ? intervalTicks
                    : InputRules.ApplyJitter((long)delay.TotalMilliseconds, InputRules.NextJitterOffsetMilliseconds(settings.JitterMaximumMilliseconds, jitter)) * Stopwatch.Frequency / 1000d;
                nextClickAt = settings.Sequence is { Length: > 0 }
                    ? Stopwatch.GetTimestamp() + effectiveIntervalTicks
                    : nextClickAt + effectiveIntervalTicks;
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
            if (heldRelease is not null)
            {
                try { SendNativeInput((uint)heldRelease.Length, heldRelease); }
                catch (Exception exception) { AppLog.Error("Could not release held input", exception); }
            }
            try { Thread.CurrentThread.Priority = originalPriority; } catch { }
            cadence?.LogSummary();
            // Return state changes to the UI thread after the worker has finished.
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
                        // StopClicking can still cancel this source on the UI thread.
                        cancellation.Dispose();
                    }
                });
            else
                cancellation.Dispose();
        }
    }

    private ClickSettings CreateClickSettings(AppDefaults source)
    {
        var input = string.IsNullOrWhiteSpace(source.Input) ? source.MouseButton : source.Input;
        var key = input switch { "Space" => 0x20, "Enter" => 0x0D, "Custom" => source.CustomKey, _ => 0 };
        var hold = InputRules.IsHoldAction(source.ClickType);
        var target = source.TargetWindowEnabled
            ? new TargetWindowRule(source.TargetExecutable, source.TargetWindowTitle)
            : new TargetWindowRule(string.Empty, null);
        var sequencePulse = source.CustomSequenceUsesGlobalInputPulse ? source.InputPulseMilliseconds : 0;
        return new ClickSettings(source.FixedPosition, source.X, source.Y, input, key == 0 ? null : key, source.ClickType == "Double", hold,
            hold ? null : source.RepeatUntilStopped ? null : Math.Clamp(source.RepeatCount, 1, 999999), input == "Sequence" ? BuildSequence(source.CustomSequence ?? []) : null,
            InputRules.NormalizeInputPulseMilliseconds(input == "Sequence" ? sequencePulse ?? 0 : source.InputPulseMilliseconds ?? 0), source.InputJitterMaximumMilliseconds,
            workerPriority, cadenceDiagnosticsEnabled, target);
    }

    // Secondary profile actions keep their own cancellation source so one hotkey never stops another.
    private void ProfileClickLoop(string actionId, TimeSpan delay, ClickSettings settings, CancellationTokenSource cancellation)
    {
        Input[]? heldRelease = null;
        var watchdogExpired = false;
        var originalPriority = Thread.CurrentThread.Priority;
        CadenceDiagnostics? cadence = null;
        try
        {
            Thread.CurrentThread.Priority = settings.WorkerPriority == WorkerPriorityOption.AboveNormal
                ? ThreadPriority.AboveNormal
                : ThreadPriority.Normal;
            using var timer = new PrecisionTimer();
            var actionInputs = settings.KeyboardVirtualKey is int key ? CreateKeyInputs(key) : CreateClickInputs(settings.Button);
            if (settings.Hold)
            {
                if (settings.FixedPosition && settings.KeyboardVirtualKey is null) SetCursorPos(settings.X, settings.Y);
                heldRelease = [actionInputs[1]];
                SendNativeInput(1, [actionInputs[0]]);
                if (settings.KeyboardVirtualKey is not null)
                {
                    var repeatAt = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 2;
                    while (!cancellation.IsCancellationRequested)
                    {
                        if (!WaitUntilGuiIsHealthy(timer, repeatAt, cancellation, ref watchdogExpired)) break;
                        SendNativeInput(1, [actionInputs[0]]);
                        repeatAt = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 30;
                    }
                    return;
                }
                while (!cancellation.IsCancellationRequested)
                    if (!WaitUntilGuiIsHealthy(timer, Stopwatch.GetTimestamp() + Stopwatch.Frequency, cancellation, ref watchdogExpired)) break;
                return;
            }
            var intervalTicks = Math.Max(1d, delay.TotalSeconds * Stopwatch.Frequency);
            var next = (double)Stopwatch.GetTimestamp();
            Random? jitter = settings.JitterMaximumMilliseconds > 0 ? new Random() : null;
            if (settings.CadenceDiagnosticsEnabled) cadence = new CadenceDiagnostics(intervalTicks, settings.InputPulseMilliseconds);
            var sent = 0;
            while (!cancellation.IsCancellationRequested && (!settings.MaximumClicks.HasValue || sent < settings.MaximumClicks.Value))
            {
                if (!WaitUntilGuiIsHealthy(timer, next, cancellation, ref watchdogExpired)) break;
                var now = Stopwatch.GetTimestamp();
                if (now - next > intervalTicks) next = now;
                if (settings.Sequence is { Length: > 0 })
                {
                    var sentSequenceAction = false;
                    foreach (var step in settings.Sequence)
                    {
                        if (step.IsDelay) { if (!WaitUntilGuiIsHealthy(timer, Stopwatch.GetTimestamp() + step.DelayAfterMilliseconds * Stopwatch.Frequency / 1000d, cancellation, ref watchdogExpired)) break; continue; }
                        if (CanSendAction(settings, step.IsMouse))
                        {
                            if (settings.FixedPosition && step.IsMouse) SetCursorPos(settings.X, settings.Y);
                            var sentAction = cadence is null
                                ? SendAction(step.Inputs, false, settings.InputPulseMilliseconds, timer, cancellation, ref watchdogExpired)
                                : SendActionWithDiagnostics(step.Inputs, false, settings.InputPulseMilliseconds, timer, cancellation, ref watchdogExpired, cadence, next);
                            if (!sentAction) break;
                            sentSequenceAction = true;
                        }
                        if (step.DelayAfterMilliseconds > 0 && !WaitUntilGuiIsHealthy(timer, Stopwatch.GetTimestamp() + step.DelayAfterMilliseconds * Stopwatch.Frequency / 1000d, cancellation, ref watchdogExpired)) break;
                    }
                    if (watchdogExpired || cancellation.IsCancellationRequested) break;
                    if (sentSequenceAction) sent++;
                }
                else if (CanSendAction(settings, settings.KeyboardVirtualKey is null))
                {
                    if (settings.FixedPosition && settings.KeyboardVirtualKey is null) SetCursorPos(settings.X, settings.Y);
                    var sentAction = cadence is null
                        ? SendAction(actionInputs, settings.DoubleClick, settings.InputPulseMilliseconds, timer, cancellation, ref watchdogExpired)
                        : SendActionWithDiagnostics(actionInputs, settings.DoubleClick, settings.InputPulseMilliseconds, timer, cancellation, ref watchdogExpired, cadence, next);
                    if (!sentAction) break;
                    sent++;
                }
                var effectiveIntervalTicks = jitter is null
                    ? intervalTicks
                    : InputRules.ApplyJitter((long)delay.TotalMilliseconds, InputRules.NextJitterOffsetMilliseconds(settings.JitterMaximumMilliseconds, jitter)) * Stopwatch.Frequency / 1000d;
                next = settings.Sequence is { Length: > 0 }
                    ? Stopwatch.GetTimestamp() + effectiveIntervalTicks
                    : next + effectiveIntervalTicks;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { AppLog.Error("Profile hotkey worker failed", exception); }
        finally
        {
            if (heldRelease is not null) { try { SendNativeInput((uint)heldRelease.Length, heldRelease); } catch { } }
            try { Thread.CurrentThread.Priority = originalPriority; } catch { }
            cadence?.LogSummary();
            if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(() =>
            {
                if (profileRuns.TryGetValue(actionId, out var current) && ReferenceEquals(current, cancellation))
                {
                    profileRuns.Remove(actionId);
                    profileTasks.Remove(actionId);
                    if (!isClosing) Status(watchdogExpired ? "A profile hotkey stopped because the GUI heartbeat timed out." : "Profile hotkey stopped.", ThemeManager.Brush(watchdogExpired ? "WarningBrush" : "SuccessBrush"));
                    if (clickCancellation is null && profileRuns.Count == 0) { SetTaskbarIcon(running: false); CollapseButton.IsEnabled = true; }
                    RefreshAdvancedFooterUi();
                    UpdateSharedBehaviorDefaultsUi();
                    StopRgbIndicator(actionId);
                    RestoreLiveArea();
                    UpdateLiveInputMode();
                }
                cancellation.Dispose();
            });
            else cancellation.Dispose();
        }
    }

    private void StopClicking()
    {
        // Clear the shared reference first so a late completion cannot stop a new run.
        var cancellation = clickCancellation;
        clickCancellation = null;
        cancellation?.Cancel();
        if (cancellation is not null) AppLog.Info("Click/spam worker stop requested.");
        StartButton.IsEnabled = true; StopButton.IsEnabled = false;
        CollapseButton.IsEnabled = true;
        UpdateSharedBehaviorDefaultsUi();
        LiveArea.Background = ThemeManager.Brush("ControlBrush");
        LiveArea.BorderBrush = ThemeManager.Brush("LiveBorderBrush");
        if (liveClickCount == 0) LiveCountLabel.Text = "Start to test";
        UpdateLiveInputMode();
        Status($"Ready — press {FormatHotkey()} to start or stop.", ThemeManager.Brush("SuccessBrush"));
        SetTaskbarIcon(running: profileRuns.Count > 0);
        StopRgbIndicator(SimpleRgbIndicatorId);
    }

    private bool WaitUntilGuiIsHealthy(PrecisionTimer timer, double targetTimestamp, CancellationTokenSource cancellation, ref bool watchdogExpired)
    {
        // Check the heartbeat during long waits.
        while (true)
        {
            var now = Stopwatch.GetTimestamp();
            if (WorkerSafety.IsGuiHeartbeatExpired(Volatile.Read(ref lastGuiHeartbeat), now, Stopwatch.Frequency))
            {
                watchdogExpired = true;
                cancellation.Cancel();
                return false;
            }
            if (now >= targetTimestamp) return true;
            timer.WaitUntil(Math.Min(targetTimestamp, now + Stopwatch.Frequency), cancellation.Token);
        }
    }

    internal bool IsClicking => clickCancellation is not null || profileRuns.Count > 0;
    internal void EmergencyStop()
    {
        clickCancellation?.Cancel();
        foreach (var cancellation in profileRuns.Values) cancellation.Cancel();
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

    private AutomationAction? SingleRunningProfileAction() => advancedMode && profileRuns.Count == 1
        ? ActiveProfile()?.Actions.FirstOrDefault(action => action.Id == profileRuns.Keys.First())
        : null;

    private bool IsTestAreaRunning => clickCancellation is not null || SingleRunningProfileAction() is not null;
    private bool HasMultipleActiveProfileActions => advancedMode && profileRuns.Count > 1;
    private AppDefaults TestAreaSettings() => SingleRunningProfileAction() is { } action ? ResolveActionSettings(action) : CreateCurrentDefaults();
    private bool IsKeyboardInputSelectedForTest()
    {
        var settings = TestAreaSettings();
        return InputRules.IsKeyboardAction(string.IsNullOrWhiteSpace(settings.Input) ? settings.MouseButton : settings.Input!);
    }

    private void LiveArea_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!IsTestAreaRunning || HasMultipleActiveProfileActions || IsKeyboardInputSelectedForTest()) return;
        var now = DateTime.UtcNow;
        var doubleClickMode = string.Equals(TestAreaSettings().ClickType, "Double", StringComparison.OrdinalIgnoreCase);
        TimeSpan? interval;
        if (doubleClickMode)
        {
            var secondClickInPair = (liveClickCount % 2) == 1;
            if (secondClickInPair)
            {
                interval = liveClickCount > 1 ? InputEventTimestamp.Elapsed(lastLiveClickTimestamp, e.Timestamp) : (TimeSpan?)null;
                lastLiveClickTimestamp = e.Timestamp;
            }
            else
            {
                interval = null;
            }
        }
        else
        {
            interval = liveClickCount > 0 ? InputEventTimestamp.Elapsed(lastLiveClickTimestamp, e.Timestamp) : (TimeSpan?)null;
            lastLiveClickTimestamp = e.Timestamp;
        }
        liveClickCount++; lastLiveClick = now;
        LiveCountLabel.Text = $"{liveClickCount:N0} clicks";
        LiveIntervalLabel.Text = interval is null
            ? (doubleClickMode ? "Waiting for next double click" : "Waiting for next click")
            : $"Last interval: ~{FormatInterval(interval.Value)}";
        // Keep live feedback visible without changing label colours.
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
        if (!IsTestAreaRunning || HasMultipleActiveProfileActions) return;
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
        if (!IsTestAreaRunning || HasMultipleActiveProfileActions || !IsKeyboardInputSelectedForTest()) return;
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
        var running = IsTestAreaRunning;
        LiveArea.Background = ThemeManager.Brush(running ? "AccentBrush" : "ControlBrush");
        LiveArea.BorderBrush = ThemeManager.Brush(running ? "AccentHoverBrush" : "LiveBorderBrush");
        UpdateLiveAreaTextContrast();
    }
    private void ResetCounterWhenIdle()
    {
        if (HasMultipleActiveProfileActions) return;
        var now = DateTime.UtcNow;
        if (liveClickCount > 0 && now - lastLiveClick >= TimeSpan.FromSeconds(3))
        {
            liveClickCount = 0; LiveCountLabel.Text = IsTestAreaRunning ? "0 clicks" : "Start to test";
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
            || IntervalHint is null || LiveCountLabel is null || LiveIntervalLabel is null || TypeCombo is null || PositionCard is null) return;
        var testSettings = TestAreaSettings();
        var testInput = string.IsNullOrWhiteSpace(testSettings.Input) ? testSettings.MouseButton : testSettings.Input;
        var targetWindowEnabled = testSettings.TargetWindowEnabled && !string.IsNullOrWhiteSpace(testSettings.TargetExecutable);
        var sequenceInput = testInput == "Sequence";
        var keyboardInput = InputRules.IsKeyboardAction(testInput!);
        var hold = InputRules.IsHoldAction(testSettings.ClickType);
        var sharedBehavior = advancedMode && ActiveProfileAction()?.UsesSharedBehavior(AutomationBehaviorOverride.Position) == true;
        TypeCombo.IsEnabled = !sequenceInput;
        PositionCard.IsEnabled = !sequenceInput && !keyboardInput;
        if (PositionContent is not null) PositionContent.IsEnabled = PositionCard.IsEnabled && !sharedBehavior;
        UpdatePositionInputEnabled();
        if (HasMultipleActiveProfileActions)
        {
            LiveArea.IsHitTestVisible = false;
            LiveArea.Opacity = 0.7;
            LiveMouseHint.Visibility = Visibility.Visible;
            LiveKeyFocusBox.Visibility = Visibility.Collapsed;
            LiveTitleLabel.Text = "MULTIPLE HOTKEYS ACTIVE";
            LiveMouseHint.Text = "Test area is disabled while multiple actions run";
            LiveCountLabel.Text = "Multiple actions active";
            LiveIntervalLabel.Text = "Stop all but one action to test it here";
            UpdateLiveAreaTextContrast();
            return;
        }
        LiveArea.IsHitTestVisible = !sequenceInput && !targetWindowEnabled;
        LiveArea.Opacity = sequenceInput || targetWindowEnabled ? 0.7 : 1;
        if (targetWindowEnabled)
        {
            LiveMouseHint.Visibility = Visibility.Visible;
            LiveKeyFocusBox.Visibility = Visibility.Collapsed;
            LiveTitleLabel.Text = "TARGET WINDOW MODE";
            LiveMouseHint.Text = "Test area disabled while targeting a window";
            LiveCountLabel.Text = "Target window active";
            LiveIntervalLabel.Text = "Input is sent only to the selected target";
            IntervalHint.Text = sequenceInput ? "Time between sequences" : hold ? "Hold stays active until stopped" : keyboardInput ? "Time between key presses" : "Time between clicks";
            UpdateLiveAreaTextContrast();
            return;
        }
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
        IntervalHint.Text = hold ? "Hold stays active until stopped" : keyboardInput ? "Time between key presses" : "Time between clicks";
        if (keyboardInput)
        {
            LiveCountLabel.Text = !IsTestAreaRunning ? "Start to test" : KeyTestBox.IsKeyboardFocusWithin ? FormatKeyPressCount(liveKeyPressCount) : "Focus the field to test";
            if (lastLiveKey == default) LiveIntervalLabel.Text = "Waiting for key presses";
        }
        if (!keyboardInput)
        {
            lastLiveKey = default;
            liveKeyPressCount = 0;
            KeyTestBox?.Clear();
            if (KeyTestPlaceholder is not null) KeyTestPlaceholder.Visibility = Visibility.Visible;
            if (!IsTestAreaRunning)
            {
                LiveCountLabel.Text = "Start to test";
                LiveIntervalLabel.Text = "Waiting for clicks";
            }
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
        : InputRules.IsHoldAction(Selected(TypeCombo))
            ? IsKeyboardInputSelected() ? "Holding key" : "Holding mouse button"
            : IsKeyboardInputSelected() ? "Spamming" : "Clicking";

    private void UpdateLiveAreaTextContrast()
    {
        if (LiveTitleLabel is null) return;
        var brush = ThemeManager.Brush(ThemeManager.Current == AppTheme.Light && IsTestAreaRunning ? "LiveAccentTextBrush" : "TextMutedBrush");
        LiveTitleLabel.Foreground = brush;
        LiveIntervalLabel.Foreground = brush;
        LiveMouseHint.Foreground = brush;
        KeyTestPlaceholder.Foreground = brush;
    }

    private static string FormatKeyPressCount(int count) => count == 1 ? "1 key press" : $"{count:N0} key presses";

    private void StartRgbIndicator() => StartRgbIndicator(SimpleRgbIndicatorId, rgbSettings, HotkeyKeyName());

    private void StartRgbIndicator(string indicatorId, RgbSettings source, string? keyName)
    {
        // OpenRGB can only address keyboard LEDs; a mouse binding deliberately has no key to illuminate.
        if (!source.Enabled || string.IsNullOrWhiteSpace(keyName)) return;
        const int openRgbStartupRetryWindowMilliseconds = 10000;
        const int openRgbStartupRetryDelayMilliseconds = 300;
        var updateSharedDevice = ReferenceEquals(source, rgbSettings);
        StopRgbIndicator(indicatorId, applyIdleWhenLast: false);
        // Work from a copy because Settings may change while OpenRGB is resolving.
        var settings = CloneLighting(source);
        settings.Enabled = true;
        var idleProfileName = (settings.IdleProfileName ?? string.Empty).Trim();
        var cancellation = new CancellationTokenSource();
        var session = new RgbIndicatorSession(cancellation);
        var shouldPrimeIdleProfile = false;
        lock (rgbLock)
        {
            shouldPrimeIdleProfile = rgbIndicators.Count == 0;
            rgbIndicators[indicatorId] = session;
        }
        session.Task = Task.Run(async () =>
        {
            RgbLightingSnapshot? snapshot = null;
            string? indicatorError = null;
            var idleProfilePrimed = false;
            try
            {
                var retryDeadline = Stopwatch.GetTimestamp() + openRgbStartupRetryWindowMilliseconds * Stopwatch.Frequency / 1000d;
                while (!cancellation.IsCancellationRequested && !isClosing && !Dispatcher.HasShutdownStarted)
                {
                    var availability = await OpenRgbHighlighter.EnsureSdkAsync(settings);
                    if (!availability.IsAvailable)
                    {
                        indicatorError = availability.Message ?? "OpenRGB's SDK server is unavailable.";
                        if (!settings.AutoStart || Stopwatch.GetTimestamp() >= retryDeadline) break;
                        await Task.Delay(openRgbStartupRetryDelayMilliseconds, cancellation.Token);
                        continue;
                    }

                    ClearOpenRgbWarning();
                    if (availability.Message is not null && !Dispatcher.HasShutdownStarted)
                        _ = Dispatcher.BeginInvoke(ShowOpenRgbStartedStatus);
                    // Resolve by saved name where possible, then keep the resolved device details.
                    var keyboard = OpenRgbHighlighter.ResolveKeyboard(settings);
                    if (keyboard is null)
                    {
                        indicatorError = "No matching OpenRGB keyboard found. Open Settings to choose one.";
                        if (!settings.AutoStart || Stopwatch.GetTimestamp() >= retryDeadline) break;
                        await Task.Delay(openRgbStartupRetryDelayMilliseconds, cancellation.Token);
                        continue;
                    }

                    settings.DeviceIndex = keyboard.Index;
                    settings.DeviceName = keyboard.Name;

                    if (!idleProfilePrimed && shouldPrimeIdleProfile && idleProfileName.Length > 0)
                    {
                        if (!OpenRgbHighlighter.TryLoadProfile(idleProfileName, out var idleError))
                        {
                            if (idleError is not null) AppLog.Info(idleError);
                        }
                        else
                        {
                            // Give OpenRGB a brief moment to apply the profile before we snapshot per-key colours.
                            await Task.Delay(120, cancellation.Token);
                        }

                        idleProfilePrimed = true;
                    }

                    snapshot = OpenRgbHighlighter.EnableKeyIndicator(settings, keyName, out indicatorError, lightImmediately: false);
                    if (snapshot is not null) break;
                    if (!settings.AutoStart || Stopwatch.GetTimestamp() >= retryDeadline) break;
                    await Task.Delay(openRgbStartupRetryDelayMilliseconds, cancellation.Token);
                }

                if (snapshot is null)
                {
                    if (cancellation.IsCancellationRequested || isClosing || Dispatcher.HasShutdownStarted) return;
                    if (indicatorError is not null)
                    {
                        ShowOpenRgbWarning(indicatorError);
                        if (!Dispatcher.HasShutdownStarted)
                            Dispatcher.BeginInvoke(() => Status(indicatorError, ThemeManager.Brush("ErrorBrush")));
                    }
                    return;
                }

                if (!settings.IsPulse) OpenRgbHighlighter.LightIndicator(snapshot);
                if (settings.IsBlink)
                    await OpenRgbHighlighter.BlinkIndicatorAsync(snapshot, settings.PulseSpeedMilliseconds, cancellation.Token);
                else if (settings.IsPulse)
                    await OpenRgbHighlighter.FadePulseIndicatorAsync(snapshot, settings.PulseSpeedMilliseconds, cancellation.Token);
                else
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellation.Token);
                if (updateSharedDevice && !Dispatcher.HasShutdownStarted && (settings.DeviceIndex != rgbSettings.DeviceIndex || !string.Equals(settings.DeviceName, rgbSettings.DeviceName, StringComparison.Ordinal)))
                    Dispatcher.BeginInvoke(() => { rgbSettings = settings; SaveRgbSettings(); });
                if (indicatorError is not null && !Dispatcher.HasShutdownStarted)
                {
                    Dispatcher.BeginInvoke(() => Status(indicatorError, ThemeManager.Brush("ErrorBrush")));
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) when (!Dispatcher.HasShutdownStarted)
            {
                AppLog.Error("OpenRGB hotkey indicator failed", exception);
                Dispatcher.BeginInvoke(() => Status($"OpenRGB unavailable: {exception.Message}", ThemeManager.Brush("ErrorBrush")));
            }
            finally
            {
                var shouldClear = true;
                var shouldApplyIdleProfile = false;
                lock (rgbLock)
                {
                    if (rgbIndicators.TryGetValue(indicatorId, out var current) && ReferenceEquals(current, session))
                    {
                        rgbIndicators.Remove(indicatorId);
                        shouldApplyIdleProfile = rgbIndicators.Count == 0;
                    }
                    else if (rgbIndicators.ContainsKey(indicatorId))
                        shouldClear = false;
                }
                if (shouldClear && snapshot is not null) OpenRgbHighlighter.ClearIndicator(snapshot);
                if (shouldApplyIdleProfile) ApplyIdleOpenRgbProfile();
                cancellation.Dispose();
            }
        });
    }

    private void ApplyIdleOpenRgbProfile()
    {
        var profileName = (rgbSettings.IdleProfileName ?? string.Empty).Trim();
        if (profileName.Length == 0) return;

        var settings = CloneLighting(rgbSettings);
        settings.Enabled = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var availability = await OpenRgbHighlighter.EnsureSdkAsync(settings);
                if (!availability.IsAvailable)
                {
                    AppLog.Info($"Could not apply idle OpenRGB profile '{profileName}': {availability.Message}");
                    return;
                }

                if (!OpenRgbHighlighter.TryLoadProfile(profileName, out var error))
                {
                    if (error is not null) AppLog.Info(error);
                    return;
                }
            }
            catch (Exception exception)
            {
                AppLog.Error($"Could not apply idle OpenRGB profile '{profileName}'", exception);
            }
        });
    }

    private void StartConfiguredOpenRgb()
    {
        if (!OpenRgbHighlighter.ShouldStartOnApplicationLaunch(rgbSettings)) return;

        var settings = new RgbSettings
        {
            Enabled = true,
            AutoStart = true,
            StopAutoStartedOnExit = rgbSettings.StopAutoStartedOnExit
        };
        _ = Task.Run(async () =>
        {
            try
            {
                var availability = await OpenRgbHighlighter.EnsureSdkAsync(settings);
                if (availability.IsAvailable)
                {
                    AppLog.Info(availability.Message ?? "OpenRGB SDK server was already available at application launch.");
                    ClearOpenRgbWarning();
                }
                else
                {
                    var message = availability.Message ?? "OpenRGB's SDK server is unavailable.";
                    AppLog.Info($"OpenRGB could not be started at application launch: {message}");
                    ShowOpenRgbWarning(message);
                }
            }
            catch (Exception exception)
            {
                AppLog.Error("Could not start OpenRGB at application launch", exception);
                ShowOpenRgbWarning($"Could not start OpenRGB: {exception.Message}");
            }
        });
    }

    private void ShowOpenRgbWarning(string message)
    {
        if (Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (isClosing) return;
            OpenRgbWarningIndicator.ToolTip = message;
            OpenRgbWarningIndicator.Visibility = Visibility.Visible;
        });
    }

    private void ClearOpenRgbWarning()
    {
        if (Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.BeginInvoke(() => OpenRgbWarningIndicator.Visibility = Visibility.Collapsed);
    }

    private void FlashSelectedHotkey()
    {
        var action = advancedMode ? ActiveProfileAction() : null;
        var source = action is null ? rgbSettings : ResolveLighting(action);
        if (!source.Enabled) return;
        var settings = CloneLighting(source);
        settings.Enabled = true;
        var keyName = action is null ? HotkeyKeyName() : LightingKeyName(action.Settings);
        if (keyName is null) return;
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
                    _ = Dispatcher.BeginInvoke(() =>
                    {
                        rgbSettings.DeviceIndex = settings.DeviceIndex;
                        rgbSettings.DeviceName = settings.DeviceName;
                        SaveRgbSettings();
                    });
            }
            catch (Exception exception)
            {
                AppLog.Error("Could not flash newly selected hotkey", exception);
            }
        });
    }

    private void StopRgbIndicator(string indicatorId, bool applyIdleWhenLast = true)
    {
        RgbIndicatorSession? session;
        var shouldApplyIdle = false;
        lock (rgbLock)
        {
            if (!rgbIndicators.Remove(indicatorId, out session)) return;
            shouldApplyIdle = applyIdleWhenLast && rgbIndicators.Count == 0;
        }
        session.Cancellation.Cancel();
        if (!shouldApplyIdle) return;

        if (session.Task is { } task)
        {
            _ = task.ContinueWith(_ => ApplyIdleOpenRgbProfile(), TaskScheduler.Default);
            return;
        }

        ApplyIdleOpenRgbProfile();
    }

    private Task[] StopAllRgbIndicators()
    {
        RgbIndicatorSession[] sessions;
        lock (rgbLock)
        {
            sessions = rgbIndicators.Values.ToArray();
            rgbIndicators.Clear();
        }
        foreach (var session in sessions) session.Cancellation.Cancel();
        return sessions.Where(session => session.Task is not null).Select(session => session.Task!).ToArray();
    }

    private sealed class RgbIndicatorSession(CancellationTokenSource cancellation)
    {
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public Task? Task { get; set; }
    }

    private void RepeatMode_Changed(object sender, RoutedEventArgs e)
    {
        if (CountBox is null || CountRadio is null || UntilStoppedRadio is null) return;
        var hold = InputRules.IsHoldAction(Selected(TypeCombo));
        if (hold) UntilStoppedRadio.IsChecked = true;
        CountRadio.IsEnabled = !hold;
        CountBox.IsEnabled = !hold && CountRadio.IsChecked == true;
        CommitBehaviorChange(AutomationBehaviorOverride.Repeat);
    }

    private void TypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UntilStoppedRadio is null || CountRadio is null) return;
        RepeatMode_Changed(sender, e);
        UpdateLiveInputMode();
        CommitSelectedActionChange();
    }
    private void PositionMode_Changed(object sender, RoutedEventArgs e)
    {
        UpdatePositionInputEnabled();
        CommitBehaviorChange(AutomationBehaviorOverride.Position);
    }

    private void RepeatDefaultFieldChanged(object sender, TextChangedEventArgs e) => CommitBehaviorChange(AutomationBehaviorOverride.Repeat);
    private void PositionDefaultFieldChanged(object sender, TextChangedEventArgs e) => CommitBehaviorChange(AutomationBehaviorOverride.Position);

    private void PickPositionButton_Click(object sender, RoutedEventArgs e)
    {
        Status("Select a position anywhere on screen. Press Escape to cancel.", ThemeManager.Brush("WarningBrush"));
        var picker = new PositionPickerWindow { Owner = this };
        if (picker.ShowDialog() != true) return;
        ApplyPickedPosition(PositionSelection.FromPickedPoint(picker.SelectedX, picker.SelectedY));
    }

    private void ApplyPickedPosition(PositionSelection selection)
    {
        FixedPositionRadio.IsChecked = selection.FixedPosition;
        XBox.Text = selection.X.ToString();
        YBox.Text = selection.Y.ToString();
        Status($"Fixed position set to X: {selection.X}, Y: {selection.Y}.", ThemeManager.Brush("SuccessBrush"));
    }

    private void UpdatePositionInputEnabled()
    {
        if (XBox is null || YBox is null || PickPositionButton is null || FixedPositionRadio is null || PositionContent is null) return;
        var enabled = PositionContent.IsEnabled && FixedPositionRadio.IsChecked == true;
        XBox.IsEnabled = enabled;
        YBox.IsEnabled = enabled;
        PickPositionButton.IsEnabled = PositionContent.IsEnabled;
    }
    private void SaveDefaultButton_Click(object sender, RoutedEventArgs e)
    {
        var confirmation = new ConfirmationWindow(
            "Set as default",
            "Save every current option as the startup default? You can reset the app back to its original defaults from Settings.",
            "Set as default") { Owner = this };
        if (confirmation.ShowDialog() == true) SaveDefaults();
    }

    private bool ResetSettings(ResetScope scope)
    {
        if (clickCancellation is not null || profileRuns.Count > 0) { Status("Stop all active hotkeys before resetting settings.", ThemeManager.Brush("WarningBrush")); return false; }
        if (scope == ResetScope.Everything) return ResetToFactoryDefaults();
        if (SettingsScopeRules.ResetsSimple(scope)) return ResetSimpleMode();
        if (SettingsScopeRules.ResetsAdvancedProfiles(scope)) return ResetAdvancedMode();
        return ResetSharedDefaults();
    }

    private bool ResetSimpleMode()
    {
        if (!WriteDefaults(DefaultsPath, new AppDefaults())) return false;
        if (!advancedMode) ApplyDefaults(new AppDefaults());
        Status("Simple mode defaults restored.", ThemeManager.Brush("SuccessBrush"));
        return true;
    }

    private bool ResetSharedDefaults()
    {
        if (!WriteDefaults(GlobalDefaultsPath, new AppDefaults())) return false;
        if (advancedMode)
        {
            if (editingProfileDefaultsId == ActiveProfile()?.Id && ActiveProfile() is { } profile) BeginProfileDefaultsEdit(profile);
            else if (ActiveProfileAction() is { } action) ApplyDefaults(ResolveActionSettings(action));
            else ApplyDefaults(LoadSavedDefaults());
            UpdateSharedBehaviorDefaultsUi();
        }
        Status("Shared Advanced-mode defaults restored.", ThemeManager.Brush("SuccessBrush"));
        return true;
    }

    private bool ResetAdvancedMode()
    {
        if (hotkeyRegistered) { UnregisterHotKey(hwnd, HotkeyId); hotkeyRegistered = false; }
        automationProfiles = AutomationProfileStore.CreateInitial(LoadSavedDefaults());
        PersistAutomationProfiles();
        profilesDirty = false;
        unsavedProfileId = null;
        if (advancedMode) ApplyDefaults(ResolveActionSettings(ActiveProfileAction()!));
        RegisterConfiguredHotkey();
        RefreshAdvancedFooterUi();
        UpdateSharedBehaviorDefaultsUi();
        Status("Advanced profiles restored to General.", ThemeManager.Brush("SuccessBrush"));
        return true;
    }

    private static bool WriteDefaults(string path, AppDefaults settings)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, JsonSerializer.Serialize(settings)); return true; }
        catch { return false; }
    }

    private bool ResetToFactoryDefaults()
    {
        if (clickCancellation is not null || profileRuns.Count > 0)
        {
            Status($"Stop {ActivityVerb().ToLowerInvariant()} before resetting defaults.", ThemeManager.Brush("WarningBrush"));
            return false;
        }

        // Re-register after restoring F6 and its modifiers.
        if (hotkeyRegistered) { UnregisterHotKey(hwnd, HotkeyId); hotkeyRegistered = false; }
        rgbSettings = new RgbSettings();
        ApplyDefaults(new AppDefaults());
        automationProfiles = AutomationProfileStore.CreateInitial(CreateCurrentDefaults());
        PersistAutomationProfiles();
        profilesDirty = false;
        advancedMode = false;
        ThemeManager.Apply(AppTheme.Dark);
        UpdateThemeButton();
        RestoreLiveArea();
        ApplyModeUi();
        RegisterConfiguredHotkey();
        try
        {
            WriteDefaults(DefaultsPath, new AppDefaults());
            WriteDefaults(GlobalDefaultsPath, new AppDefaults());
        }
        catch { }
        SaveRgbSettings();
        CrashRecovery.UpdateEnabled(rgbSettings.CrashRecoveryEnabled);
        Topmost = false;
        compactMode = false;
        quickStartSeen = false;
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
            var path = advancedMode ? GlobalDefaultsPath : DefaultsPath;
            if (!WriteDefaults(path, settings)) throw new IOException("Could not write default settings.");
            CaptureCurrentActionToProfile();
            Status("Current settings saved as the default.", ThemeManager.Brush("SuccessBrush"));
        }
        catch { Status("Could not save the default settings.", ThemeManager.Brush("ErrorBrush")); }
    }

    private AppDefaults CreateCurrentDefaults()
    {
        var interval = NormalizeIntervalBoxes();
        return new AppDefaults { Hours = interval.Hours, Minutes = interval.Minutes, Seconds = interval.Seconds, Milliseconds = interval.Milliseconds, MouseButton = Selected(ButtonCombo), Input = Selected(ButtonCombo), CustomKey = customSpamVirtualKey, CustomSequence = customSequence.Select(step => step.Clone()).ToList(), CustomSequenceUsesGlobalInputPulse = customSequenceUsesGlobalInputPulse, ClickType = Selected(TypeCombo), RepeatUntilStopped = UntilStoppedRadio.IsChecked == true, RepeatCount = Read(CountBox, 1, 999999), FixedPosition = FixedPositionRadio.IsChecked == true, X = Read(XBox, -32768, 32767), Y = Read(YBox, -32768, 32767), InputPulseMilliseconds = inputPulseMilliseconds, InputJitterMaximumMilliseconds = inputJitterMaximumMilliseconds, TargetExecutable = TargetExecutableBox.Text.Trim(), TargetWindowTitle = targetWindowTitle, TargetWindowEnabled = EnableTargetWindowCheckBox.IsChecked == true, Hotkey = hotkey, HotkeyModifiers = hotkeyModifiers, HotkeyTrigger = hotkeyTrigger, Rgb = rgbSettings };
    }

    private AppDefaults CreateUnconfiguredActionDefaults()
    {
        var settings = CreateCurrentDefaults();
        settings.Input = "Unset";
        settings.MouseButton = "Unset";
        settings.CustomKey = 0;
        settings.CustomSequence = [];
        return settings;
    }

    private string? ExportFullBackup(BackupScope scope, string path)
    {
        try
        {
            CaptureCurrentActionToProfile();
            var simpleDefaults = WithoutRgb(ReadDefaultsFile(DefaultsPath, new AppDefaults()));
            var advancedDefaults = WithoutRgb(ReadDefaultsFile(GlobalDefaultsPath, new AppDefaults()));
            var document = new ConfigBackupDocument { Scope = scope };

            if (SettingsScopeRules.IncludesSimple(scope))
            {
                document.DefaultsJson = JsonSerializer.Serialize(simpleDefaults);
                document.SimpleDefaultsJson = document.DefaultsJson;
            }
            if (SettingsScopeRules.IncludesAdvanced(scope))
            {
                document.AdvancedDefaultsJson = JsonSerializer.Serialize(advancedDefaults);
                document.AutomationProfilesJson = JsonSerializer.Serialize(automationProfiles);
            }
            if (SettingsScopeRules.IncludesSequences(scope))
                document.SequenceLibraryJson = JsonSerializer.Serialize(new SequenceLibraryDocument { Presets = sequenceLibrary.Select(preset => preset.Clone()).ToList() });
            if (SettingsScopeRules.IncludesAppSettings(scope))
            {
                document.RgbJson = JsonSerializer.Serialize(rgbSettings);
                document.UiPreferencesJson = JsonSerializer.Serialize(CurrentUiPreferences());
                document.AppearanceJson = ThemeManager.ExportConfiguration();
            }

            ConfigBackupStore.Write(path, document);
            return null;
        }
        catch (Exception exception) { AppLog.Error($"Could not export {BackupScopeInfo.DisplayName(scope)}", exception); return $"Could not export backup: {exception.Message}"; }
    }

    private string? ImportFullBackup(BackupScope scope, string path)
    {
        if (clickCancellation is not null || profileRuns.Count > 0) return "Stop AutoClicker before restoring a backup.";
        try
        {
            var backup = ConfigBackupStore.Read(path);
            switch (scope)
            {
                case BackupScope.SimpleMode:
                    RestoreSimpleSettings(ReadSimpleDefaults(backup));
                    break;
                case BackupScope.AdvancedMode:
                    RestoreAdvancedSettings(ReadAdvancedDefaults(backup), ReadProfiles(backup, ReadAdvancedDefaults(backup)));
                    break;
                case BackupScope.CustomSequences:
                    RestoreSequenceLibrary(ReadSequenceLibrary(backup));
                    break;
                default:
                    RestoreEverything(backup);
                    break;
            }
            Status($"{BackupScopeInfo.DisplayName(scope)} restored from backup.", ThemeManager.Brush("SuccessBrush"));
            return null;
        }
        catch (Exception exception) { AppLog.Error($"Could not restore {BackupScopeInfo.DisplayName(scope)}", exception); return $"Could not restore backup: {exception.Message}"; }
    }

    private void RestoreEverything(ConfigBackupDocument backup)
    {
        var simpleDefaults = ReadSimpleDefaults(backup);
        var advancedDefaults = ReadAdvancedDefaults(backup);
        var rgb = string.IsNullOrWhiteSpace(backup.RgbJson) ? new RgbSettings() : JsonSerializer.Deserialize<RgbSettings>(backup.RgbJson) ?? throw new InvalidDataException("Backup RGB settings are invalid.");
        var ui = string.IsNullOrWhiteSpace(backup.UiPreferencesJson) ? new UiPreferences() : JsonSerializer.Deserialize<UiPreferences>(backup.UiPreferencesJson) ?? throw new InvalidDataException("Backup interface settings are invalid.");
        var library = ReadSequenceLibrary(backup);
        var profiles = ReadProfiles(backup, advancedDefaults);
        if (!string.IsNullOrWhiteSpace(backup.AppearanceJson) && !ThemeManager.TryImportConfiguration(backup.AppearanceJson)) throw new InvalidDataException("Backup appearance settings are invalid.");

        rgbSettings = rgb;
        SaveRgbSettings();
        CrashRecovery.UpdateEnabled(rgb.CrashRecoveryEnabled);
        workerPriority = WorkerPriorityRules.Normalize(ui.WorkerPriority);
        cadenceDiagnosticsEnabled = ui.CadenceDiagnosticsEnabled;
        advancedMode = ui.AdvancedMode;
        Topmost = ui.Pinned;
        compactMode = ui.CompactMode;
        quickStartSeen = ui.QuickStartSeen;
        WriteOrThrow(DefaultsPath, simpleDefaults);
        RestoreAdvancedSettings(advancedDefaults, profiles, refreshUi: false);
        RestoreSequenceLibrary(library);
        ApplyDefaults(advancedMode ? ActiveProfileAction() is { } action ? ResolveActionSettings(action) : advancedDefaults : simpleDefaults);
        UpdatePinUi();
        ApplyCompactMode();
        ApplyModeUi();
        SaveUiPreferences();
        UpdateThemeButton();
        RestoreLiveArea();
        RegisterConfiguredHotkey();
    }

    private void RestoreSimpleSettings(AppDefaults settings)
    {
        settings = WithoutRgb(settings);
        WriteOrThrow(DefaultsPath, settings);
        if (!advancedMode)
        {
            ApplyDefaults(settings);
            RegisterConfiguredHotkey();
            UpdateLiveInputMode();
        }
    }

    private void RestoreAdvancedSettings(AppDefaults sharedDefaults, AutomationProfileDocument profiles, bool refreshUi = true)
    {
        sharedDefaults = WithoutRgb(sharedDefaults);
        WriteOrThrow(GlobalDefaultsPath, sharedDefaults);
        automationProfiles = profiles.Profiles.Count == 0 ? AutomationProfileStore.CreateInitial(sharedDefaults) : profiles;
        var activeProfile = ActiveProfile() ?? automationProfiles.Profiles.First();
        var activeAction = activeProfile.Actions.FirstOrDefault(action => action.Id == automationProfiles.ActiveActionId) ?? activeProfile.Actions.FirstOrDefault();
        automationProfiles.ActiveProfileId = activeProfile.Id;
        automationProfiles.ActiveActionId = activeAction?.Id ?? string.Empty;
        unsavedProfileId = null;
        selectedAdvancedActionIds.Clear();
        PersistAutomationProfiles();
        profilesDirty = false;
        if (!advancedMode) return;
        ApplyDefaults(activeAction is null ? sharedDefaults : ResolveActionSettings(activeAction));
        RegisterConfiguredHotkey();
        if (refreshUi) { RefreshAdvancedFooterUi(); UpdateSharedBehaviorDefaultsUi(); UpdateLiveInputMode(); }
    }

    private void RestoreSequenceLibrary(List<SequencePreset> library)
    {
        sequenceLibrary = library;
        SaveSequenceLibrary();
        RefreshSequencePresetActions();
    }

    private static AppDefaults ReadSimpleDefaults(ConfigBackupDocument backup)
    {
        var json = !string.IsNullOrWhiteSpace(backup.SimpleDefaultsJson) ? backup.SimpleDefaultsJson : backup.DefaultsJson;
        return JsonSerializer.Deserialize<AppDefaults>(json) ?? throw new InvalidDataException("The backup does not contain valid Simple mode settings.");
    }

    private static AppDefaults ReadAdvancedDefaults(ConfigBackupDocument backup)
    {
        var json = !string.IsNullOrWhiteSpace(backup.AdvancedDefaultsJson) ? backup.AdvancedDefaultsJson : backup.DefaultsJson;
        return JsonSerializer.Deserialize<AppDefaults>(json) ?? throw new InvalidDataException("The backup does not contain valid Advanced mode settings.");
    }

    private static AutomationProfileDocument ReadProfiles(ConfigBackupDocument backup, AppDefaults fallback)
    {
        if (string.IsNullOrWhiteSpace(backup.AutomationProfilesJson)) throw new InvalidDataException("The backup does not contain Advanced mode profiles.");
        var document = JsonSerializer.Deserialize<AutomationProfileDocument>(backup.AutomationProfilesJson) ?? AutomationProfileStore.CreateInitial(fallback);
        AutomationProfileLimits.Enforce(document);
        return document;
    }

    private static List<SequencePreset> ReadSequenceLibrary(ConfigBackupDocument backup) =>
        string.IsNullOrWhiteSpace(backup.SequenceLibraryJson)
            ? throw new InvalidDataException("The backup does not contain custom sequences.")
            : SequenceLibraryStore.Deserialize(backup.SequenceLibraryJson);

    private static AppDefaults ReadDefaultsFile(string path, AppDefaults fallback)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<AppDefaults>(File.ReadAllText(path)) ?? fallback : fallback; }
        catch { return fallback; }
    }

    private static AppDefaults WithoutRgb(AppDefaults settings)
    {
        var copy = settings.Clone();
        copy.Rgb = null;
        return copy;
    }

    private UiPreferences CurrentUiPreferences() => new() { Pinned = Topmost, CompactMode = compactMode, QuickStartSeen = quickStartSeen, WorkerPriority = workerPriority.ToString(), CadenceDiagnosticsEnabled = cadenceDiagnosticsEnabled, AdvancedMode = advancedMode };

    private static void WriteOrThrow(string path, AppDefaults settings)
    {
        if (!WriteDefaults(path, settings)) throw new IOException("Could not write restored settings.");
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
        applyingDefaults = true;
        try
        {
            HoursBox.Text = s.Hours.ToString(); MinutesBox.Text = s.Minutes.ToString(); SecondsBox.Text = s.Seconds.ToString(); MillisBox.Text = s.Milliseconds.ToString();
            customSpamVirtualKey = s.CustomKey;
            customSequence = s.CustomSequence?.Select(step => step.Clone()).ToList() ?? [];
            customSequenceUsesGlobalInputPulse = s.CustomSequenceUsesGlobalInputPulse;
            SequenceItem.Content = "Custom sequence";
            CustomKeyItem.Content = customSpamVirtualKey != 0 ? $"Key: {FormatInputKey(customSpamVirtualKey)}" : "Custom key";
            Select(ButtonCombo, string.IsNullOrWhiteSpace(s.Input) ? s.MouseButton : s.Input); UpdateActionPlaceholder(); Select(TypeCombo, s.ClickType); UntilStoppedRadio.IsChecked = s.RepeatUntilStopped; CountRadio.IsChecked = !s.RepeatUntilStopped; CountBox.Text = s.RepeatCount.ToString();
            CurrentPositionRadio.IsChecked = !s.FixedPosition; FixedPositionRadio.IsChecked = s.FixedPosition; XBox.Text = s.X.ToString(); YBox.Text = s.Y.ToString();
            TargetExecutableBox.Text = s.TargetExecutable ?? string.Empty;
            targetWindowTitle = string.IsNullOrWhiteSpace(s.TargetWindowTitle) ? null : s.TargetWindowTitle;
            EnableTargetWindowCheckBox.IsChecked = s.TargetWindowEnabled;
            UpdateTargetWindowUi();
            inputPulseMilliseconds = InputRules.NormalizeInputPulseMilliseconds(s.InputPulseMilliseconds ?? InputRules.DefaultInputPulseMilliseconds);
            inputJitterMaximumMilliseconds = InputRules.CreateJitterMaximum(0, s.InputJitterMaximumMilliseconds);
            UpdateInputPulseButton();
            UpdateInputJitterButton();
            hotkeyTrigger = s.HotkeyTrigger;
            hotkey = hotkeyTrigger == HotkeyTrigger.Keyboard && s.Hotkey <= 0 ? System.Windows.Input.KeyInterop.VirtualKeyFromKey(System.Windows.Input.Key.F6) : s.Hotkey;
            hotkeyModifiers = s.HotkeyModifiers;
            if (s.Rgb is not null)
            {
                var idleProfileName = string.IsNullOrWhiteSpace(s.Rgb.IdleProfileName)
                    ? rgbSettings.IdleProfileName
                    : s.Rgb.IdleProfileName;
                rgbSettings = s.Rgb;
                rgbSettings.IdleProfileName = idleProfileName;
            }
            RepeatMode_Changed(this, new RoutedEventArgs());
            PositionMode_Changed(this, new RoutedEventArgs());
            UpdateHotkeyLabel();
            UpdateSharedBehaviorDefaultsUi();
        }
        finally { applyingDefaults = false; }
    }

    private void UpdateActionPlaceholder()
    {
        if (ActionPlaceholder is not null)
            ActionPlaceholder.Visibility = ButtonCombo?.SelectedItem is null ? Visibility.Visible : Visibility.Collapsed;
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
        foreach (var item in sequencePresetItems) ButtonCombo.Items.Remove(item);
        sequencePresetItems.Clear();
        EmptySequencePresetsItem.Visibility = sequenceLibrary.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var insertAt = ButtonCombo.Items.IndexOf(EditSequenceItem);
        foreach (var preset in sequenceLibrary.OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase))
        {
            var item = new ComboBoxItem { Content = $"  ↳ {preset.Name}", Tag = $"SequencePreset:{preset.Id}" };
            ButtonCombo.Items.Insert(insertAt++, item);
            sequencePresetItems.Add(item);
        }
    }

    private void ApplySequencePreset(SequencePreset preset)
    {
        customSequence = preset.Steps.Select(step => step.Clone()).ToList();
        customSequenceUsesGlobalInputPulse = preset.UseGlobalInputPulse;
        SequenceItem.Content = "Custom sequence";
        updatingActionSelection = true; ButtonCombo.SelectedItem = SequenceItem; updatingActionSelection = false;
        UpdateLiveInputMode();
        CommitSelectedActionChange();
        Status($"Ready — {preset.Name} will be repeated.", ThemeManager.Brush("SuccessBrush"));
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
        quickStartSeen = preferences.QuickStartSeen;
        workerPriority = WorkerPriorityRules.Normalize(preferences.WorkerPriority);
        cadenceDiagnosticsEnabled = preferences.CadenceDiagnosticsEnabled;
        advancedMode = preferences.AdvancedMode;
        if (!advancedMode) LoadDefaults();
        UpdatePinUi();
        ApplyCompactMode();
        ApplyModeUi();
    }

    private void SaveUiPreferences()
    {
        try { UiPreferencesStore.Save(UiPreferencesPath, new UiPreferences { Pinned = Topmost, CompactMode = compactMode, QuickStartSeen = quickStartSeen, WorkerPriority = workerPriority.ToString(), CadenceDiagnosticsEnabled = cadenceDiagnosticsEnabled, AdvancedMode = advancedMode }); }
        catch { }
    }

    private void ApplyCompactMode()
    {
        if (SettingsContent is null || SetDefaultButton is null || CollapseButton is null || CollapseGlyph is null || CollapseLabel is null) return;
        SettingsContent.Visibility = compactMode ? Visibility.Collapsed : Visibility.Visible;
        SetDefaultButton.Visibility = compactMode ? Visibility.Collapsed : Visibility.Visible;
        Height = compactMode ? (advancedMode ? 84 + FooterRow.Height.Value : CompactWindowHeight) : (advancedMode ? AdvancedExpandedWindowHeight : ExpandedWindowHeight);
        var isCompact = compactMode;
        CollapseGlyph.ContentTemplate = (DataTemplate)FindResource(isCompact ? "ExpandIcon" : "CollapseIcon");
        CollapseLabel.Text = isCompact ? "Show settings" : "Hide settings";
        CollapseButton.ToolTip = CollapseLabel.Text;
    }

    private static int Read(TextBox box, int min, int max) => InputRules.ParseClamped(box.Text, min, max);
    private static string Selected(ComboBox combo)
    {
        if (combo.SelectedItem is not ComboBoxItem item) return "Unset";
        return item.Tag?.ToString() ?? item.Content.ToString()!;
    }
    private static void Select(ComboBox combo, string value)
    {
        if (value == "Unset")
        {
            combo.SelectedItem = null;
            return;
        }
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
        RefreshStatusForeground();
    }

    private void RefreshStatusForeground()
    {
        var color = ThemeManager.Brush(statusBrushKey);
        if (StatusLabel is not null) StatusLabel.Foreground = color;
        if (AdvancedStatusLabel is not null) AdvancedStatusLabel.Foreground = color;
    }
    private void SetTaskbarIcon(bool running)
    {
        var asset = running ? "AutoClickerRunningIcon.ico" : "AutoClickerIcon.ico";
        Icon = new BitmapImage(new Uri($"pack://application:,,,/Assets/{asset}", UriKind.Absolute));
    }
    private string? HotkeyKeyName() => hotkeyTrigger == HotkeyTrigger.Keyboard ? LightingKeyName(hotkey) : null;
    private static string? LightingKeyName(AppDefaults settings) => settings.HotkeyTrigger == HotkeyTrigger.Keyboard
        ? LightingKeyName(settings.Hotkey)
        : null;

    private static string LightingKeyName(int virtualKey)
    {
        if (virtualKey >= 0x30 && virtualKey <= 0x39) return (virtualKey - 0x30).ToString();
        if (virtualKey >= 0x60 && virtualKey <= 0x69) return $"NumPad{virtualKey - 0x60}";
        return virtualKey switch
        {
            0x6A => "Multiply",
            0x6B => "Add",
            0x6D => "Subtract",
            0x6E => "Decimal",
            0x6F => "Divide",
            0x90 => "NumLock",
            _ => System.Windows.Input.KeyInterop.KeyFromVirtualKey(virtualKey).ToString()
        };
    }
    private string FormatHotkey() => FormatHotkey(hotkey, hotkeyModifiers, hotkeyTrigger);
    private static string FormatHotkey(int key, uint modifiers, HotkeyTrigger trigger = HotkeyTrigger.Keyboard) => HotkeyFormatter.Format(key, modifiers, trigger);
    private static string FormatInputKey(int virtualKey)
    {
        if (virtualKey >= 0x30 && virtualKey <= 0x39) return (virtualKey - 0x30).ToString();
        var key = System.Windows.Input.KeyInterop.KeyFromVirtualKey(virtualKey);
        return key switch { System.Windows.Input.Key.Return => "Enter", System.Windows.Input.Key.Space => "Space", _ => key.ToString() };
    }
    private static uint GetModifiers() { uint m = 0; var mods = System.Windows.Input.Keyboard.Modifiers; if (mods.HasFlag(System.Windows.Input.ModifierKeys.Control)) m |= 2; if (mods.HasFlag(System.Windows.Input.ModifierKeys.Alt)) m |= 1; if (mods.HasFlag(System.Windows.Input.ModifierKeys.Shift)) m |= 4; return m; }
    private int Status(string text, Brush color)
    {
        var revision = ++statusRevision;
        statusBrushKey = ThemeManager.StatusBrushKey(color) ?? statusBrushKey;
        if (StatusLabel is not null)
        {
            StatusLabel.Text = text;
            StatusLabel.Foreground = color;
            StatusLabel.ToolTip = text;
        }
        if (AdvancedStatusLabel is not null)
        {
            AdvancedStatusLabel.Text = text;
            AdvancedStatusLabel.Foreground = color;
            AdvancedStatusLabel.ToolTip = text;
        }
        return revision;
    }

    private void ShowOpenRgbStartedStatus()
    {
        if (!IsClicking) return;
        var revision = Status("OpenRGB started automatically.", ThemeManager.Brush("SuccessBrush"));
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            if (!Dispatcher.HasShutdownStarted)
                _ = Dispatcher.BeginInvoke(() =>
                {
                    if (IsClicking && statusRevision == revision)
                        Status($"{ActivityVerb()} — press {FormatHotkey()} to stop.", ThemeManager.Brush("ErrorBrush"));
                });
        });
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (advancedMode) CaptureCurrentActionToProfile();
        if (profilesDirty)
        {
            var dialog = new ConfirmationWindow("Unsaved profile changes", "Save this profile from the footer before quitting, or discard the unsaved changes.", "Discard and quit", destructive: true) { Owner = this };
            if (dialog.ShowDialog() != true) { e.Cancel = true; return; }
            profilesDirty = false;
        }
        // Cancel first, then give the worker a short chance to release native resources.
        isClosing = true;
        var activeTask = clickTask;
        StopClicking();
        foreach (var cancellation in profileRuns.Values) cancellation.Cancel();
        var runningProfileTasks = profileTasks.Values.ToArray();
        try { activeTask?.Wait(TimeSpan.FromSeconds(2)); } catch (Exception exception) { AppLog.Error("Error while waiting for worker shutdown", exception); }
        try { Task.WaitAll(runningProfileTasks, TimeSpan.FromSeconds(2)); } catch (Exception exception) { AppLog.Error("Error while waiting for profile worker shutdown", exception); }
        var rgbTasks = StopAllRgbIndicators();
        try { Task.WaitAll(rgbTasks, TimeSpan.FromSeconds(2)); } catch (Exception exception) { AppLog.Error("Error while restoring OpenRGB lighting", exception); }
        ApplyIdleOpenRgbProfile();
        if (rgbSettings.StopAutoStartedOnExit) OpenRgbHighlighter.StopAutoStartedServer();
        // Release UI timers and the Windows hotkey hook last.
        resetTimer.Stop(); flashTimer.Stop(); guiHeartbeatTimer.Stop(); if (hotkeyRegistered) UnregisterHotKey(hwnd, HotkeyId); foreach (var id in additionalHotkeys.Keys) UnregisterHotKey(hwnd, id); mouseHotkeys.Clear(); UpdateMouseHook(); if (hwndSource is not null) hwndSource.RemoveHook(WndProc);
    }

    private static Input[] CreateClickInputs(string button)
    {
        var flags = button switch { "Right" => (MouseFlags.RightDown, MouseFlags.RightUp), "Middle" => (MouseFlags.MiddleDown, MouseFlags.MiddleUp), _ => (MouseFlags.LeftDown, MouseFlags.LeftUp) };
        return [new() { Type = 0, Data = new InputUnion { Mouse = new MouseInput { Flags = flags.Item1 } } }, new() { Type = 0, Data = new InputUnion { Mouse = new MouseInput { Flags = flags.Item2 } } }];
    }
    private static Input[] CreateKeyInputs(int virtualKey)
    {
        var scanCode = MapVirtualKey((uint)virtualKey, 0);
        var flags = (IsExtendedKey(virtualKey) ? KeyboardFlags.ExtendedKey : KeyboardFlags.None) | (scanCode != 0 ? KeyboardFlags.ScanCode : KeyboardFlags.None);
        var key = scanCode != 0 ? (ushort)0 : (ushort)virtualKey;
        var scan = (ushort)scanCode;
        return
        [
            new() { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = key, ScanCode = scan, Flags = flags } } },
            new() { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = key, ScanCode = scan, Flags = flags | KeyboardFlags.KeyUp } } }
        ];
    }
    // Build native input packets once per run.
    private static SequenceAction[] BuildSequence(IEnumerable<SequenceStep> sequence) => sequence.Select(step =>
    {
        if (step.Input == "Delay") return new SequenceAction([], false, true, Math.Clamp(step.DelayAfterMilliseconds, 1, 600000));
        var key = step.Input switch { "Space" => 0x20, "Enter" => 0x0D, "Custom" => step.CustomKey, _ => 0 };
        return new SequenceAction(key == 0 ? CreateClickInputs(step.Input) : CreateKeyInputs(key), key == 0, false, Math.Clamp(step.DelayAfterMilliseconds, 0, 600000));
    }).ToArray();
    private static bool IsExtendedKey(int virtualKey) => virtualKey is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E or 0x5B or 0x5C or 0x5D or 0xA3 or 0xA5 or 0x6F;
    private bool SendAction(Input[] inputs, bool doubleClick, int pulseMilliseconds, PrecisionTimer timer, CancellationTokenSource cancellation, ref bool watchdogExpired)
    {
        if (InputRules.NormalizeInputPulseMilliseconds(pulseMilliseconds) == 0)
        {
            SendNativeInput((uint)inputs.Length, inputs);
            if (doubleClick)
            {
                SendNativeInput((uint)inputs.Length, inputs);
            }
            return true;
        }

        var pressCount = doubleClick ? 2 : 1;
        var pulseTicks = InputRules.NormalizeInputPulseMilliseconds(pulseMilliseconds) * Stopwatch.Frequency / 1000d;
        for (var press = 0; press < pressCount; press++)
        {
            SendNativeInput(1, [inputs[0]]);
            try
            {
                if (!WaitUntilGuiIsHealthy(timer, Stopwatch.GetTimestamp() + pulseTicks, cancellation, ref watchdogExpired)) return false;
            }
            finally
            {
                SendNativeInput(1, [inputs[1]]);
            }
        }
        return true;
    }

    private bool SendActionWithDiagnostics(Input[] inputs, bool doubleClick, int pulseMilliseconds, PrecisionTimer timer, CancellationTokenSource cancellation, ref bool watchdogExpired, CadenceDiagnostics cadence, double scheduledTimestamp)
    {
        if (InputRules.NormalizeInputPulseMilliseconds(pulseMilliseconds) == 0)
        {
            cadence.RecordDown(scheduledTimestamp);
            SendNativeInput((uint)inputs.Length, inputs);
            if (doubleClick)
            {
                cadence.RecordDown(scheduledTimestamp);
                SendNativeInput((uint)inputs.Length, inputs);
            }
            return true;
        }

        var pressCount = doubleClick ? 2 : 1;
        var pulseTicks = InputRules.NormalizeInputPulseMilliseconds(pulseMilliseconds) * Stopwatch.Frequency / 1000d;
        for (var press = 0; press < pressCount; press++)
        {
            cadence.RecordDown(scheduledTimestamp);
            SendNativeInput(1, [inputs[0]]);
            try
            {
                if (!WaitUntilGuiIsHealthy(timer, Stopwatch.GetTimestamp() + pulseTicks, cancellation, ref watchdogExpired)) return false;
            }
            finally
            {
                cadence.RecordUp();
                SendNativeInput(1, [inputs[1]]);
            }
        }
        return true;
    }

    private static bool CanSendAction(ClickSettings settings, bool isMouse) =>
        !settings.Target.IsEnabled ||
        (WindowTargeting.IsForeground(settings.Target) &&
         (!settings.FixedPosition || !isMouse || WindowTargeting.IsPointInForegroundClientArea(settings.X, settings.Y)));

    private sealed record ClickSettings(bool FixedPosition, int X, int Y, string Button, int? KeyboardVirtualKey, bool DoubleClick, bool Hold, int? MaximumClicks, SequenceAction[]? Sequence, int InputPulseMilliseconds, long JitterMaximumMilliseconds, WorkerPriorityOption WorkerPriority, bool CadenceDiagnosticsEnabled, TargetWindowRule Target);
    private sealed record SequenceAction(Input[] Inputs, bool IsMouse, bool IsDelay, int DelayAfterMilliseconds);
    private sealed class CadenceDiagnostics(double intervalTicks, int pulseMilliseconds)
    {
        private readonly double intervalMilliseconds = intervalTicks * 1000d / Stopwatch.Frequency;
        private readonly double pulseMilliseconds = InputRules.NormalizeInputPulseMilliseconds(pulseMilliseconds);
        private long dispatchCount;
        private double previousDownTimestamp;
        private double lastDownTimestamp;
        private double totalIntervalDeviationMilliseconds;
        private double maximumIntervalDeviationMilliseconds;
        private double totalLatenessMilliseconds;
        private double maximumLatenessMilliseconds;
        private double totalPulseDeviationMilliseconds;
        private double maximumPulseDeviationMilliseconds;
        private long pulseCount;

        public void RecordDown(double scheduledTimestamp)
        {
            var now = (double)Stopwatch.GetTimestamp();
            if (dispatchCount > 0)
            {
                var intervalDeviation = Math.Abs((now - previousDownTimestamp) * 1000d / Stopwatch.Frequency - intervalMilliseconds);
                totalIntervalDeviationMilliseconds += intervalDeviation;
                maximumIntervalDeviationMilliseconds = Math.Max(maximumIntervalDeviationMilliseconds, intervalDeviation);
            }

            var lateness = Math.Max(0, (now - scheduledTimestamp) * 1000d / Stopwatch.Frequency);
            totalLatenessMilliseconds += lateness;
            maximumLatenessMilliseconds = Math.Max(maximumLatenessMilliseconds, lateness);
            previousDownTimestamp = now;
            lastDownTimestamp = now;
            dispatchCount++;
        }

        public void RecordUp()
        {
            if (lastDownTimestamp == 0 || pulseMilliseconds == 0) return;
            var actualPulseMilliseconds = ((double)Stopwatch.GetTimestamp() - lastDownTimestamp) * 1000d / Stopwatch.Frequency;
            var pulseDeviation = Math.Abs(actualPulseMilliseconds - pulseMilliseconds);
            totalPulseDeviationMilliseconds += pulseDeviation;
            maximumPulseDeviationMilliseconds = Math.Max(maximumPulseDeviationMilliseconds, pulseDeviation);
            pulseCount++;
        }

        public void LogSummary()
        {
            if (dispatchCount == 0) return;
            var intervals = Math.Max(1, dispatchCount - 1);
            var pulseSummary = pulseCount == 0
                ? "Pulse=Off"
                : $"PulseTargetMs={pulseMilliseconds:0.###} | PulseAvgDeviationMs={totalPulseDeviationMilliseconds / pulseCount:0.###} | PulseMaxDeviationMs={maximumPulseDeviationMilliseconds:0.###}";
            AppLog.Info($"Cadence diagnostics | Dispatches={dispatchCount} | IntervalTargetMs={intervalMilliseconds:0.###} | IntervalAvgDeviationMs={totalIntervalDeviationMilliseconds / intervals:0.###} | IntervalMaxDeviationMs={maximumIntervalDeviationMilliseconds:0.###} | WakeAvgLateMs={totalLatenessMilliseconds / dispatchCount:0.###} | WakeMaxLateMs={maximumLatenessMilliseconds:0.###} | {pulseSummary}");
        }
    }
    // Cancellable native timer for the worker loop.
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
            // Relative due time in 100-nanosecond units.
            var dueTime = -Math.Max(1L, (long)Math.Ceiling(remainingTicks * 10_000_000 / Stopwatch.Frequency));
            if (!SetWaitableTimer(handle, ref dueTime, 0, nint.Zero, nint.Zero, false)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var handles = new[] { handle, token.WaitHandle.SafeWaitHandle.DangerousGetHandle() };
            if (WaitForMultipleObjects(2, handles, false, uint.MaxValue) == WaitObject0 + 1) throw new OperationCanceledException(token);
        }

        public void Dispose() => CloseHandle(handle);
    }

    private const int MouseHookId = 14;
    private const int WmMiddleButtonDown = 0x0207;
    private const int WmXButtonDown = 0x020B;
    private const int WmMouseWheel = 0x020A;
    private const int WmMouseHorizontalWheel = 0x020E;
    private const uint LowLevelMouseInjected = 0x00000001;

    private delegate nint LowLevelMouseProc(int code, nint wParam, nint lParam);
    private readonly record struct MouseHotkey(HotkeyTrigger Trigger, uint Modifiers);

    [StructLayout(LayoutKind.Sequential)]
    private struct LowLevelMouseData
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public nint ExtraInfo;
    }

    [Flags] private enum MouseFlags : uint { LeftDown = 2, LeftUp = 4, RightDown = 8, RightUp = 16, MiddleDown = 32, MiddleUp = 64 }
    [Flags] private enum KeyboardFlags : uint { None = 0, ExtendedKey = 1, KeyUp = 2, ScanCode = 8 }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int Dx, Dy; public uint MouseData; public MouseFlags Flags; public uint Time; public nint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort VirtualKey; public ushort ScanCode; public KeyboardFlags Flags; public uint Time; public nint ExtraInfo; }

    private static uint SendNativeInput(uint count, Input[] inputs)
    {
        lock (nativeInputLock)
            return SendInput(count, inputs, Marshal.SizeOf<Input>());
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hWnd, int id);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int idHook, LowLevelMouseProc callback, nint module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? moduleName);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWaitableTimerEx(nint attributes, string? name, uint flags, uint desiredAccess);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWaitableTimer(nint attributes, bool manualReset, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetWaitableTimer(nint timer, ref long dueTime, int period, nint completionRoutine, nint argument, bool resume);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForMultipleObjects(uint count, nint[] handles, bool waitAll, uint milliseconds);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
}

// A small immutable view of a configured action for the Advanced-mode footer.
public sealed class AdvancedActionTile
{
    public AdvancedActionTile(AutomationAction action, bool isRunning, bool removalPending, bool isSelected, bool isManagementLocked, bool isMultiSelection = false, bool showInlineActionControls = true, bool hotkeyCapturePending = false)
    {
        Action = action;
        IsRunning = isRunning;
        RemovalPending = removalPending;
        HotkeyCapturePending = hotkeyCapturePending;
        IsSelected = isSelected;
        IsManagementLocked = isManagementLocked;
        IsMultiSelection = isMultiSelection;
        ShowInlineActionControls = showInlineActionControls;
    }

    public AutomationAction Action { get; }
    public bool IsRunning { get; }
    public bool RemovalPending { get; }
    public bool HotkeyCapturePending { get; }
    public bool IsSelected { get; }
    public bool IsManagementLocked { get; }
    public bool IsMultiSelection { get; }
    public bool ShowInlineActionControls { get; }
    public bool CanEdit => !IsManagementLocked;
    public bool CanStart => !IsRunning && !IsManagementLocked && InputRules.IsConfiguredAction(
        string.IsNullOrWhiteSpace(Action.Settings.Input) ? Action.Settings.MouseButton : Action.Settings.Input,
        Action.Settings.CustomKey,
        Action.Settings.CustomSequence?.Count ?? 0);
    public bool CanStop => IsRunning;
    public Visibility InlineActionControlsVisibility => ShowInlineActionControls ? Visibility.Visible : Visibility.Collapsed;
    public int ActionLabelColumnSpan => ShowInlineActionControls ? 1 : 3;
    public bool HotkeyEnabled => Action.HotkeyEnabled;
    public string HotkeyLabel => HotkeyCapturePending ? "Waiting..." : HotkeyFormatter.Format(Action.Settings.Hotkey, Action.Settings.HotkeyModifiers, Action.Settings.HotkeyTrigger);
    public string HotkeyTooltip => Action.HotkeyEnabled ? $"Hotkey: {HotkeyLabel}" : $"Hotkey disabled: {HotkeyLabel}";
    public string ActionLabel => Action.DisplayName[(Action.DisplayName.IndexOf('·') + 1)..].Trim();
    public Visibility RemoveButtonVisibility => RemovalPending || IsMultiSelection ? Visibility.Collapsed : Visibility.Visible;
    public Visibility RemovalConfirmationVisibility => RemovalPending && !IsMultiSelection ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BehaviorBadgeVisibility => !RemovalPending && Action.ActiveBehaviorOverrides != AutomationBehaviorOverride.None ? Visibility.Visible : Visibility.Collapsed;
    public string BehaviorBadge => Action.ActiveBehaviorOverrides == AutomationBehaviorOverride.None || RemovalPending ? string.Empty : "OVR";
    public string BehaviorTooltip => Action.ActiveBehaviorOverrides == AutomationBehaviorOverride.None
        ? "Uses the shared repeat, position, target, jitter, and pulse defaults."
        : Action.ActiveBehaviorOverrides == AutomationBehaviorOverride.All
            ? "OVR: this hotkey overrides the shared repeat, position, target, jitter, and pulse defaults."
            : "OVR: this hotkey overrides some shared behavior settings.";
}

public sealed class AdvancedProfileTile
{
    public AdvancedProfileTile(AutomationProfile profile, bool isSelected, bool hasUnsavedChanges) { Profile = profile; IsSelected = isSelected; HasUnsavedChanges = hasUnsavedChanges; }
    public AutomationProfile Profile { get; }
    public bool IsSelected { get; }
    public bool HasUnsavedChanges { get; }
    public Visibility UnsavedIndicatorVisibility => HasUnsavedChanges ? Visibility.Visible : Visibility.Collapsed;
    public string Name => Profile.Name;
    public string Tooltip => $"Switch to profile: {Profile.Name}";
}
