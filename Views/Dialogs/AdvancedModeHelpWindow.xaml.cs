using System.Windows;

namespace AutoClicker;

public partial class AdvancedModeHelpWindow : Window
{
    public AdvancedModeHelpWindow() => InitializeComponent();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove(); }
}
