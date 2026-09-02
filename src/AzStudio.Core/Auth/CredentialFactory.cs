using Azure.Core;
using Azure.Identity;
using AzStudio.Core.Models;
using AzStudio.Core.Security;

namespace AzStudio.Core.Auth;

/// <summary>
/// Builds an Azure.Core TokenCredential from a saved connection profile.
/// Every service module (Blob Storage, Service Bus, and any added later)
/// should authenticate through the credential returned here rather than
/// building its own, so both auth modes keep working uniformly.
/// </summary>
public static class CredentialFactory
{
    public static TokenCredential Create(ConnectionProfile profile)
    {
        return profile.AuthType switch
        {
            AuthType.ServicePrincipal => CreateServicePrincipalCredential(profile),
            AuthType.InteractiveUser => CreateInteractiveCredential(profile),
            _ => throw new NotSupportedException($"Unsupported auth type: {profile.AuthType}")
        };
    }

    /// <summary>
    /// Forces an actual token acquisition (triggering the interactive sign-in prompt right
    /// now, if that's the auth mode) instead of letting it happen lazily on the first data
    /// call. That way a failed/cancelled sign-in is reported immediately as "Connect" failing,
    /// rather than surfacing later as an empty, unexplained container/topic list.
    /// </summary>
    public static async Task VerifySignInAsync(TokenCredential credential, CancellationToken ct = default)
    {
        try
        {
            var context = new TokenRequestContext(new[] { "https://management.azure.com/.default" });
            await credential.GetTokenAsync(context, ct);
        }
        catch (CredentialUnavailableException ex)
        {
            throw new InvalidOperationException($"Sign-in unavailable: {ex.Message}", ex);
        }
        catch (AuthenticationFailedException ex)
        {
            throw new InvalidOperationException(
                $"Sign-in failed: {ex.Message} If a browser window didn't appear, make sure a default browser is configured for this Windows user session.", ex);
        }
    }

    private static TokenCredential CreateServicePrincipalCredential(ConnectionProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.TenantId))
        {
            throw new InvalidOperationException("Tenant ID is required for service principal authentication.");
        }

        if (string.IsNullOrWhiteSpace(profile.ClientId))
        {
            throw new InvalidOperationException("Client ID is required for service principal authentication.");
        }

        var secret = SecretProtector.Unprotect(profile.ProtectedClientSecret);
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException("Client secret is missing or could not be decrypted. Re-enter it and save the connection again.");
        }

        return new ClientSecretCredential(profile.TenantId, profile.ClientId, secret);
    }

    private static TokenCredential CreateInteractiveCredential(ConnectionProfile profile)
    {
        var options = new InteractiveBrowserCredentialOptions
        {
            // Persist the signed-in token to disk (DPAPI-protected by MSAL on Windows)
            // so the user isn't prompted to sign in again on every launch.
            TokenCachePersistenceOptions = new TokenCachePersistenceOptions
            {
                Name = "AzStudioTokenCache"
            }
        };

        if (!string.IsNullOrWhiteSpace(profile.TenantId))
        {
            options.TenantId = profile.TenantId;
        }

        if (!string.IsNullOrWhiteSpace(profile.ClientId))
        {
            options.ClientId = profile.ClientId;
        }

        return new InteractiveBrowserCredential(options);
    }
}
