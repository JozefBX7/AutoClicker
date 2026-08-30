// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.Runtime.InteropServices;
using System.Text.Json;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;

namespace AutoClicker;

public partial class MainWindow : Window
{
    private const int HotkeyId = 0xC11C;
    private const int WmHotkey = 0x0312;
    private const int WmEnable = 0x000A;
    private const uint HotkeyNoRepeat = 0x4000;
    private static readonly string SimpleDefaultsPath = AppPaths.ConfigFile(ConfigurationFileNames.SimpleDefaults);
    private static readonly string AdvancedSharedDefaultsPath = AppPaths.ConfigFile(ConfigurationFileNames.AdvancedSharedDefaults);
    private static readonly string RgbSettingsPath = AppPaths.ConfigFile(ConfigurationFileNames.RgbSettings);
    private static readonly string SequenceLibraryPath = AppPaths.ConfigFile(ConfigurationFileNames.SequenceLibrary);
    private static readonly string AutomationProfilesPath = AppPaths.ConfigFile(ConfigurationFileNames.AutomationProfiles);
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
    private readonly List<int> primaryHotkeyIds = [];
    private readonly Dictionary<int, RegisteredHotkeyTarget> registeredProfileHotkeys = [];
    private readonly Dictionary<MouseHotkey, RegisteredHotkeyTarget> mouseHotkeys = [];
    private AutomationAction? pendingActionDrag;
    private Point actionDragStart;
    private Border? actionDragTarget;
    private readonly LowLevelMouseProc mouseHookCallback;
    private nint mouseHook;
    private readonly Dictionary<string, CancellationTokenSource> profileRuns = [];
    private readonly Dictionary<string, Task> profileTasks = [];
    private readonly HashSet<CancellationTokenSource> heldTriggerMonitors = [];
    private volatile bool capturingHotkey;
    private AutomationAction? enableToggleHotkeyCaptureAction;
    private string? pendingNewActionId;
    private volatile bool capturingSpamKey;
    private bool updatingActionSelection;
    private ComboBoxItem? actionBeforeKeyCapture;
    private int customSpamVirtualKey;
    private List<SequenceStep> customSequence = [];
    private List<SequencePreset> sequenceLibrary = [];
    private readonly List<ComboBoxItem> sequencePresetItems = [];
    private volatile bool settingsOpen;
    private volatile bool automationOwnerEnabled = true;
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
    private string statusBrushKey = ThemeResourceKeys.SuccessBrush;
    private bool compactMode;
    private bool quickStartSeen;
    private string? targetWindowTitle;
    private int inputPulseMilliseconds = InputRules.DefaultInputPulseMilliseconds;
    private long inputJitterMaximumMilliseconds;
    private bool customSequenceUsesGlobalInputPulse = true;
    private bool profilesDirty;
    private bool applyingDefaults;
    private string savedProfileConfiguration = string.Empty;
    private string? unsavedProfileId;
    private string? pendingRemovalActionId;
    private WorkerPriorityOption workerPriority = WorkerPriorityOption.Normal;
    private bool cadenceDiagnosticsEnabled;
    private bool crashRecoveryEnabled = true;
    private bool keyboardHotkeyModifiersEnabled;
    private bool rememberPinned = true;
    private bool applyPinnedOnLaunch = true;
    private bool pinnedPreference;
    private bool deferredPinPending;
    private WindowPixelPosition? lastNormalWindowPosition;
    private AutomationProfileDocument automationProfiles = new();
    private bool advancedMode;
    private readonly SettingsEditorSession editorSession = new();

    private enum RegisteredHotkeyPurpose { RunAction, ToggleEnabled }
    private sealed record RegisteredHotkeyTarget(AutomationAction? Action, RegisteredHotkeyPurpose Purpose);

