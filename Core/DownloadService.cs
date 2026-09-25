using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TikTokScrapper.Core;

public sealed record PhotoSlide(int Index, IReadOnlyList<string> Urls);
public sealed record Clip(string Id, string Url, string Title, bool IsPhoto = false, IReadOnlyList<PhotoSlide>? Photos = null);
// Fraction is the sum of partial-file progress across active transfers.
public sealed record DownloadProgress(string Phase, string Message, int Total = 0, int Completed = 0, int Failed = 0, int Skipped = 0, int Processed = 0, double Fraction = 0, int ActivePosts = 0);
public sealed record DownloadResult(int Total, int Downloaded, int Skipped, IReadOnlyList<string> Failed, string Folder, int Videos = 0, int Images = 0, bool AvatarSaved = false);

public sealed class DownloadService(IProcessRunner runner, IPhotoDownloader? photoDownloader = null, IAvatarEncoder? avatarEncoder = null)
{
    public static List<string> CommonArguments(AppSettings settings)
    {
        var args = new List<string> { "--ignore-config", "--no-plugin-dirs", "--plugin-dirs", ExtractorPlugin.EnsureDirectory(), "--no-colors", "--encoding", "utf-8", "--socket-timeout", "30", "--retries", "3", "--extractor-retries", "2" };
        if (!string.IsNullOrWhiteSpace(settings.CookiesFile))
        {
            if (!File.Exists(settings.CookiesFile)) throw new IOException("The selected cookies file is missing. Choose it again in Settings.");
            args.AddRange(["--cookies", settings.CookiesFile]);
        }
        return args;
    }

