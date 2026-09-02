using System.Collections.ObjectModel;
using System.Windows;
using Azure;
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
    [NotifyCanExecuteChangedFor(nameof(LoadTopicsAndQueuesCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadSubscriptionsCommand))]
    [NotifyCanExecuteChangedFor(nameof(PeekMessagesCommand))]
    [NotifyCanExecuteChangedFor(nameof(PeekQueueMessagesCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendTopicMessageCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendQueueMessageCommand))]
    [NotifyCanExecuteChangedFor(nameof(ScanAllMessagesCommand))]
    [NotifyCanExecuteChangedFor(nameof(PeekDirectQueueCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendDirectQueueMessageCommand))]
    [NotifyCanExecuteChangedFor(nameof(PeekDirectSubscriptionCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendDirectTopicMessageCommand))]
    private bool isBusy;

    [ObservableProperty]
    private string statusMessage = "Not connected.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadSubscriptionsCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendTopicMessageCommand))]
    private TopicInfo? selectedTopic;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PeekMessagesCommand))]
    private SubscriptionInfo? selectedSubscription;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PeekQueueMessagesCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendQueueMessageCommand))]
    private QueueInfo? selectedQueue;

    /// <summary>Label for whichever entity the Peeked Messages list currently shows ("Queue 'x'" or "Topic 'x' / Subscription 'y'").</summary>
    [ObservableProperty]
    private string peekedMessagesSource = string.Empty;

    // Direct-entry fields: connecting straight to a named queue, or a named topic +
    // subscription, without going through ListQueuesAsync/ListTopicsAsync first. Azure
    // Service Bus requires namespace-wide "manage" rights just to list entities — a user
    // whose RBAC role is scoped to one specific queue/topic can legitimately have zero
    // ability to list anything, even though they can send/peek on that one entity directly.
    [ObservableProperty]
    private string directQueueName = string.Empty;

    [ObservableProperty]
    private string directTopicName = string.Empty;

    [ObservableProperty]
    private string directSubscriptionName = string.Empty;

    public ObservableCollection<QueueInfo> Queues { get; } = new();

    public ObservableCollection<TopicInfo> Topics { get; } = new();

    public ObservableCollection<SubscriptionInfo> Subscriptions { get; } = new();

    public ObservableCollection<PeekedMessageInfo> PeekedMessages { get; } = new();

    [ObservableProperty]
    private string allMessagesStatus = string.Empty;

    public ObservableCollection<AggregatedMessageInfo> AllMessages { get; } = new();

    /// <summary>
    /// Called once per Connect with the credential for the active connection profile.
    /// This does NOT require a namespace name — the namespace is chosen (and can be
    /// changed) directly on this tab via NamespaceName + LoadTopicsAndQueues.
    /// </summary>
    public void Activate(TokenCredential credential, string defaultNamespace)
    {
        _credential = credential;
        _service = null;
        _connectedNamespace = null;
        NamespaceName = defaultNamespace;
        Queues.Clear();
        Topics.Clear();
        Subscriptions.Clear();
        PeekedMessages.Clear();
        PeekedMessagesSource = string.Empty;
        AllMessages.Clear();
        AllMessagesStatus = string.Empty;
        DirectQueueName = string.Empty;
        DirectTopicName = string.Empty;
        DirectSubscriptionName = string.Empty;
        StatusMessage = "Connected. Enter a namespace and a queue or topic/subscription name above to connect directly, or use Load Topics & Queues below if you have list permission.";
        NotifyServiceCommands();
    }

    public void Deactivate()
    {
        _credential = null;
        _service = null;
        _connectedNamespace = null;
        Queues.Clear();
        Topics.Clear();
        Subscriptions.Clear();
        PeekedMessages.Clear();
        PeekedMessagesSource = string.Empty;
        AllMessages.Clear();
        AllMessagesStatus = string.Empty;
        DirectQueueName = string.Empty;
        DirectTopicName = string.Empty;
        DirectSubscriptionName = string.Empty;
        StatusMessage = "Not connected.";
        NotifyServiceCommands();
    }

    // CommunityToolkit's [RelayCommand] only re-evaluates a button's enabled state when
    // explicitly told to (or when an [ObservableProperty] with [NotifyCanExecuteChangedFor]
    // changes). CanRunService/CanRunOnSelected* below depend on plain fields (_credential,
    // _service), so every place that mutates those fields must call this afterward —
    // otherwise the Load/Peek/Send buttons stay stuck at whatever state they started in.
    private void NotifyServiceCommands()
    {
        LoadTopicsAndQueuesCommand.NotifyCanExecuteChanged();
        LoadSubscriptionsCommand.NotifyCanExecuteChanged();
        PeekMessagesCommand.NotifyCanExecuteChanged();
        PeekQueueMessagesCommand.NotifyCanExecuteChanged();
        SendTopicMessageCommand.NotifyCanExecuteChanged();
        SendQueueMessageCommand.NotifyCanExecuteChanged();
        ScanAllMessagesCommand.NotifyCanExecuteChanged();
        PeekDirectQueueCommand.NotifyCanExecuteChanged();
        SendDirectQueueMessageCommand.NotifyCanExecuteChanged();
        PeekDirectSubscriptionCommand.NotifyCanExecuteChanged();
        SendDirectTopicMessageCommand.NotifyCanExecuteChanged();
    }

    private bool CanRunService() => _credential is not null && !IsBusy;

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

    [RelayCommand(CanExecute = nameof(CanRunService))]
    private async Task LoadTopicsAndQueuesAsync()
    {
        var service = EnsureService();
        if (service is null) return;

        IsBusy = true;
        try
        {
            StatusMessage = $"Loading queues and topics in '{_connectedNamespace}'...";

            // Listing queues and listing topics are independent calls, each requiring
            // namespace-wide "manage" rights (Azure Service Bus Data Owner or similar) —
            // a user might have list access to one but not the other, or neither, even
            // while having full send/peek access to a specific entity they already know
            // the name of. So a 401/403 on one must not block the other, and the message
            // below points at the "connect directly" fields as the fallback.
            var deniedKinds = new List<string>();

            Queues.Clear();
            try
            {
                var queues = await service.ListQueuesAsync();
                foreach (var q in queues) Queues.Add(q);
            }
            catch (Exception ex) when (IsAccessDenied(ex))
            {
                deniedKinds.Add("queues");
            }

            Topics.Clear();
            try
            {
                var topics = await service.ListTopicsAsync();
                foreach (var t in topics) Topics.Add(t);
            }
            catch (Exception ex) when (IsAccessDenied(ex))
            {
                deniedKinds.Add("topics");
            }

            Subscriptions.Clear();
            PeekedMessages.Clear();
            PeekedMessagesSource = string.Empty;

            StatusMessage = deniedKinds.Count == 0
                ? $"{Queues.Count} queue(s) and {Topics.Count} topic(s) loaded from '{_connectedNamespace}'."
                : $"Loaded {Queues.Count} queue(s) and {Topics.Count} topic(s) from '{_connectedNamespace}'. " +
                  $"You don't have permission to list {string.Join(" or ", deniedKinds)} in this namespace " +
                  "(that needs namespace-wide access, separate from access to one specific entity). " +
                  "If you know the exact queue or topic/subscription name you have access to, use " +
                  "Use 'Connect directly to a queue or topic' above instead.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load queues/topics from '{_connectedNamespace}': {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool IsAccessDenied(Exception ex) => ex switch
    {
        RequestFailedException rfe => rfe.Status is 401 or 403,
        UnauthorizedAccessException => true,
        _ => false
    };

    partial void OnSelectedTopicChanged(TopicInfo? value)
    {
        if (value is not null)
        {
            _ = LoadSubscriptionsAsync();
        }
    }

    partial void OnSelectedQueueChanged(QueueInfo? value)
    {
        if (value is not null)
        {
            _ = PeekQueueMessagesAsync();
        }
    }

    partial void OnSelectedSubscriptionChanged(SubscriptionInfo? value)
    {
        if (value is not null)
        {
            _ = PeekMessagesAsync();
        }
    }

    private bool CanRunOnSelectedTopic() => _service is not null && !IsBusy && SelectedTopic is not null;

    [RelayCommand(CanExecute = nameof(CanRunOnSelectedTopic))]
    private async Task LoadSubscriptionsAsync()
    {
        if (_service is null || SelectedTopic is null) return;
        IsBusy = true;
        try
        {
            StatusMessage = $"Loading subscriptions for '{SelectedTopic.Name}'...";
            var subs = await _service.ListSubscriptionsAsync(SelectedTopic.Name);
            Subscriptions.Clear();
            foreach (var s in subs) Subscriptions.Add(s);
            PeekedMessages.Clear();
            PeekedMessagesSource = string.Empty;
            StatusMessage = $"{Subscriptions.Count} subscription(s) loaded.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load subscriptions: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRunOnSelectedSubscription() => _service is not null && !IsBusy && SelectedTopic is not null && SelectedSubscription is not null;

    [RelayCommand(CanExecute = nameof(CanRunOnSelectedSubscription))]
    private async Task PeekMessagesAsync()
    {
        if (_service is null || SelectedTopic is null || SelectedSubscription is null) return;
        IsBusy = true;
        try
        {
            StatusMessage = "Peeking messages...";
            var messages = await _service.PeekMessagesAsync(SelectedTopic.Name, SelectedSubscription.Name);
            PeekedMessages.Clear();
            foreach (var m in messages) PeekedMessages.Add(m);
            PeekedMessagesSource = $"Topic '{SelectedTopic.Name}' / Subscription '{SelectedSubscription.Name}'";
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

    private bool CanRunOnSelectedQueue() => _service is not null && !IsBusy && SelectedQueue is not null;

    [RelayCommand(CanExecute = nameof(CanRunOnSelectedQueue))]
    private async Task PeekQueueMessagesAsync()
    {
        if (_service is null || SelectedQueue is null) return;
        IsBusy = true;
        try
        {
            StatusMessage = $"Peeking messages in queue '{SelectedQueue.Name}'...";
            var messages = await _service.PeekQueueMessagesAsync(SelectedQueue.Name);
            PeekedMessages.Clear();
            foreach (var m in messages) PeekedMessages.Add(m);
            PeekedMessagesSource = $"Queue '{SelectedQueue.Name}'";
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

    [RelayCommand(CanExecute = nameof(CanRunOnSelectedTopic))]
    private async Task SendTopicMessageAsync()
    {
        if (_service is null || SelectedTopic is null) return;
        var result = SendMessageWindow.Prompt(Application.Current.MainWindow);
        if (result is null) return;

        IsBusy = true;
        try
        {
            await _service.SendMessageAsync(SelectedTopic.Name, result.Value.Body, result.Value.Subject);
            StatusMessage = $"Message sent to topic '{SelectedTopic.Name}'.";
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

    [RelayCommand(CanExecute = nameof(CanRunOnSelectedQueue))]
    private async Task SendQueueMessageAsync()
    {
        if (_service is null || SelectedQueue is null) return;
        var result = SendMessageWindow.Prompt(Application.Current.MainWindow);
        if (result is null) return;

        IsBusy = true;
        try
        {
            await _service.SendMessageAsync(SelectedQueue.Name, result.Value.Body, result.Value.Subject);
            StatusMessage = $"Message sent to queue '{SelectedQueue.Name}'.";
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

    [RelayCommand(CanExecute = nameof(CanRunService))]
    private async Task ScanAllMessagesAsync()
    {
        var service = EnsureService();
        if (service is null) return;

        IsBusy = true;
        AllMessages.Clear();
        AllMessagesStatus = $"Scanning every queue and topic/subscription in '{_connectedNamespace}' you have access to...";
        try
        {
            var result = await service.ScanAllAccessibleMessagesAsync();
            foreach (var m in result.Messages) AllMessages.Add(m);

            var summary = $"Peeked {result.Messages.Count} message(s) across {result.QueuesScanned} queue(s) and {result.SubscriptionsScanned} subscription(s) in {result.TopicsScanned} topic(s) of '{_connectedNamespace}'.";
            if (result.EntitiesSkipped > 0)
            {
                summary += $" Skipped {result.EntitiesSkipped} entit{(result.EntitiesSkipped == 1 ? "y" : "ies")} due to access denied.";
            }
            if (result.Messages.Count > 0)
            {
                summary += " Double-click a row for full details.";
            }
            AllMessagesStatus = summary;
        }
        catch (Exception ex)
        {
            AllMessagesStatus = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunService))]
    private async Task PeekDirectQueueAsync()
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
            var messages = await service.PeekQueueMessagesAsync(name);
            PeekedMessages.Clear();
            foreach (var m in messages) PeekedMessages.Add(m);
            PeekedMessagesSource = $"Queue '{name}'";
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

    [RelayCommand(CanExecute = nameof(CanRunService))]
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

    [RelayCommand(CanExecute = nameof(CanRunService))]
    private async Task PeekDirectSubscriptionAsync()
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
            var messages = await service.PeekMessagesAsync(topic, subscription);
            PeekedMessages.Clear();
            foreach (var m in messages) PeekedMessages.Add(m);
            PeekedMessagesSource = $"Topic '{topic}' / Subscription '{subscription}'";
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

    [RelayCommand(CanExecute = nameof(CanRunService))]
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
