namespace AzStudio.Core.ServiceBus;

public record TopicInfo(string Name, long SizeInBytes);

public record SubscriptionInfo(
    string Name,
    long ActiveMessageCount,
    long DeadLetterMessageCount,
    long TransferDeadLetterMessageCount,
    long TotalMessageCount);

public record QueueInfo(
    string Name,
    long ActiveMessageCount,
    long DeadLetterMessageCount,
    long TransferDeadLetterMessageCount,
    long TotalMessageCount,
    long SizeInBytes);

public record PeekedMessageInfo(
    long SequenceNumber,
    string MessageId,
    string? Subject,
    string Body,
    DateTimeOffset EnqueuedTime,
    string? ContentType,
    string? CorrelationId,
    string? SessionId,
    string? PartitionKey,
    int DeliveryCount,
    DateTimeOffset? ExpiresAt,
    TimeSpan TimeToLive,
    IReadOnlyDictionary<string, string> ApplicationProperties);

/// <summary>A peeked message flattened with the queue/topic it came from, for the aggregated "all accessible messages" view.</summary>
public record AggregatedMessageInfo(
    string SourceDescription,
    long SequenceNumber,
    string MessageId,
    string? Subject,
    string Body,
    DateTimeOffset EnqueuedTime,
    string? ContentType,
    string? CorrelationId,
    string? SessionId,
    string? PartitionKey,
    int DeliveryCount,
    DateTimeOffset? ExpiresAt,
    TimeSpan TimeToLive,
    IReadOnlyDictionary<string, string> ApplicationProperties);

/// <summary>
/// Result of scanning every queue, and every topic/subscription, in a namespace and peeking
/// whatever the caller's identity is authorized to read. Entities the identity can't access
/// are skipped (recorded in SkipReasons) rather than failing the whole scan.
/// </summary>
public record AccessScanResult(
    List<AggregatedMessageInfo> Messages,
    int QueuesScanned,
    int TopicsScanned,
    int SubscriptionsScanned,
    int EntitiesSkipped,
    List<string> SkipReasons);
