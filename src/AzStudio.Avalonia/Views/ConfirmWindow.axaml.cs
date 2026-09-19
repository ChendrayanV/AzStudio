using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AzStudio.Avalonia.Views;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow()
    {
        InitializeComponent();
    }

    private ConfirmWindow(string message, string title) : this()
    {
        Title = title;
        MessageText.Text = message;
    }

    private void Yes_Click(object? sender, RoutedEventArgs e) => Close(true);

    private void No_Click(object? sender, RoutedEventArgs e) => Close(false);

    public static async Task<bool> AskAsync(Window? owner, string message, string title)
    {
        var window = new ConfirmWindow(message, title);
        if (owner is null) return false;
        return await window.ShowDialog<bool>(owner);
    }
}
