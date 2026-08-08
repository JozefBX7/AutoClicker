using System.Windows;

namespace AutoClicker;

public partial class ProfileNameWindow : Window
{
    public string ProfileName { get; private set; } = string.Empty;

    public ProfileNameWindow(string title, string message, string initialName)
    {
        InitializeComponent();
        TitleLabel.Text = title;
        MessageLabel.Text = message;
        NameBox.Text = initialName;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ValidationLabel.Text = "Enter a profile name to continue.";
            NameBox.Focus();
            return;
        }

        ProfileName = name;
        DialogResult = true;
    }
}
