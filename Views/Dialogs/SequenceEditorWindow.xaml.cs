// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace AutoClicker;

public partial class SequenceEditorWindow : Window
{
    private readonly ObservableCollection<SequenceStep> steps;
    private bool pickingKey;
    private Point dragStart;
    private SequenceStep? draggingStep;
    private readonly List<SequencePreset> library;
    public bool LibraryChanged { get; private set; }
    public IReadOnlyList<SequenceStep> Steps => steps.Select(step => step.Clone()).ToList();
    public bool UseGlobalInputPulse => UseGlobalInputPulseCheckBox.IsChecked == true;
    public IReadOnlyList<SequencePreset> Library => library.Select(preset => preset.Clone()).ToList();

    public SequenceEditorWindow(IEnumerable<SequenceStep> current, bool useGlobalInputPulse, IEnumerable<SequencePreset> library)
    {
        InitializeComponent();
        steps = new ObservableCollection<SequenceStep>(current.Select(step => step.Clone()));
        this.library = library.Select(preset => preset.Clone()).ToList();
        UseGlobalInputPulseCheckBox.IsChecked = useGlobalInputPulse;
        StepsList.ItemsSource = steps;
        PresetCombo.ItemsSource = this.library;
        steps.CollectionChanged += (_, _) => UpdateEmptyStates();
        UpdateEmptyStates();
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove(); }
    private void CloseButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void DirectAction_Click(object sender, RoutedEventArgs e)
    {
        AddEvent(((Button)sender).CommandParameter?.ToString() ?? "Left");
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
        var customKey = System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
        if (customKey == 0) return;
        pickingKey = false;
        AddEvent("Custom", customKey);
    }
    private void AddDelay_Click(object sender, RoutedEventArgs e)
    {
        var delay = int.TryParse(DelayBox.Text, out var value) ? Math.Clamp(value, 1, 600000) : 100;
        var insertAt = StepsList.SelectedIndex is var selectedIndex && selectedIndex >= 0 ? selectedIndex + 1 : steps.Count;
        steps.Insert(insertAt, new SequenceStep { Input = "Delay", DelayAfterMilliseconds = delay });
        StepsList.SelectedIndex = insertAt;
        HintLabel.Text = $"Wait {delay:N0} ms added.";
    }

    private void AddEvent(string input, int key = 0)
    {
        var insertAt = StepsList.SelectedIndex is var selectedIndex && selectedIndex >= 0 ? selectedIndex + 1 : steps.Count;
        var step = new SequenceStep { Input = input, CustomKey = key };
        steps.Insert(insertAt, step);
        StepsList.SelectedIndex = insertAt;
        HintLabel.Text = $"{step.Describe()} added.";
    }

