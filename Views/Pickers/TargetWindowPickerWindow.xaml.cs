// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AutoClicker;

public partial class TargetWindowPickerWindow : Window
{
    public VisibleWindow? SelectedWindow { get; private set; }

    public TargetWindowPickerWindow(IEnumerable<VisibleWindow> windows)
    {
        InitializeComponent();
        var visibleWindows = windows.ToList();
        WindowList.ItemsSource = visibleWindows;
        EmptyLabel.Visibility = visibleWindows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        WindowList.Visibility = visibleWindows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void WindowList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectedWindow = WindowList.SelectedItem as VisibleWindow;
        SelectButton.IsEnabled = SelectedWindow is not null;
    }

    private void WindowList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedWindow is not null) DialogResult = true;
    }

    private void SelectButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedWindow is not null) DialogResult = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
