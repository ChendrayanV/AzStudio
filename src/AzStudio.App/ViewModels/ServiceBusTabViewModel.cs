using System.Collections.ObjectModel;
using System.Windows;
using Azure.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AzStudio.Core.ServiceBus;
using AzStudio.App.Views;

namespace AzStudio.App.ViewModels;

public partial class ServiceBusTabViewModel : ObservableObject
{
    private TokenCredential? _credential;
    private ServiceBusService? _service;
    private string? _connectedNamespace;

    [ObservableProperty]
    private string namespaceName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PeekDirectQueueCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshDirectQueueMessagesCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendDirectQueueMessageCommand))]
    [NotifyCanExecuteChangedFor(nameof(PeekDirectSubscriptionCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshDirectSubscriptionMessagesCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendDirectTopicMessageCommand))]
    private bool isBusy;

    [ObservableProperty]
    private string statusMessage = "Not connected.";

    /// <summary>Label for whichever entity the Peeked Messages list currently shows ("Queue 'x'" or "Topic 'x' / Subscription 'y'").</summary>
    [ObservableProperty]
    private string peekedMessagesSource = string.Empty;

    /// <summary>
    /// When set, every Peek reads from the entity's dead-letter sub-queue instead of its
    /// main queue — a separate Azure Service Bus concept (SubQueue.DeadLetter), not a
    /// different entity you'd type a name for.
    /// </summary>
    [ObservableProperty]
    private bool viewDeadLetter;

    // Connecting straight to a named queue, or a named topic + subscription. Azure Service
    // Bus requires namespace-wide "manage" rights just to list entities — a user whose RBAC
    // role is scoped to one specific queue/topic can legitimately have zero ability to list
    // anything, even though they can send/peek on that one entity directly.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PeekDirectQueueCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshDirectQueueMessagesCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendDirectQueueMessageCommand))]
    private string directQueueName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PeekDirectSubscriptionCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshDirectSubscriptionMessagesCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendDirectTopicMessageCommand))]
    private string directTopicName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PeekDirectSubscriptionCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshDirectSubscriptionMessagesCommand))]
    private string directSubscriptionName = string.Empty;

    public ObservableCollection<PeekedMessageInfo> PeekedMessages { get; } = new();

    /// <summary>
    /// Called once per Connect with the credential for the active connection profile.
    /// This does NOT require a namespace name — the namespace is chosen (and can be
    /// changed) directly on this tab via NamespaceName + the queue/topic name fields.
    /// </summary>
    public void Activate(TokenCredential credential, string defaultNamespace)
    {
        _credential = credential;
        _service = null;
        _connectedNamespace = null;
        NamespaceName = defaultNamespace;
        PeekedMessages.Clear();
        PeekedMessagesSource = string.Empty;
        DirectQueueName = string.Empty;
        DirectTopicName = string.Empty;
        DirectSubscriptionName = string.Empty;
        ViewDeadLetter = false;
        StatusMessage = "Connected. Enter a namespace and a queue or topic/subscription name to peek or send.";
        NotifyServiceCommands();
    }

    public void Deactivate()
    {
        _credential = null;
        _service = null;
        _connectedNamespace = null;
        PeekedMessages.Clear();
        PeekedMessagesSource = string.Empty;
        DirectQueueName = string.Empty;
        DirectTopicName = string.Empty;
        DirectSubscriptionName = string.Empty;
        ViewDeadLetter = false;
        StatusMessage = "Not connected.";
        NotifyServiceCommands();
    }

    // CommunityToolkit's [RelayCommand] only re-evaluates a button's enabled state when
    // explicitly told to (or when an [ObservableProperty] with [NotifyCanExecuteChangedFor]
    // changes). CanRunService/CanRunDirect* below depend on plain fields (_credential,
    // _service), so every place that mutates those fields must call this afterward —
    // otherwise the Peek/Refresh/Send buttons stay stuck at whatever state they started in.
    private void NotifyServiceCommands()
    {
        PeekDirectQueueCommand.NotifyCanExecuteChanged();
        RefreshDirectQueueMessagesCommand.NotifyCanExecuteChanged();
        SendDirectQueueMessageCommand.NotifyCanExecuteChanged();
        PeekDirectSubscriptionCommand.NotifyCanExecuteChanged();
        RefreshDirectSubscriptionMessagesCommand.NotifyCanExecuteChanged();
        SendDirectTopicMessageCommand.NotifyCanExecuteChanged();
    }

    private bool CanRunService() => _credential is not null && !IsBusy;

    private bool CanRunDirectQueue() => CanRunService() && !string.IsNullOrWhiteSpace(DirectQueueName);

    private bool CanRunDirectTopic() => CanRunService() && !string.IsNullOrWhiteSpace(DirectTopicName);

    private bool CanRunDirectSubscription() => CanRunService() && !string.IsNullOrWhiteSpace(DirectTopicName) && !string.IsNullOrWhiteSpace(DirectSubscriptionName);

    /// <summary>
    /// Builds (or rebuilds, if the namespace field has changed) the ServiceBusService
    /// for whatever namespace is currently typed into NamespaceName.
    /// </summary>
    private ServiceBusService? EnsureService()
    {
        if (_credential is null) return null;

        var name = NamespaceName.Trim();
        if (string.IsNullOrEmpty(name))
        {
            StatusMessage = "Enter a Service Bus namespace first.";
            return null;
        }

        if (_service is null || !string.Equals(_connectedNamespace, name, StringComparison.OrdinalIgnoreCase))
        {
            _service = new ServiceBusService(name, _credential);
            _connectedNamespace = name;
            NotifyServiceCommands();
        }

        return _service;
    }

    [RelayCommand(CanExecute = nameof(CanRunDirectQueue))]
    private Task PeekDirectQueueAsync() => PeekDirectQueueCoreAsync();

    [RelayCommand(CanExecute = nameof(CanRunDirectQueue))]
    private Task RefreshDirectQueueMessagesAsync() => PeekDirectQueueCoreAsync();

    private async Task PeekDirectQueueCoreAsync()
    {
        var service = EnsureService();
        if (service is null) return;

        var name = DirectQueueName.Trim();
        if (string.IsNullOrEmpty(name))
        {
            StatusMessage = "Enter a queue name first.";
            return;
        }

        IsBusy = true;
        try
        {
            StatusMessage = $"Peeking messages in queue '{name}'...";
            var messages = await service.PeekQueueMessagesAsync(name, deadLetter: ViewDeadLetter);
            PeekedMessages.Clear();
            foreach (var m in messages) PeekedMessages.Add(m);
            PeekedMessagesSource = $"Queue '{name}'{(ViewDeadLetter ? " (DLQ)" : "")}";
            StatusMessage = $"{PeekedMessages.Count} message(s) peeked from '{name}' (non-destructive). Double-click a row for full details.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Peek failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunDirectQueue))]
    private async Task SendDirectQueueMessageAsync()
    {
        var service = EnsureService();
        if (service is null) return;

        var name = DirectQueueName.Trim();
        if (string.IsNullOrEmpty(name))
        {
            StatusMessage = "Enter a queue name first.";
            return;
        }

        var result = SendMessageWindow.Prompt(Application.Current.MainWindow);
        if (result is null) return;

        IsBusy = true;
        try
        {
            await service.SendMessageAsync(name, result.Value.Body, result.Value.Subject);
            StatusMessage = $"Message sent to queue '{name}'.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Send failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunDirectSubscription))]
    private Task PeekDirectSubscriptionAsync() => PeekDirectSubscriptionCoreAsync();

    [RelayCommand(CanExecute = nameof(CanRunDirectSubscription))]
    private Task RefreshDirectSubscriptionMessagesAsync() => PeekDirectSubscriptionCoreAsync();

    private async Task PeekDirectSubscriptionCoreAsync()
    {
        var service = EnsureService();
        if (service is null) return;

        var topic = DirectTopicName.Trim();
        var subscription = DirectSubscriptionName.Trim();
        if (string.IsNullOrEmpty(topic) || string.IsNullOrEmpty(subscription))
        {
            StatusMessage = "Enter both a topic name and a subscription name first.";
            return;
        }

        IsBusy = true;
        try
        {
            StatusMessage = $"Peeking messages in '{topic}' / '{subscription}'...";
            var messages = await service.PeekMessagesAsync(topic, subscription, deadLetter: ViewDeadLetter);
            PeekedMessages.Clear();
            foreach (var m in messages) PeekedMessages.Add(m);
            PeekedMessagesSource = $"Topic '{topic}' / Subscription '{subscription}'{(ViewDeadLetter ? " (DLQ)" : "")}";
            StatusMessage = $"{PeekedMessages.Count} message(s) peeked (non-destructive). Double-click a row for full details.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Peek failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunDirectTopic))]
    private async Task SendDirectTopicMessageAsync()
    {
        var service = EnsureService();
        if (service is null) return;

        var topic = DirectTopicName.Trim();
        if (string.IsNullOrEmpty(topic))
        {
            StatusMessage = "Enter a topic name first.";
            return;
        }

        var result = SendMessageWindow.Prompt(Application.Current.MainWindow);
        if (result is null) return;

        IsBusy = true;
        try
        {
            await service.SendMessageAsync(topic, result.Value.Body, result.Value.Subject);
            StatusMessage = $"Message sent to topic '{topic}'.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Send failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
