using Avalonia.Controls;
using Avalonia.Interactivity;
using AzStudio.Avalonia.Utilities;

namespace AzStudio.Avalonia.Views;

public partial class SecretValueWindow : Window
{
    private readonly string _value;
    private bool _revealed;

    public SecretValueWindow()
    {
        InitializeComponent();
        _value = string.Empty;
    }

    private SecretValueWindow(string secretName, string version, string value) : this()
    {
        _value = value;
        NameRun.Text = secretName;
        VersionRun.Text = version;
        ValueBox.Text = Mask(value);
    }

    private static string Mask(string value) => new string('•', Math.Clamp(value.Length, 8, 40));

    private void Toggle_Click(object? sender, RoutedEventArgs e)
    {
        _revealed = !_revealed;
        ValueBox.Text = _revealed ? _value : Mask(_value);
        ToggleButton.Content = _revealed ? "Hide" : "Show";
    }

    private async void Copy_Click(object? sender, RoutedEventArgs e) => await AvaloniaDialogs.CopyToClipboardAsync(_value);

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    public static void Show(Window? owner, string secretName, string version, string value)
    {
        var window = new SecretValueWindow(secretName, version, value);
        if (owner is not null)
        {
            window.ShowDialog(owner);
        }
        else
        {
            window.Show();
        }
    }
}