    private void StepsList_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => dragStart = e.GetPosition(StepsList);
    private void StepsList_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (System.Windows.Input.Keyboard.FocusedElement is TextBox) return;
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
        var position = e.GetPosition(StepsList);
        if (Math.Abs(position.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(position.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        if (StepsList.SelectedItem is not SequenceStep step) return;
        draggingStep = step;
        try { DragDrop.DoDragDrop(StepsList, step, DragDropEffects.Move); }
        finally { draggingStep = null; }
    }
    private void StepsList_DragOver(object sender, DragEventArgs e)
    {
        if (draggingStep is null || !e.Data.GetDataPresent(typeof(SequenceStep))) { e.Effects = DragDropEffects.None; return; }
        var target = StepAt(e.OriginalSource as DependencyObject);
        if (target is not null && !ReferenceEquals(target, draggingStep))
        {
            var targetIndex = steps.IndexOf(target);
            var sourceIndex = steps.IndexOf(draggingStep);
            var container = ItemsControl.ContainerFromElement(StepsList, e.OriginalSource as DependencyObject) as FrameworkElement;
            var insertAfter = container is not null && e.GetPosition(container).Y > container.ActualHeight / 2;
            var destination = targetIndex + (insertAfter ? 1 : 0);
            if (sourceIndex < destination) destination--;
            // Show the insertion point while dragging.
            if (sourceIndex >= 0 && destination >= 0 && sourceIndex != destination) steps.Move(sourceIndex, destination);
            StepsList.SelectedItem = draggingStep;
        }
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }
    private void StepsList_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
    }
    private SequenceStep? StepAt(DependencyObject? element) => element is null ? null : (ItemsControl.ContainerFromElement(StepsList, element) as FrameworkElement)?.DataContext as SequenceStep;

    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetCombo.SelectedItem is SequencePreset preset) PresetNameBox.Text = preset.Name;
        if (DeletePresetButton is not null) DeletePresetButton.IsEnabled = PresetCombo.SelectedItem is SequencePreset;
    }

    private void LoadPreset_Click(object sender, RoutedEventArgs e)
    {
        if (PresetCombo.SelectedItem is not SequencePreset preset) { HintLabel.Text = "Choose a saved sequence to load."; return; }
        steps.Clear();
        foreach (var step in preset.Steps) steps.Add(step.Clone());
        UseGlobalInputPulseCheckBox.IsChecked = preset.UseGlobalInputPulse;
        HintLabel.Text = $"Loaded {preset.Name}.";
    }

    private void NewSequence_Click(object sender, RoutedEventArgs e)
    {
        if (steps.Count > 0)
        {
            var confirmation = new ConfirmationWindow("Start new sequence", "Clear the current sequence? Saved sequences will not be changed.", "Clear") { Owner = this };
            if (confirmation.ShowDialog() != true) return;
        }

        pickingKey = false;
        steps.Clear();
        StepsList.SelectedItem = null;
        PresetCombo.SelectedItem = null;
        PresetNameBox.Clear();
        UseGlobalInputPulseCheckBox.IsChecked = true;
        HintLabel.Text = "New empty sequence.";
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
        preset.UseGlobalInputPulse = UseGlobalInputPulse;
        LibraryChanged = true;
        PresetCombo.Items.Refresh();
        PresetCombo.SelectedItem = preset;
        UpdateEmptyStates();
        HintLabel.Text = $"Saved {name} to your sequence library.";
    }

    private void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        if (PresetCombo.SelectedItem is not SequencePreset preset) return;
        var confirmation = new ConfirmationWindow("Delete saved sequence", $"Delete \"{preset.Name}\" from your saved sequences?", "Delete", destructive: true) { Owner = this };
        if (confirmation.ShowDialog() != true) return;

        library.Remove(preset);
        PresetCombo.Items.Refresh();
        PresetCombo.SelectedItem = null;
        PresetNameBox.Clear();
        DeletePresetButton.IsEnabled = false;
        LibraryChanged = true;
        UpdateEmptyStates();
        HintLabel.Text = $"Deleted {preset.Name}.";
    }

    private void UpdateEmptyStates()
    {
        if (EmptyStepsLabel is not null) EmptyStepsLabel.Visibility = steps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (EmptyPresetsLabel is not null) EmptyPresetsLabel.Visibility = library.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void DelayEventBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox { DataContext: SequenceStep { Input: "Delay" } step } box) return;
        if (int.TryParse(box.Text, out var milliseconds)) step.DelayAfterMilliseconds = Math.Clamp(milliseconds, 1, 600000);
    }

    private void DelayEventBox_GotKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box) box.SelectAll();
    }

    private void DelayEventBox_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: SequenceStep { Input: "Delay" } step } box)
            box.Text = Math.Clamp(step.DelayAfterMilliseconds, 1, 600000).ToString();
    }
    private void Remove_Click(object sender, RoutedEventArgs e) { if (StepsList.SelectedItem is SequenceStep step) steps.Remove(step); }
    private void MoveUp_Click(object sender, RoutedEventArgs e) { var i = StepsList.SelectedIndex; if (i > 0) { steps.Move(i, i - 1); StepsList.SelectedIndex = i - 1; } }
    private void MoveDown_Click(object sender, RoutedEventArgs e) { var i = StepsList.SelectedIndex; if (i >= 0 && i < steps.Count - 1) { steps.Move(i, i + 1); StepsList.SelectedIndex = i + 1; } }
    private void Save_Click(object sender, RoutedEventArgs e) { if (steps.Count < 2) { HintLabel.Text = "Add at least two actions to use a custom sequence."; return; } DialogResult = true; }
}
