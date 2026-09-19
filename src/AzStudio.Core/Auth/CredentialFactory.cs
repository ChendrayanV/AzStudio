using Azure.Core;
using Azure.Identity;
using AzStudio.Core.Models;

namespace AzStudio.Core.Auth;

/// <summary>
/// Builds an Azure.Core TokenCredential from a saved connection profile.
/// Every service module (Blob Storage, Service Bus, and any added later)
/// should authenticate through the credential returned here rather than
/// building its own, so both auth modes keep working uniformly.
///
/// No credential material is ever persisted to disk: a Service Principal's client secret is
/// passed in by the caller for each call rather than living on ConnectionProfile, and this
/// method never reads or writes anything under %APPDATA%. Every call here does a fresh
/// authentication (a browser sign-in, or a client-secret credential build) — it's up to the
/// caller (see MainViewModel's in-memory credential cache) whether a given call actually
/// happens or a previously-created credential is reused instead. Either way, whatever is
/// cached only lives in process memory and disappears when the app exits — including for
/// InteractiveUser: this deliberately uses the plain system-browser + loopback-listener flow
/// rather than the Windows broker (WAM), because WAM's account/token cache is written to disk
/// by the OS TokenBroker service itself and there is no supported way, from this app or from
/// MSAL/Azure.Identity's own configuration surface, to make it operate in-memory-only or to
/// fully clear it afterward.
/// </summary>
public static class CredentialFactory
{
    public static async Task<TokenCredential> CreateAsync(ConnectionProfile profile, string? servicePrincipalSecret = null, CancellationToken ct = default)
    {
        return profile.AuthType switch
        {
            AuthType.ServicePrincipal => CreateServicePrincipalCredential(profile, servicePrincipalSecret),
            AuthType.InteractiveUser => await CreateInteractiveCredentialAsync(profile, ct),
            _ => throw new NotSupportedException($"Unsupported auth type: {profile.AuthType}")
        };
    }

    private static TokenCredential CreateServicePrincipalCredential(ConnectionProfile profile, string? clientSecret)
    {
        if (string.IsNullOrWhiteSpace(profile.TenantId))
        {
            throw new InvalidOperationException("Tenant ID is required for service principal authentication.");
        }

        if (string.IsNullOrWhiteSpace(profile.ClientId))
        {
            throw new InvalidOperationException("Client ID is required for service principal authentication.");
        }

        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException("Client secret is required for service principal authentication.");
        }

        return new ClientSecretCredential(profile.TenantId, profile.ClientId, clientSecret);
    }

    private static async Task<TokenCredential> CreateInteractiveCredentialAsync(ConnectionProfile profile, CancellationToken ct)
    {
        var options = new InteractiveBrowserCredentialOptions();

        if (!string.IsNullOrWhiteSpace(profile.TenantId))
        {
            options.TenantId = profile.TenantId;
        }

        if (!string.IsNullOrWhiteSpace(profile.ClientId))
        {
            options.ClientId = profile.ClientId;
        }

        var credential = new InteractiveBrowserCredential(options);

        try
        {
            // Anchor the sign-in once, up front, with a lightweight default-scope
            // authentication (not a request for any specific Azure resource's token).
            // Blob Storage and Service Bus are separate token audiences, so without this
            // anchor each one can independently decide silent reuse isn't possible and pop
            // its own interactive prompt — this is what "Service Bus asks to log in again
            // after Storage already worked" actually is. The anchor lives in the in-memory
            // MSAL cache held by this one credential instance; the caller is expected to keep
            // reusing this same instance (rather than calling CreateAsync again) for as long
            // as it wants that anchor to keep working, since nothing here is written to disk.
            await credential.AuthenticateAsync(ct);
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
