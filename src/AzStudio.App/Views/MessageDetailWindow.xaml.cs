using System.Windows;
using System.Windows.Controls;
using AzStudio.Core.ServiceBus;

namespace AzStudio.App.Views;

public partial class MessageDetailWindow : Window
{
    public MessageDetailWindow()
    {
        InitializeComponent();
    }

    private void AddRow(string label, string value)
    {
        var rowIndex = PropertiesGrid.RowDefinitions.Count;
        PropertiesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var labelBlock = new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 10, 6)
        };
        Grid.SetRow(labelBlock, rowIndex);
        Grid.SetColumn(labelBlock, 0);
        PropertiesGrid.Children.Add(labelBlock);

        var valueBlock = new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        };
        Grid.SetRow(valueBlock, rowIndex);
        Grid.SetColumn(valueBlock, 1);
        PropertiesGrid.Children.Add(valueBlock);
    }

    private void Populate(
        string? source, long sequenceNumber, string messageId, string? subject,
        string body, DateTimeOffset enqueuedTime, string? contentType, string? correlationId, string? sessionId,
        string? partitionKey, int deliveryCount, DateTimeOffset? expiresAt, TimeSpan timeToLive,
        IReadOnlyDictionary<string, string> applicationProperties)
    {
        if (!string.IsNullOrEmpty(source)) AddRow("Source", source);
        AddRow("Sequence #", sequenceNumber.ToString());
        AddRow("Message ID", messageId);
        AddRow("Subject", subject ?? "(none)");
        AddRow("Content Type", contentType ?? "(none)");
        AddRow("Correlation ID", correlationId ?? "(none)");
        AddRow("Session ID", sessionId ?? "(none)");
        AddRow("Partition Key", partitionKey ?? "(none)");
        AddRow("Delivery Count", deliveryCount.ToString());
        AddRow("Enqueued", enqueuedTime.ToString("u"));
        AddRow("Expires At", expiresAt?.ToString("u") ?? "(none)");
        AddRow("Time To Live", timeToLive.ToString());

        PropertiesBox.Text = applicationProperties.Count == 0
            ? "(none)"
            : string.Join(Environment.NewLine, applicationProperties.Select(kv => $"{kv.Key} = {kv.Value}"));

        BodyBox.Text = body;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    public static void Show(Window? owner, PeekedMessageInfo message, string? source = null)
    {
        var window = new MessageDetailWindow { Owner = owner };
        window.Populate(
            source, message.SequenceNumber, message.MessageId, message.Subject, message.Body,
            message.EnqueuedTime, message.ContentType, message.CorrelationId, message.SessionId,
            message.PartitionKey, message.DeliveryCount, message.ExpiresAt, message.TimeToLive,
            message.ApplicationProperties);
        window.ShowDialog();
    }
}
