using System.Windows;

namespace AzStudio.App.Views;

public partial class TextInputWindow : Window
{
    public string? Value { get; private set; }

    public TextInputWindow(string title, string prompt)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        Loaded += (_, _) => ValueBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Value = ValueBox.Text;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    public static string? Prompt(Window? owner, string title, string prompt)
    {
        var window = new TextInputWindow(title, prompt) { Owner = owner };
        return window.ShowDialog() == true ? window.Value : null;
    }
}
