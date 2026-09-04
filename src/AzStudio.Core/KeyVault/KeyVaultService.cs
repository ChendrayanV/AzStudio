using Azure.Core;
using Azure.Security.KeyVault.Secrets;

namespace AzStudio.Core.KeyVault;

/// <summary>
/// Thin wrapper around SecretClient scoped to one key vault, authenticated with
/// whatever TokenCredential CredentialFactory produced for the active connection
/// profile.
/// </summary>
public class KeyVaultService
{
    private readonly SecretClient _client;

    public KeyVaultService(string vaultName, TokenCredential credential)
    {
        if (string.IsNullOrWhiteSpace(vaultName))
        {
            throw new ArgumentException("Key vault name is required.", nameof(vaultName));
        }

        var endpoint = new Uri($"https://{vaultName}.vault.azure.net/");
        _client = new SecretClient(endpoint, credential);
    }

    public async Task<List<SecretSummaryInfo>> ListSecretsAsync(CancellationToken ct = default)
    {
        var results = new List<SecretSummaryInfo>();
        await foreach (var props in _client.GetPropertiesOfSecretsAsync(ct))
        {
            results.Add(new SecretSummaryInfo(props.Name, props.Enabled, props.UpdatedOn));
        }

        return results;
    }

    public async Task<List<SecretVersionInfo>> ListSecretVersionsAsync(string name, CancellationToken ct = default)
    {
        var results = new List<SecretVersionInfo>();
        await foreach (var props in _client.GetPropertiesOfSecretVersionsAsync(name, ct))
        {
            results.Add(new SecretVersionInfo(props.Version, props.Enabled, props.NotBefore, props.ExpiresOn, props.CreatedOn));
        }

        return results;
    }

    /// <summary>Fetches the plaintext value of one specific secret version. Callers are responsible for not
    /// logging or persisting the returned value anywhere.</summary>
    public async Task<string> GetSecretValueAsync(string name, string version, CancellationToken ct = default)
    {
        var secret = await _client.GetSecretAsync(name, version, ct);
        return secret.Value.Value;
    }
}
