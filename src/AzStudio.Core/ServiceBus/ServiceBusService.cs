using Azure;
using Azure.Core;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;

namespace AzStudio.Core.ServiceBus;

/// <summary>
/// Wraps the Service Bus administration and data-plane clients for one namespace,
/// authenticated with whatever TokenCredential CredentialFactory produced for the
/// active connection profile. Covers both queues and topics/subscriptions.
/// </summary>
public class ServiceBusService : IAsyncDisposable
{
    private readonly string _fullyQualifiedNamespace;
    private readonly TokenCredential _credential;
    private readonly ServiceBusAdministrationClient _adminClient;
    private ServiceBusClient? _dataClient;

    public ServiceBusService(string namespaceName, TokenCredential credential)
    {
        if (string.IsNullOrWhiteSpace(namespaceName))
        {
            throw new ArgumentException("Service Bus namespace is required.", nameof(namespaceName));
        }

        _fullyQualifiedNamespace = namespaceName.Contains('.')
            ? namespaceName
            : $"{namespaceName}.servicebus.windows.net";

        _credential = credential;
        _adminClient = new ServiceBusAdministrationClient(_fullyQualifiedNamespace, credential);
    }

    private ServiceBusClient DataClient => _dataClient ??= new ServiceBusClient(_fullyQualifiedNamespace, _credential);

    public async Task<List<QueueInfo>> ListQueuesAsync(CancellationToken ct = default)
    {
        var results = new List<QueueInfo>();
        await foreach (var queue in _adminClient.GetQueuesRuntimePropertiesAsync(ct))
        {
            results.Add(new QueueInfo(
                queue.Name,
                queue.ActiveMessageCount,
                queue.DeadLetterMessageCount,
                queue.TransferDeadLetterMessageCount,
                queue.TotalMessageCount,
                queue.SizeInBytes));
        }

        return results;
    }

    public async Task<List<TopicInfo>> ListTopicsAsync(CancellationToken ct = default)
    {
        var results = new List<TopicInfo>();
        await foreach (var topic in _adminClient.GetTopicsRuntimePropertiesAsync(ct))
        {
            results.Add(new TopicInfo(topic.Name, topic.SizeInBytes));
        }

        return results;
    }

    public async Task<List<SubscriptionInfo>> ListSubscriptionsAsync(string topicName, CancellationToken ct = default)
    {
        var results = new List<SubscriptionInfo>();
        await foreach (var sub in _adminClient.GetSubscriptionsRuntimePropertiesAsync(topicName, ct))
        {
            results.Add(new SubscriptionInfo(
                sub.SubscriptionName,
                sub.ActiveMessageCount,
                sub.DeadLetterMessageCount,
                sub.TransferDeadLetterMessageCount,
                sub.TotalMessageCount));
        }

        return results;
    }

    public async Task<List<PeekedMessageInfo>> PeekQueueMessagesAsync(string queueName, int maxMessages = 100, bool deadLetter = false, CancellationToken ct = default)
    {
        await using var receiver = DataClient.CreateReceiver(queueName, new ServiceBusReceiverOptions
        {
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
            SubQueue = deadLetter ? SubQueue.DeadLetter : SubQueue.None
        });

        var messages = await receiver.PeekMessagesAsync(maxMessages, cancellationToken: ct);
        return messages.Select(ToPeekedMessageInfo).ToList();
    }

    public async Task<List<PeekedMessageInfo>> PeekMessagesAsync(string topicName, string subscriptionName, int maxMessages = 100, bool deadLetter = false, CancellationToken ct = default)
    {
        await using var receiver = DataClient.CreateReceiver(topicName, subscriptionName, new ServiceBusReceiverOptions
        {
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
            SubQueue = deadLetter ? SubQueue.DeadLetter : SubQueue.None
        });

        var messages = await receiver.PeekMessagesAsync(maxMessages, cancellationToken: ct);
        return messages.Select(ToPeekedMessageInfo).ToList();
    }

