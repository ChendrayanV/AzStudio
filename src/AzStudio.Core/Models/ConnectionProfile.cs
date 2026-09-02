namespace AzStudio.Core.Models;

/// <summary>
/// A saved connection: an Azure AD identity (service principal or interactive user)
/// plus optional default targets for each service module. New service modules
/// should add their own optional target fields here rather than creating a
/// parallel profile concept.
/// </summary>
public class ConnectionProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public AuthType AuthType { get; set; } = AuthType.InteractiveUser;

    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// App registration (client) ID. Required for ServicePrincipal.
    /// Optional for InteractiveUser (leave blank to use the built-in
    /// Azure.Identity developer sign-in app).
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// DPAPI-protected, base64-encoded client secret. Only used for ServicePrincipal.
    /// Never store the plain-text secret on disk.
    /// </summary>
    public string ProtectedClientSecret { get; set; } = string.Empty;

    /// <summary>Default storage account name for the Blob Storage module, e.g. "mystorageacct".</summary>
    public string StorageAccountName { get; set; } = string.Empty;

    /// <summary>Default Service Bus namespace for the Service Bus module, e.g. "my-namespace" (no suffix).</summary>
    public string ServiceBusNamespace { get; set; } = string.Empty;
}
