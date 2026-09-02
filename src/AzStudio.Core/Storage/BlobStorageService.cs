using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace AzStudio.Core.Storage;

/// <summary>
/// Thin wrapper around BlobServiceClient scoped to one storage account, authenticated
/// with whatever TokenCredential CredentialFactory produced for the active connection profile.
/// </summary>
public class BlobStorageService
{
    private readonly BlobServiceClient _client;

    public BlobStorageService(string accountName, TokenCredential credential)
    {
        if (string.IsNullOrWhiteSpace(accountName))
        {
            throw new ArgumentException("Storage account name is required.", nameof(accountName));
        }

        var endpoint = new Uri($"https://{accountName}.blob.core.windows.net");
        _client = new BlobServiceClient(endpoint, credential);
    }

    public async Task<List<BlobContainerInfo>> ListContainersAsync(CancellationToken ct = default)
    {
        var results = new List<BlobContainerInfo>();
        await foreach (var container in _client.GetBlobContainersAsync(cancellationToken: ct))
        {
            results.Add(new BlobContainerInfo(container.Name, container.Properties.LastModified));
        }

        return results;
    }

    public async Task CreateContainerAsync(string containerName, CancellationToken ct = default)
    {
        await _client.CreateBlobContainerAsync(containerName, cancellationToken: ct);
    }

    public async Task DeleteContainerAsync(string containerName, CancellationToken ct = default)
    {
        await _client.DeleteBlobContainerAsync(containerName, cancellationToken: ct);
    }

    public async Task<List<BlobItemInfo>> ListBlobsAsync(string containerName, string? prefix = null, CancellationToken ct = default)
    {
        var containerClient = _client.GetBlobContainerClient(containerName);
        var results = new List<BlobItemInfo>();
        await foreach (var blob in containerClient.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, ct))
        {
            results.Add(new BlobItemInfo(
                blob.Name,
                blob.Properties.ContentLength ?? 0,
                blob.Properties.LastModified,
                blob.Properties.ContentType));
        }

        return results;
    }

    public async Task UploadAsync(string containerName, string blobName, Stream content, bool overwrite = true, CancellationToken ct = default)
    {
        var containerClient = _client.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobName);
        await blobClient.UploadAsync(content, overwrite, ct);
    }

    public async Task DownloadAsync(string containerName, string blobName, Stream destination, CancellationToken ct = default)
    {
        var containerClient = _client.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobName);
        var download = await blobClient.DownloadStreamingAsync(cancellationToken: ct);
        await download.Value.Content.CopyToAsync(destination, ct);
    }

    public async Task DeleteBlobAsync(string containerName, string blobName, CancellationToken ct = default)
    {
        var containerClient = _client.GetBlobContainerClient(containerName);
        await containerClient.DeleteBlobIfExistsAsync(blobName, DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: ct);
    }
}
