// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace AutoClicker;

public partial class SequenceEditorWindow : Window
{
    private readonly ObservableCollection<SequenceStep> steps;
    private static List<SequenceStep> copiedSteps = [];
    private bool pickingKey;
    private Point dragStart;
    private SequenceStep? dragCandidate;
    private bool collapseSelectionOnClick;
    private List<SequenceStep>? draggingSteps;
    private readonly List<SequenceDragRow> realizedDragRows = [];
    private Point pendingDropPosition;
    private bool hasPendingDropPosition;
    private int? dropInsertionIndex;
    private readonly List<SequencePreset> library;
    private bool updatingStepMode;
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
        steps.CollectionChanged += (_, _) =>
        {
            UpdateEmptyStates();
            UpdateTimelinePreview();
        };
        UpdateEmptyStates();
        UpdateTimelinePreview();
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove(); }
    private void CloseButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void DirectAction_Click(object sender, RoutedEventArgs e)
    {
        AddEvent(((Button)sender).CommandParameter?.ToString() ?? AutomationInputIds.Left);
    }

    private void KeyboardActionButton_Click(object sender, RoutedEventArgs e)
    {
        pickingKey = true;
        HintLabel.Text = "Press the key to add, or Escape to cancel key selection.";
        Focus();
    }

    private void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        var recorder = new SequenceRecorderWindow { Owner = this };
        if (recorder.ShowDialog() != true || recorder.Steps.Count == 0) return;
        var recorded = recorder.Steps.Select(step => step.Clone()).ToList();
        var insertAt = InsertionIndexAfterSelection();
        for (var index = 0; index < recorded.Count; index++) steps.Insert(insertAt + index, recorded[index]);
        SelectOnly(recorded);
        HintLabel.Text = $"{recorded.Count:N0} recorded event{(recorded.Count == 1 ? string.Empty : "s")} added.";
    }
    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (draggingSteps is not null && (e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key) == System.Windows.Input.Key.Escape)
        {
            EndStepDrag();
            HintLabel.Text = "Event move cancelled.";
            e.Handled = true;
            return;
        }

        if (pickingKey)
        {
            e.Handled = true;
            var capturedKey = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            if (capturedKey == System.Windows.Input.Key.Escape) { pickingKey = false; HintLabel.Text = "Keyboard selection cancelled."; return; }
            var customKey = System.Windows.Input.KeyInterop.VirtualKeyFromKey(capturedKey);
            if (customKey == 0) return;
            pickingKey = false;
            AddEvent(AutomationInputIds.Custom, customKey);
            return;
        }

        if (System.Windows.Input.Keyboard.FocusedElement is TextBox) return;
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        var control = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control);
        if (control && key == System.Windows.Input.Key.A) { SelectAllSteps(); e.Handled = true; }
        else if (control && key == System.Windows.Input.Key.C) { CopySelectedSteps(); e.Handled = true; }
        else if (control && key == System.Windows.Input.Key.V) { PasteCopiedSteps(); e.Handled = true; }
        else if (control && key == System.Windows.Input.Key.D) { DuplicateSelectedSteps(); e.Handled = true; }
        else if (key == System.Windows.Input.Key.Delete) { RemoveSelectedSteps(); e.Handled = true; }
    }
    private void AddDelay_Click(object sender, RoutedEventArgs e)
    {
        var delay = int.TryParse(DelayBox.Text, out var value) ? Math.Clamp(value, 1, 600000) : 100;
        var insertAt = InsertionIndexAfterSelection();
        steps.Insert(insertAt, new SequenceStep { Input = AutomationInputIds.Delay, DelayAfterMilliseconds = delay });
        SelectOnly([steps[insertAt]]);
        HintLabel.Text = $"Wait {delay:N0} ms added.";
    }

    private void AddEvent(string input, int key = 0)
    {
        var insertAt = InsertionIndexAfterSelection();
        var step = new SequenceStep { Input = input, CustomKey = key };
        steps.Insert(insertAt, step);
        SelectOnly([step]);
        HintLabel.Text = $"{step.Describe()} added.";
    }

    private void StepsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StepModeCombo is null) return;
        updatingStepMode = true;
        try
        {
            if (StepsList.SelectedItems.Count != 1
                || StepsList.SelectedItem is not SequenceStep { Input: not AutomationInputIds.Delay } step
                || InputRules.IsInstantaneousMouseAction(step.Input))
            {
                StepModeCombo.SelectedIndex = -1;
                StepModeCombo.IsEnabled = false;
                return;
            }

            StepModeCombo.IsEnabled = true;
            StepModeCombo.SelectedItem = StepModeCombo.Items.OfType<ComboBoxItem>()
                .First(item => string.Equals(item.Tag?.ToString(), step.Mode.ToString(), StringComparison.Ordinal));
        }
        finally { updatingStepMode = false; }
    }

    private void StepModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingStepMode || StepsList.SelectedItem is not SequenceStep { Input: not AutomationInputIds.Delay } step) return;
        if (!Enum.TryParse<SequenceStepMode>((StepModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var mode)) return;
        step.Mode = mode;
        StepsList.Items.Refresh();
        SelectOnly([step]);
        UpdateTimelinePreview();
        HintLabel.Text = mode switch
        {
            SequenceStepMode.Hold => $"{step.Describe()} will stay down until a matching Release event or the action stops. Without a Release, it remains held across loops.",
            SequenceStepMode.Release => $"{step.Describe()} will release an earlier matching Hold event.",
            _ => $"{step.Describe()} will use a normal down/up press."
        };
    }

    private void StepsList_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        collapseSelectionOnClick = false;
        dragCandidate = IsWithinTextBox(e.OriginalSource as DependencyObject)
            ? null
            : StepAt(e.OriginalSource as DependencyObject);
        if (dragCandidate is null) return;

        dragStart = e.GetPosition(StepsList);
        var noModifiers = System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.None;
        if (noModifiers)
        {
            // Take ownership of an ordinary row press so WPF's selection mouse capture cannot
            // consume a direct drag. Preserve a selected group until mouse-up so it remains available
            // for dragging, then collapse it when the gesture proves to be an ordinary click.
            var wasSelected = StepsList.SelectedItems.Contains(dragCandidate);
            collapseSelectionOnClick = wasSelected && StepsList.SelectedItems.Count > 1;
            if (!wasSelected) SelectOnly([dragCandidate]);
            else FocusStep(dragCandidate);
            e.Handled = true;
        }
    }

    private void StepsList_PreviewMouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var clicked = StepAt(e.OriginalSource as DependencyObject);
        if (clicked is not null && !StepsList.SelectedItems.Contains(clicked)) SelectOnly([clicked]);

        var selected = SelectedStepsInOrder();
        var menu = new ContextMenu { PlacementTarget = StepsList };
        AddMenuItem(menu, selected.Count <= 1 ? "Remove" : $"Remove {selected.Count:N0} events", SequenceEditorAutomationIds.RemoveSelected, RemoveSelectedSteps, selected.Count > 0, "Del");
        AddMenuItem(menu, selected.Count <= 1 ? "Duplicate" : $"Duplicate {selected.Count:N0} events", SequenceEditorAutomationIds.DuplicateSelected, DuplicateSelectedSteps, selected.Count > 0, "Ctrl+D");
        AddMenuItem(menu, selected.Count <= 1 ? "Copy" : $"Copy {selected.Count:N0} events", SequenceEditorAutomationIds.CopySelected, CopySelectedSteps, selected.Count > 0, "Ctrl+C");
        AddMenuItem(menu, copiedSteps.Count <= 1 ? "Paste" : $"Paste {copiedSteps.Count:N0} events", SequenceEditorAutomationIds.Paste, () => PasteCopiedSteps(clicked), copiedSteps.Count > 0, "Ctrl+V");

        if (clicked is { Input: not AutomationInputIds.Delay, Mode: SequenceStepMode.Hold } && !HasMatchingReleaseAfter(clicked))
        {
            menu.Items.Add(new Separator());
            AddMenuItem(menu, "Add matching release at end", SequenceEditorAutomationIds.AddMatchingRelease, () => AddMatchingRelease(clicked), enabled: true);
        }

        menu.Items.Add(new Separator());
        AddMenuItem(menu, "Select all", SequenceEditorAutomationIds.SelectAll, SelectAllSteps, steps.Count > 0, "Ctrl+A");
        menu.IsOpen = true;
        e.Handled = true;
    }

    private static void AddMenuItem(ContextMenu menu, string header, string automationId, Action action, bool enabled, string? gesture = null)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled, InputGestureText = gesture ?? string.Empty };
        AutomationProperties.SetAutomationId(item, automationId);
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private void StepsList_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
        {
            if (draggingSteps is not null) EndStepDrag();
            return;
        }

        var position = e.GetPosition(StepsList);
        if (draggingSteps is not null)
        {
            QueueDropIndicator(position);
            e.Handled = true;
            return;
        }

        if (System.Windows.Input.Keyboard.FocusedElement is TextBox) return;
        if (dragCandidate is null) return;
        if (Math.Abs(position.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(position.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var selected = SelectedStepsInOrder();
        if (!selected.Contains(dragCandidate)) return;
        draggingSteps = selected;
        hasPendingDropPosition = false;
        dropInsertionIndex = null;
        if (!StepsList.CaptureMouse()) { draggingSteps = null; return; }
        StepsList.Cursor = System.Windows.Input.Cursors.SizeAll;
        CompositionTarget.Rendering += UpdateDropIndicatorOnRender;
        QueueDropIndicator(position);
        e.Handled = true;
    }

    private void StepsList_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var clicked = dragCandidate;
        var collapseSelection = collapseSelectionOnClick;
        dragCandidate = null;
        collapseSelectionOnClick = false;
        if (draggingSteps is null)
        {
            if (collapseSelection && clicked is not null && ReferenceEquals(clicked, StepAt(e.OriginalSource as DependencyObject)))
            {
                SelectOnly([clicked]);
                e.Handled = true;
            }
            return;
        }

        var group = draggingSteps;
        var position = e.GetPosition(StepsList);
        var insideList = position.X >= 0 && position.X <= StepsList.ActualWidth
            && position.Y >= 0 && position.Y <= StepsList.ActualHeight;
        var insertionIndex = insideList ? ResolveDropInsertionIndex(position) : (int?)null;
        EndStepDrag();
        if (insertionIndex is { } index) CommitStepDrop(group, index);
        e.Handled = true;
    }

    private void StepsList_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, StepsList) && draggingSteps is not null)
            EndStepDrag(releaseMouseCapture: false);
    }

    private void QueueDropIndicator(Point position)
    {
        var insideList = position.X >= 0 && position.X <= StepsList.ActualWidth
            && position.Y >= 0 && position.Y <= StepsList.ActualHeight;
        if (!insideList)
        {
            hasPendingDropPosition = false;
            dropInsertionIndex = null;
            DropIndicator.Visibility = Visibility.Collapsed;
            return;
        }

        pendingDropPosition = position;
        hasPendingDropPosition = true;
    }

    private void EndStepDrag(bool releaseMouseCapture = true)
    {
        CompositionTarget.Rendering -= UpdateDropIndicatorOnRender;
        draggingSteps = null;
        dragCandidate = null;
        collapseSelectionOnClick = false;
        hasPendingDropPosition = false;
        dropInsertionIndex = null;
        DropIndicator.Visibility = Visibility.Collapsed;
        StepsList.Cursor = null;
        if (releaseMouseCapture && StepsList.IsMouseCaptured) StepsList.ReleaseMouseCapture();
    }

    private void CommitStepDrop(IReadOnlyList<SequenceStep> group, int insertionIndex)
    {
        MoveStepGroup(group, insertionIndex);
        HintLabel.Text = group.Count == 1 ? "Event moved." : $"{group.Count:N0} selected events moved together.";
    }
    private SequenceStep? StepAt(DependencyObject? element) => element is null ? null : (ItemsControl.ContainerFromElement(StepsList, element) as FrameworkElement)?.DataContext as SequenceStep;

    private void UpdateDropIndicatorOnRender(object? sender, EventArgs e)
    {
        if (draggingSteps is null || !hasPendingDropPosition) return;
        hasPendingDropPosition = false;
        UpdateDropIndicator(ResolveDropInsertionIndex(pendingDropPosition));
    }

    private int ResolveDropInsertionIndex(Point position)
    {
        realizedDragRows.Clear();
        for (var index = 0; index < steps.Count; index++)
        {
            if (StepsList.ItemContainerGenerator.ContainerFromIndex(index) is not FrameworkElement container) continue;
            var top = container.TranslatePoint(new Point(0, 0), StepsList).Y;
            realizedDragRows.Add(new SequenceDragRow(index, top + container.ActualHeight / 2));
        }

        return SequenceEditorDragRules.ResolveInsertionIndex(position.Y, realizedDragRows, steps.Count);
    }

    private void UpdateDropIndicator(int insertionIndex)
    {
        if (dropInsertionIndex == insertionIndex) return;
        dropInsertionIndex = insertionIndex;

        FrameworkElement? adjacent = insertionIndex < steps.Count
            ? StepsList.ItemContainerGenerator.ContainerFromIndex(insertionIndex) as FrameworkElement
            : StepsList.ItemContainerGenerator.ContainerFromIndex(steps.Count - 1) as FrameworkElement;
        if (adjacent is null)
        {
            DropIndicator.Visibility = Visibility.Collapsed;
            return;
        }

        var edge = adjacent.TranslatePoint(
            new Point(0, insertionIndex < steps.Count ? 0 : adjacent.ActualHeight),
            StepsList).Y;
        DropIndicator.Margin = new Thickness(4, Math.Clamp(edge, 3, Math.Max(3, StepsList.ActualHeight - 3)), 4, 0);
        DropIndicator.Visibility = Visibility.Visible;
    }

    private static bool IsWithinTextBox(DependencyObject? source)
    {
        for (var current = source; current is not null; current = current switch
        {
            System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D => System.Windows.Media.VisualTreeHelper.GetParent(current),
            FrameworkContentElement contentElement => contentElement.Parent,
            _ => LogicalTreeHelper.GetParent(current)
        })
            if (current is TextBox) return true;
        return false;
    }

    private List<SequenceStep> SelectedStepsInOrder() =>
        steps.Where(step => StepsList.SelectedItems.Contains(step)).ToList();

    private int InsertionIndexAfterSelection()
    {
        var selected = SelectedStepsInOrder();
        return selected.Count == 0 ? steps.Count : selected.Max(step => steps.IndexOf(step)) + 1;
    }

    private void SelectOnly(IEnumerable<SequenceStep> selected)
    {
        StepsList.SelectedItems.Clear();
        foreach (var step in selected) StepsList.SelectedItems.Add(step);
        if (StepsList.SelectedItems.Count == 0) return;
        if (StepsList.SelectedItems[^1] is not SequenceStep focused) return;
        StepsList.ScrollIntoView(focused);
        FocusStep(focused);
    }

    private void FocusStep(SequenceStep step)
    {
        StepsList.UpdateLayout();
        if (StepsList.ItemContainerGenerator.ContainerFromItem(step) is ListBoxItem item)
            item.Focus();
    }

    private void SelectAllSteps()
    {
        if (steps.Count == 0) return;
        StepsList.SelectAll();
        HintLabel.Text = $"{steps.Count:N0} events selected. Drag any selected event to move them together.";
    }

    private void CopySelectedSteps()
    {
        var selected = SelectedStepsInOrder();
        if (selected.Count == 0) return;
        copiedSteps = selected.Select(step => step.Clone()).ToList();
        HintLabel.Text = selected.Count == 1 ? "Event copied." : $"{selected.Count:N0} events copied in sequence order.";
    }

    private void PasteCopiedSteps(SequenceStep? insertionAnchor = null)
    {
        if (copiedSteps.Count == 0) return;
        var anchorIndex = insertionAnchor is null ? -1 : steps.IndexOf(insertionAnchor);
        var insertAt = anchorIndex >= 0 ? anchorIndex + 1 : InsertionIndexAfterSelection();
        var pasted = copiedSteps.Select(step => step.Clone()).ToList();
        for (var index = 0; index < pasted.Count; index++) steps.Insert(insertAt + index, pasted[index]);
        SelectOnly(pasted);
        HintLabel.Text = pasted.Count == 1 ? "Event pasted." : $"{pasted.Count:N0} events pasted in sequence order.";
    }

    private void DuplicateSelectedSteps()
    {
        var selected = SelectedStepsInOrder();
        if (selected.Count == 0) return;
        var insertAt = InsertionIndexAfterSelection();
        var duplicates = selected.Select(step => step.Clone()).ToList();
        for (var index = 0; index < duplicates.Count; index++) steps.Insert(insertAt + index, duplicates[index]);
        SelectOnly(duplicates);
        HintLabel.Text = duplicates.Count == 1 ? "Event duplicated." : $"{duplicates.Count:N0} events duplicated in sequence order.";
    }

    private void RemoveSelectedSteps()
    {
        var selected = SelectedStepsInOrder();
        if (selected.Count == 0) return;
        var nextIndex = selected.Min(step => steps.IndexOf(step));
        foreach (var step in selected) steps.Remove(step);
        if (steps.Count > 0) SelectOnly([steps[Math.Min(nextIndex, steps.Count - 1)]]);
        HintLabel.Text = selected.Count == 1 ? "Event removed." : $"{selected.Count:N0} selected events removed.";
    }

    private void MoveStepGroup(IReadOnlyList<SequenceStep> group, int insertionIndex)
    {
        var ordered = steps.Where(group.Contains).ToList();
        if (ordered.Count == 0) return;
        var selectedIndicesBeforeInsertion = ordered.Count(step => steps.IndexOf(step) < insertionIndex);
        var adjustedInsertion = Math.Clamp(insertionIndex - selectedIndicesBeforeInsertion, 0, steps.Count - ordered.Count);
        foreach (var step in ordered) steps.Remove(step);
        for (var index = 0; index < ordered.Count; index++) steps.Insert(adjustedInsertion + index, ordered[index]);
        SelectOnly(ordered);
    }

    private bool HasMatchingReleaseAfter(SequenceStep hold)
    {
        var holdIndex = steps.IndexOf(hold);
        if (holdIndex < 0) return false;
        var identity = SequenceHoldRules.Identity(hold);
        return steps.Skip(holdIndex + 1).Any(step => step.Mode == SequenceStepMode.Release && SequenceHoldRules.Identity(step) == identity);
    }

    private void AddMatchingRelease(SequenceStep hold)
    {
        if (HasMatchingReleaseAfter(hold)) return;
        var release = new SequenceStep { Input = hold.Input, CustomKey = hold.CustomKey, Mode = SequenceStepMode.Release };
        steps.Add(release);
        SelectOnly([release]);
        HintLabel.Text = $"Release {hold.Describe()} added at the end. Drag it to adjust the hold duration.";
    }

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
        if (SequenceHoldRules.ValidationError(steps) is { } holdError) { HintLabel.Text = holdError; return; }
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

    private void UpdateTimelinePreview()
    {
        if (TimelinePreviewLabel is not null) TimelinePreviewLabel.Text = SequenceTimelinePreview.Build(steps).Describe();
    }

    private void DelayEventBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox { DataContext: SequenceStep { Input: AutomationInputIds.Delay } step } box) return;
        if (int.TryParse(box.Text, out var milliseconds))
        {
            step.DelayAfterMilliseconds = Math.Clamp(milliseconds, 1, 600000);
            UpdateTimelinePreview();
        }
    }

    private void DelayEventBox_GotKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box) box.SelectAll();
    }

    private void DelayEventBox_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: SequenceStep { Input: AutomationInputIds.Delay } step } box)
            box.Text = Math.Clamp(step.DelayAfterMilliseconds, 1, 600000).ToString();
    }
    private void Remove_Click(object sender, RoutedEventArgs e) => RemoveSelectedSteps();
    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedStepsInOrder();
        if (selected.Count == 0) return;
        var first = selected.Min(step => steps.IndexOf(step));
        if (first == 0) return;
        MoveStepGroup(selected, first - 1);
    }
    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedStepsInOrder();
        if (selected.Count == 0) return;
        var last = selected.Max(step => steps.IndexOf(step));
        if (last >= steps.Count - 1) return;
        MoveStepGroup(selected, last + 2);
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (steps.Count < 2) { HintLabel.Text = "Add at least two actions to use a custom sequence."; return; }
        if (SequenceHoldRules.ValidationError(steps) is { } holdError) { HintLabel.Text = holdError; return; }
        DialogResult = true;
    }
}
