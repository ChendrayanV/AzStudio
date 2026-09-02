using System.Text.Json;
using AzStudio.Core.Models;

namespace AzStudio.Core.Profiles;

/// <summary>
/// Loads and saves connection profiles to a JSON file under %APPDATA%\AzStudio.
/// Client secrets are DPAPI-protected before serialization by the caller
/// (ConnectionProfile.ProtectedClientSecret already holds the protected value).
/// </summary>
public class ProfileStore
{
    private readonly string _filePath;

    public ProfileStore(string? filePath = null)
    {
        if (filePath is not null)
        {
            _filePath = filePath;
            return;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "AzStudio");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "profiles.json");
    }

    public List<ConnectionProfile> Load()
    {
        if (!File.Exists(_filePath))
        {
            return new List<ConnectionProfile>();
        }

        var json = File.ReadAllText(_filePath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<ConnectionProfile>();
        }

        return JsonSerializer.Deserialize<List<ConnectionProfile>>(json) ?? new List<ConnectionProfile>();
    }

    public void Save(IEnumerable<ConnectionProfile> profiles)
    {
        var json = JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }
}
