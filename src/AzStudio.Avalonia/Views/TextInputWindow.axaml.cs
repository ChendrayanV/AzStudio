using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AzStudio.Avalonia.Views;

public partial class TextInputWindow : Window
{
    public TextInputWindow()
    {
        InitializeComponent();
    }

    public TextInputWindow(string title, string prompt) : this()
    {
        Title = title;
        PromptText.Text = prompt;
        Opened += (_, _) => ValueBox.Focus();
    }

    private void Ok_Click(object? sender, RoutedEventArgs e) => Close(ValueBox.Text);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

    public static async Task<string?> PromptAsync(Window? owner, string title, string prompt)
    {
        var window = new TextInputWindow(title, prompt);
        return owner is not null ? await window.ShowDialog<string?>(owner) : null;
    }
}
