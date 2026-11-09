using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace InterviewCompanion;

public record AiSettings(string Endpoint = "", string Model = "", string ApiKey = "");

public sealed class AiSettingsStore
{
    private readonly string path;
    public AiSettingsStore(string? directory = null) => path = Path.Combine(directory ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InterviewCompanion"), "ai-settings.json");

    public AiSettings Load()
    {
        if (!File.Exists(path)) return new();
        var saved = JsonSerializer.Deserialize<SavedSettings>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Invalid settings file.");
        var key = string.IsNullOrEmpty(saved.ProtectedKey) ? "" : Encoding.UTF8.GetString(
            ProtectedData.Unprotect(Convert.FromBase64String(saved.ProtectedKey), null, DataProtectionScope.CurrentUser));
        return new(saved.Endpoint ?? "", saved.Model ?? "", key);
    }

    public void Save(AiSettings settings)
    {
        var encrypted = string.IsNullOrEmpty(settings.ApiKey) ? "" : Convert.ToBase64String(
            ProtectedData.Protect(Encoding.UTF8.GetBytes(settings.ApiKey), null, DataProtectionScope.CurrentUser));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new SavedSettings(settings.Endpoint, settings.Model, encrypted)));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private record SavedSettings(string? Endpoint, string? Model, string? ProtectedKey);
}
