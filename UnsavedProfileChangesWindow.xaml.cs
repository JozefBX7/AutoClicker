using System.Windows;

namespace AutoClicker;

public enum ProfileChangeDecision { Cancel, Discard, Save }

public partial class UnsavedProfileChangesWindow : Window
{
    public ProfileChangeDecision Decision { get; private set; }
    public string? SavedProfileName { get; private set; }
    private readonly bool needsProfileName;

    public UnsavedProfileChangesWindow(string nextStep, bool needsProfileName = false, string? suggestedProfileName = null)
    {
        InitializeComponent();
        this.needsProfileName = needsProfileName;
        MessageLabel.Text = $"Save or discard the current profile changes before {nextStep}?";
        ProfileNamePanel.Visibility = needsProfileName ? Visibility.Visible : Visibility.Collapsed;
        ProfileNameBox.Text = suggestedProfileName ?? "New profile";
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();
    private void DiscardButton_Click(object sender, RoutedEventArgs e) { Decision = ProfileChangeDecision.Discard; DialogResult = true; }
    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (needsProfileName)
        {
            var name = ProfileNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                ValidationLabel.Text = "Enter a profile name to save it.";
                ProfileNameBox.Focus();
                return;
            }
            SavedProfileName = name;
        }
        Decision = ProfileChangeDecision.Save;
        DialogResult = true;
    }
}
