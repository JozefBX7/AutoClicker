using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace AutoClicker;

public partial class SequenceEditorWindow : Window
{
    private readonly ObservableCollection<SequenceStep> steps;
    private bool pickingKey;
    private int customKey;
    private string selectedAction = "Left";
    private readonly List<SequencePreset> library;
    public bool LibraryChanged { get; private set; }
    public IReadOnlyList<SequenceStep> Steps => steps.Select(step => step.Clone()).ToList();
    public IReadOnlyList<SequencePreset> Library => library.Select(preset => preset.Clone()).ToList();

    public SequenceEditorWindow(IEnumerable<SequenceStep> current, IEnumerable<SequencePreset> library)
    {
        InitializeComponent();
        steps = new ObservableCollection<SequenceStep>(current.Select(step => step.Clone()));
        this.library = library.Select(preset => preset.Clone()).ToList();
        StepsList.ItemsSource = steps;
        PresetCombo.ItemsSource = this.library;
        UpdateActionButtons();
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove(); }
    private void CloseButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void MouseActionButton_Click(object sender, RoutedEventArgs e)
    {
        selectedAction = ((Button)sender).CommandParameter?.ToString() ?? "Left";
        pickingKey = false;
        UpdateActionButtons();
        HintLabel.Text = $"{new SequenceStep { Input = selectedAction }.Describe()} ready to add.";
    }

    private void KeyboardActionButton_Click(object sender, RoutedEventArgs e)
    {
        pickingKey = true;
        HintLabel.Text = "Press the key to add, or Escape to cancel key selection.";
        Focus();
    }
    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!pickingKey) return;
        e.Handled = true;
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        if (key == System.Windows.Input.Key.Escape) { pickingKey = false; HintLabel.Text = "Keyboard selection cancelled."; return; }
        customKey = System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
        if (customKey == 0) return;
        selectedAction = "Custom";
        pickingKey = false;
        UpdateActionButtons();
        HintLabel.Text = $"{key} ready to add.";
    }
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var input = selectedAction;
        if (input == "Custom" && customKey == 0) { HintLabel.Text = "Choose the keyboard button and press a key first."; return; }
        var delay = int.TryParse(DelayBox.Text, out var value) ? Math.Clamp(value, 0, 600000) : 0;
        steps.Add(new SequenceStep { Input = input, CustomKey = input == "Custom" ? customKey : 0, DelayAfterMilliseconds = delay });
        StepsList.SelectedIndex = steps.Count - 1;
        HintLabel.Text = "Action added. Add another step, or save the sequence.";
    }

    private void UpdateActionButtons()
    {
        LeftActionButton.Tag = selectedAction == "Left" ? "Pinned" : null;
        MiddleActionButton.Tag = selectedAction == "Middle" ? "Pinned" : null;
        RightActionButton.Tag = selectedAction == "Right" ? "Pinned" : null;
        KeyboardActionButton.Tag = selectedAction == "Custom" ? "Pinned" : null;
        KeyboardActionButton.ToolTip = selectedAction == "Custom" && customKey != 0
            ? $"Keyboard key: {System.Windows.Input.KeyInterop.KeyFromVirtualKey(customKey)} — click to change"
            : "Add a keyboard key";
    }

    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetCombo.SelectedItem is SequencePreset preset) PresetNameBox.Text = preset.Name;
    }

    private void LoadPreset_Click(object sender, RoutedEventArgs e)
    {
        if (PresetCombo.SelectedItem is not SequencePreset preset) { HintLabel.Text = "Choose a saved sequence to load."; return; }
        steps.Clear();
        foreach (var step in preset.Steps) steps.Add(step.Clone());
        HintLabel.Text = $"Loaded {preset.Name}.";
    }

    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        var name = PresetNameBox.Text.Trim();
        if (steps.Count < 2) { HintLabel.Text = "Add at least two actions before saving a preset."; return; }
        if (string.IsNullOrWhiteSpace(name)) { HintLabel.Text = "Give this sequence a preset name."; return; }
        var preset = PresetCombo.SelectedItem as SequencePreset;
        if (preset is null || !string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            preset = library.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            if (preset is null) { preset = new SequencePreset { Name = name }; library.Add(preset); }
        }
        preset.Name = name;
        preset.Steps = steps.Select(step => step.Clone()).ToList();
        LibraryChanged = true;
        PresetCombo.Items.Refresh();
        PresetCombo.SelectedItem = preset;
        HintLabel.Text = $"Saved {name} to your sequence library.";
    }
    private void Remove_Click(object sender, RoutedEventArgs e) { if (StepsList.SelectedItem is SequenceStep step) steps.Remove(step); }
    private void MoveUp_Click(object sender, RoutedEventArgs e) { var i = StepsList.SelectedIndex; if (i > 0) { steps.Move(i, i - 1); StepsList.SelectedIndex = i - 1; } }
    private void MoveDown_Click(object sender, RoutedEventArgs e) { var i = StepsList.SelectedIndex; if (i >= 0 && i < steps.Count - 1) { steps.Move(i, i + 1); StepsList.SelectedIndex = i + 1; } }
    private void Save_Click(object sender, RoutedEventArgs e) { if (steps.Count < 2) { HintLabel.Text = "Add at least two actions to use a custom sequence."; return; } DialogResult = true; }
}
