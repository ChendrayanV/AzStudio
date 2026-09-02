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
    public static async Task<TokenCredential> CreateAsync(ConnectionProfile profile, CancellationToken ct = default)
    {
        return profile.AuthType switch
        {
            AuthType.ServicePrincipal => CreateServicePrincipalCredential(profile),
            AuthType.InteractiveUser => await CreateInteractiveCredentialAsync(profile, ct),
            _ => throw new NotSupportedException($"Unsupported auth type: {profile.AuthType}")
        };
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

    private static async Task<TokenCredential> CreateInteractiveCredentialAsync(ConnectionProfile profile, CancellationToken ct)
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

        // If we've signed this profile in before, hand the exact account back to MSAL so it
        // resumes that account specifically, instead of guessing from whatever's in the cache.
        var existingRecord = AuthenticationRecordStore.Load(profile.Id);
        if (existingRecord is not null)
        {
            options.AuthenticationRecord = existingRecord;
        }

        var credential = new InteractiveBrowserCredential(options);

        try
        {
            // Anchor the sign-in once, up front, with a lightweight default-scope
            // authentication (not a request for any specific Azure resource's token).
            // Blob Storage and Service Bus are separate token audiences, so without this
            // anchor each one can independently decide silent reuse isn't possible and pop
            // its own interactive prompt — this is what "Service Bus asks to log in again
            // after Storage already worked" actually is. Once anchored, and the resulting
            // record persisted, MSAL's silent token acquisition can resolve *any* later
            // resource request against this same account without a new prompt.
            var record = await credential.AuthenticateAsync(ct);
            AuthenticationRecordStore.Save(profile.Id, record);
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

        return credential;
    }
}
