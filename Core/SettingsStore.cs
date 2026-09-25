using System.IO;
using System.Text.Json;

namespace TikTokScrapper.Core;

public sealed record AppSettings(string SaveFolder, string? CookiesFile = null, bool NotifyWhenFinished = false, int ConcurrentDownloads = 3)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public int DownloadLimit => Math.Clamp(ConcurrentDownloads, 1, 10);
}

public static class SettingsStore
{
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TikTok Scrapper");
    public static string DefaultFolder => Path.Combine(AppContext.BaseDirectory, "scrapped");
    public static AppSettings Load()
    {
        try
        {
            var path = Path.Combine(DataDirectory, "settings.json");
            if (File.Exists(path))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
                if (settings is not null && Path.IsPathFullyQualified(settings.SaveFolder)) return settings with { ConcurrentDownloads = settings.DownloadLimit };
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
        return new(DefaultFolder);
    }
    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(DataDirectory);
        var path = Path.Combine(DataDirectory, "settings.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(settings with { ConcurrentDownloads = settings.DownloadLimit }, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
    }
}
