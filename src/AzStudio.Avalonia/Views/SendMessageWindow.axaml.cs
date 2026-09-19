using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AzStudio.Avalonia.Views;

public partial class SendMessageWindow : Window
{
    public SendMessageWindow()
    {
        InitializeComponent();
        Opened += (_, _) => BodyBox.Focus();
    }

    private void Send_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(BodyBox.Text))
        {
            ErrorText.Text = "Enter a message body.";
            ErrorText.IsVisible = true;
            return;
        }

        Close((SubjectBox.Text ?? string.Empty, BodyBox.Text));
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

    public static async Task<(string Subject, string Body)?> PromptAsync(Window? owner)
    {
        var window = new SendMessageWindow();
        if (owner is null) return null;
        return await window.ShowDialog<(string, string)?>(owner);
    }
}
