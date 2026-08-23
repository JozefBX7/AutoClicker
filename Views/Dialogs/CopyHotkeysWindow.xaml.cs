// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.Windows;
using System.Windows.Controls;

namespace AutoClicker;

public sealed class ProfileCopyDestination
{
    public ProfileCopyDestination(AutomationProfile? profile) => Profile = profile;
    public AutomationProfile? Profile { get; }
    public bool IsNewProfile => Profile is null;
    public string Name => Profile?.Name ?? "New profile…";
    public override string ToString() => Name;
}

public partial class CopyHotkeysWindow : Window
{
    public AutomationProfile? DestinationProfile => (DestinationCombo.SelectedItem as ProfileCopyDestination)?.Profile;
    public string NewProfileName => NewProfileNameBox.Text.Trim();
    public ProfileCopyConflictResolution ConflictResolution => ReplaceConflictsRadio.IsChecked == true ? ProfileCopyConflictResolution.Replace : ProfileCopyConflictResolution.Skip;

    public CopyHotkeysWindow(IEnumerable<AutomationProfile> destinations, int hotkeyCount)
    {
        InitializeComponent();
        DescriptionLabel.Text = $"Copy {hotkeyCount} selected hotkey{(hotkeyCount == 1 ? string.Empty : "s")} to another profile.";
        DestinationCombo.ItemsSource = destinations.Select(profile => new ProfileCopyDestination(profile)).Append(new ProfileCopyDestination(null)).ToList();
        DestinationCombo.SelectedItem = DestinationCombo.Items.Cast<ProfileCopyDestination>().Last();
        NewProfileNameBox.Text = AutomationProfileNames.New;
    }

    private void DestinationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        NewProfileNamePanel.Visibility = DestinationProfile is null ? Visibility.Visible : Visibility.Collapsed;
        ValidationLabel.Text = string.Empty;
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (DestinationProfile is null && string.IsNullOrWhiteSpace(NewProfileName))
        {
            ValidationLabel.Text = "Enter a name for the new profile.";
            NewProfileNameBox.Focus();
            return;
        }
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();
    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove(); }
}
