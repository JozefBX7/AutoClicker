using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AutoClicker;

public partial class InputPulseWindow : Window
{
    public int PulseMilliseconds { get; private set; }

    public InputPulseWindow(int pulseMilliseconds)
    {
        InitializeComponent();
        PulseMilliseconds = InputRules.NormalizeInputPulseMilliseconds(pulseMilliseconds);
        PulseCombo.SelectedItem = PulseCombo.Items.OfType<ComboBoxItem>().First(item => item.Tag?.ToString() == PulseMilliseconds.ToString());
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (PulseCombo.SelectedItem is ComboBoxItem { Tag: string value } && int.TryParse(value, out var milliseconds))
            PulseMilliseconds = InputRules.NormalizeInputPulseMilliseconds(milliseconds);
        DialogResult = true;
    }
}