    public async Task<DownloadResult> DownloadAsync(ProfileUrl profile, AppSettings settings, string engine, IProgress<DownloadProgress> progress, Action<string> log, CancellationToken token)
    {
        var folder = Path.Combine(settings.SaveFolder, profile.FolderName);
        Directory.CreateDirectory(folder);
        // Fail before network activity when the chosen destination is not writable.
        var probe = Path.Combine(folder, ".write-test-" + Guid.NewGuid().ToString("N"));
        using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
        progress.Report(new("scan", "Finding posts on @" + profile.Username + "…"));
        var listing = new StringBuilder();
        var errors = new List<string>();
        var scanArgs = CommonArguments(settings);
        scanArgs.AddRange(["--flat-playlist", "--dump-single-json", "--skip-download", "--", profile.Url]);
        var scanCode = await runner.RunAsync(engine, scanArgs,
            line => { if (listing.Length < 64 * 1024 * 1024) listing.AppendLine(line); },
            line => { errors.Add(line); log(line); }, token);
        if (scanCode != 0) throw new IOException(FriendlyError(errors.LastOrDefault() ?? "TikTok did not return this profile."));
        var clips = ParseClips(listing.ToString(), profile);
        using var listingDocument = JsonDocument.Parse(listing.ToString());
        var avatarAvailable = listingDocument.RootElement.TryGetProperty("scrapper_avatar", out var avatar);
        if (clips.Count == 0 && !avatarAvailable) throw new IOException("No public posts were returned. The profile may be empty, private, or restricted by TikTok.");
        int downloaded = 0, skipped = 0, processed = 0, videos = 0, images = 0;
        int total = clips.Sum(c => c.IsPhoto ? Math.Max(1, c.Photos?.Count ?? 0) : 1) + (avatarAvailable ? 1 : 0);
        var failed = new List<string>();
        var avatarSaved = false;
        if (avatarAvailable)
        {
            var temporary = Path.Combine(folder, ".avatar." + Guid.NewGuid().ToString("N") + ".download.image");
            var jpeg = temporary + ".jpeg";
            try
            {
                token.ThrowIfCancellationRequested();
                progress.Report(new("download", "Saving profile avatar…", total));
                var file = Path.Combine(folder, "_profile.jpeg");
                if (PhotoDownloader.DetectExtension(file) == ".jpg") skipped++;
                else
                {
                    var urls = avatar.ValueKind == JsonValueKind.Array ? avatar.EnumerateArray()
                        .Where(u => u.ValueKind == JsonValueKind.String && PhotoDownloader.IsMediaUrl(u.GetString())).Select(u => u.GetString()!).ToList() : [];
                    var extension = await (photoDownloader ?? new PhotoDownloader()).DownloadAsync(urls, profile.Url, temporary,
                        fraction => progress.Report(new("download", "Saving profile avatar…", total, Fraction: fraction)), token);
                    if (extension == ".jpg" && PhotoDownloader.DetectExtension(temporary) == ".jpg") File.Move(temporary, jpeg);
                    else if (avatarEncoder is not null) avatarEncoder.WriteJpeg(temporary, jpeg);
                    else throw new IOException("The JPEG avatar converter is unavailable.");
                    token.ThrowIfCancellationRequested();
                    if (PhotoDownloader.DetectExtension(jpeg) != ".jpg") throw new IOException("The profile avatar was not a complete JPEG.");
                    File.Move(jpeg, file, true);
                }
                downloaded++;
                avatarSaved = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add("_profile.jpeg"); log("Profile avatar: " + ex.Message);
            }
            finally
            {
                foreach (var file in new[] { temporary, jpeg })
                    try { File.Delete(file); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log("Avatar cleanup: " + ex.Message); }
            }
            processed++;
            progress.Report(new("download", avatarSaved ? "Ready · _profile.jpeg" : "Could not save · _profile.jpeg", total, downloaded, failed.Count, skipped, processed));
        }
        var stateLock = new object();
        var fractions = new Dictionary<string, double>();
        int activePosts = 0;
        void Log(string message) { lock (stateLock) log(message); }
        void Report(string message)
        {
            // Called while holding stateLock, preserving snapshot and delivery order.
            progress.Report(new("download", message, total, downloaded, failed.Count, skipped, processed, fractions.Values.Sum(), activePosts));
        }
        void ReportFraction(string id, string message, double fraction)
        {
            lock (stateLock)
            {
                if (!fractions.ContainsKey(id)) return;
                fractions[id] = Math.Max(fractions[id], Math.Clamp(fraction, 0, 1));
                Report(message);
            }
        }
        async Task DownloadPostAsync(Clip clip, AppSettings workerSettings, CancellationToken workerToken)
        {
            workerToken.ThrowIfCancellationRequested();
            var slides = clip.Photos ?? [];
            if (clip.IsPhoto && (slides.Count == 0 || slides.Any(slide => FindSavedImage(folder, $"{clip.Id}_{slide.Index:000}") is null)))
            {
                lock (stateLock) Report("Preparing photo post " + clip.Id);
                // Refresh signed image URLs when the post is reached, even after a long profile scan.
                var fresh = await RefreshPhotosAsync(clip, workerSettings, engine, Log, workerToken);
                if (fresh is { Count: > 0 })
                {
                    lock (stateLock) total += fresh.Count - Math.Max(1, slides.Count);
                    slides = fresh;
                }
                if (slides.Count == 0) slides = [new PhotoSlide(1, [])];
            }
            foreach (var slide in clip.IsPhoto ? slides.Cast<PhotoSlide?>() : new PhotoSlide?[] { null })
            {
                workerToken.ThrowIfCancellationRequested();
                var id = slide is null ? clip.Id : $"{clip.Id}_{slide.Index:000}";
                var label = slide is null ? "Video " + clip.Id : $"{clip.Id} · Image {slide.Index} of {slides.Count}";
                var file = Path.Combine(folder, id + ".mp4");
                if (slide is null ? IsMp4(file) : FindSavedImage(folder, id) is not null)
                {
                    lock (stateLock)
                    {
                        skipped++; downloaded++; processed++;
                        if (slide is null) videos++; else images++;
                        Report("Already saved · " + id);
                    }
                    continue;
                }
                // Stage incomplete-file retries on the same volume; never replace a complete existing file.
                var temporary = Path.Combine(folder, "." + id + "." + Guid.NewGuid().ToString("N") + (slide is null ? ".download.mp4" : ".download.image"));
                lock (stateLock) { fractions[id] = 0; Report(label); }
                var clipErrors = new List<string>();
                var saved = false; var alreadySaved = false;
                try
                {
                    var valid = false;
                    if (slide is not null)
                    {
                        var extension = await (photoDownloader ?? new PhotoDownloader()).DownloadAsync(slide.Urls, clip.Url, temporary,
                            fraction => ReportFraction(id, label, fraction), workerToken);
                        valid = extension == PhotoDownloader.DetectExtension(temporary);
                        if (valid) file = Path.Combine(folder, id + extension);
                    }
                    else
                    {
                        var arguments = CommonArguments(workerSettings);
                        arguments.AddRange(["--no-playlist", "--newline", "--progress", "--progress-delta", "0.3", "--no-simulate", "--force-overwrites",
                            "--format", "best[ext=mp4][vcodec!=none]", "--format-sort-force", "--format-sort", "res,fps,br,size", "--windows-filenames",
                            "--output", temporary.Replace("%", "%%"), "--progress-template", "download:__PROGRESS__:%(progress)j", "--", clip.Url]);
                        var exit = await runner.RunAsync(engine, arguments,
                            line =>
                            {
                                if (TryProgress(line, out var fraction, out var detail))
                                    ReportFraction(id, $"{clip.Id}  ·  {detail}", fraction);
                                else Log(line);
                            }, line => { clipErrors.Add(line); Log(line); }, workerToken);
                        valid = exit == 0 && IsMp4(temporary);
                    }
                    workerToken.ThrowIfCancellationRequested();
                    if (valid)
                    {
                        // Recheck in case another app instance completed this file during transfer.
                        alreadySaved = slide is null ? IsMp4(file) : FindSavedImage(folder, id) is not null;
                        if (!alreadySaved) File.Move(temporary, file, overwrite: true);
                        saved = true;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    clipErrors.Add(ex.Message);
                }
                finally
                {
                    // Only remove this attempt's explicitly named temporary files.
                    foreach (var leftover in new[] { temporary, temporary + ".part", temporary + ".ytdl" })
                    {
                        try { File.Delete(leftover); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log("Temporary file cleanup: " + ex.Message); }
                    }
                }
                lock (stateLock)
                {
                    fractions.Remove(id);
                    processed++;
                    if (saved)
                    {
                        downloaded++;
                        if (slide is null) videos++; else images++;
                        if (alreadySaved) skipped++;
                    }
                    else
                    {
                        failed.Add(id);
                        Log($"Failed {id}: {clipErrors.LastOrDefault() ?? "No complete media file was saved."}");
                    }
                    Report(saved ? "Saved · " + id : "Could not save · " + id);
                }
            }
        }
        using var stopWorkers = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var sessions = new WorkerSessions(settings, Math.Min(settings.DownloadLimit, clips.Count), Log);
        int nextClip = -1;
        var workers = sessions.Settings.Select(workerSettings => Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    stopWorkers.Token.ThrowIfCancellationRequested();
                    var index = Interlocked.Increment(ref nextClip);
                    if (index >= clips.Count) return;
                    lock (stateLock) { activePosts++; Report("Starting post " + clips[index].Id); }
                    try { await DownloadPostAsync(clips[index], workerSettings, stopWorkers.Token); }
                    finally
                    {
                        lock (stateLock)
                        {
                            activePosts--;
                            if (!stopWorkers.IsCancellationRequested) Report("Finished post " + clips[index].Id);
                        }
                    }
                }
            }
            catch { stopWorkers.Cancel(); throw; }
        })).ToArray();
        // Await every worker before cleanup, completion alerts, or returning after cancellation.
        await Task.WhenAll(workers);
        token.ThrowIfCancellationRequested();
        return new(total, downloaded, skipped, failed.Order(StringComparer.Ordinal).ToArray(), folder, videos, images, avatarSaved);
    }

    private static string? FindSavedImage(string folder, string id)
    {
        foreach (var extension in new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".avif" })
        {
            var file = Path.Combine(folder, id + extension);
            if (PhotoDownloader.DetectExtension(file) == (extension == ".jpeg" ? ".jpg" : extension)) return file;
        }
        return null;
    }

    public static List<Clip> ParseClips(string json, ProfileUrl profile)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("scrapper_schema", out var schema) || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var version) || version != 1)
            throw new IOException("The photo-post extractor did not load. Update the download engine in Settings and retry; no posts were silently skipped.");
        if (!doc.RootElement.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array) throw new IOException("TikTok returned an unexpected profile response. Try updating the engine in Settings.");
        var clips = new List<Clip>();
        var seen = new HashSet<string>();
        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("id", out var idProperty)) continue;
            var id = idProperty.ToString();
            if (!Regex.IsMatch(id, @"^[0-9]{5,30}$") || !seen.Add(id)) continue;
            var url = entry.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
            var photos = ParsePhotos(entry);
            var photo = photos.Count > 0 || entry.TryGetProperty("scrapper_photo", out var isPhoto) && isPhoto.ValueKind == JsonValueKind.True || url?.Contains("/photo/", StringComparison.OrdinalIgnoreCase) == true;
            // Reconstruct from validated identifiers; never execute URLs supplied by a playlist.
            clips.Add(new(id, $"https://www.tiktok.com/@{profile.Username}/video/{id}", entry.TryGetProperty("title", out var t) ? t.ToString() : id, photo, photos));
        }
        return clips;
    }

    private static List<PhotoSlide> ParsePhotos(JsonElement entry)
    {
        var result = new List<PhotoSlide>();
        if (entry.TryGetProperty("scrapper_images", out var images) && images.ValueKind == JsonValueKind.Array)
        {
            foreach (var image in images.EnumerateArray())
            {
                var urls = new List<string>();
                if (image.ValueKind == JsonValueKind.Object && image.TryGetProperty("urls", out var candidates) && candidates.ValueKind == JsonValueKind.Array)
                    foreach (var candidate in candidates.EnumerateArray())
                        if (candidate.ValueKind == JsonValueKind.String && PhotoDownloader.IsMediaUrl(candidate.GetString())) urls.Add(candidate.GetString()!);
                result.Add(new(result.Count + 1, urls));
            }
        }
        return result;
    }

    private async Task<IReadOnlyList<PhotoSlide>?> RefreshPhotosAsync(Clip clip, AppSettings settings, string engine, Action<string> log, CancellationToken token)
    {
        var arguments = CommonArguments(settings);
        arguments.AddRange(["--no-playlist", "--dump-single-json", "--skip-download", "--ignore-no-formats-error", "--", clip.Url]);
        var json = new StringBuilder();
        try
        {
            var code = await runner.RunAsync(engine, arguments, line => { if (json.Length < 8 * 1024 * 1024) json.AppendLine(line); }, log, token);
            if (code == 0)
            {
                using var doc = JsonDocument.Parse(json.ToString());
                if (doc.RootElement.TryGetProperty("id", out var id) && id.ToString() == clip.Id) return ParsePhotos(doc.RootElement);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException) { log("Photo URL refresh: " + ex.Message); }
        log("Using photo URLs from the profile listing for " + clip.Id + ".");
        return null;
    }

    public static bool IsMp4(string file)
    {
        if (!File.Exists(file)) return false;
        using var stream = File.OpenRead(file);
        if (stream.Length <= 32) return false;
        Span<byte> header = stackalloc byte[12];
        return stream.Read(header) == 12 && header.Slice(4, 4).SequenceEqual("ftyp"u8);
    }

    public static bool TryProgress(string line, out double fraction, out string detail)
    {
        fraction = 0; detail = "Downloading…";
        if (!line.StartsWith("__PROGRESS__:", StringComparison.Ordinal)) return false;
        try
        {
            using var doc = JsonDocument.Parse(line[13..]);
            var data = doc.RootElement;
            double Number(string name) => data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : 0;
            var total = Number("total_bytes");
            if (total <= 0) total = Number("total_bytes_estimate");
            fraction = total > 0 ? Math.Clamp(Number("downloaded_bytes") / total, 0, 1) : 0;
            var speed = Number("speed");
            detail = $"{fraction:P0}" + (speed > 0 ? $"  ·  {speed / 1048576:0.0} MB/s" : "");
            return true;
        }
        catch (JsonException) { return false; }
    }

    public static string FriendlyError(string error)
    {
        var prefix = "TikTok could not be reached. ";
        if (error.Contains("private", StringComparison.OrdinalIgnoreCase)) prefix = "This profile is private or unavailable. ";
        else if (error.Contains("login", StringComparison.OrdinalIgnoreCase) || error.Contains("403") || error.Contains("secondary user ID", StringComparison.OrdinalIgnoreCase))
            prefix = "TikTok restricted access to this profile. Update the engine or add a cookies file in Settings, then retry. ";
        return prefix + error;
    }
}