    private static PeekedMessageInfo ToPeekedMessageInfo(ServiceBusReceivedMessage m) => new(
        m.SequenceNumber,
        m.MessageId,
        m.Subject,
        DecodeBody(m),
        m.EnqueuedTime,
        m.ContentType,
        m.CorrelationId,
        m.SessionId,
        m.PartitionKey,
        m.DeliveryCount,
        m.ExpiresAt,
        m.TimeToLive,
        m.ApplicationProperties.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? string.Empty));

    /// <summary>
    /// Scans every queue, and every topic/subscription, in the namespace and peeks messages
    /// from each, skipping (rather than failing on) any entity the caller's identity isn't
    /// authorized to list or read. This is how "show all messages I have access to" is
    /// implemented: access control is enforced by Azure RBAC on each call, not by AzStudio.
    /// </summary>
    public async Task<AccessScanResult> ScanAllAccessibleMessagesAsync(int maxMessagesPerEntity = 100, CancellationToken ct = default)
    {
        var messages = new List<AggregatedMessageInfo>();
        var skipReasons = new List<string>();
        var entitiesSkipped = 0;

        List<QueueInfo> queues;
        try
        {
            queues = await ListQueuesAsync(ct);
        }
        catch (Exception ex) when (IsAccessDenied(ex))
        {
            queues = new List<QueueInfo>();
            skipReasons.Add($"Access denied listing queues: {ex.Message}");
        }

        foreach (var queue in queues)
        {
            try
            {
                var peeked = await PeekQueueMessagesAsync(queue.Name, maxMessagesPerEntity, deadLetter: false, ct);
                messages.AddRange(peeked.Select(m => ToAggregated($"Queue: {queue.Name}", m)));
            }
            catch (Exception ex) when (IsAccessDenied(ex))
            {
                entitiesSkipped++;
                skipReasons.Add($"Queue '{queue.Name}': access denied peeking messages.");
            }

            if (queue.DeadLetterMessageCount <= 0) continue;
            try
            {
                var peeked = await PeekQueueMessagesAsync(queue.Name, maxMessagesPerEntity, deadLetter: true, ct);
                messages.AddRange(peeked.Select(m => ToAggregated($"Queue: {queue.Name} (DLQ)", m)));
            }
            catch (Exception ex) when (IsAccessDenied(ex))
            {
                entitiesSkipped++;
                skipReasons.Add($"Queue '{queue.Name}' dead-letter: access denied peeking messages.");
            }
        }

        List<TopicInfo> topics;
        try
        {
            topics = await ListTopicsAsync(ct);
        }
        catch (Exception ex) when (IsAccessDenied(ex))
        {
            skipReasons.Add($"Access denied listing topics: {ex.Message}");
            return new AccessScanResult(messages, queues.Count, 0, 0, entitiesSkipped, skipReasons);
        }

        var subscriptionsScanned = 0;
        foreach (var topic in topics)
        {
            List<SubscriptionInfo> subscriptions;
            try
            {
                subscriptions = await ListSubscriptionsAsync(topic.Name, ct);
            }
            catch (Exception ex) when (IsAccessDenied(ex))
            {
                skipReasons.Add($"Topic '{topic.Name}': access denied listing subscriptions.");
                continue;
            }

            foreach (var subscription in subscriptions)
            {
                subscriptionsScanned++;
                try
                {
                    var peeked = await PeekMessagesAsync(topic.Name, subscription.Name, maxMessagesPerEntity, deadLetter: false, ct);
                    messages.AddRange(peeked.Select(m => ToAggregated($"Topic: {topic.Name} / Sub: {subscription.Name}", m)));
                }
                catch (Exception ex) when (IsAccessDenied(ex))
                {
                    entitiesSkipped++;
                    skipReasons.Add($"Topic '{topic.Name}' / Subscription '{subscription.Name}': access denied peeking messages.");
                }

                if (subscription.DeadLetterMessageCount <= 0) continue;
                try
                {
                    var peeked = await PeekMessagesAsync(topic.Name, subscription.Name, maxMessagesPerEntity, deadLetter: true, ct);
                    messages.AddRange(peeked.Select(m => ToAggregated($"Topic: {topic.Name} / Sub: {subscription.Name} (DLQ)", m)));
                }
                catch (Exception ex) when (IsAccessDenied(ex))
                {
                    entitiesSkipped++;
                    skipReasons.Add($"Topic '{topic.Name}' / Subscription '{subscription.Name}' dead-letter: access denied peeking messages.");
                }
            }
        }

        return new AccessScanResult(messages, queues.Count, topics.Count, subscriptionsScanned, entitiesSkipped, skipReasons);
    }

    private static AggregatedMessageInfo ToAggregated(string sourceDescription, PeekedMessageInfo m) => new(
        sourceDescription, m.SequenceNumber, m.MessageId, m.Subject, m.Body, m.EnqueuedTime, m.ContentType,
        m.CorrelationId, m.SessionId, m.PartitionKey, m.DeliveryCount, m.ExpiresAt, m.TimeToLive, m.ApplicationProperties);

    private static bool IsAccessDenied(Exception ex) => ex switch
    {
        RequestFailedException rfe => rfe.Status is 401 or 403,
        UnauthorizedAccessException => true,
        _ => false
    };

    /// <summary>Sends to a queue or a topic — both take a plain entity path, so one method covers both.</summary>
    public async Task SendMessageAsync(string entityName, string body, string? subject = null, CancellationToken ct = default)
    {
        await using var sender = DataClient.CreateSender(entityName);
        var message = new ServiceBusMessage(body)
        {
            Subject = subject
        };
        await sender.SendMessageAsync(message, ct);
    }

    private static string DecodeBody(ServiceBusReceivedMessage message)
    {
        try
        {
            return message.Body.ToString();
        }
        catch
        {
            return Convert.ToBase64String(message.Body.ToArray());
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_dataClient is not null)
        {
            await _dataClient.DisposeAsync();
        }
    }
}
