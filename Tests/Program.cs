using System.Text;
using TikTokScrapper.Core;

if (args.Length == 3 && args[0] == "--live-smoke")
{
    ProfileUrl.TryParse("https://www.tiktok.com/@orgonite222888", out var liveProfile);
    var liveService = new DownloadService(new LimitedLiveRunner());
    var liveResult = await liveService.DownloadAsync(liveProfile!, new(args[2]), args[1], new ImmediateProgress(p => Console.WriteLine($"{p.Phase}: {p.Completed}/{p.Total}, {p.Message}")), Console.WriteLine, CancellationToken.None);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(liveResult));
    if (liveResult.Total != 2 || liveResult.Failed.Count != 0 || liveResult.Downloaded != 2) Environment.Exit(1);
    return;
}

int passed = 0;
void Check(bool result, string name) { if (!result) throw new Exception("FAILED: " + name); Console.WriteLine("PASS " + name); passed++; }
foreach (var url in new[] { "https://www.tiktok.com/@orgonite222888", " https://www.tiktok.com/@name_2/?lang=en ", "tiktok.com/@name.2", "https://m.tiktok.com/@test" })
    Check(ProfileUrl.TryParse(url, out _), "valid profile: " + url);
foreach (var url in new[] { "", "not a link", "https://www.tiktok.com.evil.test/@user", "https://evil.test/@user", "https://tiktok.com/@name/video/12345", "https://tiktok.com/@name/extra", "https://tiktok.com/@name.", "https://user@tiktok.com/@name", "file:///tiktok.com/@name", "https://tiktok.com:8080/@name", "https://tiktok.com/@../", "https://tiktok.com/@name%2fother" })
    Check(!ProfileUrl.TryParse(url, out _), "invalid profile: " + url);
ProfileUrl.TryParse("http://tiktok.com/@name?utm_source=x", out var profile);
Check(profile!.Url == "https://www.tiktok.com/@name", "canonical HTTPS profile without query");
Check(new ProfileUrl("CON.txt", "https://www.tiktok.com/@CON.txt").FolderName == "@CON.txt", "@ prefix keeps Windows device names safe");
var clips = DownloadService.ParseClips("""{"scrapper_schema":1,"entries":[null,{"id":"12345","url":"https://evil.test/"},{"id":"12345"},{"id":"67890","url":"https://www.tiktok.com/@name/photo/67890"},{"id":"../bad"},{"id":"54321"}]}""", profile);
Check(clips.Count == 3 && clips[1].IsPhoto && clips.All(c => c.Url.StartsWith("https://www.tiktok.com/@name/video/")), "deduplication, photo inclusion, ID validation, URL reconstruction");
Check(DownloadService.TryProgress("__PROGRESS__:{\"downloaded_bytes\":50,\"total_bytes\":100,\"speed\":1048576}", out var f, out _) && f == .5, "progress fraction");
Check(DownloadService.TryProgress("__PROGRESS__:{\"downloaded_bytes\":200,\"total_bytes_estimate\":100}", out f, out _) && f == 1, "estimated progress clamps to one");
Check(!DownloadService.TryProgress("__PROGRESS__:not-json", out _, out _), "malformed progress does not crash");
Check(DownloadService.TryProgress("__PROGRESS__:{\"downloaded_bytes\":20,\"total_bytes\":null}", out f, out _) && f == 0, "unknown total");

