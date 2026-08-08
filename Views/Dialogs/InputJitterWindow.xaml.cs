using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AutoClicker;

public partial class InputJitterWindow : Window
{
    public long MaximumJitterMilliseconds { get; private set; }

    public InputJitterWindow(long maximumJitterMilliseconds, string scopeDescription)
    {
        InitializeComponent();
        ScopeLabel.Text = $"Editing: {scopeDescription}";
        var parts = InputRules.DescribeJitter(maximumJitterMilliseconds);
        SecondsBox.Text = parts.Seconds.ToString();
        MillisecondsBox.Text = parts.Milliseconds.ToString();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        var textBox = FindParent<TextBox>(source);
        if (textBox is null) return;
        textBox.Focus();
        textBox.SelectAll();
        e.Handled = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        MaximumJitterMilliseconds = 0;
        DialogResult = true;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        MaximumJitterMilliseconds = InputRules.CreateJitterMaximum(
            InputRules.ParseClamped(SecondsBox.Text, 0, 59),
            InputRules.ParseClamped(MillisecondsBox.Text, 0, 999));
        DialogResult = true;
    }

    private static T? FindParent<T>(DependencyObject source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match) return match;
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }
        return null;
    }
}
