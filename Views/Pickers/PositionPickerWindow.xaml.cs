// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace AutoClicker;

public partial class PositionPickerWindow : Window
{
    private readonly DispatcherTimer cursorTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private readonly LowLevelMouseProc mouseHookProc;
    private nint mouseHook;

    public int SelectedX { get; private set; }
    public int SelectedY { get; private set; }

    public PositionPickerWindow()
    {
        InitializeComponent();
        mouseHookProc = MouseHookCallback;
        cursorTimer.Tick += (_, _) => UpdateCursorPosition();
        Loaded += (_, _) =>
        {
            if (AppRuntime.IsEndToEndTest)
            {
                UpdateCursorPosition();
                Activate();
                Focus();
                return;
            }
            mouseHook = SetWindowsHookEx(WhMouseLl, mouseHookProc, GetModuleHandle(Process.GetCurrentProcess().MainModule?.ModuleName), 0);
            if (mouseHook == 0)
            {
                DialogResult = false;
                return;
            }
            UpdateCursorPosition();
            cursorTimer.Start();
            Activate();
            Focus();
        };
        Closed += (_, _) =>
        {
            cursorTimer.Stop();
            ReleaseMouseHook();
        };
    }

    private void UpdateCursorPosition()
    {
        if (!GetCursorPos(out var position)) return;
        SelectedX = position.X;
        SelectedY = position.Y;
        PositionLabel.Text = $"X: {SelectedX}   Y: {SelectedY}";
        PositionBadge(position);
    }

    private void PositionBadge(NativePoint position)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0 || !GetWindowRect(handle, out var bounds)) return;
        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        var workArea = GetWorkArea(position);
        var x = position.X + 16;
        var y = position.Y + 20;
        if (x + width > workArea.Right) x = position.X - width - 16;
        if (y + height > workArea.Bottom) y = position.Y - height - 20;
        SetWindowPos(handle, TopmostWindow, x, y, 0, 0, NoActivate | NoSize | ShowWindow);
    }

    private nint MouseHookCallback(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && wParam.ToInt32() == WmLButtonDown)
        {
            var data = Marshal.PtrToStructure<MouseHookData>(lParam);
            Dispatcher.BeginInvoke(() =>
            {
                SelectedX = data.Point.X;
                SelectedY = data.Point.Y;
                DialogResult = true;
            });
            return new nint(1);
        }
        return CallNextHookEx(mouseHook, code, wParam, lParam);
    }

    private void ReleaseMouseHook()
    {
        if (mouseHook == 0) return;
        UnhookWindowsHookEx(mouseHook);
        mouseHook = 0;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        DialogResult = false;
        e.Handled = true;
    }

    private static NativeRect GetWorkArea(NativePoint point)
    {
        var monitor = MonitorFromPoint(point, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(monitor, ref monitorInfo) ? monitorInfo.WorkArea : new NativeRect { Right = int.MaxValue, Bottom = int.MaxValue };
    }

    private const int WhMouseLl = 14;
    private const int WmLButtonDown = 0x0201;
    private const uint MonitorDefaultToNearest = 2;
    private static readonly nint TopmostWindow = new(-1);
    private const uint NoSize = 0x0001;
    private const uint NoActivate = 0x0010;
    private const uint ShowWindow = 0x0040;

    private delegate nint LowLevelMouseProc(int code, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelMouseProc callback, nint module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint window, out NativeRect rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookData { public NativePoint Point; public uint MouseData; public uint Flags; public uint Time; public nint ExtraInfo; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor; public NativeRect WorkArea; public uint Flags; }
}
