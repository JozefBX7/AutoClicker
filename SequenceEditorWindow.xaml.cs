using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace AutoClicker;

public partial class SequenceEditorWindow : Window
{
    private readonly ObservableCollection<SequenceStep> steps;
    private bool pickingKey;
    private int customKey;
    public IReadOnlyList<SequenceStep> Steps => steps.Select(step => step.Clone()).ToList();

    public SequenceEditorWindow(IEnumerable<SequenceStep> current)
    {
        InitializeComponent();
        steps = new ObservableCollection<SequenceStep>(current.Select(step => step.Clone()));
        StepsList.ItemsSource = steps;
        ActionCombo.SelectedIndex = 0;
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove(); }
    private void CloseButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private string Selected() => ((ComboBoxItem)ActionCombo.SelectedItem).Tag?.ToString() ?? "Left";
    private void ActionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Selected() != "Pick") return;
        pickingKey = true;
        HintLabel.Text = "Press the key to add, or Escape to cancel key selection.";
        Focus();
    }
    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!pickingKey) return;
        e.Handled = true;
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        if (key == System.Windows.Input.Key.Escape) { pickingKey = false; ActionCombo.SelectedIndex = 0; return; }
        customKey = System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
        if (customKey == 0) return;
        CustomKeyItem.Content = $"Key: {key}";
        ActionCombo.SelectedItem = CustomKeyItem;
        pickingKey = false;
        HintLabel.Text = "Picked key ready to add.";
    }
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var input = Selected();
        if (input == "Pick") { HintLabel.Text = "Press the key to add first."; return; }
        if (input == "Custom" && customKey == 0) { HintLabel.Text = "Pick a key first."; return; }
        var delay = int.TryParse(DelayBox.Text, out var value) ? Math.Clamp(value, 0, 600000) : 0;
        steps.Add(new SequenceStep { Input = input, CustomKey = input == "Custom" ? customKey : 0, DelayAfterMilliseconds = delay });
        StepsList.SelectedIndex = steps.Count - 1;
        HintLabel.Text = "Action added. Add another step, or save the sequence.";
    }
    private void Remove_Click(object sender, RoutedEventArgs e) { if (StepsList.SelectedItem is SequenceStep step) steps.Remove(step); }
    private void MoveUp_Click(object sender, RoutedEventArgs e) { var i = StepsList.SelectedIndex; if (i > 0) { steps.Move(i, i - 1); StepsList.SelectedIndex = i - 1; } }
    private void MoveDown_Click(object sender, RoutedEventArgs e) { var i = StepsList.SelectedIndex; if (i >= 0 && i < steps.Count - 1) { steps.Move(i, i + 1); StepsList.SelectedIndex = i + 1; } }
    private void Save_Click(object sender, RoutedEventArgs e) { if (steps.Count < 2) { HintLabel.Text = "Add at least two actions to use a custom sequence."; return; } DialogResult = true; }
}
