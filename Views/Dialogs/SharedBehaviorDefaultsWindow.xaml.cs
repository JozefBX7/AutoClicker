// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.Windows;

namespace AutoClicker;

public partial class SharedBehaviorDefaultsWindow : Window
{
    public SharedBehaviorDefaultsWindow(AutomationBehaviorOverride selectedOverrides, int hotkeyCount = 1, string? scopeLabel = null)
    {
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(scopeLabel))
        {
            TitleLabel.Text = "Use global Advanced defaults";
            DescriptionLabel.Text = $"Choose which settings to return from {scopeLabel} to the global Advanced defaults.";
        }
        else if (hotkeyCount > 1)
        {
            TitleLabel.Text = $"Shared defaults for {hotkeyCount} hotkeys";
            DescriptionLabel.Text = "Choose which settings to return to inherited defaults. Profile defaults take precedence over global Advanced defaults. Hotkeys already inheriting a selected setting are unchanged.";
        }
        SetSelected(selectedOverrides == AutomationBehaviorOverride.None ? AutomationBehaviorOverride.All : selectedOverrides);
    }

    public AutomationBehaviorOverride SelectedOverrides { get; private set; }
    public bool RevertAll { get; private set; }

    private void SetSelected(AutomationBehaviorOverride selected)
    {
        IntervalCheck.IsChecked = selected.HasFlag(AutomationBehaviorOverride.Interval);
        RepeatCheck.IsChecked = selected.HasFlag(AutomationBehaviorOverride.Repeat);
        PositionCheck.IsChecked = selected.HasFlag(AutomationBehaviorOverride.Position);
        TargetWindowCheck.IsChecked = selected.HasFlag(AutomationBehaviorOverride.TargetWindow);
        InputJitterCheck.IsChecked = selected.HasFlag(AutomationBehaviorOverride.InputJitter);
        InputPulseCheck.IsChecked = selected.HasFlag(AutomationBehaviorOverride.InputPulse);
    }

    private AutomationBehaviorOverride ReadSelected() =>
        (IntervalCheck.IsChecked == true ? AutomationBehaviorOverride.Interval : AutomationBehaviorOverride.None) |
        (RepeatCheck.IsChecked == true ? AutomationBehaviorOverride.Repeat : AutomationBehaviorOverride.None) |
        (PositionCheck.IsChecked == true ? AutomationBehaviorOverride.Position : AutomationBehaviorOverride.None) |
        (TargetWindowCheck.IsChecked == true ? AutomationBehaviorOverride.TargetWindow : AutomationBehaviorOverride.None) |
        (InputJitterCheck.IsChecked == true ? AutomationBehaviorOverride.InputJitter : AutomationBehaviorOverride.None) |
        (InputPulseCheck.IsChecked == true ? AutomationBehaviorOverride.InputPulse : AutomationBehaviorOverride.None);

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void RevertSelectedButton_Click(object sender, RoutedEventArgs e) { SelectedOverrides = ReadSelected(); DialogResult = true; }
    private void RevertAllButton_Click(object sender, RoutedEventArgs e) { RevertAll = true; SelectedOverrides = AutomationBehaviorOverride.All; DialogResult = true; }
}
