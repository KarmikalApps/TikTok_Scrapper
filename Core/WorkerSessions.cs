using System.IO;

namespace TikTokScrapper.Core;

// yt-dlp writes its cookie jar on exit. Each worker needs a private copy so
// simultaneous child processes cannot overwrite or truncate one another's jar.
internal sealed class WorkerSessions : IDisposable
{
    private readonly List<string> files = [];
    private readonly Action<string> log;
    private string? directory;
    public IReadOnlyList<AppSettings> Settings { get; }

    public WorkerSessions(AppSettings settings, int count, Action<string> log)
    {
        this.log = log;
        var workers = new List<AppSettings>();
        try
        {
            if (!string.IsNullOrWhiteSpace(settings.CookiesFile))
            {
                directory = Path.Combine(Path.GetTempPath(), "TikTokScrapper-cookies-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
            }
            for (var index = 0; index < count; index++)
            {
                if (directory is null) workers.Add(settings);
                else
                {
                    var file = Path.Combine(directory, index + ".txt");
                    files.Add(file);
                    File.Copy(settings.CookiesFile!, file);
                    workers.Add(settings with { CookiesFile = file });
                }
            }
            Settings = workers;
        }
        catch { Dispose(); throw; }
    }

    public void Dispose()
    {
        foreach (var file in files)
            try { File.Delete(file); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log("Temporary cookie cleanup: " + ex.Message); }
        if (directory is not null)
            try { Directory.Delete(directory); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log("Temporary cookie cleanup: " + ex.Message); }
    }
}
