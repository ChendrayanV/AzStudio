using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AzStudio.Avalonia.Views;

public partial class ClientSecretPromptWindow : Window
{
    public ClientSecretPromptWindow()
    {
        InitializeComponent();
    }

    private ClientSecretPromptWindow(string connectionName) : this()
    {
        PromptText.Text = $"Enter the client secret for '{connectionName}':";
        Opened += (_, _) => SecretBox.Focus();
    }

    private void Ok_Click(object? sender, RoutedEventArgs e) => Close(SecretBox.Text);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

    public static async Task<string?> PromptAsync(Window? owner, string connectionName)
    {
        var window = new ClientSecretPromptWindow(connectionName);
        return owner is not null ? await window.ShowDialog<string?>(owner) : null;
    }
}
