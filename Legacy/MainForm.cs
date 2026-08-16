using System.Runtime.InteropServices;
using System.Text.Json;

namespace AutoClicker;

public sealed class MainForm : Form
{
    private const int HotkeyId = 0xC11C;
    private const int WmHotkey = 0x0312;

    private readonly NumericUpDown hours = Number(0, 999, 0);
    private readonly NumericUpDown minutes = Number(0, 59, 0);
    private readonly NumericUpDown seconds = Number(0, 59, 0);
    private readonly NumericUpDown milliseconds = Number(1, 999, 100);
    private readonly ComboBox mouseButton = Select("Left", "Right", "Middle");
    private readonly ComboBox clickType = Select("Single", "Double");
    private readonly RadioButton repeatUntilStopped = new() { Text = "Repeat until stopped", AutoSize = true, Checked = true };
    private readonly RadioButton repeatCount = new() { Text = "Repeat", AutoSize = true };
    private readonly NumericUpDown count = Number(1, 999999, 10, enabled: false);
    private readonly RadioButton currentPosition = new() { Text = "Current cursor position", AutoSize = true, Checked = true };
    private readonly RadioButton fixedPosition = new() { Text = "Pick a fixed position", AutoSize = true };
    private readonly NumericUpDown x = Number(-32768, 32767, 0, enabled: false);
    private readonly NumericUpDown y = Number(-32768, 32767, 0, enabled: false);
    private readonly Button startButton = new() { Text = "Start", AutoSize = false, Height = 42 };
    private readonly Button stopButton = new() { Text = "Stop", AutoSize = false, Height = 42, Enabled = false };
    private readonly Button saveDefault = new() { Text = "Set as default", AutoSize = false, Height = 42 };
    private readonly Label state = new() { Text = "Ready to click", AutoSize = true, ForeColor = Color.FromArgb(21, 128, 61) };
    private readonly Label hotkeyValue = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 13F), ForeColor = Color.FromArgb(15, 23, 42) };
    private readonly Button changeHotkey = new() { Text = "Change hotkey", AutoSize = true };
    private readonly Panel liveClickArea = new() { Size = new Size(176, 54), BackColor = Color.FromArgb(148, 163, 184), Cursor = Cursors.Cross };
    private readonly Label liveClickCaption = new() { Text = "LIVE CLICK AREA", AutoSize = true, Font = new Font("Segoe UI Semibold", 7.5F), ForeColor = Color.White, Location = new Point(12, 8) };
    private readonly Label liveClickCount = new() { Text = "Start to test", AutoSize = true, Font = new Font("Segoe UI Semibold", 11F), ForeColor = Color.White, Location = new Point(12, 24) };
    private readonly System.Windows.Forms.Timer clickResetTimer = new() { Interval = 250 };
    private readonly System.Windows.Forms.Timer clickFlashTimer = new() { Interval = 80 };
    private CancellationTokenSource? clickCancellation;
    private bool hotkeyRegistered;
    private bool capturingHotkey;
    private Keys hotkey = Keys.F6;
    private uint hotkeyModifiers;
    private int liveClickCountValue;
    private DateTime lastLiveClickAt;
    private static readonly string DefaultsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoClicker", "defaults.json");

    public MainForm()
    {
        Text = "AutoClicker";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        ClientSize = new Size(560, 572);
        MinimumSize = Size;
        BackColor = Color.FromArgb(244, 247, 251);
        Font = new Font("Segoe UI", 9F);
        KeyPreview = true;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 380));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(15, 23, 42) };
        header.Controls.Add(new Label { Text = "AutoClicker", Font = new Font("Segoe UI Semibold", 24F), ForeColor = Color.White, AutoSize = true, Location = new Point(24, 16) });
        header.Controls.Add(new Label { Text = "Set it once. Toggle it from anywhere.", Font = new Font("Segoe UI", 9F), ForeColor = Color.FromArgb(203, 213, 225), AutoSize = true, Location = new Point(27, 57) });
        header.Controls.Add(new Label { Text = "TOGGLE HOTKEY", AutoSize = true, Font = new Font("Segoe UI Semibold", 7.5F), ForeColor = Color.FromArgb(148, 163, 184), Location = new Point(370, 14) });
        hotkeyValue.ForeColor = Color.White;
        hotkeyValue.Location = new Point(369, 31);
        changeHotkey.Location = new Point(369, 64);
        changeHotkey.FlatStyle = FlatStyle.Flat;
        changeHotkey.FlatAppearance.BorderColor = Color.FromArgb(71, 85, 105);
        changeHotkey.BackColor = Color.FromArgb(30, 41, 59);
        changeHotkey.ForeColor = Color.FromArgb(219, 234, 254);
        changeHotkey.Click += (_, _) => BeginHotkeyCapture();
        header.Controls.AddRange([hotkeyValue, changeHotkey]);

        var content = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18, 14, 18, 0), ColumnCount = 2, RowCount = 3 };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
        content.Controls.Add(Card("Click interval", "How long to wait between clicks", IntervalContent()), 0, 0);
        content.SetColumnSpan(content.GetControlFromPosition(0, 0)!, 2);
        content.Controls.Add(Card("Click options", "Choose the button and action", ClickOptionsContent()), 0, 1);
        content.Controls.Add(Card("Repeat", "Control when the sequence ends", RepeatContent()), 1, 1);
        content.Controls.Add(Card("Cursor position", "Click where the pointer is, or choose a location", PositionContent()), 0, 2);
        content.SetColumnSpan(content.GetControlFromPosition(0, 2)!, 2);

        var footer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 6, 18, 8), BackColor = BackColor };
        StylePrimaryButton(startButton, Color.FromArgb(37, 99, 235));
        StylePrimaryButton(stopButton, Color.FromArgb(220, 38, 38));
        StyleSecondaryButton(saveDefault);
        SetRunControls(isRunning: false);
        startButton.Location = new Point(18, 8); startButton.Width = 94; startButton.Click += (_, _) => StartClicking();
        stopButton.Location = new Point(118, 8); stopButton.Width = 84; stopButton.Click += (_, _) => StopClicking();
        saveDefault.Location = new Point(208, 8); saveDefault.Width = 132; saveDefault.Click += (_, _) => SaveDefaults();
        state.Location = new Point(18, 53);
        state.AutoSize = false;
        state.Size = new Size(322, 20);
        liveClickArea.Location = new Point(366, 7);
        liveClickArea.Controls.AddRange([liveClickCaption, liveClickCount]);
        liveClickArea.MouseDown += (_, _) => RecordLiveClick();
        liveClickCaption.MouseDown += (_, _) => RecordLiveClick();
        liveClickCount.MouseDown += (_, _) => RecordLiveClick();
        clickResetTimer.Tick += (_, _) => ResetCounterWhenIdle();
        clickFlashTimer.Tick += (_, _) => RestoreLiveClickArea();
        clickResetTimer.Start();
        footer.Controls.AddRange([startButton, stopButton, saveDefault, state, liveClickArea]);
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(content, 0, 1);
        root.Controls.Add(footer, 0, 2);
        Controls.Add(root);

        repeatCount.CheckedChanged += (_, _) => count.Enabled = repeatCount.Checked;
        fixedPosition.CheckedChanged += (_, _) => { x.Enabled = fixedPosition.Checked; y.Enabled = fixedPosition.Checked; };
        FormClosing += (_, _) => StopClicking();
        LoadDefaults();
        Shown += (_, _) => RegisterHotkey();
        UpdateHotkeyDisplay();
    }

    private static Panel Card(string title, string description, Control content)
    {
        var card = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(0, 0, 12, 12), Padding = new Padding(16, 14, 16, 12) };
        card.Controls.Add(new Label { Text = title, Font = new Font("Segoe UI Semibold", 10F), ForeColor = Color.FromArgb(15, 23, 42), AutoSize = true, Location = new Point(16, 13) });
        card.Controls.Add(new Label { Text = description, Font = new Font("Segoe UI", 8F), ForeColor = Color.FromArgb(100, 116, 139), AutoSize = true, Location = new Point(16, 33) });
        content.Location = new Point(16, 55);
        content.Width = card.Width - 32;
        content.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        card.Controls.Add(content);
        return card;
    }

    private Control IntervalContent()
    {
        var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
        string[] labels = ["Hours", "Minutes", "Seconds", "Milliseconds"];
        NumericUpDown[] inputs = [hours, minutes, seconds, milliseconds];
        for (var i = 0; i < 4; i++)
        {
            var field = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, Width = 108, Height = 42, Margin = new Padding(0, 0, 3, 0) };
            field.Controls.Add(new Label { Text = labels[i], Width = 90, TextAlign = ContentAlignment.MiddleLeft, AutoSize = false, Height = 16 });
            inputs[i].Width = 76;
            field.Controls.Add(inputs[i]);
            flow.Controls.Add(field);
        }
        return flow;
    }

    private Control ClickOptionsContent()
    {
        var panel = Stack();
        panel.Controls.Add(Row("Button", mouseButton));
        panel.Controls.Add(Row("Type", clickType));
        return panel;
    }

    private Control RepeatContent()
    {
        var panel = Stack();
        panel.Controls.Add(repeatUntilStopped);
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.Add(repeatCount); row.Controls.Add(count); row.Controls.Add(new Label { Text = "times", AutoSize = true, Padding = new Padding(3, 5, 0, 0) });
        panel.Controls.Add(row);
        return panel;
    }

    private Control PositionContent()
    {
        var panel = Stack();
        panel.Controls.Add(currentPosition);
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.Add(fixedPosition);
        row.Controls.Add(new Label { Text = "X", AutoSize = true, Padding = new Padding(9, 5, 0, 0) }); row.Controls.Add(x);
        row.Controls.Add(new Label { Text = "Y", AutoSize = true, Padding = new Padding(9, 5, 0, 0) }); row.Controls.Add(y);
        panel.Controls.Add(row);
        return panel;
    }

    private static FlowLayoutPanel Stack() => new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
    private static Control Row(string label, Control input)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.Add(new Label { Text = label, Width = 50, Padding = new Padding(0, 5, 0, 0) });
        input.Width = 116; row.Controls.Add(input);
        return row;
    }
    private static NumericUpDown Number(decimal min, decimal max, decimal value, bool enabled = true) => new() { Minimum = min, Maximum = max, Value = value, Width = 60, ThousandsSeparator = false, Enabled = enabled };
    private static void StylePrimaryButton(Button button, Color color)
    {
        button.BackColor = color;
        button.ForeColor = Color.White;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.Font = new Font("Segoe UI Semibold", 9F);
    }
    private static void StyleSecondaryButton(Button button)
    {
        button.BackColor = Color.White;
        button.ForeColor = Color.FromArgb(30, 64, 175);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Color.FromArgb(191, 219, 254);
        button.Font = new Font("Segoe UI Semibold", 9F);
    }
    private static ComboBox Select(params string[] values)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 116 };
        combo.Items.AddRange(values);
        combo.SelectedIndex = 0;
        return combo;
    }

    private void BeginHotkeyCapture()
    {
        if (hotkeyRegistered)
        {
            UnregisterHotKey(Handle, HotkeyId);
            hotkeyRegistered = false;
        }
        capturingHotkey = true;
        changeHotkey.Text = "Press a key…";
        changeHotkey.ForeColor = Color.FromArgb(180, 83, 9);
        state.Text = "Press the key combination you want to use. Esc cancels.";
        state.ForeColor = Color.FromArgb(180, 83, 9);
        ActiveControl = null;
        Focus();
    }

    private void RegisterHotkey()
    {
        hotkeyRegistered = RegisterHotKey(Handle, HotkeyId, hotkeyModifiers, (uint)hotkey);
        if (!hotkeyRegistered)
        {
            state.Text = $"{FormatHotkey()} is in use - choose another hotkey.";
            state.ForeColor = Color.FromArgb(190, 24, 93);
        }
    }

    private void CompleteHotkeyCapture(Keys keyData)
    {
        if ((keyData & Keys.KeyCode) == Keys.Escape)
        {
            CancelHotkeyCapture();
            return;
        }

        var candidateKey = keyData & Keys.KeyCode;
        if (candidateKey is Keys.None or Keys.ControlKey or Keys.ShiftKey or Keys.Menu)
            return;

        var candidateModifiers = HotkeyModifiers(keyData);
        if (hotkeyRegistered) UnregisterHotKey(Handle, HotkeyId);
        if (RegisterHotKey(Handle, HotkeyId, candidateModifiers, (uint)candidateKey))
        {
            hotkey = candidateKey;
            hotkeyModifiers = candidateModifiers;
            hotkeyRegistered = true;
            capturingHotkey = false;
            UpdateHotkeyDisplay();
            state.Text = $"Ready - press {FormatHotkey()} to start or stop.";
            state.ForeColor = Color.FromArgb(21, 128, 61);
        }
        else
        {
            hotkeyRegistered = RegisterHotKey(Handle, HotkeyId, hotkeyModifiers, (uint)hotkey);
            state.Text = $"{FormatHotkey(candidateKey, candidateModifiers)} is already in use. Try another.";
            state.ForeColor = Color.FromArgb(190, 24, 93);
        }
        CancelHotkeyCapture(keepStatus: true);
    }

    private void CancelHotkeyCapture(bool keepStatus = false)
    {
        capturingHotkey = false;
        changeHotkey.Text = "Change hotkey";
        changeHotkey.ForeColor = Color.FromArgb(219, 234, 254);
        if (!hotkeyRegistered) RegisterHotkey();
        if (!keepStatus)
        {
            state.Text = $"Ready - press {FormatHotkey()} to start or stop.";
            state.ForeColor = Color.FromArgb(21, 128, 61);
        }
    }

    private void UpdateHotkeyDisplay() => hotkeyValue.Text = FormatHotkey();
    private string FormatHotkey() => FormatHotkey(hotkey, hotkeyModifiers);
    private static string FormatHotkey(Keys key, uint modifiers)
    {
        var names = new List<string>();
        if ((modifiers & 0x0002) != 0) names.Add("Ctrl");
        if ((modifiers & 0x0001) != 0) names.Add("Alt");
        if ((modifiers & 0x0004) != 0) names.Add("Shift");
        names.Add(key.ToString());
        return string.Join(" + ", names);
    }
    private static uint HotkeyModifiers(Keys keyData)
    {
        uint modifiers = 0;
        if ((keyData & Keys.Control) != 0) modifiers |= 0x0002;
        if ((keyData & Keys.Alt) != 0) modifiers |= 0x0001;
        if ((keyData & Keys.Shift) != 0) modifiers |= 0x0004;
        return modifiers;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (capturingHotkey)
        {
            CompleteHotkeyCapture(keyData);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void WndProc(ref Message m)
    {
        if (!capturingHotkey && m.Msg == WmHotkey && m.WParam.ToInt32() == HotkeyId) ToggleClicking();
        base.WndProc(ref m);
    }

    private void ToggleClicking()
    {
        if (clickCancellation is null) StartClicking(); else StopClicking();
    }

    private void StartClicking()
    {
        if (clickCancellation is not null) return;
        var delay = TimeSpan.FromHours((double)hours.Value) + TimeSpan.FromMinutes((double)minutes.Value) + TimeSpan.FromSeconds((double)seconds.Value) + TimeSpan.FromMilliseconds((double)milliseconds.Value);
        clickCancellation = new CancellationTokenSource();
        SetRunControls(isRunning: true);
        state.Text = $"Clicking - press {FormatHotkey()} to stop."; state.ForeColor = Color.FromArgb(220, 38, 38);
        SetLiveClickAreaActive(true);
        _ = ClickLoopAsync(delay, repeatCount.Checked ? (int)count.Value : null, clickCancellation.Token);
    }

    private async Task ClickLoopAsync(TimeSpan delay, int? maximumClicks, CancellationToken token)
    {
        var clicks = 0;
        try
        {
            while (!token.IsCancellationRequested && (!maximumClicks.HasValue || clicks < maximumClicks))
            {
                if (fixedPosition.Checked) SetCursorPos((int)x.Value, (int)y.Value);
                SendClick((string)mouseButton.SelectedItem!, (string)clickType.SelectedItem! == "Double");
                clicks++;
                await Task.Delay(delay, token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (!IsDisposed) BeginInvoke(StopClicking);
        }
    }

    private void StopClicking()
    {
        clickCancellation?.Cancel(); clickCancellation?.Dispose(); clickCancellation = null;
        SetRunControls(isRunning: false);
        state.Text = $"Ready - press {FormatHotkey()} to start or stop."; state.ForeColor = Color.FromArgb(21, 128, 61);
        SetLiveClickAreaActive(false);
    }

    private void RecordLiveClick()
    {
        if (clickCancellation is null) return;
        liveClickCountValue++;
        lastLiveClickAt = DateTime.UtcNow;
        liveClickCount.Text = $"{liveClickCountValue:N0} clicks";
        liveClickArea.BackColor = Color.FromArgb(96, 165, 250);
        clickFlashTimer.Stop();
        clickFlashTimer.Start();
    }

    private void ResetCounterWhenIdle()
    {
        if (liveClickCountValue == 0 || DateTime.UtcNow - lastLiveClickAt < TimeSpan.FromSeconds(3)) return;
        liveClickCountValue = 0;
        liveClickCount.Text = clickCancellation is null ? "Start to test" : "0 clicks";
    }

    private void RestoreLiveClickArea()
    {
        clickFlashTimer.Stop();
        liveClickArea.BackColor = clickCancellation is null ? Color.FromArgb(148, 163, 184) : Color.FromArgb(37, 99, 235);
    }

    private void SetLiveClickAreaActive(bool active)
    {
        if (active)
        {
            liveClickArea.BackColor = Color.FromArgb(37, 99, 235);
            liveClickCaption.Text = "LIVE CLICK AREA";
            if (liveClickCountValue == 0) liveClickCount.Text = "0 clicks";
        }
        else
        {
            liveClickArea.BackColor = Color.FromArgb(148, 163, 184);
            liveClickCaption.Text = "LIVE CLICK AREA";
            if (liveClickCountValue == 0) liveClickCount.Text = "Start to test";
        }
    }

    private void SetRunControls(bool isRunning)
    {
        startButton.Enabled = !isRunning;
        startButton.BackColor = isRunning ? Color.FromArgb(203, 213, 225) : Color.FromArgb(37, 99, 235);
        startButton.ForeColor = isRunning ? Color.FromArgb(100, 116, 139) : Color.White;
        stopButton.Enabled = isRunning;
        stopButton.BackColor = isRunning ? Color.FromArgb(220, 38, 38) : Color.FromArgb(203, 213, 225);
        stopButton.ForeColor = isRunning ? Color.White : Color.FromArgb(100, 116, 139);
    }

    private void SaveDefaults()
    {
        try
        {
            var defaults = new AppDefaults
            {
                Hours = (int)hours.Value, Minutes = (int)minutes.Value, Seconds = (int)seconds.Value, Milliseconds = (int)milliseconds.Value,
                MouseButton = mouseButton.SelectedItem?.ToString() ?? "Left", ClickType = clickType.SelectedItem?.ToString() ?? "Single",
                RepeatUntilStopped = repeatUntilStopped.Checked, RepeatCount = (int)count.Value,
                FixedPosition = fixedPosition.Checked, X = (int)x.Value, Y = (int)y.Value,
                Hotkey = (int)hotkey, HotkeyModifiers = hotkeyModifiers
            };
            Directory.CreateDirectory(Path.GetDirectoryName(DefaultsPath)!);
            File.WriteAllText(DefaultsPath, JsonSerializer.Serialize(defaults));
            state.Text = "Current settings saved as your default.";
            state.ForeColor = Color.FromArgb(21, 128, 61);
        }
        catch (Exception)
        {
            state.Text = "Could not save the default settings.";
            state.ForeColor = Color.FromArgb(190, 24, 93);
        }
    }

    private void LoadDefaults()
    {
        try
        {
            if (!File.Exists(DefaultsPath)) return;
            var defaults = JsonSerializer.Deserialize<AppDefaults>(File.ReadAllText(DefaultsPath));
            if (defaults is null) return;
            hours.Value = Clamp(defaults.Hours, hours); minutes.Value = Clamp(defaults.Minutes, minutes); seconds.Value = Clamp(defaults.Seconds, seconds); milliseconds.Value = Clamp(defaults.Milliseconds, milliseconds);
            SetSelection(mouseButton, defaults.MouseButton); SetSelection(clickType, defaults.ClickType);
            repeatUntilStopped.Checked = defaults.RepeatUntilStopped; repeatCount.Checked = !defaults.RepeatUntilStopped; count.Value = Clamp(defaults.RepeatCount, count);
            currentPosition.Checked = !defaults.FixedPosition; fixedPosition.Checked = defaults.FixedPosition; x.Value = Clamp(defaults.X, x); y.Value = Clamp(defaults.Y, y);
            hotkey = Enum.IsDefined((Keys)defaults.Hotkey) ? (Keys)defaults.Hotkey : Keys.F6;
            hotkeyModifiers = defaults.HotkeyModifiers;
        }
        catch (Exception) { }
    }

    private static decimal Clamp(int value, NumericUpDown input) => Math.Clamp(value, (int)input.Minimum, (int)input.Maximum);
    private static void SetSelection(ComboBox combo, string? value)
    {
        var index = combo.FindStringExact(value ?? string.Empty);
        if (index >= 0) combo.SelectedIndex = index;
    }

    private static void SendClick(string button, bool doubleClick)
    {
        var flags = button switch { "Right" => (MouseEventFlags.RightDown, MouseEventFlags.RightUp), "Middle" => (MouseEventFlags.MiddleDown, MouseEventFlags.MiddleUp), _ => (MouseEventFlags.LeftDown, MouseEventFlags.LeftUp) };
        SendMouseClick(flags); if (doubleClick) SendMouseClick(flags);
    }
    private static void SendMouseClick((MouseEventFlags Down, MouseEventFlags Up) flags)
    {
        INPUT[] input = [new() { Type = 0, Data = new InputUnion { Mouse = new MOUSEINPUT { DwFlags = flags.Down } } }, new() { Type = 0, Data = new InputUnion { Mouse = new MOUSEINPUT { DwFlags = flags.Up } } }];
        SendInput((uint)input.Length, input, Marshal.SizeOf<INPUT>());
    }

    protected override void Dispose(bool disposing)
    {
        StopClicking();
        clickResetTimer.Dispose();
        clickFlashTimer.Dispose();
        if (hotkeyRegistered) UnregisterHotKey(Handle, HotkeyId);
        base.Dispose(disposing);
    }

    [Flags] private enum MouseEventFlags : uint { LeftDown = 0x0002, LeftUp = 0x0004, RightDown = 0x0008, RightUp = 0x0010, MiddleDown = 0x0020, MiddleUp = 0x0040 }
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int Dx, Dy; public uint MouseData; public MouseEventFlags DwFlags; public uint Time; public nint DwExtraInfo; }
    private sealed class AppDefaults
    {
        public int Hours { get; set; }
        public int Minutes { get; set; }
        public int Seconds { get; set; }
        public int Milliseconds { get; set; } = 100;
        public string MouseButton { get; set; } = "Left";
        public string ClickType { get; set; } = "Single";
        public bool RepeatUntilStopped { get; set; } = true;
        public int RepeatCount { get; set; } = 10;
        public bool FixedPosition { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Hotkey { get; set; } = (int)Keys.F6;
        public uint HotkeyModifiers { get; set; }
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(nint hWnd, int id);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint cInputs, INPUT[] pInputs, int cbSize);
}
