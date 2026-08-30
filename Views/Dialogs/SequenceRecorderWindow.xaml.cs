// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace AutoClicker;

public partial class SequenceRecorderWindow : Window
{
    private readonly LowLevelKeyboardProc keyboardHookProc;
    private readonly LowLevelMouseProc mouseHookProc;
    private readonly DispatcherTimer displayTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private SequenceRecordingBuilder? recording;
    private List<SequenceStep> recordedSteps = [];
    private SequenceStep? pendingDeleteStep;
    private SequenceStep? selectionClickCandidate;
    private bool collapseSelectionOnClick;
    private nint keyboardHook;
    private nint mouseHook;
    private long recordingStartedAt;
    private bool isRecording;
    private bool closing;

    public IReadOnlyList<SequenceStep> Steps => recordedSteps.Select(step => step.Clone()).ToList();

    public SequenceRecorderWindow()
    {
        InitializeComponent();
        keyboardHookProc = KeyboardHookCallback;
        mouseHookProc = MouseHookCallback;
        displayTimer.Tick += (_, _) => RefreshRecordingStatus();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        recording = new SequenceRecordingBuilder(
            IncludeDelaysCheckBox.IsChecked == true,
            TreatBriefTapsAsPressesCheckBox.IsChecked == true);
        recordedSteps = [];
        pendingDeleteStep = null;
        selectionClickCandidate = null;
        collapseSelectionOnClick = false;
        RecordedEventsList.ItemsSource = null;
        RecordedEventsPanel.Visibility = Visibility.Collapsed;
        EventCountLabel.Text = "No events";
        if (!InstallHooks())
        {
            StatusLabel.Text = "Could not start recording";
            DetailLabel.Text = "Windows did not allow the input recorder to start.";
            StatusLabel.Foreground = ThemeManager.Brush(ThemeResourceKeys.ErrorBrush);
            return;
        }

        isRecording = true;
        recordingStartedAt = Stopwatch.GetTimestamp();
        IncludeDelaysCheckBox.IsEnabled = false;
        TreatBriefTapsAsPressesCheckBox.IsEnabled = false;
        StartButton.Visibility = Visibility.Collapsed;
        StopButton.Visibility = Visibility.Visible;
        UseButton.IsEnabled = false;
        RecordingIndicator.Fill = ThemeManager.Brush(ThemeResourceKeys.ErrorBrush);
        StatusLabel.Foreground = ThemeManager.Brush(ThemeResourceKeys.TextStrongBrush);
        Topmost = !AppRuntime.IsEndToEndTest;
        displayTimer.Start();
        RefreshRecordingStatus();
        Focus();
        Keyboard.Focus(this);
    }

    private void StopButton_Click(object sender, RoutedEventArgs e) => StopRecording();

    private void StopRecording()
    {
        if (!isRecording) return;
        isRecording = false;
        ReleaseHooks();
        displayTimer.Stop();
        recording?.Complete(CurrentMilliseconds());
        recordedSteps = recording?.Build().Select(step => step.Clone()).ToList() ?? [];
        pendingDeleteStep = null;
        Topmost = false;
        IncludeDelaysCheckBox.IsEnabled = true;
        TreatBriefTapsAsPressesCheckBox.IsEnabled = true;
        StartButton.Content = "Record again";
        StartButton.Visibility = Visibility.Visible;
        StopButton.Visibility = Visibility.Collapsed;
        RecordingIndicator.Fill = ThemeManager.Brush(ThemeResourceKeys.SuccessBrush);
        RefreshRecordedEvents();
        UpdateStoppedRecordingSummary();
    }