var root = Path.Combine(Path.GetTempPath(), "TikTokScrapper-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var runner = new FakeRunner();
    var service = new DownloadService(runner);
    var updates = new List<DownloadProgress>();
    var result = await service.DownloadAsync(profile, new(root, ConcurrentDownloads: 1), "unused.exe", new ImmediateProgress(updates.Add), _ => { }, CancellationToken.None);
    Check(result.Downloaded == 2 && result.Total == 2 && result.Failed.Count == 0, "sequential successful downloads");
    Check(result.Folder == Path.Combine(root, "@name") && Directory.Exists(result.Folder) && !Directory.Exists(Path.Combine(root, "name")), "download directory includes @ before the profile name");
    Check(runner.PeakConcurrency == 1, "setting 1 downloads sequentially");
    Check(updates.Any(u => u.Total == 2 && u.Completed == 1), "accurate completed count");
    Check(runner.LastArguments!.Contains("best[ext=mp4][vcodec!=none]") && runner.LastArguments.Contains("--ignore-config"), "MP4 video format and isolated engine config");
    Check(runner.LastArguments[runner.LastArguments.IndexOf("--output") + 1].Contains(".download.mp4"), "engine writes to a staging file, not the final filename");
    runner.ContentMarker = 9;
    result = await service.DownloadAsync(profile, new(root, ConcurrentDownloads: 1), "unused.exe", new ImmediateProgress(_ => { }), _ => { }, CancellationToken.None);
    Check(result.Skipped == 2 && result.Downloaded == 2 && runner.DownloadCalls == 2 && File.ReadAllBytes(Path.Combine(result.Folder, "22222.mp4"))[^1] == 1, "existing videos count as downloaded without network transfers or overwrites");
    runner.ContentMarker = 10;
    runner.FailId = "22222";
    File.Delete(Path.Combine(result.Folder, "22222.mp4"));
    result = await service.DownloadAsync(profile, new(root, ConcurrentDownloads: 1), "unused.exe", new ImmediateProgress(_ => { }), _ => { }, CancellationToken.None);
    Check(result.Downloaded == 1 && result.Failed.SequenceEqual(new[] { "22222" }), "partial failure does not claim full success");
    Check(result.Skipped == 1 && File.ReadAllBytes(Path.Combine(result.Folder, "11111.mp4"))[^1] == 1, "failed missing-file retry keeps completed files untouched");
    runner.FailId = null; runner.NoOutput = true;
    result = await service.DownloadAsync(profile, new(root, ConcurrentDownloads: 1), "unused.exe", new ImmediateProgress(_ => { }), _ => { }, CancellationToken.None);
    Check(result.Failed.Count == 1 && result.Downloaded == 1 && result.Skipped == 1 && !File.Exists(Path.Combine(result.Folder, "22222.mp4")), "exit zero without new MP4 is a failure while existing videos remain counted");
    runner.NoOutput = false; runner.ContentMarker = 77;
    using var cancelReplacement = new CancellationTokenSource();
    runner.AfterWrite = cancelReplacement.Cancel;
    File.WriteAllText(Path.Combine(result.Folder, "11111.mp4"), "incomplete previous file");
    try { await service.DownloadAsync(profile, new(root, ConcurrentDownloads: 1), "unused.exe", new ImmediateProgress(_ => { }), _ => { }, cancelReplacement.Token); Check(false, "cancelled replacement must throw"); }
    catch (OperationCanceledException)
    {
        Check(File.ReadAllText(Path.Combine(result.Folder, "11111.mp4")) == "incomplete previous file", "cancelled retry does not destroy a previous incomplete file");
        Check(Directory.GetFiles(result.Folder, "*.download.mp4*").Length == 0, "cancelled staging files are cleaned up");
    }
    runner.AfterWrite = null;
    using var cancel = new CancellationTokenSource(); cancel.Cancel();
    try { await service.DownloadAsync(profile, new(root, ConcurrentDownloads: 1), "unused.exe", new ImmediateProgress(_ => { }), _ => { }, cancel.Token); Check(false, "cancel must throw"); }
    catch (OperationCanceledException) { Check(true, "cancellation propagated"); }
    var bad = Path.Combine(root, "fake.mp4"); File.WriteAllText(bad, new string('x', 100));
    Check(!DownloadService.IsMp4(bad), "non-MP4 file is not counted as saved");
    await PhotoTests.RunAsync(root, profile, Check, args);
    await ConcurrencyTests.RunAsync(root, profile, Check);
    // Real child-process plumbing, including argument quoting and process-tree cancellation.
    var actualRunner = new ProcessRunner();
    var lines = new List<string>();
    var command = "[Console]::OutputEncoding=[Text.UTF8Encoding]::new(); Write-Output 'paths [with spaces]'; [Console]::Error.WriteLine('stderr drained')";
    var code = await actualRunner.RunAsync("powershell.exe", ["-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(command))], lines.Add, _ => { }, CancellationToken.None);
    Check(code == 0 && lines.Contains("paths [with spaces]"), "real child stdout and stderr drained");
    using var cancelProcess = new CancellationTokenSource(TimeSpan.FromMilliseconds(600));
    var watch = System.Diagnostics.Stopwatch.StartNew();
    try { await actualRunner.RunAsync("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30"], _ => { }, _ => { }, cancelProcess.Token); Check(false, "process cancellation must throw"); }
    catch (OperationCanceledException) { Check(watch.Elapsed < TimeSpan.FromSeconds(8), "child process cancelled promptly"); }
}
finally { Directory.Delete(root, true); }
Console.WriteLine($"{passed} checks passed.");

sealed class ImmediateProgress(Action<DownloadProgress> receive) : IProgress<DownloadProgress> { public void Report(DownloadProgress value) => receive(value); }
sealed class LimitedLiveRunner : IProcessRunner
{
    public Task<int> RunAsync(string executable, IEnumerable<string> arguments, Action<string> output, Action<string> error, CancellationToken cancellationToken)
    {
        var limited = arguments.ToList();
        if (limited.Contains("--dump-single-json")) limited.InsertRange(limited.IndexOf("--"), new[] { "--playlist-start", "3", "--playlist-end", "4" });
        return new ProcessRunner().RunAsync(executable, limited, output, error, cancellationToken);
    }
}
sealed class FakeRunner : IProcessRunner
{
    public string Listing = """{"scrapper_schema":1,"entries":[{"id":"11111"},{"id":"22222"}]}""";
    public string? PhotoDetails;
    public int PeakConcurrency, Concurrent;
    public int DownloadCalls;
    private readonly object gate = new();
    public string? FailId;
    public bool NoOutput;
    public byte ContentMarker = 1;
    public Action? AfterWrite;
    public List<string>? LastArguments;
    public async Task<int> RunAsync(string executable, IEnumerable<string> arguments, Action<string> output, Action<string> error, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var callArguments = arguments.ToList();
        lock (gate) { LastArguments = callArguments; Concurrent++; PeakConcurrency = Math.Max(PeakConcurrency, Concurrent); }
        try
        {
            await Task.Delay(5, cancellationToken);
            if (callArguments.Contains("--dump-single-json")) { if (callArguments.Contains("--flat-playlist")) { output(Listing); return 0; } if (PhotoDetails is not null) { output(PhotoDetails); return 0; } return 1; }
            Interlocked.Increment(ref DownloadCalls);
            var file = callArguments[callArguments.IndexOf("--output") + 1].Replace("%%", "%");
            if (FailId is not null && callArguments[^1].EndsWith("/" + FailId)) { error("HTTP Error 403"); return 1; }
            output("__PROGRESS__:{\"downloaded_bytes\":50,\"total_bytes\":100}");
            if (!NoOutput) { var bytes = new byte[100]; "ftyp"u8.CopyTo(bytes.AsSpan(4)); bytes[^1] = ContentMarker; File.WriteAllBytes(file, bytes); AfterWrite?.Invoke(); }
            return 0;
        }
        finally { lock (gate) Concurrent--; }
    }
}
