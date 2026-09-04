namespace AzStudio.Core.KeyVault;

public record SecretSummaryInfo(string Name, bool? Enabled, DateTimeOffset? UpdatedOn);

public record SecretVersionInfo(
    string Version,
    bool? Enabled,
    DateTimeOffset? ActivatesOn,
    DateTimeOffset? ExpiresOn,
    DateTimeOffset? CreatedOn);