    private void UseButton_Click(object sender, RoutedEventArgs e)
    {
        if (recordedSteps.Count == 0) return;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!isRecording && e.Key == System.Windows.Input.Key.Delete && RecordedEventsList.SelectedItems.Count > 0)
        {
            DeleteRecordedSteps(SelectedRecordedStepsInOrder());
            e.Handled = true;
        }
    }

    private void Window_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (pendingDeleteStep is null) return;
        var button = FindParent<Button>(e.OriginalSource as DependencyObject);
        if (button?.ToolTip is "Confirm deletion" or "Keep this recorded event" or "Delete this recorded event") return;
        pendingDeleteStep = null;
        RefreshRecordedEvents();
    }

    private void RecordedEventsList_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        collapseSelectionOnClick = false;
        selectionClickCandidate = FindParent<Button>(e.OriginalSource as DependencyObject) is null
            ? RecordedStepAt(e.OriginalSource as DependencyObject)
            : null;
        if (selectionClickCandidate is null) return;

        var noModifiers = System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.None;
        if (!noModifiers) return;
        var wasSelected = SelectedRecordedStepsInOrder().Contains(selectionClickCandidate);
        collapseSelectionOnClick = wasSelected && RecordedEventsList.SelectedItems.Count > 1;
        if (!wasSelected) SelectOnlyRecordedSteps([selectionClickCandidate]);
        else FocusRecordedStep(selectionClickCandidate);
        e.Handled = true;
    }

    private void RecordedEventsList_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var clicked = selectionClickCandidate;
        selectionClickCandidate = null;
        var collapse = collapseSelectionOnClick;
        collapseSelectionOnClick = false;
        if (!collapse || clicked is null || !ReferenceEquals(clicked, RecordedStepAt(e.OriginalSource as DependencyObject))) return;
        SelectOnlyRecordedSteps([clicked]);
        e.Handled = true;
    }

    private void RecordedEventsList_PreviewMouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var clicked = RecordedStepAt(e.OriginalSource as DependencyObject);
        if (clicked is not null && !SelectedRecordedStepsInOrder().Contains(clicked)) SelectOnlyRecordedSteps([clicked]);
        var selected = SelectedRecordedStepsInOrder();
        var item = new MenuItem
        {
            Header = selected.Count <= 1 ? "Delete" : $"Delete {selected.Count:N0} events",
            IsEnabled = selected.Count > 0,
            InputGestureText = "Del",
            Style = FindResource("DangerMenuItem") as Style
        };
        AutomationProperties.SetAutomationId(item, SequenceRecorderAutomationIds.DeleteRecordedEvents);
        item.Click += (_, _) => DeleteRecordedSteps(SelectedRecordedStepsInOrder());
        var menu = new ContextMenu { PlacementTarget = RecordedEventsList };
        menu.Items.Add(item);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void DeleteRecordedEvent_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not SequenceStep step || !recordedSteps.Contains(step)) return;
        if (!ReferenceEquals(pendingDeleteStep, step))
        {
            pendingDeleteStep = step;
            RefreshRecordedEvents();
            SelectOnlyRecordedSteps([step]);
            return;
        }
        DeleteRecordedSteps([step]);
    }

    private void CancelDeleteRecordedEvent_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not SequenceStep step || !ReferenceEquals(pendingDeleteStep, step)) return;
        pendingDeleteStep = null;
        RefreshRecordedEvents();
        SelectOnlyRecordedSteps([step]);
    }

    private void DeleteRecordedSteps(IReadOnlyList<SequenceStep> selected)
    {
        if (selected.Count == 0) return;
        var nextIndex = selected.Min(step => recordedSteps.IndexOf(step));
        foreach (var step in selected) recordedSteps.Remove(step);
        pendingDeleteStep = null;
        RefreshRecordedEvents();
        if (recordedSteps.Count > 0) SelectOnlyRecordedSteps([recordedSteps[Math.Min(nextIndex, recordedSteps.Count - 1)]]);
        UpdateStoppedRecordingSummary();
    }

    private void RefreshRecordedEvents()
    {
        RecordedEventsList.ItemsSource = recordedSteps
            .Select(step => new RecordedSequenceEventRow(step, ReferenceEquals(step, pendingDeleteStep)))
            .ToList();
        RecordedEventsPanel.Visibility = Visibility.Visible;
        EmptyRecordedEventsLabel.Visibility = recordedSteps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateStoppedRecordingSummary()
    {
        UseButton.IsEnabled = recordedSteps.Count > 0;
        StatusLabel.Text = recordedSteps.Count == 0 ? "Nothing recorded" : "Recording stopped";
        StatusLabel.Foreground = ThemeManager.Brush(recordedSteps.Count == 0 ? ThemeResourceKeys.WarningBrush : ThemeResourceKeys.TextStrongBrush);
        DetailLabel.Text = recordedSteps.Count == 0
            ? "Record again, or cancel to return to the sequence editor."
            : $"{recordedSteps.Count:N0} sequence event{(recordedSteps.Count == 1 ? string.Empty : "s")} ready to add.";
        EventCountLabel.Text = $"{recordedSteps.Count:N0} event{(recordedSteps.Count == 1 ? string.Empty : "s")}";
    }

    private List<SequenceStep> SelectedRecordedStepsInOrder()
    {
        var selected = RecordedEventsList.SelectedItems.OfType<RecordedSequenceEventRow>().Select(row => row.Step).ToHashSet();
        return recordedSteps.Where(selected.Contains).ToList();
    }

    private void SelectOnlyRecordedSteps(IEnumerable<SequenceStep> selected)
    {
        var targets = selected.ToHashSet();
        RecordedEventsList.SelectedItems.Clear();
        foreach (var row in RecordedEventsList.Items.OfType<RecordedSequenceEventRow>().Where(row => targets.Contains(row.Step)))
            RecordedEventsList.SelectedItems.Add(row);
        if (RecordedEventsList.SelectedItems.Count == 0) return;
        if (RecordedEventsList.SelectedItems[^1] is not RecordedSequenceEventRow focused) return;
        RecordedEventsList.ScrollIntoView(focused);
        FocusRecordedStep(focused.Step);
    }

    private void FocusRecordedStep(SequenceStep step)
    {
        var row = RecordedEventsList.Items.OfType<RecordedSequenceEventRow>().FirstOrDefault(candidate => ReferenceEquals(candidate.Step, step));
        if (row is null) return;
        RecordedEventsList.UpdateLayout();
        if (RecordedEventsList.ItemContainerGenerator.ContainerFromItem(row) is ListBoxItem item)
            item.Focus();
    }

    private SequenceStep? RecordedStepAt(DependencyObject? element) =>
        element is null ? null : (ItemsControl.ContainerFromElement(RecordedEventsList, element) as FrameworkElement)?.DataContext is RecordedSequenceEventRow row ? row.Step : null;

    private static T? FindParent<T>(DependencyObject? source) where T : DependencyObject
    {
        for (var current = source; current is not null; current = current switch
        {
            Visual or System.Windows.Media.Media3D.Visual3D => VisualTreeHelper.GetParent(current),
            FrameworkContentElement content => content.Parent,
            _ => LogicalTreeHelper.GetParent(current)
        })
            if (current is T match) return match;
        return null;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        closing = true;
        isRecording = false;
        displayTimer.Stop();
        ReleaseHooks();
    }

    private void RefreshRecordingStatus()
    {
        if (!isRecording || recording is null) return;
        var elapsed = TimeSpan.FromMilliseconds(CurrentMilliseconds() - ToMilliseconds(recordingStartedAt));
        StatusLabel.Text = "Recording…";
        DetailLabel.Text = $"{elapsed:mm\\:ss\\.f} elapsed · use Stop when finished";
        EventCountLabel.Text = "Capturing…";
    }

    private bool InstallHooks()
    {
        var module = GetModuleHandle(Process.GetCurrentProcess().MainModule?.ModuleName);
        keyboardHook = SetWindowsHookEx(WhKeyboardLl, keyboardHookProc, module, 0);
        if (keyboardHook == 0)
        {
            AppLog.Error("Could not install the sequence recorder keyboard hook", new Win32Exception(Marshal.GetLastWin32Error()));
            return false;
        }

        mouseHook = SetWindowsHookEx(WhMouseLl, mouseHookProc, module, 0);
        if (mouseHook != 0) return true;
        AppLog.Error("Could not install the sequence recorder mouse hook", new Win32Exception(Marshal.GetLastWin32Error()));
        ReleaseHooks();
        return false;
    }

    private void ReleaseHooks()
    {
        if (keyboardHook != 0) UnhookWindowsHookEx(keyboardHook);
        if (mouseHook != 0) UnhookWindowsHookEx(mouseHook);
        keyboardHook = 0;
        mouseHook = 0;
    }

    private nint KeyboardHookCallback(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && isRecording && !closing && recording is not null)
        {
            var data = Marshal.PtrToStructure<LowLevelKeyboardData>(lParam);
            var message = wParam.ToInt32();
            var isDown = message is WmKeyDown or WmSystemKeyDown;
            var isUp = message is WmKeyUp or WmSystemKeyUp;
            var virtualKey = (int)data.VirtualKey;
            if ((isDown || isUp)
                && virtualKey is > 0 and <= 0xFF
                && ((data.Flags & LowLevelKeyboardInjected) == 0 || AppRuntime.IsEndToEndTest))
                recording.Record(AutomationInputIds.Custom, virtualKey, isDown, CurrentMilliseconds());
        }
        return CallNextHookEx(keyboardHook, code, wParam, lParam);
    }

    private nint MouseHookCallback(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && isRecording && !closing && recording is not null)
        {
            var data = Marshal.PtrToStructure<LowLevelMouseData>(lParam);
            if (((data.Flags & LowLevelMouseInjected) == 0 || AppRuntime.IsEndToEndTest)
                && TryMapMouseMessage(wParam.ToInt32(), data.MouseData, out var input, out var isDown, out var isInstantaneous)
                && (!isDown || !ShouldIgnoreMouseDown(data.Point)))
            {
                if (isInstantaneous) recording.RecordPress(input, CurrentMilliseconds());
                else recording.Record(input, 0, isDown, CurrentMilliseconds());
            }
        }
        return CallNextHookEx(mouseHook, code, wParam, lParam);
    }

    private static bool TryMapMouseMessage(int message, uint mouseData, out string input, out bool isDown, out bool isInstantaneous)
    {
        var xButton = ((mouseData >> 16) & 0xffff) == 2 ? AutomationInputIds.Mouse5 : AutomationInputIds.Mouse4;
        var wheelDelta = unchecked((short)(mouseData >> 16));
        (input, isDown, isInstantaneous) = message switch
        {
            WmLeftButtonDown => (AutomationInputIds.Left, true, false),
            WmLeftButtonUp => (AutomationInputIds.Left, false, false),
            WmRightButtonDown => (AutomationInputIds.Right, true, false),
            WmRightButtonUp => (AutomationInputIds.Right, false, false),
            WmMiddleButtonDown => (AutomationInputIds.Middle, true, false),
            WmMiddleButtonUp => (AutomationInputIds.Middle, false, false),
            WmXButtonDown => (xButton, true, false),
            WmXButtonUp => (xButton, false, false),
            WmMouseWheel when wheelDelta > 0 => (AutomationInputIds.ScrollUp, false, true),
            WmMouseWheel when wheelDelta < 0 => (AutomationInputIds.ScrollDown, false, true),
            WmMouseHorizontalWheel when wheelDelta > 0 => (AutomationInputIds.ScrollRight, false, true),
            WmMouseHorizontalWheel when wheelDelta < 0 => (AutomationInputIds.ScrollLeft, false, true),
            _ => (string.Empty, false, false)
        };
        return input.Length > 0;
    }

    private static bool IsAutoClickerWindow(nint window)
    {
        if (window == 0) return false;
        _ = GetWindowThreadProcessId(window, out var processId);
        return processId == Environment.ProcessId;
    }

    private bool ShouldIgnoreMouseDown(NativePoint point)
    {
        var target = WindowFromPoint(point);
        if (!IsAutoClickerWindow(target)) return false;
        if (target != new WindowInteropHelper(this).Handle) return true;
        var relative = PointFromScreen(new Point(point.X, point.Y));
        var element = InputHitTest(relative) as DependencyObject;
        while (element is not null)
        {
            if (element is ButtonBase) return true;
            element = element switch
            {
                System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D => VisualTreeHelper.GetParent(element),
                FrameworkContentElement content => content.Parent,
                _ => LogicalTreeHelper.GetParent(element)
            };
        }
        return false;
    }

    private static long CurrentMilliseconds() => ToMilliseconds(Stopwatch.GetTimestamp());
    private static long ToMilliseconds(long timestamp) => timestamp * 1_000 / Stopwatch.Frequency;

    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSystemKeyDown = 0x0104;
    private const int WmSystemKeyUp = 0x0105;
    private const int WmLeftButtonDown = 0x0201;
    private const int WmLeftButtonUp = 0x0202;
    private const int WmRightButtonDown = 0x0204;
    private const int WmRightButtonUp = 0x0205;
    private const int WmMiddleButtonDown = 0x0207;
    private const int WmMiddleButtonUp = 0x0208;
    private const int WmMouseWheel = 0x020A;
    private const int WmXButtonDown = 0x020B;
    private const int WmXButtonUp = 0x020C;
    private const int WmMouseHorizontalWheel = 0x020E;
    private const uint LowLevelKeyboardInjected = 0x10;
    private const uint LowLevelMouseInjected = 0x1;

    private delegate nint LowLevelKeyboardProc(int code, nint wParam, nint lParam);
    private delegate nint LowLevelMouseProc(int code, nint wParam, nint lParam);

    [DllImport(NativeLibraryNames.User32, SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc callback, nint module, uint threadId);

    [DllImport(NativeLibraryNames.User32, SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelMouseProc callback, nint module, uint threadId);

    [DllImport(NativeLibraryNames.User32, SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport(NativeLibraryNames.User32)]
    private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport(NativeLibraryNames.User32)]
    private static extern nint WindowFromPoint(NativePoint point);

    [DllImport(NativeLibraryNames.User32)]
    private static extern uint GetWindowThreadProcessId(nint window, out int processId);

    [DllImport(NativeLibraryNames.Kernel32, CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct LowLevelKeyboardData { public uint VirtualKey; public uint ScanCode; public uint Flags; public uint Time; public nint ExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct LowLevelMouseData { public NativePoint Point; public uint MouseData; public uint Flags; public uint Time; public nint ExtraInfo; }
}

public sealed class RecordedSequenceEventRow
{
    public RecordedSequenceEventRow(SequenceStep step, bool deletePending)
    {
        Step = step;
        DeletePending = deletePending;
    }

    public SequenceStep Step { get; }
    public bool DeletePending { get; }
    public string Label => Step.ToString();
    public Visibility DeleteButtonVisibility => DeletePending ? Visibility.Collapsed : Visibility.Visible;
    public Visibility DeleteConfirmationVisibility => DeletePending ? Visibility.Visible : Visibility.Collapsed;
}
