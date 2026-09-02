using Azure.Identity;

namespace AzStudio.Core.Auth;

/// <summary>
/// Persists the MSAL AuthenticationRecord for an interactive sign-in, per connection profile,
/// so InteractiveBrowserCredential can silently resume the same signed-in account on every
/// later Connect — and, critically, for every new Azure resource audience requested within a
/// session (Blob Storage and Service Bus are different token audiences, so without an anchored
/// account each one can independently fall back to a fresh interactive prompt). The record only
/// identifies the account (tenant/client/object IDs); it carries no secret or token, so unlike
/// the client secret it's safe to store as plain text.
/// </summary>
public static class AuthenticationRecordStore
{
    private static string GetPath(string profileId)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "AzStudio", "auth-records");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{profileId}.json");
    }

    public static AuthenticationRecord? Load(string profileId)
    {
        var path = GetPath(profileId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            return AuthenticationRecord.Deserialize(stream);
        }
        catch
        {
            // Corrupt or unreadable record — fall back to a fresh interactive sign-in.
            return null;
        }
    }

    public static void Save(string profileId, AuthenticationRecord record)
    {
        using var stream = File.Create(GetPath(profileId));
        record.Serialize(stream);
    }
}