    public MainWindow()
    {
        // Keep the delegate alive for the entire window lifetime; Windows calls it outside normal WPF input routing.
        mouseHookCallback = MouseHookProc;
        InitializeComponent();
        LoadSequenceLibrary();
        RefreshSequencePresetActions();
        LoadSimpleDefaults();
        LoadAutomationProfiles();
        UpdateInputPulseButton();
        UpdateInputJitterButton();
        LoadRgbSettings();
        LoadApplicationPreferences();
        UpdateHotkeyLabel();
        UpdateThemeButton();
        UpdateLiveInputMode();
        resetTimer.Tick += (_, _) => ResetCounterWhenIdle();
        flashTimer.Tick += (_, _) => RestoreLiveArea();
        guiHeartbeatTimer.Tick += (_, _) => Volatile.Write(ref lastGuiHeartbeat, Stopwatch.GetTimestamp());
        Volatile.Write(ref lastGuiHeartbeat, Stopwatch.GetTimestamp());
        resetTimer.Start();
        guiHeartbeatTimer.Start();
        if (!AppRuntime.IsEndToEndTest)
        {
            Loaded += (_, _) => _ = Dispatcher.BeginInvoke(ShowQuickStart, DispatcherPriority.ContextIdle);
            Loaded += (_, _) => StartConfiguredOpenRgb();
        }
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

    private SettingsEditorStorageTarget CurrentEditorStorageTarget() =>
        editorSession.StorageTarget(advancedMode, ActiveProfile()?.Id, automationProfiles.ActiveActionId);

    private string InputTimingScopeDescription() => CurrentEditorStorageTarget() switch
    {
        SettingsEditorStorageTarget.GlobalDefaults => "global Advanced defaults",
        SettingsEditorStorageTarget.ProfileDefaults => $"{ActiveProfile()?.Name ?? "current"} profile defaults",
        SettingsEditorStorageTarget.HotkeyOverride => $"{FormatHotkey()} hotkey override",
        _ => "Simple mode settings"
    };

    private void CommitInputTimingChange(string settingName)
    {
        switch (CurrentEditorStorageTarget())
        {
            case SettingsEditorStorageTarget.GlobalDefaults:
            {
                var defaults = LoadSavedDefaults();
                defaults.InputPulseMilliseconds = inputPulseMilliseconds;
                defaults.InputJitterMaximumMilliseconds = inputJitterMaximumMilliseconds;
                if (WriteDefaults(AdvancedSharedDefaultsPath, defaults))
                    Status($"{settingName} saved to global Advanced defaults.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
                else
                    Status($"Could not save the global {settingName.ToLowerInvariant()} default.", ThemeManager.Brush(ThemeResourceKeys.ErrorBrush));
                break;
            }
            case SettingsEditorStorageTarget.ProfileDefaults:
                MarkProfileDefaultsEdited();
                Status($"{settingName} updated for {ActiveProfile()?.Name ?? "this"} profile defaults - save the profile when ready.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
                break;
            case SettingsEditorStorageTarget.HotkeyOverride:
                CaptureCurrentActionToProfile();
                Status($"{settingName} updated for the {FormatHotkey()} hotkey override.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
                break;
            default:
                Status($"{settingName} updated for Simple mode.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
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
            ? targetWindowTitle is null ? "Input runs only while any active window from this executable is focused." : $"{targetWindowTitle} - {TargetExecutableBox.Text}"
            : "Input is sent to whichever window is active.";
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        hwnd = new WindowInteropHelper(this).Handle;
        RestoreMainWindowPosition();
        hwndSource = HwndSource.FromHwnd(hwnd);
        hwndSource?.AddHook(WndProc);
        RegisterConfiguredHotkey();
    }

    private void Window_LocationChanged(object? sender, EventArgs e) => CaptureNormalWindowPosition();

    private void RestoreMainWindowPosition()
    {
        if (lastNormalWindowPosition is not { } saved || !TryGetWindowBounds(out var currentBounds)) return;
        var savedBounds = currentBounds with { Left = saved.Left, Top = saved.Top };
        var restored = WindowPlacementRules.RestoreToVisibleWorkArea(savedBounds, CurrentWorkAreas());
        SetWindowPosition(restored);
        lastNormalWindowPosition = restored;
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (!TryGetWindowBounds(out var actualBounds)) return;
            var visible = WindowPlacementRules.RestoreToVisibleWorkArea(actualBounds, CurrentWorkAreas());
            SetWindowPosition(visible);
            lastNormalWindowPosition = visible;
        }, DispatcherPriority.Loaded);
    }

    private void CaptureNormalWindowPosition()
    {
        if (hwnd == 0 || WindowState != WindowState.Normal || !TryGetWindowBounds(out var bounds)) return;
        lastNormalWindowPosition = new WindowPixelPosition(bounds.Left, bounds.Top);
    }

    private bool TryGetWindowBounds(out WindowPixelBounds bounds)
        => WindowPlacementPlatform.TryGetBounds(hwnd, out bounds);

    private void SetWindowPosition(WindowPixelPosition position) =>
        WindowPlacementPlatform.Move(hwnd, position);

    private static IReadOnlyList<WindowWorkArea> CurrentWorkAreas() =>
        WindowPlacementPlatform.CurrentWorkAreas();

    private nint WndProc(nint handle, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmEnable)
        {
            automationOwnerEnabled = wParam != 0;
            if (!automationOwnerEnabled) StopAutomationForModalContext();
            return 0;
        }
        // Ignore the global hotkey during key capture.
        if (!capturingHotkey && !capturingSpamKey && msg == WmHotkey && primaryHotkeyIds.Contains(wParam.ToInt32()))
        {
            if (hotkeyTrigger == HotkeyTrigger.Keyboard
                && !IsKeyboardModifierMatch(keyboardHotkeyModifiersEnabled, hotkeyModifiers, HotkeyMessageModifiers(lParam))) return 0;
            ActivateHotkey(advancedMode ? ActiveProfileAction() : null, hotkey, hotkeyModifiers, HotkeyTrigger.Keyboard);
            handled = true;
        }
        else if (!capturingHotkey && !capturingSpamKey && msg == WmHotkey && registeredProfileHotkeys.TryGetValue(wParam.ToInt32(), out var target))
        {
            var binding = target.Purpose == RegisteredHotkeyPurpose.ToggleEnabled
                ? target.Action?.EnableToggleHotkey
                : target.Action is null ? null : AutomationHotkeyBindingRules.RunBinding(target.Action);
            if (binding is null || !IsKeyboardModifierMatch(keyboardHotkeyModifiersEnabled, binding.Modifiers, HotkeyMessageModifiers(lParam))) return 0;
            HandleRegisteredHotkey(target, binding);
            handled = true;
        }
        return 0;
    }

    private bool CanExecuteAutomation() => AutomationExecutionGuard.CanExecute(
        automationOwnerEnabled,
        isClosing,
        settingsOpen,
        capturingHotkey,
        capturingSpamKey);

    private void StopAutomationForModalContext()
    {
        if (!IsClicking) return;

        // A modal owner boundary is also a fail-safe boundary: no existing worker should continue behind an
        // editor, picker, confirmation, or system file dialog that prevents access to the main controls.
        if (clickCancellation is not null) StopClicking();
        foreach (var actionId in profileRuns.Keys.ToList()) StopProfileAction(actionId);
        Status("Automation stopped while a dialog is open.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
    }

    internal static bool IsKeyboardModifierMatch(bool modifiersEnabled, uint configuredModifiers, uint messageModifiers)
    {
        if (!modifiersEnabled) return true;
        const uint supported = 0x1 | 0x2 | 0x4;
        return (messageModifiers & supported) == (configuredModifiers & supported);
    }

    private static uint HotkeyMessageModifiers(nint lParam) => (uint)(lParam.ToInt64() & 0xffff);

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (advancedMode && !IsClicking)
        {
            CommitPendingIntervalBeforeEditorTransition(editorTransition: true);
            ShowAdvancedSharedDefaults(announce: true);
        }
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
        if (deferredPinPending)
        {
            deferredPinPending = false;
            Topmost = true;
        }
        else
        {
            Topmost = !Topmost;
        }
        pinnedPreference = Topmost;
        UpdatePinUi();
        SaveApplicationPreferences();
    }

    private void Window_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        ApplyDeferredPinAfterInteraction(e.OriginalSource as DependencyObject);

    private void ApplyDeferredPinAfterInteraction(DependencyObject? source)
    {
        if (!deferredPinPending || (source is not null && IsWithin(source, PinButton))) return;
        deferredPinPending = false;
        Topmost = true;
        pinnedPreference = true;
        UpdatePinUi();
    }

    private void CollapseButton_Click(object sender, RoutedEventArgs e)
    {
        compactMode = !compactMode;
        ApplyCompactMode();
        SaveApplicationPreferences();
    }

    private void UpdatePinUi()
    {
        PinButton.Tag = Topmost ? "Pinned" : null;
        PinButton.ToolTip = Topmost ? "Always on top - click to unpin" : "Keep on top";
    }

    private void ShowQuickStart()
    {
        if (quickStartSeen || isClosing) return;
        var dialog = new QuickStartWindow { Owner = this };
        dialog.ShowDialog();
        quickStartSeen = true;
        SaveApplicationPreferences();
    }
    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (clickCancellation is not null || profileRuns.Count > 0)
        {
            Status($"Stop {ActivityVerb().ToLowerInvariant()} before opening Settings.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
            return;
        }
        if (settingsOpen) return;
        settingsOpen = true;
        var dialog = new SettingsWindow(rgbSettings, CurrentApplicationPreferences(), FormatHotkey(), HotkeyKeyName(), ResetSettings, ExportFullBackup, ImportFullBackup) { Owner = this };
        try
        {
            if (dialog.ShowDialog() == true)
            {
                var savedPreferences = dialog.ApplicationPreferences;
                var modifiersSettingChanged = keyboardHotkeyModifiersEnabled != savedPreferences.KeyboardHotkeyModifiersEnabled;
                rgbSettings = dialog.RgbSettings;
                workerPriority = WorkerPriorityRules.Normalize(savedPreferences.WorkerPriority);
                cadenceDiagnosticsEnabled = savedPreferences.CadenceDiagnosticsEnabled;
                crashRecoveryEnabled = savedPreferences.CrashRecoveryEnabled;
                keyboardHotkeyModifiersEnabled = savedPreferences.KeyboardHotkeyModifiersEnabled;
                rememberPinned = savedPreferences.RememberPinned;
                applyPinnedOnLaunch = savedPreferences.ApplyPinnedOnLaunch;
                pinnedPreference = Topmost;
                deferredPinPending = false;
                if (savedPreferences.AdvancedMode != advancedMode) SetAdvancedMode(savedPreferences.AdvancedMode);
                if (modifiersSettingChanged) RegisterConfiguredHotkey();
                SaveRgbSettings();
                SaveApplicationPreferences();
                CrashRecovery.UpdateEnabled(crashRecoveryEnabled);
                if (!rgbSettings.Enabled) ClearOpenRgbWarning();
                Status(rgbSettings.Enabled ? "OpenRGB hotkey lighting enabled." : "OpenRGB hotkey lighting disabled.", rgbSettings.Enabled ? ThemeManager.Brush(ThemeResourceKeys.SuccessBrush) : ThemeManager.Brush(ThemeResourceKeys.TextMutedBrush));
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
        SuspendHotkeysForCapture();
        if (!advancedMode)
        {
            button.Content = "Cancel";
            button.ContentTemplate = (DataTemplate)FindResource("HotkeyCancelIcon");
            button.Width = 31;
            button.Padding = new Thickness(0);
        }
        button.ToolTip = "Keep the current hotkey";
        Status(advancedMode ? "Press a key combination or supported mouse input, or Escape to keep the current hotkey." : "Press a key combination or supported mouse input, or click Cancel to keep the current hotkey.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
        Focus();
    }

    private void BeginEnableToggleHotkeyCapture(AutomationAction action)
    {
        if (capturingHotkey || profileRuns.Count > 0) return;
        enableToggleHotkeyCaptureAction = action;
        SuspendHotkeysForCapture();
        Status($"Press the key combination or supported mouse input that will enable or disable {HotkeyFormatter.Format(action.Settings.Hotkey, action.Settings.HotkeyModifiers, action.Settings.HotkeyTrigger)}, or Escape to cancel.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
        Focus();
    }

    private void SuspendHotkeysForCapture()
    {
        if (hotkeyRegistered) UnregisterPrimaryHotkeys();
        foreach (var registeredId in registeredProfileHotkeys.Keys.ToList()) UnregisterHotKey(hwnd, registeredId);
        registeredProfileHotkeys.Clear();
        mouseHotkeys.Clear();
        capturingHotkey = true;
        UpdateMouseHook();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        ApplyDeferredPinAfterInteraction(e.OriginalSource as DependencyObject);
        if (TrySubmitEditorTextField(e)) return;

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
                Status($"{FormatInputKey(virtualKey)} is also the start/stop hotkey. Choose another key or change the hotkey first.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
                return;
            }
            customSpamVirtualKey = virtualKey;
            CustomKeyItem.Content = $"Key: {FormatInputKey(virtualKey)}";
            capturingSpamKey = false;
            Select(ButtonCombo, AutomationInputIds.Custom);
            if (!CommitSelectedActionChange()) ShowReadyActionStatus();
            Status($"Ready - {FormatInputKey(virtualKey)} will be repeated.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
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
            Status($"{FormatHotkey(candidate, modifiers)} is already assigned in this profile.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
            return;
        }
        CompleteCapturedHotkey(candidate, modifiers, HotkeyTrigger.Keyboard);
    }

    private bool TrySubmitEditorTextField(System.Windows.Input.KeyEventArgs e)
    {
        var field = GetEditorTextFieldKind(System.Windows.Input.Keyboard.FocusedElement as TextBox);
        var inputCapturePending = capturingSpamKey || capturingHotkey;
        if (!SettingsEditorPolicy.ShouldSubmitTextField(field, e.Key == System.Windows.Input.Key.Enter, inputCapturePending)) return false;

        switch (field)
        {
            case SettingsEditorTextFieldKind.Interval:
                CommitIntervalChange();
                break;
            case SettingsEditorTextFieldKind.RepeatCount:
                CountBox.Text = Read(CountBox, 1, 999999).ToString();
                CommitBehaviorChange(AutomationBehaviorOverride.Repeat);
                break;
            case SettingsEditorTextFieldKind.CursorPosition:
                XBox.Text = Read(XBox, -32768, 32767).ToString();
                YBox.Text = Read(YBox, -32768, 32767).ToString();
                CommitBehaviorChange(AutomationBehaviorOverride.Position);
                break;
            case SettingsEditorTextFieldKind.TargetWindow:
                TargetExecutableBox.Text = TargetExecutableBox.Text.Trim();
                CommitBehaviorChange(AutomationBehaviorOverride.TargetWindow);
                break;
        }

        e.Handled = true;
        System.Windows.Input.Keyboard.ClearFocus();
        return true;
    }

    private SettingsEditorTextFieldKind GetEditorTextFieldKind(TextBox? textBox)
    {
        if (IsIntervalTextBox(textBox)) return SettingsEditorTextFieldKind.Interval;
        if (textBox == CountBox) return SettingsEditorTextFieldKind.RepeatCount;
        if (textBox == XBox || textBox == YBox) return SettingsEditorTextFieldKind.CursorPosition;
        if (textBox == TargetExecutableBox) return SettingsEditorTextFieldKind.TargetWindow;
        return SettingsEditorTextFieldKind.None;
    }

    private void CompleteCapturedHotkey(int virtualKey, uint modifiers, HotkeyTrigger trigger)
    {
        if (IsProfileHotkeyAlreadyAssigned(virtualKey, modifiers, trigger))
        {
            CancelHotkeyCapture(keepStatus: true);
            Status($"{FormatHotkey(virtualKey, modifiers, trigger)} is already assigned in this profile.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
            return;
        }
        var toggleTarget = enableToggleHotkeyCaptureAction;
        if (toggleTarget is not null)
        {
            toggleTarget.EnableToggleHotkey = new AutomationHotkeyBinding { VirtualKey = virtualKey, Modifiers = modifiers, Trigger = trigger };
            MarkProfilesDirty();
        }
        else
        {
            hotkey = virtualKey;
            hotkeyModifiers = modifiers;
            hotkeyTrigger = trigger;
            CaptureCurrentActionToProfile();
            pendingNewActionId = null;
            UpdateHotkeyLabel();
        }
        RefreshAdvancedFooterUi();
        var registered = CancelHotkeyCapture(keepStatus: true);
        if (registered)
        {
            if (toggleTarget is not null)
                Status($"{FormatHotkey(virtualKey, modifiers, trigger)} will enable or disable {HotkeyFormatter.Format(toggleTarget.Settings.Hotkey, toggleTarget.Settings.HotkeyModifiers, toggleTarget.Settings.HotkeyTrigger)}.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
            else
            {
                Status($"Ready - press {FormatHotkey()} to start or stop.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
                FlashSelectedHotkey();
            }
        }
        else
            Status($"{FormatHotkey(virtualKey, modifiers, trigger)} is in use - choose another key.", ThemeManager.Brush(ThemeResourceKeys.ErrorBrush));
    }

    private bool IsProfileHotkeyAlreadyAssigned(int candidate, uint modifiers, HotkeyTrigger trigger)
    {
        if (!advancedMode || ActiveProfile() is not { } profile) return false;
        var target = enableToggleHotkeyCaptureAction ?? ActiveProfileAction();
        if (target is null) return false;
        return AutomationHotkeyBindingRules.IsAssigned(
            profile,
            new AutomationHotkeyBinding { VirtualKey = candidate, Modifiers = modifiers, Trigger = trigger },
            keyboardHotkeyModifiersEnabled,
            target.Id,
            enableToggleHotkeyCaptureAction is null ? AutomationHotkeyAssignmentKind.RunAction : AutomationHotkeyAssignmentKind.ToggleEnabled);
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
        var returnToSharedDefaults = !IsClicking && !clickedProfileButton
            && (ShouldReturnToSharedDefaults(advancedMode, IsWithinAdvancedActionTile(source), withinFooter)
                || ShouldReturnFromEditorDeadSpace(advancedMode, editorDeadSpace));
        CommitPendingIntervalBeforeEditorTransition(returnToSharedDefaults);
        if (returnToSharedDefaults)
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

    private bool IsIntervalTextBox(TextBox? textBox) =>
        textBox == HoursBox || textBox == MinutesBox || textBox == SecondsBox || textBox == MillisBox;

    private void CommitPendingIntervalBeforeEditorTransition(bool editorTransition)
    {
        var intervalHasKeyboardFocus = IsIntervalTextBox(System.Windows.Input.Keyboard.FocusedElement as TextBox);
        if (!SettingsEditorPolicy.ShouldCommitAndReleasePendingIntervalBeforeTransition(intervalHasKeyboardFocus, editorTransition)) return;

        CommitIntervalChange();
        // PreviewMouseDown changes the editor before WPF's normal focus transfer. Explicitly release focus so
        // backdrop clicks have the same commit-and-blur behavior as moving to another input.
        System.Windows.Input.Keyboard.ClearFocus();
    }

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
        var target = CurrentEditorStorageTarget();
        if (target == SettingsEditorStorageTarget.SimpleDefaults)
        {
            SaveDefaults();
            return;
        }
        if (target == SettingsEditorStorageTarget.ProfileDefaults && ActiveProfile() is { } profile)
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
        if (target == SettingsEditorStorageTarget.HotkeyOverride && ActiveProfileAction() is { } action)
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
        WriteDefaults(AdvancedSharedDefaultsPath, defaults);
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

    private bool CancelHotkeyCapture(bool keepStatus = false)
    {
        var wasEnableToggleCapture = enableToggleHotkeyCaptureAction is not null;
        enableToggleHotkeyCaptureAction = null;
        var button = ActiveHotkeyButton();
        capturingHotkey = false;
        if (!wasEnableToggleCapture)
        {
            button.Content = "Edit";
            button.ContentTemplate = (DataTemplate)FindResource("HotkeyEditIcon");
            button.Width = 31;
            button.Padding = new Thickness(0);
            button.ToolTip = "Change hotkey";
        }
        if (pendingNewActionId is { } pendingActionId)
        {
            pendingNewActionId = null;
            AbandonPendingNewAction(pendingActionId);
            if (!keepStatus) Status("New hotkey was not added.", ThemeManager.Brush(ThemeResourceKeys.TextMutedBrush));
            return false;
        }
        var registered = !(advancedMode || !hotkeyRegistered) || RegisterConfiguredHotkey();
        if (!keepStatus)
            Status(wasEnableToggleCapture ? "Enable-toggle hotkey unchanged." : $"Ready - press {FormatHotkey()} to start or stop.", ThemeManager.Brush(wasEnableToggleCapture ? ThemeResourceKeys.TextMutedBrush : ThemeResourceKeys.SuccessBrush));
        return registered;
    }

    private void AbandonPendingNewAction(string actionId)
    {
        var profile = ActiveProfile();
        var action = profile?.Actions.FirstOrDefault(item => item.Id == actionId);
        if (profile is not null && action is not null)
        {
            profile.Actions.Remove(action);
            automationProfiles.ActiveActionId = profile.Actions.FirstOrDefault()?.Id ?? string.Empty;
            editorSession.RemoveHotkey(actionId);
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
        if (selectedAction == AutomationInputIds.Sequence)
        {
            if (!InputRules.IsWhileHeldAction(Selected(TypeCombo))) Select(TypeCombo, AutomationActionTypeIds.Single);
            SequenceItem.Content = AutomationInputLabels.CustomSequence;
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
            if (customSequence.Count >= 2) { SequenceItem.Content = AutomationInputLabels.CustomSequence; updatingActionSelection = true; ButtonCombo.SelectedItem = SequenceItem; updatingActionSelection = false; }
            else if (previous is not null) { updatingActionSelection = true; ButtonCombo.SelectedItem = previous; updatingActionSelection = false; }
            UpdateLiveInputMode();
            if (!accepted || !CommitSelectedActionChange()) ShowReadyActionStatus();
            return;
        }
        if (InputRules.IsInstantaneousMouseAction(selectedAction) && InputRules.IsHoldAction(Selected(TypeCombo)))
            Select(TypeCombo, AutomationActionTypeIds.Single);
        if (Selected(ButtonCombo) != AutomationInputIds.Custom)
        {
            UpdateLiveInputMode();
            if (!CommitSelectedActionChange()) ShowReadyActionStatus();
            return;
        }

        actionBeforeKeyCapture = e.RemovedItems.OfType<ComboBoxItem>().FirstOrDefault();
        capturingSpamKey = true;
        Status("Press the key to repeat, or Escape to cancel.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
        Focus();
    }

    private void CancelSpamKeyCapture()
    {
        capturingSpamKey = false;
        updatingActionSelection = true;
        ButtonCombo.SelectedItem = actionBeforeKeyCapture ?? ButtonCombo.Items.OfType<ComboBoxItem>().First();
        updatingActionSelection = false;
        Status("Key selection cancelled.", ThemeManager.Brush(ThemeResourceKeys.TextMutedBrush));
    }

    // Input selection is the one part of a hotkey that always belongs to the tile, even when it uses shared behavior defaults.
    private bool CommitSelectedActionChange()
    {
        if (applyingDefaults || !advancedMode || !IsEditingAdvancedAction() || ActiveProfileAction() is not { } action) return false;
        action.Settings = CreateCurrentDefaults();
        MarkProfilesDirty();
        UpdateActionEditorHint();
        Status($"Updated {FormatHotkey()} - {action.ActionDescription}.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
        return true;
    }

    private bool RegisterConfiguredHotkey()
    {
        if (hwnd == 0) return false;
        if (AppRuntime.IsEndToEndTest && !AppRuntime.RegisterEndToEndKeyboardHotkeys)
        {
            primaryHotkeyIds.Clear();
            primaryHotkeyIds.Add(HotkeyId);
            var testAction = ActiveProfileAction();
            if (advancedMode && testAction is not null)
            {
                hotkey = testAction.Settings.Hotkey;
                hotkeyModifiers = testAction.Settings.HotkeyModifiers;
                hotkeyTrigger = testAction.Settings.HotkeyTrigger;
            }
            hotkeyRegistered = false;
            return true;
        }
        if (hotkeyRegistered) UnregisterPrimaryHotkeys();
        foreach (var registeredId in registeredProfileHotkeys.Keys.ToList()) UnregisterHotKey(hwnd, registeredId);
        registeredProfileHotkeys.Clear();
        primaryHotkeyIds.Clear();
        mouseHotkeys.Clear();
        var activeAction = ActiveProfileAction();
        if (advancedMode)
        {
            hotkey = activeAction?.Settings.Hotkey ?? 0;
            hotkeyModifiers = activeAction?.Settings.HotkeyModifiers ?? 0;
            hotkeyTrigger = activeAction?.Settings.HotkeyTrigger ?? HotkeyTrigger.Keyboard;
        }
        var registerActiveHotkey = !advancedMode || activeAction?.HotkeyEnabled == true;
        var activeRegistrationSucceeded = !registerActiveHotkey;
        if (registerActiveHotkey && hotkeyTrigger == HotkeyTrigger.Keyboard && hotkey > 0)
        {
            var nextId = RegisterKeyboardHotkeyVariants(HotkeyId, hotkeyModifiers, hotkey, target: null, trackAsPrimary: true);
            hotkeyRegistered = primaryHotkeyIds.Count > 0;
            activeRegistrationSucceeded = hotkeyRegistered;
            if (!hotkeyRegistered) Status($"{FormatHotkey()} is in use - choose another key.", ThemeManager.Brush(ThemeResourceKeys.ErrorBrush));
            if (nextId > HotkeyId + 1) registeredProfileHotkeys.Clear();
        }
        else hotkeyRegistered = false;
        if (registerActiveHotkey && hotkeyTrigger != HotkeyTrigger.Keyboard)
        {
            var binding = new MouseHotkey(hotkeyTrigger, hotkeyModifiers);
            RegisterMouseHotkey(binding, new RegisteredHotkeyTarget(advancedMode ? activeAction : null, RegisteredHotkeyPurpose.RunAction));
            activeRegistrationSucceeded = mouseHotkeys.ContainsKey(binding);
        }
        if (!advancedMode) { UpdateMouseHook(); return activeRegistrationSucceeded; }
        var profile = ActiveProfile();
        if (profile is null) { UpdateMouseHook(); return activeRegistrationSucceeded; }
        var additionalId = Math.Max(HotkeyId + 1, (primaryHotkeyIds.Count == 0 ? HotkeyId + 1 : primaryHotkeyIds.Max() + 1));
        foreach (var action in profile.Actions.Where(action => action.HotkeyEnabled && action.Id != automationProfiles.ActiveActionId && HotkeyFormatter.IsConfigured(action.Settings.Hotkey, action.Settings.HotkeyTrigger)))
        {
            if (action.Settings.HotkeyTrigger == hotkeyTrigger && action.Settings.Hotkey == hotkey && NormalizeKeyboardHotkeyModifiers(action.Settings.HotkeyModifiers) == NormalizeKeyboardHotkeyModifiers(hotkeyModifiers)) continue;
            if (action.Settings.HotkeyTrigger != HotkeyTrigger.Keyboard)
            {
                RegisterMouseHotkey(new MouseHotkey(action.Settings.HotkeyTrigger, action.Settings.HotkeyModifiers), new RegisteredHotkeyTarget(action, RegisteredHotkeyPurpose.RunAction));
                continue;
            }
            additionalId = RegisterKeyboardHotkeyVariants(additionalId, action.Settings.HotkeyModifiers, action.Settings.Hotkey, new RegisteredHotkeyTarget(action, RegisteredHotkeyPurpose.RunAction), trackAsPrimary: false);
        }
        foreach (var action in profile.Actions.Where(action => action.EnableToggleHotkey?.IsConfigured == true))
        {
            var binding = action.EnableToggleHotkey!;
            var target = new RegisteredHotkeyTarget(action, RegisteredHotkeyPurpose.ToggleEnabled);
            if (binding.Trigger != HotkeyTrigger.Keyboard)
            {
                RegisterMouseHotkey(new MouseHotkey(binding.Trigger, binding.Modifiers), target);
                continue;
            }
            additionalId = RegisterKeyboardHotkeyVariants(additionalId, binding.Modifiers, binding.VirtualKey, target, trackAsPrimary: false);
        }
        UpdateMouseHook();
        return activeRegistrationSucceeded;
    }

    private int RegisterKeyboardHotkeyVariants(int startId, uint modifiers, int virtualKey, RegisteredHotkeyTarget? target, bool trackAsPrimary)
    {
        var id = startId;
        foreach (var variantModifiers in KeyboardHotkeyModifierVariants(modifiers))
        {
            var registered = RegisterHotKey(hwnd, id, WindowsHotkeyRegistrationModifiers(variantModifiers), (uint)virtualKey);
            AppRuntime.RecordEndToEndEvent(
                "hotkey-registration",
                $"id={id};key={virtualKey};modifiers={variantModifiers};action={target?.Action?.Id ?? "primary"};success={registered};purpose={target?.Purpose.ToString() ?? RegisteredHotkeyPurpose.RunAction.ToString()}");
            if (registered)
            {
                if (trackAsPrimary) primaryHotkeyIds.Add(id);
                else if (target is not null) registeredProfileHotkeys[id] = target;
            }
            else AppLog.Info($"Could not register {(target is null ? "active" : target.Purpose == RegisteredHotkeyPurpose.ToggleEnabled ? "enable-toggle" : "profile")} hotkey variant {HotkeyFormatter.Format(virtualKey, variantModifiers)}.");
            id++;
        }
        return id;
    }

    private IEnumerable<uint> KeyboardHotkeyModifierVariants(uint configuredModifiers) =>
        KeyboardHotkeyModifierVariants(keyboardHotkeyModifiersEnabled, configuredModifiers);

    internal static IEnumerable<uint> KeyboardHotkeyModifierVariants(bool modifiersEnabled, uint configuredModifiers)
    {
        if (modifiersEnabled)
        {
            yield return configuredModifiers;
            yield break;
        }

        const uint alt = 0x1;
        const uint control = 0x2;
        const uint shift = 0x4;
        var requiredAlt = configuredModifiers & alt;
        yield return requiredAlt;
        yield return requiredAlt | control;
        yield return requiredAlt | shift;
        yield return requiredAlt | control | shift;
    }

    internal static uint WindowsHotkeyRegistrationModifiers(uint configuredModifiers) => configuredModifiers | HotkeyNoRepeat;

    private void UnregisterPrimaryHotkeys()
    {
        foreach (var id in primaryHotkeyIds)
            UnregisterHotKey(hwnd, id);
        primaryHotkeyIds.Clear();
        hotkeyRegistered = false;
    }

    private uint NormalizeKeyboardHotkeyModifiers(uint modifiers)
    {
        if (keyboardHotkeyModifiersEnabled) return modifiers;
        const uint alt = 0x1;
        return modifiers & alt;
    }

    private void RegisterMouseHotkey(MouseHotkey hotkeyBinding, RegisteredHotkeyTarget target)
    {
        if (!mouseHotkeys.TryAdd(hotkeyBinding, target))
            AppLog.Info($"Could not register duplicate mouse hotkey {HotkeyFormatter.Format(0, hotkeyBinding.Modifiers, hotkeyBinding.Trigger)}.");
    }

    // A low-level hook is only kept while a mouse binding exists or the capture prompt is open.
    // Its callback only matches gestures and returns immediately; all UI and worker work is queued to WPF.
    private void UpdateMouseHook()
    {
        if (AppRuntime.IsEndToEndTest) return;
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
        if (!CanExecuteAutomation() || !mouseHotkeys.TryGetValue(binding, out var target))
            return CallNextHookEx(mouseHook, code, wParam, lParam);

        _ = Dispatcher.BeginInvoke(() =>
        {
            if (!CanExecuteAutomation()) return;
            HandleRegisteredHotkey(target, new AutomationHotkeyBinding { Modifiers = binding.Modifiers, Trigger = binding.Trigger });
        });
        // A mouse hotkey behaves like a keyboard hotkey: it is reserved for AutoClicker, not forwarded to the target app.
        return 1;
    }

    private void HandleRegisteredHotkey(RegisteredHotkeyTarget target, AutomationHotkeyBinding binding)
    {
        if (target.Purpose == RegisteredHotkeyPurpose.ToggleEnabled)
        {
            if (target.Action is not null) SetHotkeysEnabled([target.Action], !target.Action.HotkeyEnabled);
            return;
        }
        ActivateHotkey(target.Action, binding.VirtualKey, binding.Modifiers, binding.Trigger);
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
        automationProfiles = AutomationProfileStore.Load(AutomationProfilesPath, LoadSavedDefaults());
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
            AutomationProfileStore.Save(AutomationProfilesPath, automationProfiles);
            savedProfileConfiguration = AutomationProfileConfiguration.Fingerprint(automationProfiles);
            profilesDirty = false;
        }
        catch (Exception exception) { AppLog.Error("Could not save automation profiles", exception); }
    }

    // Active/recent profile navigation is useful state, but changing it must not silently save an edited profile.
    private void PersistProfileNavigation()
    {
        if (profilesDirty) return;
        try { AutomationProfileStore.Save(AutomationProfilesPath, automationProfiles); }
        catch (Exception exception) { AppLog.Error("Could not save selected automation profile", exception); }
    }

    private void SaveAutomationProfiles() => MarkProfilesDirty();

    private void MarkProfilesDirty()
    {
        profilesDirty = SettingsEditorDirtyState.IsProfileDocumentDirty(
            automationProfiles, savedProfileConfiguration, ActiveProfile()?.Id, unsavedProfileId);
        RefreshAdvancedFooterUi();
    }

    private void CaptureCurrentActionToProfile()
    {
        var target = CurrentEditorStorageTarget();
        if (target == SettingsEditorStorageTarget.ProfileDefaults) { CaptureProfileDefaults(); return; }
        if (target != SettingsEditorStorageTarget.HotkeyOverride) return;
        var action = ActiveProfileAction();
        if (action is null) return;
        var settings = CreateCurrentDefaults();
        var current = ResolveActionSettings(action);
        if (JsonSerializer.Serialize(current) == JsonSerializer.Serialize(settings)) return;
        action.Settings = settings;
        MarkProfilesDirty();
    }

    private bool CaptureProfileDefaults()
    {
        if (!advancedMode || !IsEditingProfileDefaults() || ActiveProfile() is not { } profile) return false;
        var changed = SettingsEditorProfileDraft.Capture(profile, CreateCurrentDefaults(), LoadSavedDefaults());
        // Some editor exits (notably a backdrop PreviewMouseDown) happen before an interval TextBox loses focus.
        // Capturing must therefore participate in dirty tracking instead of relying on the control event to do it.
        if (changed) MarkProfilesDirty();
        return changed;
    }

    private void MarkProfileDefaultsEdited()
    {
        if (!IsEditingProfileDefaults()) return;
        // Keep the profile model current as the editor changes, so the save cue reflects real differences only.
        // Capture marks model changes itself; an equivalent edit still needs one recalculation to clear a prior change.
        if (!CaptureProfileDefaults()) MarkProfilesDirty();
    }

    private bool IsEditingProfileDefaults() => advancedMode && editorSession.IsEditingProfile(ActiveProfile()?.Id);

    private bool IsEditingAdvancedAction() => advancedMode && editorSession.IsEditingHotkey(automationProfiles.ActiveActionId);

    private IReadOnlyList<AutomationAction> SelectedAdvancedActions()
    {
        var profile = ActiveProfile();
        return profile?.Actions.Where(action => editorSession.SelectedActionIds.Contains(action.Id)).ToList() ?? [];
    }

    // Shared defaults deliberately leave the registered hotkey alone: they configure behavior, not an assignment.
    private void ShowAdvancedSharedDefaults(bool clearSelection = true, bool announce = false)
    {
        if (!advancedMode) return;
        CaptureProfileDefaults();
        editorSession.EnterSharedDefaults(clearSelection);
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
        if (announce) Status("Ready - editing shared defaults.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
    }

    private AppDefaults ResolveActionSettings(AutomationAction action)
    {
        return AutomationBehaviorSettingsResolver.Resolve(LoadSavedDefaults(), ActiveProfile(), action);
    }

    private static AppDefaults LoadSavedDefaults()
    {
        try
        {
            return File.Exists(AdvancedSharedDefaultsPath)
                ? JsonSerializer.Deserialize<AppDefaults>(File.ReadAllText(AdvancedSharedDefaultsPath)) ?? new AppDefaults()
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
                AutomationBehaviorSettingsResolver.RevertActionBehaviorToInherited(LoadSavedDefaults(), ActiveProfile(), action, reverted);
            }
            RefreshAdvancedEditorAfterActionChange();
            MarkProfilesDirty();
            var detail = reverted == AutomationBehaviorOverride.All ? "all behavior settings" : DescribeBehaviorOverrides(reverted);
            Status($"Shared defaults restored for {detail}.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
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
        Status(targets.Count == 1 ? "This hotkey now has its own behavior settings." : $"{targets.Count} hotkeys now have their own behavior settings.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
    }

    private void SharedBehaviorSurface_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag } || !Enum.TryParse<AutomationBehaviorOverride>(tag, out var aspect)) return;
        if (IsClicking || !advancedMode) return;
        if (aspect == AutomationBehaviorOverride.Position && IsKeyboardInputSelected())
        {
            Status("Position settings apply to mouse actions only.", ThemeManager.Brush(ThemeResourceKeys.TextMutedBrush));
            e.Handled = true;
            return;
        }
        if (aspect == AutomationBehaviorOverride.Interval && IsEditingAdvancedAction() && ActiveProfileAction() is { } activeAction && InputRules.IsHoldAction(activeAction.Settings.ClickType))
        {
            Status("Hold hotkeys do not use an interval.", ThemeManager.Brush(ThemeResourceKeys.TextMutedBrush));
            e.Handled = true;
            return;
        }

        if (IsEditingProfileDefaults() && ActiveProfile() is { } profile && profile.UsesSharedBehavior(aspect))
        {
            var local = profile.BehaviorDefaults?.Clone() ?? LoadSavedDefaults();
            CopyBehaviorOverride(CreateCurrentDefaults(), local, aspect);
            profile.BehaviorDefaults = local;
            profile.UsesSharedBehaviorDefaults = true;
            profile.BehaviorOverrides |= aspect;
            RefreshAdvancedFooterUi();
            UpdateSharedBehaviorDefaultsUi();
            MarkProfilesDirty();
            Status($"This profile now uses its own {DescribeBehaviorOverrides(aspect)} settings.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
            e.Handled = true;
            return;
        }

        if (!IsEditingAdvancedAction() || ActiveProfileAction() is not { } action || !action.UsesSharedBehavior(aspect)) return;

        CopyBehaviorOverride(CreateCurrentDefaults(), action.Settings, aspect);
        action.UsesSharedBehaviorDefaults = true;
        action.BehaviorOverrides |= aspect;
        RefreshAdvancedEditorAfterActionChange();
        MarkProfilesDirty();
        Status($"This hotkey now uses its own {DescribeBehaviorOverrides(aspect)} settings.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
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
        var editingProfileDefaults = IsEditingProfileDefaults();
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
        AutomationBehaviorOverride.Interval => IntervalSharedOverlay,
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
        Status(message, ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
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
            action.LightingOverride = CloneLighting(dialog.RgbSettings);
        }
        MarkProfilesDirty();
        RefreshAdvancedEditorAfterActionChange();
        Status(targets.Count == 1 ? "Lighting override saved for this hotkey." : "Lighting override saved for the selected hotkeys.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
    }

    private void RefreshAdvancedEditorAfterActionChange()
    {
        if (IsEditingAdvancedAction() && ActiveProfileAction() is { } action) ApplyDefaults(ResolveActionSettings(action));
        else ShowAdvancedSharedDefaults(clearSelection: false);
    }

    private void NewProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (clickCancellation is not null || profileRuns.Count > 0) { Status("Stop active hotkeys before creating a profile.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush)); return; }
        var replacingDraft = ActiveProfile()?.Id == unsavedProfileId;
        if (replacingDraft) DiscardActiveDraft();
        else if (!ResolveUnsavedProfileChanges("creating a new profile")) return;
        var profile = new AutomationProfile { Name = "Unsaved" };
        automationProfiles.Profiles.Add(profile);
        automationProfiles.ActiveProfileId = profile.Id;
        automationProfiles.ActiveActionId = string.Empty;
        unsavedProfileId = profile.Id;
        TouchRecentProfile(profile.Id);
        ShowAdvancedSharedDefaults();
        MarkProfilesDirty();
        RegisterConfiguredHotkey();
        Status("New profile - add a hotkey when you are ready.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
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
                var dialog = new ProfileNameWindow("Save profile", "Give this profile a name before saving it.", AutomationProfileNames.New) { Owner = this };
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
        Status($"{profile.Name} saved.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
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
            editorSession.EnterSharedDefaults();
            DiscardActiveDraft();
            return true;
        }

        // Leave profile/hotkey edit scope before replacing the document. The restored profile deliberately keeps
        // the same ID, so a stale profile scope would otherwise capture the old controls back into the fresh model.
        editorSession.EnterSharedDefaults(clearSelection: false);
        // Reloading from the atomic store restores the current profile exactly as it was last saved.
        automationProfiles = AutomationProfileStore.Load(AutomationProfilesPath, CreateCurrentDefaults());
        unsavedProfileId = null;
        savedProfileConfiguration = AutomationProfileConfiguration.Fingerprint(automationProfiles);
        profilesDirty = false;
        return true;
    }

    private void UpdateSharedBehaviorDefaultsUi()
    {
        if (RepeatCard is null || RepeatContent is null || PositionCard is null || PositionContent is null || TargetWindowCard is null || TargetWindowContent is null) return;
        var locked = IsClicking;
        var editingProfileDefaults = IsEditingProfileDefaults();
        var editingSharedDefaults = advancedMode && !IsEditingAdvancedAction();
        var profile = ActiveProfile();
        var action = advancedMode && IsEditingAdvancedAction() ? ActiveProfileAction() : null;
        var holdingHotkey = action is not null && InputRules.IsHoldAction(action.Settings.ClickType);
        // Hold actions run continuously until stopped, so an interval is neither used nor configurable for them.
        var sharedInterval = !holdingHotkey && (editingProfileDefaults ? profile?.UsesSharedBehavior(AutomationBehaviorOverride.Interval) == true : action?.UsesSharedBehavior(AutomationBehaviorOverride.Interval) == true);
        var sharedRepeat = editingProfileDefaults ? profile?.UsesSharedBehavior(AutomationBehaviorOverride.Repeat) == true : action?.UsesSharedBehavior(AutomationBehaviorOverride.Repeat) == true;
        var sharedPosition = editingProfileDefaults ? profile?.UsesSharedBehavior(AutomationBehaviorOverride.Position) == true : action?.UsesSharedBehavior(AutomationBehaviorOverride.Position) == true;
        var sharedTarget = editingProfileDefaults ? profile?.UsesSharedBehavior(AutomationBehaviorOverride.TargetWindow) == true : action?.UsesSharedBehavior(AutomationBehaviorOverride.TargetWindow) == true;
        var sharedJitter = editingProfileDefaults ? profile?.UsesSharedBehavior(AutomationBehaviorOverride.InputJitter) == true : action?.UsesSharedBehavior(AutomationBehaviorOverride.InputJitter) == true;
        var sharedPulse = editingProfileDefaults ? profile?.UsesSharedBehavior(AutomationBehaviorOverride.InputPulse) == true : action?.UsesSharedBehavior(AutomationBehaviorOverride.InputPulse) == true;
        var positionAvailable = editingSharedDefaults || (!IsKeyboardInputSelected() && Selected(ButtonCombo) != AutomationInputIds.Sequence);
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
        UpdateSharedBehaviorSurface(IntervalCard, sharedInterval, "interval", overrideScope);
        if (holdingHotkey)
        {
            IntervalCard.ToolTip = "Interval is unavailable for Hold actions.";
            IntervalCard.Cursor = System.Windows.Input.Cursors.Arrow;
        }
        UpdateSharedBehaviorSurface(RepeatCard, sharedRepeat, "repeat", overrideScope);
        UpdateSharedBehaviorSurface(PositionCard, sharedPosition, "position", overrideScope);
        UpdateSharedBehaviorSurface(TargetWindowCard, sharedTarget, "target window", overrideScope);
        IntervalSharedOverlayLabel.Text = holdingHotkey
            ? "Interval is unavailable\nfor Hold actions"
            : $"Click to override interval for this {overrideScope}";
        RepeatSharedOverlayLabel.Text = $"Click to override repeat for this {overrideScope}";
        PositionSharedOverlayLabel.Text = $"Click to override position for this {overrideScope}";
        TargetWindowSharedOverlayLabel.Text = $"Click to override target window for this {overrideScope}";
        UpdateSharedBehaviorSurface(InputJitterOverrideHost, sharedJitter, "input jitter", overrideScope);
        UpdateSharedBehaviorSurface(InputPulseOverrideHost, sharedPulse, "input pulse", overrideScope);
        IntervalSharedOverlay.Visibility = sharedInterval || holdingHotkey ? Visibility.Visible : Visibility.Collapsed;
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
        if (IsEditingProfileDefaults())
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
            Status("Stop all active hotkeys before changing profiles.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
            return;
        }
        var menu = new ContextMenu { PlacementTarget = ManageProfilesButton };
        var import = new MenuItem { Header = "Import profile…" };
        AutomationProperties.SetAutomationId(import, "ImportProfile");
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
        var fileName = AppRuntime.SaveFilePathOverride;
        if (fileName is null)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = $"Export {AppIdentity.Name} profile",
                Filter = $"{AppIdentity.Name} profile (*{ConfigurationFileExtensions.Profile})|*{ConfigurationFileExtensions.Profile}",
                FileName = SafeProfileFileName(profile.Name) + ConfigurationFileExtensions.Profile,
                DefaultExt = ConfigurationFileExtensions.Profile,
                AddExtension = true
            };
            if (dialog.ShowDialog(this) != true) return;
            fileName = dialog.FileName;
        }
        try
        {
            ProfileTransferStore.Save(fileName, profile);
            Status($"{profile.Name} exported.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
        }
        catch (Exception exception)
        {
            AppLog.Error($"Could not export profile '{profile.Name}'", exception);
            Status("Could not export that profile. See the log for details.", ThemeManager.Brush(ThemeResourceKeys.ErrorBrush));
        }
    }

    private void ImportProfile_Click(object? sender, RoutedEventArgs e)
    {
        if (profilesDirty)
        {
            Status("Save or discard current profile changes before importing a profile.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
            return;
        }
        var fileName = AppRuntime.OpenFilePathOverride;
        if (fileName is null)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = $"Import {AppIdentity.Name} profile",
                Filter = $"{AppIdentity.Name} profiles (*{ConfigurationFileExtensions.Profile})|*{ConfigurationFileExtensions.Profile}|Other JSON files (*.json)|*.json",
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != true) return;
            fileName = dialog.FileName;
        }
        try
        {
            var profile = ProfileTransferStore.Load(fileName);
            profile.Name = UniqueProfileName(profile.Name);
            automationProfiles.Profiles.Add(profile);
            automationProfiles.ActiveProfileId = profile.Id;
            automationProfiles.ActiveActionId = profile.Actions.FirstOrDefault()?.Id ?? string.Empty;
            TouchRecentProfile(profile.Id);
            if (profile.Actions.FirstOrDefault() is { } action)
            {
                editorSession.EnterHotkey(action.Id);
                ApplyDefaults(ResolveActionSettings(action));
            }
            else ShowAdvancedSharedDefaults();
            PersistAutomationProfiles();
            RegisterConfiguredHotkey();
            RefreshAdvancedFooterUi();
            Status($"{profile.Name} imported.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
        }
        catch (Exception exception)
        {
            AppLog.Error("Could not import profile", exception);
            Status("Could not import that profile. See the log for details.", ThemeManager.Brush(ThemeResourceKeys.ErrorBrush));
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
            Status("Stop all active hotkeys before changing modes.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
            return;
        }
        if (hotkeyRegistered) UnregisterPrimaryHotkeys();
        if (!enabled)
        {
            CaptureCurrentActionToProfile();
            LoadSimpleDefaults();
        }
        advancedMode = enabled;
        ApplyModeUi();
        RegisterConfiguredHotkey();
        SaveApplicationPreferences();
        Status(enabled ? "Advanced mode enabled - use the footer to manage hotkeys." : "Simple mode enabled.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
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
        ModeButton.Content = advancedMode ? AppModeIds.Advanced : "S";
        ModeButton.ToolTip = advancedMode ? "Switch to Simple mode" : "Switch to Advanced profiles";
        if (!advancedMode) editorSession.EnterSimple();
        else if (editorSession.Scope.Kind == SettingsEditorScopeKind.Simple) ShowAdvancedSharedDefaults(clearSelection: false);
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
        var multiSelection = editorSession.SelectedActionCount > 1;
        var showInlineActionControls = (profile?.Actions.Count ?? 0) < AutomationProfileLimits.HideInlineActionControlsAt;
        AdvancedActionsFooterList.ItemsSource = profile?.Actions.Select(action => new AdvancedActionTile(action, profileRuns.ContainsKey(action.Id), action.Id == pendingRemovalActionId, editorSession.SelectedActionIds.Contains(action.Id), profileRuns.Count > 0, multiSelection, showInlineActionControls, hotkeyCapturePending: action.Id == pendingNewActionId)).ToList();
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
        if (editProfileDefaults) BeginProfileDefaultsEdit(profile);
        else ShowAdvancedSharedDefaults();
        if (hotkeyRegistered) UnregisterPrimaryHotkeys();
        RegisterConfiguredHotkey();
        RefreshAdvancedFooterUi();
        if (!editProfileDefaults) Status($"{profile.Name} selected - shared defaults are ready.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
    }

    private void SelectAdvancedAction(AutomationAction action, bool startHotkeyCapture = false)
    {
        if (IsEditingProfileDefaults())
        {
            CaptureProfileDefaults();
            editorSession.EnterSharedDefaults();
        }
        if (action.Id != automationProfiles.ActiveActionId || !IsEditingAdvancedAction())
        {
            CaptureCurrentActionToProfile();
            automationProfiles.ActiveActionId = action.Id;
            ApplyDefaults(ResolveActionSettings(action));
            if (hotkeyRegistered) UnregisterPrimaryHotkeys();
            RegisterConfiguredHotkey();
        }
        editorSession.EnterHotkey(action.Id);
        RefreshAdvancedFooterUi();
        UpdateSharedBehaviorDefaultsUi();
        UpdateActionEditorHint();
        Status($"Editing {action.DisplayName}.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
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
        editorSession.EnterProfileDefaults(profile.Id);
        ApplyDefaults(AutomationBehaviorSettingsResolver.ResolveProfileDefaults(LoadSavedDefaults(), profile));
        RefreshAdvancedFooterUi();
        UpdateSharedBehaviorDefaultsUi();
        Status($"Editing {profile.Name} profile defaults - save the profile when ready.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
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
            Status("This profile already uses the global Advanced defaults.", ThemeManager.Brush(ThemeResourceKeys.TextMutedBrush));
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
            Status("This profile already uses the app defaults.", ThemeManager.Brush(ThemeResourceKeys.TextMutedBrush));
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
        Status($"Profile now uses app defaults for {detail}.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
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
        profile.LightingDefaults = CloneLighting(dialog.RgbSettings);
        MarkProfilesDirty();
        Status($"Profile lighting saved for {profile.Name}.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
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
            Status("This profile already uses the app lighting defaults.", ThemeManager.Brush(ThemeResourceKeys.TextMutedBrush));
            return;
        }
        profile.LightingDefaults = null;
        MarkProfilesDirty();
        Status($"Profile lighting reset to app defaults for {profile.Name}.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
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
        switch (CurrentEditorStorageTarget())
        {
            case SettingsEditorStorageTarget.SimpleDefaults:
            case SettingsEditorStorageTarget.ProfileDefaults:
                MarkProfileDefaultsEdited();
                return;
            case SettingsEditorStorageTarget.HotkeyOverride:
                CaptureCurrentActionToProfile();
                return;
        }

        var defaults = LoadSavedDefaults();
        var updated = defaults.Clone();
        CopyBehaviorOverride(CreateCurrentDefaults(), updated, aspect);
        if (JsonSerializer.Serialize(defaults) == JsonSerializer.Serialize(updated)) return;
        if (!WriteDefaults(AdvancedSharedDefaultsPath, updated))
            Status("Could not save the global Advanced default.", ThemeManager.Brush(ThemeResourceKeys.ErrorBrush));
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
        var previousScope = editorSession.Scope;
        if (!DiscardUnsavedProfileChanges()) return;
        RestoreEditorScopeAfterDocumentReload(previousScope);
        RegisterConfiguredHotkey();
        RefreshAdvancedFooterUi();
        Status($"{ActiveProfile()?.Name ?? profile.Name} restored to its last saved state.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
    }

    private void RestoreEditorScopeAfterDocumentReload(SettingsEditorScope previousScope)
    {
        if (!advancedMode) return;
        var profile = ActiveProfile();
        var restoredScope = SettingsEditorPolicy.ResolveScopeAfterDocumentReload(
            previousScope,
            profile?.Id,
            profile?.Actions.Select(action => action.Id) ?? []);

        if (restoredScope.Kind == SettingsEditorScopeKind.ProfileDefaults && profile is not null)
        {
            editorSession.EnterProfileDefaults(profile.Id);
            ApplyDefaults(AutomationBehaviorSettingsResolver.ResolveProfileDefaults(LoadSavedDefaults(), profile));
            UpdateSharedBehaviorDefaultsUi();
            return;
        }

        if (restoredScope.Kind == SettingsEditorScopeKind.Hotkey
            && profile?.Actions.FirstOrDefault(action => action.Id == restoredScope.TargetId) is { } action)
        {
            automationProfiles.ActiveActionId = action.Id;
            editorSession.EnterHotkey(action.Id);
            ApplyDefaults(ResolveActionSettings(action));
            UpdateSharedBehaviorDefaultsUi();
            return;
        }

        ShowAdvancedSharedDefaults(clearSelection: false);
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AutomationProfile profile || profile.Id == ActiveProfile()?.Id) return;
        if (profilesDirty)
        {
            Status("Save or discard current profile changes before deleting another profile.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
            return;
        }

        var confirmation = new ConfirmationWindow("Delete profile", $"Delete {profile.Name}? This cannot be undone.", "Delete", destructive: true) { Owner = this };
        if (confirmation.ShowDialog() != true) return;
        automationProfiles.Profiles.RemoveAll(item => item.Id == profile.Id);
        automationProfiles.RecentProfileIds.Remove(profile.Id);
        PersistAutomationProfiles();
        RefreshAdvancedFooterUi();
        Status($"{profile.Name} deleted.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
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
        copy.Id = Guid.NewGuid().ToString(AppIdentity.CompactGuidFormat);
        foreach (var action in copy.Actions) action.Id = Guid.NewGuid().ToString(AppIdentity.CompactGuidFormat);
        copy.Name = "Unsaved";
        automationProfiles.Profiles.Add(copy);
        automationProfiles.ActiveProfileId = copy.Id;
        automationProfiles.ActiveActionId = copy.Actions.FirstOrDefault()?.Id ?? string.Empty;
        unsavedProfileId = copy.Id;
        TouchRecentProfile(copy.Id);
        if (copy.Actions.FirstOrDefault() is { } copiedAction)
        {
            editorSession.EnterHotkey(copiedAction.Id);
            ApplyDefaults(ResolveActionSettings(copiedAction));
        }
        else ShowAdvancedSharedDefaults();
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
        editorSession.ToggleHotkey(action.Id);

        var selected = SelectedAdvancedActions();
        if (selected.Count == 1)
        {
            SelectAdvancedAction(selected[0]);
            return;
        }

        ShowAdvancedSharedDefaults(clearSelection: false);
        Status(selected.Count == 0 ? "Shared defaults selected." : $"{selected.Count} hotkeys selected - use their menu for shared options.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
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

    private void ConfigureEnableToggleHotkey_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is AutomationAction action) BeginEnableToggleHotkeyCapture(action);
    }

    private void RemoveEnableToggleHotkey_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AutomationAction action || action.EnableToggleHotkey is null) return;
        action.EnableToggleHotkey = null;
        RegisterConfiguredHotkey();
        MarkProfilesDirty();
        RefreshAdvancedFooterUi();
        Status("Enable-toggle hotkey removed.", ThemeManager.Brush(ThemeResourceKeys.TextMutedBrush));
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
        Status("Hotkey order changed - save the profile when ready.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
        e.Handled = true;
    }

    private void SetActionDragTarget(Border target)
    {
        if (ReferenceEquals(actionDragTarget, target)) return;
        ClearActionDragTarget();
        actionDragTarget = target;
        target.BorderBrush = ThemeManager.Brush(ThemeResourceKeys.AccentFocusBrush);
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
            AutomationProperties.SetAutomationId(changeHotkey, "ChangeActionHotkey");
            changeHotkey.Click += ChangeAdvancedActionHotkey_Click;
            menu.Items.Add(changeHotkey);
            var enableToggleBinding = action.EnableToggleHotkey;
            var configureEnableToggle = new MenuItem
            {
                Header = enableToggleBinding?.IsConfigured == true
                    ? $"Change enable-toggle hotkey… ({enableToggleBinding})"
                    : "Configure enable-toggle hotkey…",
                Tag = action
            };
            AutomationProperties.SetAutomationId(configureEnableToggle, "ConfigureEnableToggleHotkey");
            configureEnableToggle.Click += ConfigureEnableToggleHotkey_Click;
            menu.Items.Add(configureEnableToggle);
            if (enableToggleBinding?.IsConfigured == true)
            {
                var removeEnableToggle = new MenuItem { Header = "Remove enable-toggle hotkey", Tag = action };
                AutomationProperties.SetAutomationId(removeEnableToggle, "RemoveEnableToggleHotkey");
                removeEnableToggle.Click += RemoveEnableToggleHotkey_Click;
                menu.Items.Add(removeEnableToggle);
            }
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
        AutomationProperties.SetAutomationId(hotkeyEnabled, "ToggleActionEnabled");
        hotkeyEnabled.Click += (_, _) => SetHotkeysEnabled(targets, hotkeyEnabled.IsChecked);
        menu.Items.Add(hotkeyEnabled);
        menu.Items.Add(new Separator());

        var allUseSharedBehavior = targets.All(item => item.ActiveBehaviorOverrides == AutomationBehaviorOverride.None);
        var behaviorState = SharedBehaviorMenuState(targets);
        var sharedBehavior = new MenuItem
        {
            Header = $"{behaviorState}Use shared behavior defaults"
        };
        AutomationProperties.SetAutomationId(sharedBehavior, "ToggleActionSharedBehavior");
        sharedBehavior.Click += (_, _) => ApplySharedBehaviorDefaults(targets, !allUseSharedBehavior);
        menu.Items.Add(sharedBehavior);
        menu.Items.Add(new Separator());

        var allUseSharedLighting = targets.All(item => item.UsesSharedLightingSettings);
        var lightingState = SharedMenuState(targets.Select(item => (bool?)item.UsesSharedLightingSettings));
        var sharedLighting = new MenuItem
        {
            Header = $"{lightingState}Use inherited lighting settings"
        };
        AutomationProperties.SetAutomationId(sharedLighting, "ToggleActionSharedLighting");
        sharedLighting.Click += (_, _) => ApplySharedLightingSettings(targets, !allUseSharedLighting);
        menu.Items.Add(sharedLighting);
        var configureLighting = new MenuItem { Header = "Configure lighting override…" };
        AutomationProperties.SetAutomationId(configureLighting, "ConfigureActionLighting");
        configureLighting.Click += (_, _) => ConfigureLightingOverride(targets);
        menu.Items.Add(configureLighting);
        menu.Items.Add(new Separator());
        var copy = new MenuItem { Header = targets.Count == 1 ? "Copy hotkey to profile…" : "Copy selected hotkeys to profile…" };
        AutomationProperties.SetAutomationId(copy, "CopyActionsToProfile");
        copy.Click += (_, _) => CopyHotkeysToProfile(targets);
        menu.Items.Add(copy);
        if (targets.Count > 1)
        {
            menu.Items.Add(new Separator());
            var deleteSelected = new MenuItem { Header = "Delete selected hotkeys…" };
            AutomationProperties.SetAutomationId(deleteSelected, "DeleteSelectedActions");
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
            Status("Save or discard current profile changes before copying hotkeys.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
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
            if (destination.Actions.FirstOrDefault() is { } copiedAction)
            {
                editorSession.EnterHotkey(copiedAction.Id);
                ApplyDefaults(ResolveActionSettings(copiedAction));
            }
            else ShowAdvancedSharedDefaults();
            result = new ProfileCopyResult(destination.Actions.Count, 0, 0);
        }
        else
        {
            destination = dialog.DestinationProfile;
            result = AutomationProfileCopy.CopyTo(destination, actions, dialog.ConflictResolution, keyboardHotkeyModifiersEnabled);
        }

        MarkProfilesDirty();
        RefreshAdvancedFooterUi();
        var details = result.ReplacedCount > 0 ? $" Replaced {result.ReplacedCount}." : string.Empty;
        details += result.SkippedCount > 0 ? $" Skipped {result.SkippedCount}." : string.Empty;
        Status($"Copied {result.CopiedCount} hotkey{(result.CopiedCount == 1 ? string.Empty : "s")} to {destination.Name}.{details} Save to keep the change.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
    }

    private static string SharedBehaviorMenuState(IEnumerable<AutomationAction> actions)
    {
        var states = actions.Select(action => action.ActiveBehaviorOverrides == AutomationBehaviorOverride.None
            ? (bool?)true
            : action.ActiveBehaviorOverrides != AutomationBehaviorOverride.All ? null : false);
        return SharedMenuState(states);
    }

    internal static string SharedMenuState(IEnumerable<bool?> states)
    {
        var values = states.Distinct().ToList();
        return values.Count == 1 && values[0] == true ? "✓  " : values.Any(value => value is true or null) ? "~  " : string.Empty;
    }

    private void SetHotkeysEnabled(IEnumerable<AutomationAction> actions, bool enabled)
    {
        var targets = actions.DistinctBy(action => action.Id).ToList();
        if (targets.Count == 0) return;
        if (!enabled)
            foreach (var action in targets) StopProfileAction(action.Id);
        foreach (var action in targets) action.HotkeyEnabled = enabled;
        RegisterConfiguredHotkey();
        MarkProfilesDirty();
        RefreshAdvancedFooterUi();
        if (enabled)
            foreach (var action in targets) ActivateWhileHeldActionIfTriggerIsDown(action);
        var noun = targets.Count == 1 ? "hotkey" : "hotkeys";
        Status(enabled ? $"{targets.Count} {noun} enabled." : $"{targets.Count} {noun} disabled.", ThemeManager.Brush(enabled ? ThemeResourceKeys.SuccessBrush : ThemeResourceKeys.TextMutedBrush));
    }

    private void ActivateWhileHeldActionIfTriggerIsDown(AutomationAction action)
    {
        if (!AutomationHotkeyBindingRules.ShouldActivateWhileHeldOnEnable(action, IsBindingTriggerDown)) return;
        var binding = AutomationHotkeyBindingRules.RunBinding(action);
        ActivateHotkey(action, binding.VirtualKey, binding.Modifiers, binding.Trigger);
    }

    private bool IsBindingTriggerDown(AutomationHotkeyBinding binding)
    {
        var physicalKey = HotkeyHoldSafety.PhysicalVirtualKey(binding.Trigger, binding.VirtualKey);
        if (physicalKey is null) return false;
        var requiredModifiers = binding.Trigger == HotkeyTrigger.Keyboard
            ? HotkeyHoldSafety.RequiredKeyboardModifiers(keyboardHotkeyModifiersEnabled, binding.Modifiers)
            : binding.Modifiers & 0x7;
        return HotkeyHoldSafety.IsTriggerDown(physicalKey.Value, requiredModifiers, IsKeyPressed);
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
        editorSession.RemoveHotkey(action.Id);
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
        Status("Hotkey removed.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
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
        if (targets.Any(action => action.Id == automationProfiles.ActiveActionId))
            automationProfiles.ActiveActionId = profile.Actions.FirstOrDefault()?.Id ?? string.Empty;
        if (advancedMode) ShowAdvancedSharedDefaults();
        SaveAutomationProfiles();
        RegisterConfiguredHotkey();
        RefreshAdvancedFooterUi();
        UpdateLiveInputMode();
        Status($"{targets.Count} hotkeys removed.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
    }

    private void AddAdvancedAction_Click(object sender, RoutedEventArgs e)
    {
        if (profileRuns.Count > 0)
        {
            Status("Stop active hotkeys before adding another.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
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
                Status("Finish choosing the hotkey, or press Escape to cancel it.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
                return;
            }
        }
        var profile = ActiveProfile();
        if (profile is null) return;
        if (profile.Actions.Count >= AutomationProfileLimits.MaximumHotkeys)
        {
            Status($"A profile can have up to {AutomationProfileLimits.MaximumHotkeys} hotkeys.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
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
        editorSession.EnterHotkey(action.Id);
        SaveAutomationProfiles();
        if (hotkeyRegistered) UnregisterPrimaryHotkeys();
        ApplyDefaults(ResolveActionSettings(action));
        RefreshAdvancedFooterUi();
        BeginHotkeyCapture();
    }

    private void ToggleProfileAction(AutomationAction action)
    {
        if (!action.HotkeyEnabled) return;
        if (settingsOpen) { Status("Close Settings before starting another hotkey.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush)); return; }
        if (profileRuns.ContainsKey(action.Id)) StopProfileAction(action.Id); else StartProfileAction(action);
    }

    private void ActivateHotkey(AutomationAction? action, int virtualKey, uint modifiers, HotkeyTrigger trigger)
    {
        if (!CanExecuteAutomation()) return;
        if (action is not null && !action.HotkeyEnabled) return;
        var actionType = action is null ? Selected(TypeCombo) : action.Settings.ClickType;
        if (!InputRules.IsWhileHeldAction(actionType))
        {
            if (action is not null) ToggleProfileAction(action); else ToggleClicking();
            return;
        }

        var effectiveSettings = action is null ? CreateCurrentDefaults() : ResolveActionSettings(action);
        var physicalKey = HotkeyHoldSafety.PhysicalVirtualKey(trigger, virtualKey);
        if (physicalKey is null)
        {
            Status("While held requires a keyboard key, Middle mouse, Mouse 4, or Mouse 5; wheel gestures cannot be held.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
            return;
        }
        var input = string.IsNullOrWhiteSpace(effectiveSettings.Input) ? effectiveSettings.MouseButton : effectiveSettings.Input;
        if (trigger == HotkeyTrigger.Keyboard
            && InputRules.ActionUsesVirtualKey(input, effectiveSettings.CustomKey, effectiveSettings.CustomSequence, virtualKey))
        {
            Status("A While held action cannot send its own hotkey key. Choose a different action or hotkey.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
            return;
        }

        CancellationTokenSource? cancellation;
        string? actionId = null;
        if (action is not null)
        {
            if (!profileRuns.TryGetValue(action.Id, out cancellation))
            {
                StartProfileAction(action);
                if (!profileRuns.TryGetValue(action.Id, out cancellation)) return;
            }
            actionId = action.Id;
        }
        else
        {
            cancellation = clickCancellation;
            if (cancellation is null)
            {
                StartClicking();
                cancellation = clickCancellation;
                if (cancellation is null) return;
            }
        }

        // Register at most one release monitor per run. This ignores Windows key-repeat messages while still
        // allowing a run started from the UI to become safely release-bound when its hotkey is next held.
        if (!heldTriggerMonitors.Add(cancellation)) return;
        var requiredModifiers = trigger == HotkeyTrigger.Keyboard
            ? HotkeyHoldSafety.RequiredKeyboardModifiers(keyboardHotkeyModifiersEnabled, modifiers)
            : modifiers & 0x7;
        MonitorHeldTriggerRelease(physicalKey.Value, requiredModifiers, cancellation, actionId);
    }

    private void MonitorHeldTriggerRelease(int physicalKey, uint requiredModifiers, CancellationTokenSource cancellation, string? actionId)
    {
        var token = cancellation.Token;
        _ = Task.Run(async () =>
        {
            var monitorFailed = false;
            try
            {
                // Always yield once so the Windows hook/message that started the action can finish first.
                await Task.Delay(10, token);
                while (HotkeyHoldSafety.IsTriggerDown(physicalKey, requiredModifiers, IsKeyPressed))
                    await Task.Delay(10, token);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception exception)
            {
                monitorFailed = true;
                AppLog.Error("Held-hotkey release monitor failed", exception);
            }

            if (token.IsCancellationRequested || isClosing || Dispatcher.HasShutdownStarted) return;
            _ = Dispatcher.BeginInvoke(() =>
            {
                if (!IsCurrentRun(cancellation, actionId)) return;
                // A very fast release/repress can race the dispatcher. Keep monitoring the same run if the
                // trigger is physically down again; otherwise fail closed and cancel this exact run instance.
                if (!monitorFailed && HotkeyHoldSafety.IsTriggerDown(physicalKey, requiredModifiers, IsKeyPressed))
                {
                    MonitorHeldTriggerRelease(physicalKey, requiredModifiers, cancellation, actionId);
                    return;
                }
                heldTriggerMonitors.Remove(cancellation);
                if (actionId is null) StopClicking(); else StopProfileAction(actionId);
            });
        });
    }

    private bool IsCurrentRun(CancellationTokenSource cancellation, string? actionId) => actionId is null
        ? ReferenceEquals(clickCancellation, cancellation)
        : profileRuns.TryGetValue(actionId, out var current) && ReferenceEquals(current, cancellation);

    private void StartProfileAction(AutomationAction action)
    {
        if (!CanExecuteAutomation()) return;
        if (!HotkeyFormatter.IsConfigured(action.Settings.Hotkey, action.Settings.HotkeyTrigger) || profileRuns.ContainsKey(action.Id)) return;
        var effectiveSettings = ResolveActionSettings(action);
        var input = string.IsNullOrWhiteSpace(effectiveSettings.Input) ? effectiveSettings.MouseButton : effectiveSettings.Input;
        if (!InputRules.IsConfiguredAction(input, effectiveSettings.CustomKey, effectiveSettings.CustomSequence?.Count ?? 0))
        {
            Status($"Set an action for {HotkeyFormatter.Format(action.Settings.Hotkey, action.Settings.HotkeyModifiers, action.Settings.HotkeyTrigger)} before starting it.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
            return;
        }
        if (input == AutomationInputIds.Sequence && SequenceHoldRules.ValidationError(effectiveSettings.CustomSequence ?? []) is { } sequenceError)
        {
            Status($"Custom sequence cannot run: {sequenceError}", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
            return;
        }
        if (InputRules.IsHoldAction(effectiveSettings.ClickType) && effectiveSettings.TargetWindowEnabled && !string.IsNullOrWhiteSpace(effectiveSettings.TargetExecutable))
        {
            Status("Target-window mode does not support held input. Override target window on the hotkey if desired.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
            return;
        }
        if (input == AutomationInputIds.Sequence && SequenceHoldRules.ContainsHold(effectiveSettings.CustomSequence ?? []) && effectiveSettings.TargetWindowEnabled && !string.IsNullOrWhiteSpace(effectiveSettings.TargetExecutable))
        {
            Status("Target-window mode does not support held sequence events. Override target window on the hotkey if desired.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
            return;
        }
        if (InputRules.IsHoldAction(effectiveSettings.ClickType) && InputRules.IsInstantaneousMouseAction(input))
        {
            Status("Scroll inputs cannot be held. Choose Single, Double, or While held.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
            return;
        }
        if (AutomationHotkeyBindingRules.ActionEmitsOwnKeyboardBinding(action, effectiveSettings))
        {
            Status("Choose run and enable-toggle hotkeys that this action never sends. Generated input must not trigger its own bindings.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
            return;
        }
        var cancellation = new CancellationTokenSource();
        profileRuns[action.Id] = cancellation;
        var settings = CreateClickSettings(effectiveSettings);
        var interval = InputRules.CreateInterval(effectiveSettings.Hours, effectiveSettings.Minutes, effectiveSettings.Seconds, effectiveSettings.Milliseconds);
        AppLog.Info($"Starting profile action {action.DisplayName} | IntervalMs={interval.TotalMilliseconds:0.###} | PulseMs={settings.InputPulseMilliseconds} | JitterMaxMs={settings.JitterMaximumMilliseconds} | WorkerPriority={settings.WorkerPriority} | Repeat={(settings.MaximumClicks?.ToString() ?? "until stopped")}");
        profileTasks[action.Id] = AutomationWorkerScheduler.Start(() => ProfileClickLoop(action.Id, interval, settings, cancellation));
        CollapseButton.IsEnabled = false;
        Status($"{action.DisplayName} active.", ThemeManager.Brush(ThemeResourceKeys.ErrorBrush));
        RefreshTaskbarActivityIndicator();
        RefreshAdvancedFooterUi();
        UpdateSharedBehaviorDefaultsUi();
        StartRgbIndicator(action.Id, ResolveLighting(action), LightingKeyName(action.Settings));
        RestoreLiveArea();
        UpdateLiveInputMode();
    }

    private void StopProfileAction(string actionId)
    {
        if (!profileRuns.Remove(actionId, out var cancellation)) return;
        heldTriggerMonitors.Remove(cancellation);
        cancellation.Cancel();
        profileTasks.Remove(actionId);
        Status("Profile hotkey stopped.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
        if (clickCancellation is null && profileRuns.Count == 0) CollapseButton.IsEnabled = true;
        RefreshTaskbarActivityIndicator();
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
            Status($"Close Settings before {ActivityVerb().ToLowerInvariant()}.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
            return;
        }
        if (clickCancellation is null) StartClicking(); else StopClicking();
    }
    private void StartClicking()
    {
        // Reject states that cannot produce a valid worker configuration.
        if (!CanExecuteAutomation())
        {
            Status("Close the open dialog or finish key capture before starting automation.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
            return;
        }
        if (clickCancellation is not null) return;
        if (capturingSpamKey)
        {
            Status("Finish choosing the key to repeat first.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
            return;
        }
        var input = Selected(ButtonCombo);
        if (!InputRules.IsConfiguredAction(input, customSpamVirtualKey, customSequence.Count))
        {
            Status("Set an action before starting.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
            return;
        }
        if (input == AutomationInputIds.Sequence && SequenceHoldRules.ValidationError(customSequence) is { } sequenceError)
        {
            Status($"Custom sequence cannot run: {sequenceError}", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
            return;
        }
        if (InputRules.IsHoldAction(Selected(TypeCombo)) && EnableTargetWindowCheckBox.IsChecked == true && !string.IsNullOrWhiteSpace(TargetExecutableBox.Text))
        {
            Status("Target-window mode does not support held input.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
            return;
        }
        if (input == AutomationInputIds.Sequence && SequenceHoldRules.ContainsHold(customSequence) && EnableTargetWindowCheckBox.IsChecked == true && !string.IsNullOrWhiteSpace(TargetExecutableBox.Text))
        {
            Status("Target-window mode does not support held sequence events.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
            return;
        }
        if (InputRules.IsHoldAction(Selected(TypeCombo)) && InputRules.IsInstantaneousMouseAction(input))
        {
            Status("Scroll inputs cannot be held. Choose Single, Double, or While held.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
            return;
        }
        // Resolve keyboard shortcuts before handing work to the background thread.
        var keyboardVirtualKey = input switch { AutomationInputIds.Space => 0x20, AutomationInputIds.Enter => 0x0D, AutomationInputIds.Custom => customSpamVirtualKey, _ => 0 };
        var sequenceUsesHotkey = input == AutomationInputIds.Sequence && InputRules.ActionUsesVirtualKey(input, customSpamVirtualKey, customSequence, hotkey);
        if (hotkeyTrigger == HotkeyTrigger.Keyboard && (keyboardVirtualKey == hotkey || sequenceUsesHotkey))
        {
            Status("The action sends its own start/stop hotkey. Choose another input or change the hotkey first.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
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
        var settings = new ClickSettings(FixedPositionRadio.IsChecked == true, Read(XBox, -32768, 32767), Read(YBox, -32768, 32767), input, keyboardVirtualKey == 0 ? null : keyboardVirtualKey, Selected(TypeCombo) == AutomationActionTypeIds.Double, hold, InputRules.RequiresContinuousRun(Selected(TypeCombo)) ? null : CountRadio.IsChecked == true ? Read(CountBox, 1, 999999) : null, input == AutomationInputIds.Sequence ? BuildSequence(customSequence) : null, InputRules.NormalizeInputPulseMilliseconds(input == AutomationInputIds.Sequence ? sequencePulseMilliseconds : inputPulseMilliseconds), inputJitterMaximumMilliseconds, workerPriority, cadenceDiagnosticsEnabled, target);
        // Reflect the running state before the worker can send its first input.
        CaptureCurrentActionToProfile();
        AppLog.Info($"Starting {ActivityVerb().ToLowerInvariant()} | Input={input} | IntervalMs={delay.TotalMilliseconds:0.###} | PulseMs={settings.InputPulseMilliseconds} | JitterMaxMs={settings.JitterMaximumMilliseconds} | WorkerPriority={settings.WorkerPriority} | Repeat={(settings.MaximumClicks?.ToString() ?? "until stopped")}");
        StartButton.IsEnabled = false; StopButton.IsEnabled = true;
        CollapseButton.IsEnabled = false;
        UpdateSharedBehaviorDefaultsUi();
        LiveArea.Background = ThemeManager.Brush(ThemeResourceKeys.AccentBrush);
        LiveArea.BorderBrush = ThemeManager.Brush(ThemeResourceKeys.AccentHoverBrush);
        LiveCountLabel.Text = liveClickCount == 0 ? "0 clicks" : $"{liveClickCount:N0} clicks";
        UpdateLiveInputMode();
        Status($"{ActivityVerb()} - press {FormatHotkey()} to stop.", ThemeManager.Brush(ThemeResourceKeys.ErrorBrush));
        RefreshTaskbarActivityIndicator();
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
        var heldSequenceInputs = new Dictionary<SequenceInputIdentity, HeldSequenceInput>();
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
                if (settings.FixedPosition && settings.KeyboardVirtualKey is null) MoveCursor(settings.X, settings.Y);
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
                var reachedNextAction = settings.Sequence is { Length: > 0 }
                    ? WaitForSequenceDeadline(timer, nextClickAt, heldSequenceInputs, cancellation, ref watchdogExpired)
                    : WaitUntilGuiIsHealthy(timer, nextClickAt, cancellation, ref watchdogExpired);
                if (!reachedNextAction) break;
                var now = Stopwatch.GetTimestamp();
                // Resume from the current time instead of catching up in a burst.
                if (now - nextClickAt > intervalTicks) nextClickAt = now;
                if (settings.Sequence is { Length: > 0 })
                {
                    if (!ExecuteSequence(settings, timer, cancellation, ref watchdogExpired, cadence, nextClickAt, heldSequenceInputs, out var sentSequenceAction)) break;
                    if (sentSequenceAction) sent++;
                }
                else
                {
                    // Single mouse/key actions share the main interval.
                    if (CanSendAction(settings, settings.KeyboardVirtualKey is null))
                    {
                        if (settings.FixedPosition && settings.KeyboardVirtualKey is null) MoveCursor(settings.X, settings.Y);
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
            ReleaseHeldSequenceInputs(heldSequenceInputs);
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
                            if (watchdogExpired) Status("Stopped - the GUI heartbeat timed out.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
                            else if (failure is not null) Status("Stopped - details were written to AutoClicker.log.", ThemeManager.Brush(ThemeResourceKeys.ErrorBrush));
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
        var key = input switch { AutomationInputIds.Space => 0x20, AutomationInputIds.Enter => 0x0D, AutomationInputIds.Custom => source.CustomKey, _ => 0 };
        var hold = InputRules.IsHoldAction(source.ClickType);
        var target = source.TargetWindowEnabled
            ? new TargetWindowRule(source.TargetExecutable, source.TargetWindowTitle)
            : new TargetWindowRule(string.Empty, null);
        var sequencePulse = source.CustomSequenceUsesGlobalInputPulse ? source.InputPulseMilliseconds : 0;
        return new ClickSettings(source.FixedPosition, source.X, source.Y, input, key == 0 ? null : key, source.ClickType == AutomationActionTypeIds.Double, hold,
            InputRules.RequiresContinuousRun(source.ClickType) || source.RepeatUntilStopped ? null : Math.Clamp(source.RepeatCount, 1, 999999), input == AutomationInputIds.Sequence ? BuildSequence(source.CustomSequence ?? []) : null,
            InputRules.NormalizeInputPulseMilliseconds(input == AutomationInputIds.Sequence ? sequencePulse ?? 0 : source.InputPulseMilliseconds ?? 0), source.InputJitterMaximumMilliseconds,
            workerPriority, cadenceDiagnosticsEnabled, target);
    }

    // Secondary profile actions keep their own cancellation source so one hotkey never stops another.
    private void ProfileClickLoop(string actionId, TimeSpan delay, ClickSettings settings, CancellationTokenSource cancellation)
    {
        Input[]? heldRelease = null;
        var heldSequenceInputs = new Dictionary<SequenceInputIdentity, HeldSequenceInput>();
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
                if (settings.FixedPosition && settings.KeyboardVirtualKey is null) MoveCursor(settings.X, settings.Y);
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
                var reachedNextAction = settings.Sequence is { Length: > 0 }
                    ? WaitForSequenceDeadline(timer, next, heldSequenceInputs, cancellation, ref watchdogExpired)
                    : WaitUntilGuiIsHealthy(timer, next, cancellation, ref watchdogExpired);
                if (!reachedNextAction) break;
                var now = Stopwatch.GetTimestamp();
                if (now - next > intervalTicks) next = now;
                if (settings.Sequence is { Length: > 0 })
                {
                    if (!ExecuteSequence(settings, timer, cancellation, ref watchdogExpired, cadence, next, heldSequenceInputs, out var sentSequenceAction)) break;
                    if (sentSequenceAction) sent++;
                }
                else if (CanSendAction(settings, settings.KeyboardVirtualKey is null))
                {
                    if (settings.FixedPosition && settings.KeyboardVirtualKey is null) MoveCursor(settings.X, settings.Y);
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
            ReleaseHeldSequenceInputs(heldSequenceInputs);
            if (heldRelease is not null) { try { SendNativeInput((uint)heldRelease.Length, heldRelease); } catch { } }
            try { Thread.CurrentThread.Priority = originalPriority; } catch { }
            cadence?.LogSummary();
            if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(() =>
            {
                if (profileRuns.TryGetValue(actionId, out var current) && ReferenceEquals(current, cancellation))
                {
                    profileRuns.Remove(actionId);
                    heldTriggerMonitors.Remove(cancellation);
                    profileTasks.Remove(actionId);
                    if (!isClosing) Status(watchdogExpired ? "A profile hotkey stopped because the GUI heartbeat timed out." : "Profile hotkey stopped.", ThemeManager.Brush(watchdogExpired ? ThemeResourceKeys.WarningBrush : ThemeResourceKeys.SuccessBrush));
                    if (clickCancellation is null && profileRuns.Count == 0) CollapseButton.IsEnabled = true;
                    RefreshTaskbarActivityIndicator();
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
        if (cancellation is not null) heldTriggerMonitors.Remove(cancellation);
        cancellation?.Cancel();
        if (cancellation is not null) AppLog.Info("Click/spam worker stop requested.");
        StartButton.IsEnabled = true; StopButton.IsEnabled = false;
        CollapseButton.IsEnabled = true;
        UpdateSharedBehaviorDefaultsUi();
        LiveArea.Background = ThemeManager.Brush(ThemeResourceKeys.ControlBrush);
        LiveArea.BorderBrush = ThemeManager.Brush(ThemeResourceKeys.LiveBorderBrush);
        if (liveClickCount == 0) LiveCountLabel.Text = "Start to test";
        UpdateLiveInputMode();
        Status($"Ready - press {FormatHotkey()} to start or stop.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
        RefreshTaskbarActivityIndicator();
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
        OpenRgbHighlighter.SuppressAutoStart();
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
        var doubleClickMode = string.Equals(TestAreaSettings().ClickType, AutomationActionTypeIds.Double, StringComparison.OrdinalIgnoreCase);
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
        LiveArea.Background = ThemeManager.Brush(ThemeResourceKeys.LiveFlashBrush);
        LiveArea.BorderBrush = ThemeManager.Brush(ThemeResourceKeys.LiveFlashBorderBrush);
        if (ThemeManager.Current == AppTheme.Light)
        {
            var flashText = ThemeManager.Brush(ThemeResourceKeys.TextSecondaryBrush);
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
        LiveArea.Background = ThemeManager.Brush(running ? ThemeResourceKeys.AccentBrush : ThemeResourceKeys.ControlBrush);
        LiveArea.BorderBrush = ThemeManager.Brush(running ? ThemeResourceKeys.AccentHoverBrush : ThemeResourceKeys.LiveBorderBrush);
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
        var sequenceInput = testInput == AutomationInputIds.Sequence;
        var instantaneousMouseInput = InputRules.IsInstantaneousMouseAction(testInput);
        var keyboardInput = InputRules.IsKeyboardAction(testInput!);
        var hold = InputRules.IsHoldAction(testSettings.ClickType);
        var whileHeld = InputRules.IsWhileHeldAction(testSettings.ClickType);
        var sharedBehavior = advancedMode && ActiveProfileAction()?.UsesSharedBehavior(AutomationBehaviorOverride.Position) == true;
        DoubleTypeItem.IsEnabled = !sequenceInput;
        HoldTypeItem.IsEnabled = !sequenceInput && !instantaneousMouseInput;
        TypeCombo.IsEnabled = !IsClicking && (!advancedMode || IsEditingAdvancedAction());
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
            IntervalHint.Text = sequenceInput ? "Time between sequences" : hold ? "Hold stays active until stopped" : whileHeld ? "Time between actions while the hotkey is held" : keyboardInput ? "Time between key presses" : "Time between clicks";
            UpdateLiveAreaTextContrast();
            return;
        }
        if (sequenceInput)
        {
            LiveMouseHint.Visibility = Visibility.Visible;
            LiveKeyFocusBox.Visibility = Visibility.Collapsed;
            LiveTitleLabel.Text = "SEQUENCE MODE";
            LiveMouseHint.Text = "Test area disabled";
            LiveCountLabel.Text = AutomationInputLabels.CustomSequence;
            LiveIntervalLabel.Text = "Configure steps from the Input menu";
            IntervalHint.Text = whileHeld ? "Time between sequences while the hotkey is held" : "Time between sequences";
            UpdateLiveAreaTextContrast();
            return;
        }
        LiveMouseHint.Text = "Hover here while running";
        LiveMouseHint.Visibility = keyboardInput ? Visibility.Collapsed : Visibility.Visible;
        LiveKeyFocusBox.Visibility = keyboardInput ? Visibility.Visible : Visibility.Collapsed;
        LiveTitleLabel.Text = keyboardInput ? "LIVE SPAM AREA" : "LIVE CLICK AREA";
        IntervalHint.Text = hold ? "Hold stays active until stopped" : whileHeld ? "Time between actions while the hotkey is held" : keyboardInput ? "Time between key presses" : "Time between clicks";
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
        Status($"Ready - {SelectedActionDescription()} will be repeated.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
    }

    private string SelectedActionDescription() => InputRules.DescribeAction(Selected(ButtonCombo), customSpamVirtualKey);

    private string ActivityVerb() => Selected(ButtonCombo) == AutomationInputIds.Sequence
        ? "Running sequence"
        : InputRules.IsInstantaneousMouseAction(Selected(ButtonCombo))
            ? "Scrolling"
        : InputRules.IsHoldAction(Selected(TypeCombo))
            ? IsKeyboardInputSelected() ? "Holding key" : "Holding mouse button"
            : IsKeyboardInputSelected() ? "Spamming" : "Clicking";

    private void UpdateLiveAreaTextContrast()
    {
        if (LiveTitleLabel is null) return;
        var brush = ThemeManager.Brush(ThemeManager.Current == AppTheme.Light && IsTestAreaRunning ? ThemeResourceKeys.LiveAccentTextBrush : ThemeResourceKeys.TextMutedBrush);
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
                        indicatorError = availability.Message ?? OpenRgbMessages.SdkServerUnavailable;
                        if (!settings.AutoStart || Stopwatch.GetTimestamp() >= retryDeadline) break;
                        await Task.Delay(openRgbStartupRetryDelayMilliseconds, cancellation.Token);
                        continue;
                    }

                    ClearOpenRgbWarning();
                    if (availability.WasStarted && !Dispatcher.HasShutdownStarted)
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
                            _ = Dispatcher.BeginInvoke(() => Status(indicatorError, ThemeManager.Brush(ThemeResourceKeys.ErrorBrush)));
                    }
                    return;
                }

                if (!settings.UsesFadeEffect) OpenRgbHighlighter.LightIndicator(snapshot);
                if (settings.UsesBlinkEffect)
                    await OpenRgbHighlighter.BlinkIndicatorAsync(snapshot, settings.EffectSpeedMilliseconds, cancellation.Token);
                else if (settings.UsesFadeEffect)
                    await OpenRgbHighlighter.FadeIndicatorAsync(snapshot, settings.EffectSpeedMilliseconds, cancellation.Token);
                else
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellation.Token);
                if (updateSharedDevice && !Dispatcher.HasShutdownStarted && (settings.DeviceIndex != rgbSettings.DeviceIndex || !string.Equals(settings.DeviceName, rgbSettings.DeviceName, StringComparison.Ordinal)))
                    _ = Dispatcher.BeginInvoke(() => { rgbSettings = settings; SaveRgbSettings(); });
                if (indicatorError is not null && !Dispatcher.HasShutdownStarted)
                {
                    _ = Dispatcher.BeginInvoke(() => Status(indicatorError, ThemeManager.Brush(ThemeResourceKeys.ErrorBrush)));
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) when (!Dispatcher.HasShutdownStarted)
            {
                AppLog.Error("OpenRGB hotkey indicator failed", exception);
                _ = Dispatcher.BeginInvoke(() => Status(OpenRgbMessages.Unavailable(exception.Message), ThemeManager.Brush(ThemeResourceKeys.ErrorBrush)));
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
        // Shutdown owns one final, bounded profile restore. Background continuations
        // must not race that restore or relaunch OpenRGB after it has been stopped.
        if (isClosing) return;
        _ = ApplyIdleOpenRgbProfileAsync(allowAutoStart: true);
    }

    private Task ApplyIdleOpenRgbProfileAsync(bool allowAutoStart)
    {
        var profileName = (rgbSettings.IdleProfileName ?? string.Empty).Trim();
        if (profileName.Length == 0) return Task.CompletedTask;

        var settings = OpenRgbHighlighter.CreateIdleProfileSettings(CloneLighting(rgbSettings), allowAutoStart);
        return Task.Run(async () =>
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
                    if (availability.WasStarted && !Dispatcher.HasShutdownStarted)
                        _ = Dispatcher.BeginInvoke(ShowOpenRgbStartedStatus);
                }
                else
                {
                    var message = availability.Message ?? OpenRgbMessages.SdkServerUnavailable;
                    AppLog.Info($"OpenRGB could not be started at application launch: {message}");
                    ShowOpenRgbWarning(message);
                }
            }
            catch (Exception exception)
            {
                AppLog.Error("Could not start OpenRGB at application launch", exception);
                ShowOpenRgbWarning(OpenRgbMessages.CouldNotStart(exception.Message));
            }
        });
    }

    private void ShowOpenRgbWarning(string message)
    {
        if (!OpenRgbWarningRules.ShouldDisplay(rgbSettings.Enabled, isClosing) || Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (!OpenRgbWarningRules.ShouldDisplay(rgbSettings.Enabled, isClosing)) return;
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
        var continuous = InputRules.RequiresContinuousRun(Selected(TypeCombo));
        if (continuous) UntilStoppedRadio.IsChecked = true;
        CountRadio.IsEnabled = !continuous;
        CountBox.IsEnabled = !continuous && CountRadio.IsChecked == true;
        CommitBehaviorChange(AutomationBehaviorOverride.Repeat);
    }

    private void TypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UntilStoppedRadio is null || CountRadio is null) return;
        RepeatMode_Changed(sender, e);
        UpdateLiveInputMode();
        CommitSelectedActionChange();
        RemoveIntervalOverrideFromHoldHotkey();
        UpdateSharedBehaviorDefaultsUi();
    }

    private void RemoveIntervalOverrideFromHoldHotkey()
    {
        if (applyingDefaults || !advancedMode || !IsEditingAdvancedAction() || ActiveProfileAction() is not { } action || !InputRules.IsHoldAction(action.Settings.ClickType)) return;
        var overrides = action.ActiveBehaviorOverrides;
        if (!overrides.HasFlag(AutomationBehaviorOverride.Interval)) return;

        // Preserve every other local behavior value while returning the unused interval to inheritance.
        action.UsesSharedBehaviorDefaults = true;
        action.BehaviorOverrides = overrides & ~AutomationBehaviorOverride.Interval;
        MarkProfilesDirty();
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
        Status("Select a position anywhere on screen. Press Escape to cancel.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
        var picker = new PositionPickerWindow { Owner = this };
        if (picker.ShowDialog() != true) return;
        ApplyPickedPosition(PositionSelection.FromPickedPoint(picker.SelectedX, picker.SelectedY));
    }

    private void ApplyPickedPosition(PositionSelection selection)
    {
        FixedPositionRadio.IsChecked = selection.FixedPosition;
        XBox.Text = selection.X.ToString();
        YBox.Text = selection.Y.ToString();
        Status($"Fixed position set to X: {selection.X}, Y: {selection.Y}.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
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
        if (clickCancellation is not null || profileRuns.Count > 0) { Status("Stop all active hotkeys before resetting settings.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush)); return false; }
        if (scope == ResetScope.Everything) return ResetToFactoryDefaults();
        if (SettingsScopeRules.ResetsSimple(scope)) return ResetSimpleMode();
        if (SettingsScopeRules.ResetsAdvancedProfiles(scope)) return ResetAdvancedMode();
        return ResetSharedDefaults();
    }

    private bool ResetSimpleMode()
    {
        if (!WriteDefaults(SimpleDefaultsPath, new AppDefaults())) return false;
        if (!advancedMode) ApplyDefaults(new AppDefaults());
        Status("Simple mode defaults restored.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
        return true;
    }

    private bool ResetSharedDefaults()
    {
        if (!WriteDefaults(AdvancedSharedDefaultsPath, new AppDefaults())) return false;
        if (advancedMode)
        {
            switch (CurrentEditorStorageTarget())
            {
                case SettingsEditorStorageTarget.ProfileDefaults when ActiveProfile() is { } profile:
                    BeginProfileDefaultsEdit(profile);
                    break;
                case SettingsEditorStorageTarget.HotkeyOverride when ActiveProfileAction() is { } action:
                    ApplyDefaults(ResolveActionSettings(action));
                    break;
                default:
                    ApplyDefaults(LoadSavedDefaults());
                    break;
            }
            UpdateSharedBehaviorDefaultsUi();
        }
        Status("Shared Advanced-mode defaults restored.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
        return true;
    }

    private bool ResetAdvancedMode()
    {
        if (hotkeyRegistered) UnregisterPrimaryHotkeys();
        automationProfiles = AutomationProfileStore.CreateInitial(LoadSavedDefaults());
        PersistAutomationProfiles();
        profilesDirty = false;
        unsavedProfileId = null;
        if (advancedMode && ActiveProfileAction() is { } action)
        {
            editorSession.EnterHotkey(action.Id);
            ApplyDefaults(ResolveActionSettings(action));
        }
        RegisterConfiguredHotkey();
        RefreshAdvancedFooterUi();
        UpdateSharedBehaviorDefaultsUi();
        Status("Advanced profiles restored to General.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
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
            Status($"Stop {ActivityVerb().ToLowerInvariant()} before resetting defaults.", ThemeManager.Brush(ThemeResourceKeys.WarningBrush));
            return false;
        }

        // Re-register after restoring F6 and its modifiers.
        if (hotkeyRegistered) UnregisterPrimaryHotkeys();
        rgbSettings = new RgbSettings();
        var defaultPreferences = new ApplicationPreferences();
        ApplyDefaults(new AppDefaults());
        automationProfiles = AutomationProfileStore.CreateInitial(CreateCurrentDefaults());
        PersistAutomationProfiles();
        profilesDirty = false;
        advancedMode = defaultPreferences.AdvancedMode;
        workerPriority = WorkerPriorityRules.Normalize(defaultPreferences.WorkerPriority);
        cadenceDiagnosticsEnabled = defaultPreferences.CadenceDiagnosticsEnabled;
        crashRecoveryEnabled = defaultPreferences.CrashRecoveryEnabled;
        keyboardHotkeyModifiersEnabled = defaultPreferences.KeyboardHotkeyModifiersEnabled;
        rememberPinned = defaultPreferences.RememberPinned;
        applyPinnedOnLaunch = defaultPreferences.ApplyPinnedOnLaunch;
        pinnedPreference = defaultPreferences.Pinned;
        deferredPinPending = false;
        ThemeManager.Apply(AppTheme.Dark);
        UpdateThemeButton();
        RestoreLiveArea();
        ApplyModeUi();
        RegisterConfiguredHotkey();
        try
        {
            WriteDefaults(SimpleDefaultsPath, new AppDefaults());
            WriteDefaults(AdvancedSharedDefaultsPath, new AppDefaults());
        }
        catch { }
        SaveRgbSettings();
        Topmost = defaultPreferences.Pinned;
        compactMode = defaultPreferences.CompactMode;
        quickStartSeen = defaultPreferences.QuickStartSeen;
        UpdatePinUi();
        ApplyCompactMode();
        SaveApplicationPreferences();
        CrashRecovery.UpdateEnabled(crashRecoveryEnabled);
        Status("Factory default values restored.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
        return true;
    }

    private void SaveDefaults()
    {
        try
        {
            var settings = CreateCurrentDefaults();
            var path = advancedMode ? AdvancedSharedDefaultsPath : SimpleDefaultsPath;
            if (!WriteDefaults(path, settings)) throw new IOException("Could not write default settings.");
            CaptureCurrentActionToProfile();
            Status("Current settings saved as the default.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
        }
        catch { Status("Could not save the default settings.", ThemeManager.Brush(ThemeResourceKeys.ErrorBrush)); }
    }

    private AppDefaults CreateCurrentDefaults()
    {
        var interval = NormalizeIntervalBoxes();
        return new AppDefaults { Hours = interval.Hours, Minutes = interval.Minutes, Seconds = interval.Seconds, Milliseconds = interval.Milliseconds, MouseButton = Selected(ButtonCombo), Input = Selected(ButtonCombo), CustomKey = customSpamVirtualKey, CustomSequence = customSequence.Select(step => step.Clone()).ToList(), CustomSequenceUsesGlobalInputPulse = customSequenceUsesGlobalInputPulse, ClickType = Selected(TypeCombo), RepeatUntilStopped = UntilStoppedRadio.IsChecked == true, RepeatCount = Read(CountBox, 1, 999999), FixedPosition = FixedPositionRadio.IsChecked == true, X = Read(XBox, -32768, 32767), Y = Read(YBox, -32768, 32767), InputPulseMilliseconds = inputPulseMilliseconds, InputJitterMaximumMilliseconds = inputJitterMaximumMilliseconds, TargetExecutable = TargetExecutableBox.Text.Trim(), TargetWindowTitle = targetWindowTitle, TargetWindowEnabled = EnableTargetWindowCheckBox.IsChecked == true, Hotkey = hotkey, HotkeyModifiers = hotkeyModifiers, HotkeyTrigger = hotkeyTrigger, Rgb = rgbSettings };
    }

    private AppDefaults CreateUnconfiguredActionDefaults()
    {
        var settings = CreateCurrentDefaults();
        settings.Input = AutomationInputIds.Unset;
        settings.MouseButton = AutomationInputIds.Unset;
        settings.CustomKey = 0;
        settings.CustomSequence = [];
        return settings;
    }

    private string? ExportFullBackup(
        BackupScope scope,
        string path,
        RgbSettings? currentRgbSettings,
        ApplicationPreferences? currentApplicationPreferences)
    {
        try
        {
            CaptureCurrentActionToProfile();
            var simpleDefaults = WithoutRgb(ReadDefaultsFile(SimpleDefaultsPath, new AppDefaults()));
            var advancedDefaults = WithoutRgb(ReadDefaultsFile(AdvancedSharedDefaultsPath, new AppDefaults()));
            var document = new ConfigBackupDocument { Scope = scope };

            if (SettingsScopeRules.IncludesSimple(scope))
            {
                document.LegacySharedDefaultsJson = JsonSerializer.Serialize(simpleDefaults);
                document.SimpleDefaultsJson = document.LegacySharedDefaultsJson;
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
                document.RgbJson = JsonSerializer.Serialize(currentRgbSettings ?? rgbSettings);
                document.ApplicationPreferencesJson = JsonSerializer.Serialize(currentApplicationPreferences ?? CurrentApplicationPreferences());
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
            Status($"{BackupScopeInfo.DisplayName(scope)} restored from backup.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
            return null;
        }
        catch (Exception exception) { AppLog.Error($"Could not restore {BackupScopeInfo.DisplayName(scope)}", exception); return $"Could not restore backup: {exception.Message}"; }
    }

    private void RestoreEverything(ConfigBackupDocument backup)
    {
        var simpleDefaults = ReadSimpleDefaults(backup);
        var advancedDefaults = ReadAdvancedDefaults(backup);
        var rgb = string.IsNullOrWhiteSpace(backup.RgbJson) ? new RgbSettings() : JsonSerializer.Deserialize<RgbSettings>(backup.RgbJson) ?? throw new InvalidDataException("Backup RGB settings are invalid.");
        var preferences = ReadApplicationPreferences(backup);
        var library = ReadSequenceLibrary(backup);
        var profiles = ReadProfiles(backup, advancedDefaults);
        if (!string.IsNullOrWhiteSpace(backup.AppearanceJson) && !ThemeManager.TryImportConfiguration(backup.AppearanceJson)) throw new InvalidDataException("Backup appearance settings are invalid.");

        rgbSettings = rgb;
        SaveRgbSettings();
        crashRecoveryEnabled = preferences.CrashRecoveryEnabled;
        workerPriority = WorkerPriorityRules.Normalize(preferences.WorkerPriority);
        cadenceDiagnosticsEnabled = preferences.CadenceDiagnosticsEnabled;
        advancedMode = preferences.AdvancedMode;
        keyboardHotkeyModifiersEnabled = preferences.KeyboardHotkeyModifiersEnabled;
        rememberPinned = preferences.RememberPinned;
        applyPinnedOnLaunch = preferences.ApplyPinnedOnLaunch;
        pinnedPreference = PinnedWindowPreferenceRules.PersistedPinnedState(rememberPinned, preferences.Pinned);
        lastNormalWindowPosition = preferences.MainWindowPosition is { } restoredPosition
            ? new WindowPixelPosition(restoredPosition.Left, restoredPosition.Top)
            : null;
        deferredPinPending = false;
        Topmost = pinnedPreference;
        compactMode = preferences.CompactMode;
        quickStartSeen = preferences.QuickStartSeen;
        WriteOrThrow(SimpleDefaultsPath, simpleDefaults);
        RestoreAdvancedSettings(advancedDefaults, profiles, refreshUi: false);
        RestoreSequenceLibrary(library);
        ApplyDefaults(advancedMode ? ActiveProfileAction() is { } action ? ResolveActionSettings(action) : advancedDefaults : simpleDefaults);
        UpdatePinUi();
        ApplyCompactMode();
        ApplyModeUi();
        SaveApplicationPreferences();
        CrashRecovery.UpdateEnabled(crashRecoveryEnabled);
        UpdateThemeButton();
        RestoreLiveArea();
        RegisterConfiguredHotkey();
    }

    private void RestoreSimpleSettings(AppDefaults settings)
    {
        settings = WithoutRgb(settings);
        WriteOrThrow(SimpleDefaultsPath, settings);
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
        WriteOrThrow(AdvancedSharedDefaultsPath, sharedDefaults);
        automationProfiles = profiles.Profiles.Count == 0 ? AutomationProfileStore.CreateInitial(sharedDefaults) : profiles;
        var activeProfile = ActiveProfile() ?? automationProfiles.Profiles.First();
        var activeAction = activeProfile.Actions.FirstOrDefault(action => action.Id == automationProfiles.ActiveActionId) ?? activeProfile.Actions.FirstOrDefault();
        automationProfiles.ActiveProfileId = activeProfile.Id;
        automationProfiles.ActiveActionId = activeAction?.Id ?? string.Empty;
        unsavedProfileId = null;
        PersistAutomationProfiles();
        profilesDirty = false;
        if (!advancedMode) return;
        if (activeAction is null) ShowAdvancedSharedDefaults();
        else
        {
            editorSession.EnterHotkey(activeAction.Id);
            ApplyDefaults(ResolveActionSettings(activeAction));
        }
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
        var json = !string.IsNullOrWhiteSpace(backup.SimpleDefaultsJson) ? backup.SimpleDefaultsJson : backup.LegacySharedDefaultsJson;
        return JsonSerializer.Deserialize<AppDefaults>(json) ?? throw new InvalidDataException("The backup does not contain valid Simple mode settings.");
    }

    private static AppDefaults ReadAdvancedDefaults(ConfigBackupDocument backup)
    {
        var json = !string.IsNullOrWhiteSpace(backup.AdvancedDefaultsJson) ? backup.AdvancedDefaultsJson : backup.LegacySharedDefaultsJson;
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

    private ApplicationPreferences CurrentApplicationPreferences() => new()
    {
        Pinned = PinnedWindowPreferenceRules.PersistedPinnedState(rememberPinned, pinnedPreference),
        RememberPinned = rememberPinned,
        ApplyPinnedOnLaunch = applyPinnedOnLaunch,
        CompactMode = compactMode,
        QuickStartSeen = quickStartSeen,
        WorkerPriority = workerPriority.ToString(),
        CadenceDiagnosticsEnabled = cadenceDiagnosticsEnabled,
        AdvancedMode = advancedMode,
        KeyboardHotkeyModifiersEnabled = keyboardHotkeyModifiersEnabled,
        CrashRecoveryEnabled = crashRecoveryEnabled,
        MainWindowPosition = lastNormalWindowPosition is { } position
            ? new PersistedWindowPosition { Left = position.Left, Top = position.Top }
            : null
    };

    private static ApplicationPreferences ReadApplicationPreferences(ConfigBackupDocument backup)
    {
        var usesLegacyPreferences = string.IsNullOrWhiteSpace(backup.ApplicationPreferencesJson);
        var json = usesLegacyPreferences ? backup.LegacyApplicationPreferencesJson : backup.ApplicationPreferencesJson;
        var preferences = string.IsNullOrWhiteSpace(json)
            ? new ApplicationPreferences()
            : JsonSerializer.Deserialize<ApplicationPreferences>(json) ?? throw new InvalidDataException("Backup application preferences are invalid.");

        if (usesLegacyPreferences && ApplicationPreferencesStore.TryReadLegacyCrashRecoveryEnabledFromJson(backup.RgbJson) is { } enabled)
            preferences.CrashRecoveryEnabled = enabled;
        return preferences;
    }

    private static void WriteOrThrow(string path, AppDefaults settings)
    {
        if (!WriteDefaults(path, settings)) throw new IOException("Could not write restored settings.");
    }

    private bool LoadSimpleDefaults()
    {
        try
        {
            if (!File.Exists(SimpleDefaultsPath)) return false;
            var s = JsonSerializer.Deserialize<AppDefaults>(File.ReadAllText(SimpleDefaultsPath)); if (s is null) return false;
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
            SequenceItem.Content = AutomationInputLabels.CustomSequence;
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
        SequenceItem.Content = AutomationInputLabels.CustomSequence;
        updatingActionSelection = true; ButtonCombo.SelectedItem = SequenceItem; updatingActionSelection = false;
        UpdateLiveInputMode();
        CommitSelectedActionChange();
        Status($"Ready - {preset.Name} will be repeated.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
    }

    private void LoadRgbSettings()
    {
        try
        {
            if (File.Exists(RgbSettingsPath)) rgbSettings = JsonSerializer.Deserialize<RgbSettings>(File.ReadAllText(RgbSettingsPath)) ?? rgbSettings;
        }
        catch { }
    }

    private void LoadApplicationPreferences()
    {
        var preferences = ApplicationPreferencesRepository.Load();
        rememberPinned = preferences.RememberPinned;
        applyPinnedOnLaunch = preferences.ApplyPinnedOnLaunch;
        pinnedPreference = PinnedWindowPreferenceRules.PersistedPinnedState(rememberPinned, preferences.Pinned);
        Topmost = PinnedWindowPreferenceRules.ApplyOnLaunch(preferences);
        deferredPinPending = PinnedWindowPreferenceRules.DeferUntilInteraction(preferences);
        compactMode = preferences.CompactMode;
        quickStartSeen = preferences.QuickStartSeen;
        workerPriority = WorkerPriorityRules.Normalize(preferences.WorkerPriority);
        cadenceDiagnosticsEnabled = preferences.CadenceDiagnosticsEnabled;
        crashRecoveryEnabled = preferences.CrashRecoveryEnabled;
        advancedMode = preferences.AdvancedMode;
        keyboardHotkeyModifiersEnabled = preferences.KeyboardHotkeyModifiersEnabled;
        lastNormalWindowPosition = preferences.MainWindowPosition is { } position
            ? new WindowPixelPosition(position.Left, position.Top)
            : null;
        if (lastNormalWindowPosition is not null) WindowStartupLocation = WindowStartupLocation.Manual;
        if (!advancedMode) LoadSimpleDefaults();
        UpdatePinUi();
        ApplyCompactMode();
        ApplyModeUi();
    }

    private void SaveApplicationPreferences()
    {
        try { ApplicationPreferencesRepository.Save(CurrentApplicationPreferences()); }
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
        if (combo.SelectedItem is not ComboBoxItem item) return AutomationInputIds.Unset;
        return item.Tag?.ToString() ?? item.Content.ToString()!;
    }
    private static void Select(ComboBox combo, string value)
    {
        if (value == AutomationInputIds.Unset)
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
    private void RefreshTaskbarActivityIndicator()
    {
        var presentation = AutomationActivityState.GetTaskbarPresentation(clickCancellation is not null, profileRuns.Count);
        TaskbarActivityIndicator.Overlay = presentation.ShowActiveBadge ? (ImageSource)FindResource("TaskbarActiveOverlay") : null;
        TaskbarActivityIndicator.ProgressState = presentation.ShowIndeterminateProgress ? TaskbarItemProgressState.Indeterminate : TaskbarItemProgressState.None;
        TaskbarActivityIndicator.Description = presentation.IsActive ? $"{AppIdentity.Name} active" : AppIdentity.Name;
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
        return key switch { System.Windows.Input.Key.Return => AutomationInputIds.Enter, System.Windows.Input.Key.Space => AutomationInputIds.Space, _ => key.ToString() };
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
        var revision = Status("OpenRGB started automatically.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            if (!Dispatcher.HasShutdownStarted)
                _ = Dispatcher.BeginInvoke(() =>
                {
                    if (statusRevision != revision) return;
                    if (IsClicking)
                        Status($"{ActivityVerb()} - press {FormatHotkey()} to stop.", ThemeManager.Brush(ThemeResourceKeys.ErrorBrush));
                    else
                        Status($"Ready - press {FormatHotkey()} to start or stop.", ThemeManager.Brush(ThemeResourceKeys.SuccessBrush));
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
        CaptureNormalWindowPosition();
        SaveApplicationPreferences();
        // Cancel first, then give the worker a short chance to release native resources.
        isClosing = true;
        OpenRgbHighlighter.SuppressAutoStart();
        var activeTask = clickTask;
        StopClicking();
        foreach (var cancellation in profileRuns.Values) cancellation.Cancel();
        var runningProfileTasks = profileTasks.Values.ToArray();
        try { activeTask?.Wait(TimeSpan.FromSeconds(2)); } catch (Exception exception) { AppLog.Error("Error while waiting for worker shutdown", exception); }
        try { Task.WaitAll(runningProfileTasks, TimeSpan.FromSeconds(2)); } catch (Exception exception) { AppLog.Error("Error while waiting for profile worker shutdown", exception); }
        var rgbTasks = StopAllRgbIndicators();
        try { Task.WaitAll(rgbTasks, TimeSpan.FromSeconds(2)); } catch (Exception exception) { AppLog.Error("Error while restoring OpenRGB lighting", exception); }
        try { ApplyIdleOpenRgbProfileAsync(allowAutoStart: false).Wait(TimeSpan.FromSeconds(2)); } catch (Exception exception) { AppLog.Error("Error while applying the final OpenRGB idle profile", exception); }
        if (rgbSettings.StopAutoStartedOnExit) OpenRgbHighlighter.StopAutoStartedServer();
        // Release UI timers and the Windows hotkey hook last.
        resetTimer.Stop(); flashTimer.Stop(); guiHeartbeatTimer.Stop(); if (hotkeyRegistered) UnregisterPrimaryHotkeys(); foreach (var id in registeredProfileHotkeys.Keys) UnregisterHotKey(hwnd, id); mouseHotkeys.Clear(); UpdateMouseHook(); if (hwndSource is not null) hwndSource.RemoveHook(WndProc);
    }

    private static Input[] CreateClickInputs(string button)
    {
        var wheel = button switch
        {
            AutomationInputIds.ScrollUp => (MouseFlags.Wheel, 120),
            AutomationInputIds.ScrollDown => (MouseFlags.Wheel, -120),
            AutomationInputIds.ScrollLeft => (MouseFlags.HorizontalWheel, -120),
            AutomationInputIds.ScrollRight => (MouseFlags.HorizontalWheel, 120),
            _ => (MouseFlags.None, 0)
        };
        if (wheel.Item1 != MouseFlags.None)
            return [new() { Type = 0, Data = new InputUnion { Mouse = new MouseInput { MouseData = unchecked((uint)wheel.Item2), Flags = wheel.Item1 } } }];

        var (down, up, mouseData) = button switch
        {
            AutomationInputIds.Right => (MouseFlags.RightDown, MouseFlags.RightUp, 0u),
            AutomationInputIds.Middle => (MouseFlags.MiddleDown, MouseFlags.MiddleUp, 0u),
            AutomationInputIds.Mouse4 => (MouseFlags.XDown, MouseFlags.XUp, 1u),
            AutomationInputIds.Mouse5 => (MouseFlags.XDown, MouseFlags.XUp, 2u),
            _ => (MouseFlags.LeftDown, MouseFlags.LeftUp, 0u)
        };
        return
        [
            new() { Type = 0, Data = new InputUnion { Mouse = new MouseInput { MouseData = mouseData, Flags = down } } },
            new() { Type = 0, Data = new InputUnion { Mouse = new MouseInput { MouseData = mouseData, Flags = up } } }
        ];
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
        if (step.Input == AutomationInputIds.Delay) return new SequenceAction([], false, true, Math.Clamp(step.DelayAfterMilliseconds, 1, 600000), SequenceStepMode.Press, default);
        var key = step.Input switch { AutomationInputIds.Space => 0x20, AutomationInputIds.Enter => 0x0D, AutomationInputIds.Custom => step.CustomKey, _ => 0 };
        return new SequenceAction(key == 0 ? CreateClickInputs(step.Input) : CreateKeyInputs(key), key == 0, false, Math.Clamp(step.DelayAfterMilliseconds, 0, 600000), step.Mode, SequenceHoldRules.Identity(step));
    }).ToArray();

    private bool ExecuteSequence(
        ClickSettings settings,
        PrecisionTimer timer,
        CancellationTokenSource cancellation,
        ref bool watchdogExpired,
        CadenceDiagnostics? cadence,
        double scheduledTimestamp,
        Dictionary<SequenceInputIdentity, HeldSequenceInput> heldInputs,
        out bool sentSequenceAction)
    {
        sentSequenceAction = false;
        foreach (var step in settings.Sequence ?? [])
        {
            if (cancellation.IsCancellationRequested || watchdogExpired) return false;
            SendDueHeldKeyRepeats(heldInputs);
            if (step.IsDelay)
            {
                if (!WaitForSequenceDeadline(timer, Stopwatch.GetTimestamp() + step.DelayAfterMilliseconds * Stopwatch.Frequency / 1000d, heldInputs, cancellation, ref watchdogExpired)) return false;
                continue;
            }

            if (step.Mode == SequenceStepMode.Release)
            {
                if (heldInputs.Remove(step.Identity, out var held))
                {
                    SendNativeInput(1, [held.Action.Inputs[1]]);
                    cadence?.RecordUp();
                    sentSequenceAction = true;
                }
            }
            else if (CanSendAction(settings, step.IsMouse))
            {
                if (settings.FixedPosition && step.IsMouse) MoveCursor(settings.X, settings.Y);
                if (step.Mode == SequenceStepMode.Hold)
                {
                    if (!heldInputs.ContainsKey(step.Identity))
                    {
                        cadence?.RecordDown(scheduledTimestamp);
                        SendNativeInput(1, [step.Inputs[0]]);
                        heldInputs.Add(step.Identity, new HeldSequenceInput(
                            step,
                            step.IsMouse ? double.PositiveInfinity : Stopwatch.GetTimestamp() + Stopwatch.Frequency / 2d));
                    }
                    sentSequenceAction = true;
                }
                else
                {
                    var sent = cadence is null
                        ? SendAction(step.Inputs, false, settings.InputPulseMilliseconds, timer, cancellation, ref watchdogExpired, heldInputs)
                        : SendActionWithDiagnostics(step.Inputs, false, settings.InputPulseMilliseconds, timer, cancellation, ref watchdogExpired, cadence, scheduledTimestamp, heldInputs);
                    if (!sent) return false;
                    sentSequenceAction = true;
                }
            }

            if (step.DelayAfterMilliseconds > 0 &&
                !WaitForSequenceDeadline(timer, Stopwatch.GetTimestamp() + step.DelayAfterMilliseconds * Stopwatch.Frequency / 1000d, heldInputs, cancellation, ref watchdogExpired)) return false;
        }
        return !cancellation.IsCancellationRequested && !watchdogExpired;
    }

    private bool WaitForSequenceDeadline(
        PrecisionTimer timer,
        double deadline,
        Dictionary<SequenceInputIdentity, HeldSequenceInput> heldInputs,
        CancellationTokenSource cancellation,
        ref bool watchdogExpired)
    {
        while (!cancellation.IsCancellationRequested && Stopwatch.GetTimestamp() < deadline)
        {
            var nextRepeat = heldInputs.Count == 0
                ? double.PositiveInfinity
                : heldInputs.Values.Min(held => held.NextRepeatAt);
            if (!WaitUntilGuiIsHealthy(timer, Math.Min(deadline, nextRepeat), cancellation, ref watchdogExpired)) return false;
            if (cancellation.IsCancellationRequested) return false;
            SendDueHeldKeyRepeats(heldInputs);
        }
        return !cancellation.IsCancellationRequested && !watchdogExpired;
    }

    private static void SendDueHeldKeyRepeats(Dictionary<SequenceInputIdentity, HeldSequenceInput> heldInputs)
    {
        var now = Stopwatch.GetTimestamp();
        foreach (var held in heldInputs.Values)
        {
            if (held.Action.IsMouse || held.NextRepeatAt > now) continue;
            SendNativeInput(1, [held.Action.Inputs[0]]);
            held.NextRepeatAt = now + Stopwatch.Frequency / 30d;
        }
    }

    private static void ReleaseHeldSequenceInputs(Dictionary<SequenceInputIdentity, HeldSequenceInput> heldInputs)
    {
        foreach (var held in heldInputs.Values.Reverse())
        {
            try { SendNativeInput(1, [held.Action.Inputs[1]]); }
            catch (Exception exception) { AppLog.Error("Could not release a held sequence input", exception); }
        }
        heldInputs.Clear();
    }

    private static bool IsExtendedKey(int virtualKey) => virtualKey is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E or 0x5B or 0x5C or 0x5D or 0xA3 or 0xA5 or 0x6F;
    private bool SendAction(
        Input[] inputs,
        bool doubleClick,
        int pulseMilliseconds,
        PrecisionTimer timer,
        CancellationTokenSource cancellation,
        ref bool watchdogExpired,
        Dictionary<SequenceInputIdentity, HeldSequenceInput>? heldInputs = null)
    {
        if (inputs.Length == 1)
        {
            SendNativeInput(1, inputs);
            if (doubleClick) SendNativeInput(1, inputs);
            return true;
        }
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
                if (!WaitForInputDeadline(timer, Stopwatch.GetTimestamp() + pulseTicks, heldInputs, cancellation, ref watchdogExpired)) return false;
            }
            finally
            {
                SendNativeInput(1, [inputs[1]]);
            }
        }
        return true;
    }

    private bool SendActionWithDiagnostics(
        Input[] inputs,
        bool doubleClick,
        int pulseMilliseconds,
        PrecisionTimer timer,
        CancellationTokenSource cancellation,
        ref bool watchdogExpired,
        CadenceDiagnostics cadence,
        double scheduledTimestamp,
        Dictionary<SequenceInputIdentity, HeldSequenceInput>? heldInputs = null)
    {
        if (inputs.Length == 1)
        {
            cadence.RecordDown(scheduledTimestamp);
            SendNativeInput(1, inputs);
            if (doubleClick)
            {
                cadence.RecordDown(scheduledTimestamp);
                SendNativeInput(1, inputs);
            }
            return true;
        }
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
                if (!WaitForInputDeadline(timer, Stopwatch.GetTimestamp() + pulseTicks, heldInputs, cancellation, ref watchdogExpired)) return false;
            }
            finally
            {
                cadence.RecordUp();
                SendNativeInput(1, [inputs[1]]);
            }
        }
        return true;
    }

    private bool WaitForInputDeadline(
        PrecisionTimer timer,
        double deadline,
        Dictionary<SequenceInputIdentity, HeldSequenceInput>? heldInputs,
        CancellationTokenSource cancellation,
        ref bool watchdogExpired) => heldInputs is null
        ? WaitUntilGuiIsHealthy(timer, deadline, cancellation, ref watchdogExpired)
        : WaitForSequenceDeadline(timer, deadline, heldInputs, cancellation, ref watchdogExpired);

    private static bool CanSendAction(ClickSettings settings, bool isMouse) =>
        !settings.Target.IsEnabled ||
        (WindowTargeting.IsForeground(settings.Target) &&
         (!settings.FixedPosition || !isMouse || WindowTargeting.IsPointInForegroundClientArea(settings.X, settings.Y)));

    private sealed record ClickSettings(bool FixedPosition, int X, int Y, string Button, int? KeyboardVirtualKey, bool DoubleClick, bool Hold, int? MaximumClicks, SequenceAction[]? Sequence, int InputPulseMilliseconds, long JitterMaximumMilliseconds, WorkerPriorityOption WorkerPriority, bool CadenceDiagnosticsEnabled, TargetWindowRule Target);
    private sealed record SequenceAction(Input[] Inputs, bool IsMouse, bool IsDelay, int DelayAfterMilliseconds, SequenceStepMode Mode, SequenceInputIdentity Identity);
    private sealed class HeldSequenceInput(SequenceAction action, double nextRepeatAt)
    {
        internal SequenceAction Action { get; } = action;
        internal double NextRepeatAt { get; set; } = nextRepeatAt;
    }
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

    [Flags] private enum MouseFlags : uint { None = 0, LeftDown = 2, LeftUp = 4, RightDown = 8, RightUp = 16, MiddleDown = 32, MiddleUp = 64, XDown = 128, XUp = 256, Wheel = 2048, HorizontalWheel = 4096 }
    [Flags] private enum KeyboardFlags : uint { None = 0, ExtendedKey = 1, KeyUp = 2, ScanCode = 8 }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int Dx, Dy; public uint MouseData; public MouseFlags Flags; public uint Time; public nint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort VirtualKey; public ushort ScanCode; public KeyboardFlags Flags; public uint Time; public nint ExtraInfo; }

    private static uint SendNativeInput(uint count, Input[] inputs)
    {
        if (AppRuntime.IsEndToEndTest)
        {
            var dispatched = inputs.Take((int)Math.Min(count, (uint)inputs.Length));
            AppRuntime.RecordEndToEndEvent("input", $"packets={count};data={string.Join(',', dispatched.Select(DescribeEndToEndInput))}");
            return count;
        }
        lock (nativeInputLock)
            return SendInput(count, inputs, Marshal.SizeOf<Input>());
    }

    private static string DescribeEndToEndInput(Input input) => input.Type switch
    {
        0 => $"mouse:{(uint)input.Data.Mouse.Flags}:data={unchecked((int)input.Data.Mouse.MouseData)}",
        1 => $"keyboard:vk={input.Data.Keyboard.VirtualKey}:scan={input.Data.Keyboard.ScanCode}:flags={(uint)input.Data.Keyboard.Flags}",
        _ => $"type:{input.Type}"
    };

    private static bool MoveCursor(int x, int y)
    {
        if (!AppRuntime.IsEndToEndTest) return SetCursorPos(x, y);
        AppRuntime.RecordEndToEndEvent("cursor", $"x={x};y={y}");
        return true;
    }

    [DllImport(NativeLibraryNames.User32, SetLastError = true)] private static extern bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint vk);
    [DllImport(NativeLibraryNames.User32)] private static extern bool UnregisterHotKey(nint hWnd, int id);
    [DllImport(NativeLibraryNames.User32, SetLastError = true)] private static extern nint SetWindowsHookEx(int idHook, LowLevelMouseProc callback, nint module, uint threadId);
    [DllImport(NativeLibraryNames.User32, SetLastError = true)] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport(NativeLibraryNames.User32)] private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport(NativeLibraryNames.User32)] private static extern short GetAsyncKeyState(int virtualKey);
    [DllImport(NativeLibraryNames.Kernel32, CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? moduleName);
    [DllImport(NativeLibraryNames.User32)] private static extern bool SetCursorPos(int x, int y);
    [DllImport(NativeLibraryNames.User32)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport(NativeLibraryNames.User32)] private static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport(NativeLibraryNames.Kernel32, CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWaitableTimerEx(nint attributes, string? name, uint flags, uint desiredAccess);
    [DllImport(NativeLibraryNames.Kernel32, CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWaitableTimer(nint attributes, bool manualReset, string? name);
    [DllImport(NativeLibraryNames.Kernel32, SetLastError = true)] private static extern bool SetWaitableTimer(nint timer, ref long dueTime, int period, nint completionRoutine, nint argument, bool resume);
    [DllImport(NativeLibraryNames.Kernel32, SetLastError = true)] private static extern uint WaitForMultipleObjects(uint count, nint[] handles, bool waitAll, uint milliseconds);
    [DllImport(NativeLibraryNames.Kernel32)] private static extern bool CloseHandle(nint handle);
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
    // Active workers lock editing and profile management, but another configured tile must remain startable so
    // Advanced actions can be launched concurrently from the UI as well as from their global hotkeys.
    public bool CanStart => !IsRunning && InputRules.IsConfiguredAction(
        string.IsNullOrWhiteSpace(Action.Settings.Input) ? Action.Settings.MouseButton : Action.Settings.Input,
        Action.Settings.CustomKey,
        Action.Settings.CustomSequence?.Count ?? 0);
    public bool CanStop => IsRunning;
    public Visibility InlineActionControlsVisibility => ShowInlineActionControls ? Visibility.Visible : Visibility.Collapsed;
    public int ActionLabelColumnSpan => ShowInlineActionControls ? 1 : 3;
    public bool HotkeyEnabled => Action.HotkeyEnabled;
    public string HotkeyLabel => HotkeyCapturePending ? "Waiting..." : HotkeyFormatter.Format(Action.Settings.Hotkey, Action.Settings.HotkeyModifiers, Action.Settings.HotkeyTrigger);
    public string HotkeyTooltip
    {
        get
        {
            var state = Action.HotkeyEnabled ? $"Hotkey: {HotkeyLabel}" : $"Hotkey disabled: {HotkeyLabel}";
            return Action.EnableToggleHotkey?.IsConfigured == true
                ? $"{state}\nEnable/disable: {Action.EnableToggleHotkey}"
                : state;
        }
    }
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
