using System.Windows;

namespace AzStudio.App.Views;

public partial class SendMessageWindow : Window
{
    public (string Subject, string Body)? Result { get; private set; }

    public SendMessageWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => BodyBox.Focus();
    }

    private void Send_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(BodyBox.Text))
        {
            MessageBox.Show(this, "Enter a message body.", "AzStudio", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = (SubjectBox.Text, BodyBox.Text);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    public static (string Subject, string Body)? Prompt(Window? owner)
    {
        var window = new SendMessageWindow { Owner = owner };
        return window.ShowDialog() == true ? window.Result : null;
    }
}
