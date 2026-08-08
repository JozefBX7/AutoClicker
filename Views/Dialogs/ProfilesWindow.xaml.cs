using System.Windows;
using System.Windows.Controls;

namespace AutoClicker;

public partial class ProfilesWindow : Window
{
    private readonly AutomationProfileDocument document;
    private readonly AppDefaults currentSettings;
    public AutomationProfileDocument Document => document;
    public string? SelectedProfileId { get; private set; }
    public string? SelectedActionId { get; private set; }

    public ProfilesWindow(AutomationProfileDocument current, AppDefaults currentSettings)
    {
        InitializeComponent();
        document = new AutomationProfileDocument { ActiveProfileId = current.ActiveProfileId, ActiveActionId = current.ActiveActionId, Profiles = current.Profiles.Select(profile => profile.Clone()).ToList() };
        this.currentSettings = currentSettings.Clone();
        ProfilesList.ItemsSource = document.Profiles;
        CopyTargetCombo.ItemsSource = document.Profiles;
        ProfilesList.SelectedItem = document.Profiles.FirstOrDefault(profile => profile.Id == document.ActiveProfileId) ?? document.Profiles.First();
        CopyTargetCombo.SelectedItem = document.Profiles.FirstOrDefault(profile => profile.Id != document.ActiveProfileId) ?? document.Profiles.First();
    }

    private void ProfilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfilesList.SelectedItem is not AutomationProfile profile) return;
        document.ActiveProfileId = profile.Id;
        ActionsTitle.Text = $"Hotkeys · {profile.Name}";
        ActionsList.ItemsSource = profile.Actions;
        ActionsList.SelectedItem = profile.Actions.FirstOrDefault(action => action.Id == document.ActiveActionId) ?? profile.Actions.FirstOrDefault();
    }

    private void ActionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ActionsList.SelectedItem is AutomationAction action) document.ActiveActionId = action.Id;
    }
    private void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        var profile = new AutomationProfile { Name = NextProfileName(), Actions = [new AutomationAction { Settings = currentSettings.Clone(), UsesSharedBehaviorDefaults = true }] };
        EnsureUniqueHotkey(profile.Actions[0], profile);
        document.Profiles.Add(profile); RefreshProfiles(); ProfilesList.SelectedItem = profile; HintLabel.Text = "New profile created from the current action.";
    }
    private void RemoveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesList.SelectedItem is not AutomationProfile profile || document.Profiles.Count == 1) { HintLabel.Text = "Keep at least one profile."; return; }
        document.Profiles.Remove(profile); RefreshProfiles(); ProfilesList.SelectedItem = document.Profiles.First();
    }
    private void AddAction_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesList.SelectedItem is not AutomationProfile profile) return;
        if (profile.Actions.Count >= AutomationProfileLimits.MaximumHotkeys) { HintLabel.Text = $"Profiles can have up to {AutomationProfileLimits.MaximumHotkeys} hotkeys."; return; }
        var action = new AutomationAction { Settings = currentSettings.Clone(), UsesSharedBehaviorDefaults = true }; EnsureUniqueHotkey(action, profile); profile.Actions.Add(action); ActionsList.Items.Refresh(); ActionsList.SelectedItem = action; HintLabel.Text = "Added from the current action. Select it and use it to set its hotkey or input.";
    }
    private void RemoveAction_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesList.SelectedItem is not AutomationProfile profile) return;
        foreach (var action in ActionsList.SelectedItems.Cast<AutomationAction>().ToList()) profile.Actions.Remove(action);
        ActionsList.Items.Refresh(); HintLabel.Text = "Selected actions removed.";
    }
    private void CopyActions_Click(object sender, RoutedEventArgs e)
    {
        if (CopyTargetCombo.SelectedItem is not AutomationProfile destination || ProfilesList.SelectedItem is not AutomationProfile source || destination == source) { HintLabel.Text = "Choose a different profile to copy to."; return; }
        var selected = ActionsList.SelectedItems.Cast<AutomationAction>().ToList(); if (selected.Count == 0) { HintLabel.Text = "Select one or more hotkeys to copy."; return; }
        var copied = 0;
        foreach (var original in selected)
        {
            if (destination.Actions.Count >= AutomationProfileLimits.MaximumHotkeys) break;
            var copy = original.Clone(); copy.Id = Guid.NewGuid().ToString("N"); EnsureUniqueHotkey(copy, destination); destination.Actions.Add(copy); copied++;
        }
        HintLabel.Text = copied == selected.Count
            ? $"Copied {copied} hotkey{(copied == 1 ? string.Empty : "s")} to {destination.Name}."
            : $"Copied {copied}; {selected.Count - copied} skipped because profiles support up to {AutomationProfileLimits.MaximumHotkeys} hotkeys.";
    }
    private void Done_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesList.SelectedItem is not AutomationProfile profile) { HintLabel.Text = "Select a profile first."; return; }
        document.ActiveProfileId = profile.Id;
        document.ActiveActionId = (ActionsList.SelectedItem as AutomationAction ?? profile.Actions.FirstOrDefault())?.Id ?? string.Empty;
        DialogResult = true;
    }
    private void RefreshProfiles() { ProfilesList.Items.Refresh(); CopyTargetCombo.Items.Refresh(); }
    private string NextProfileName() => AutomationProfileNameRules.MakeUnique("Profile 2", document.Profiles);
    private static void EnsureUniqueHotkey(AutomationAction action, AutomationProfile profile)
    {
        for (var key = 0x76; key <= 0x7E; key++) if (!profile.Actions.Any(existing => existing != action && existing.Settings.Hotkey == key && existing.Settings.HotkeyModifiers == 0)) { action.Settings.Hotkey = key; action.Settings.HotkeyModifiers = 0; return; }
        action.Settings.Hotkey = 0; action.Settings.HotkeyModifiers = 0;
    }
    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove(); }
    private void CloseButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
