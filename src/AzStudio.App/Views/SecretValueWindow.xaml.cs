using System.Windows;

namespace AzStudio.App.Views;

public partial class SecretValueWindow : Window
{
    private readonly string _value;
    private bool _revealed;

    private SecretValueWindow(string secretName, string version, string value)
    {
        InitializeComponent();
        _value = value;
        NameRun.Text = secretName;
        VersionRun.Text = version;
        ValueBox.Text = Mask(value);
    }

    private static string Mask(string value) => new string('•', Math.Clamp(value.Length, 8, 40));

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        _revealed = !_revealed;
        ValueBox.Text = _revealed ? _value : Mask(_value);
        ToggleButton.Content = _revealed ? "Hide" : "Show";
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(_value);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    public static void Show(Window? owner, string secretName, string version, string value)
    {
        var window = new SecretValueWindow(secretName, version, value) { Owner = owner };
        window.ShowDialog();
    }
}
