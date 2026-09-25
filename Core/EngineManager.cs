using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace TikTokScrapper.Core;

public sealed class EngineManager
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string UpdatedEngine => Path.Combine(SettingsStore.DataDirectory, "tools", "yt-dlp.exe");
    public string EnginePath => File.Exists(UpdatedEngine) ? UpdatedEngine : Path.Combine(AppContext.BaseDirectory, "tools", "yt-dlp.exe");
    public Task EnsureAsync(IProgress<string> status, CancellationToken token) => File.Exists(EnginePath) ? Task.CompletedTask : UpdateAsync(status, token);
    public async Task UpdateAsync(IProgress<string> status, CancellationToken token)
    {
        await Gate.WaitAsync(token);
        string? temporary = null;
        try
        {
            status.Report("Checking the official download engine…");
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest");
            request.Headers.UserAgent.ParseAdd("TikTokScrapper/1.0");
            using var response = await Http.SendAsync(request, token);
            response.EnsureSuccessStatusCode();
            using var release = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            var tag = release.RootElement.GetProperty("tag_name").GetString();
            if (tag is null || !System.Text.RegularExpressions.Regex.IsMatch(tag, @"^\d{4}\.\d{2}\.\d{2}(?:\.\d+)?$")) throw new IOException("Unexpected engine release identifier.");
            var root = $"https://github.com/yt-dlp/yt-dlp/releases/download/{tag}/";
            var checksums = await Http.GetStringAsync(root + "SHA2-256SUMS", token);
            var checksum = checksums.Split('\n').Select(l => l.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                .FirstOrDefault(parts => parts.Length == 2 && parts[1].TrimStart('*') == "yt-dlp.exe")?[0];
            if (checksum is null || checksum.Length != 64) throw new IOException("The official engine checksum was not found.");
            status.Report($"Downloading engine {tag}…");
            Directory.CreateDirectory(Path.GetDirectoryName(UpdatedEngine)!);
            temporary = UpdatedEngine + "." + Guid.NewGuid().ToString("N") + ".download";
            using (var download = await Http.GetAsync(root + "yt-dlp.exe", HttpCompletionOption.ResponseHeadersRead, token))
            {
                download.EnsureSuccessStatusCode();
                await using var destination = File.Create(temporary);
                await download.Content.CopyToAsync(destination, token);
            }
            await using (var file = File.OpenRead(temporary))
            {
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(file, token));
                if (!actual.Equals(checksum, StringComparison.OrdinalIgnoreCase)) throw new IOException("Engine checksum verification failed. The existing engine was kept.");
            }
            File.Move(temporary, UpdatedEngine, true);
            status.Report($"Engine {tag} is ready.");
        }
        finally
        {
            if (temporary is not null && File.Exists(temporary)) File.Delete(temporary);
            Gate.Release();
        }
    }
}
