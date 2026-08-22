// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.Windows;

namespace AutoClicker;

public partial class ConfirmationWindow : Window
{
    public ConfirmationWindow(string title, string message, string confirmText, bool destructive = false, bool showCancel = true)
    {
        InitializeComponent();
        TitleLabel.Text = title;
        MessageLabel.Text = message;
        ConfirmButton.Content = confirmText;
        CancelButton.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;
        ConfirmButton.Margin = showCancel ? new Thickness(8, 0, 0, 0) : new Thickness(0);
        if (!destructive) ConfirmButton.Style = (Style)FindResource("PrimaryButton");
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void ConfirmButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
