namespace AzStudio.Core.Storage;

public record BlobContainerInfo(string Name, DateTimeOffset? LastModified);

public record BlobItemInfo(string Name, long SizeBytes, DateTimeOffset? LastModified, string? ContentType);
