using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace TikTokScrapper.Core;

public static class ExtractorPlugin
{
    private static readonly object Gate = new();
    public static string EnsureDirectory(string? root = null)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TikTokScrapper.PhotoPlugin")
            ?? throw new IOException("The photo-post extractor is missing from this build.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var directory = Path.Combine(root ?? SettingsStore.DataDirectory, "plugins", hash);
        var script = Path.Combine(directory, "scrapper", "yt_dlp_plugins", "extractor", "tiktok_scrapper.py");
        lock (Gate)
        {
            if (!File.Exists(script) || !File.ReadAllBytes(script).SequenceEqual(bytes))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(script)!);
                var temporary = script + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, script, true); }
                finally { File.Delete(temporary); }
            }
        }
        return directory;
    }
}
