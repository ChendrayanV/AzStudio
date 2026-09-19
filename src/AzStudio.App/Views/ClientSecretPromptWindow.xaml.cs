using System.Windows;

namespace AzStudio.App.Views;

/// <summary>
/// Asks for a Service Principal client secret at Connect time, every time.
/// The secret is never saved anywhere — not with the connection profile, not on disk —
/// so re-entering it here on each Connect is the intended behavior, not a missing "remember me".
/// </summary>
public partial class ClientSecretPromptWindow : Window
{
    public string? Value { get; private set; }

    private ClientSecretPromptWindow(string connectionName)
    {
        InitializeComponent();
        PromptText.Text = $"Enter the client secret for '{connectionName}':";
        Loaded += (_, _) => SecretBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Value = SecretBox.Password;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    public static string? Prompt(Window? owner, string connectionName)
    {
        var window = new ClientSecretPromptWindow(connectionName) { Owner = owner };
        return window.ShowDialog() == true ? window.Value : null;
    }
}
