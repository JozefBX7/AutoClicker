// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.Windows;

namespace AutoClicker;

public partial class ResetOptionsWindow : Window
{
    private readonly Func<ResetScope, bool> reset;

    public ResetOptionsWindow(Func<ResetScope, bool> reset)
    {
        InitializeComponent();
        this.reset = reset;
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string value || !Enum.TryParse<ResetScope>(value, out var scope)) return;
        var wording = scope switch
        {
            ResetScope.SimpleMode => ("Reset Simple mode", "Restore the simple-mode configuration to its original values?", "Reset Simple mode", false),
            ResetScope.AdvancedMode => ("Reset Advanced mode", "Remove saved Advanced profiles and restore the General profile? Advanced shared defaults will be kept.", "Reset Advanced mode", true),
            ResetScope.SharedDefaults => ("Reset Advanced shared defaults", "Restore the global Advanced defaults to their original values? Simple settings and profiles will be kept.", "Reset shared defaults", false),
            _ => ("Reset everything", "Restore all settings, profiles, defaults, lighting, and appearance to their original values?", "Reset everything", true)
        };
        var confirmation = new ConfirmationWindow(wording.Item1, wording.Item2, wording.Item3, wording.Item4) { Owner = this };
        if (confirmation.ShowDialog() == true && reset(scope)) DialogResult = true;
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove(); }
    private void CloseButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
