using System.Collections.Concurrent;
using System.Text.Json;
using TikTokScrapper.Core;

internal static class ConcurrencyTests
{
    public static async Task RunAsync(string root, ProfileUrl profile, Action<bool, string> check)
    {
        check(new AppSettings(root).DownloadLimit == 3 && JsonSerializer.Deserialize<AppSettings>("{\"SaveFolder\":\"C:/test\"}")!.DownloadLimit == 3, "parallelism defaults to three for new and upgraded settings");
        check(new AppSettings(root, ConcurrentDownloads: -2).DownloadLimit == 1 && new AppSettings(root, ConcurrentDownloads: 99).DownloadLimit == 10, "invalid stored parallelism is bounded to one through ten");
        check(JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings(root, ConcurrentDownloads: 7)))!.DownloadLimit == 7, "selected parallelism survives settings serialization");
        foreach (var limit in new[] { 1, 3, 10 })
        {
            var folder = Path.Combine(root, "parallel-" + limit);
            var runner = new ControlledRunner(12, limit);
            var updates = new ConcurrentQueue<DownloadProgress>();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var download = new DownloadService(runner).DownloadAsync(profile, new(folder, ConcurrentDownloads: limit), "unused", new ImmediateProgress(updates.Enqueue), _ => { }, timeout.Token);
            await runner.Saturated.Task.WaitAsync(timeout.Token);
            check(runner.Peak == limit && !download.IsCompleted, $"{limit} post slots are active concurrently without completing early");
            runner.Release.TrySetResult();
            var result = await download;
            check(result.Total == 12 && result.Downloaded == 12 && result.Skipped == 0 && runner.Peak <= limit && runner.Active == 0, $"limit {limit}: exact totals and all workers joined");
            var snapshots = updates.Where(p => p.Phase == "download").ToArray();
            check(snapshots.All(p => p.Completed + p.Failed == p.Processed && p.Processed + p.Fraction <= p.Total + .0001 && p.ActivePosts <= limit) &&
                  snapshots.Zip(snapshots.Skip(1)).All(pair => pair.First.Completed <= pair.Second.Completed), $"limit {limit}: combined progress is consistent and completed count never regresses");
            if (limit > 1) check(snapshots.Any(p => p.Fraction > 1), $"limit {limit}: progress includes simultaneous partial transfers");
        }

        var retryFolder = Path.Combine(root, "parallel-retry");
        var partial = new ControlledRunner(8, 3) { FailId = "90002" };
        partial.Release.TrySetResult();
        var first = await new DownloadService(partial).DownloadAsync(profile, new(retryFolder), "unused", new ImmediateProgress(_ => { }), _ => { }, CancellationToken.None);
        check(first.Downloaded == 7 && first.Failed.SequenceEqual(new[] { "90002" }), "one parallel failure does not stop other posts");
        var existingFile = Path.Combine(first.Folder, "90001.mp4");
        var timestamp = File.GetLastWriteTimeUtc(existingFile);
        var retry = new ControlledRunner(8, 1); retry.Release.TrySetResult();
        var resultRetry = await new DownloadService(retry).DownloadAsync(profile, new(retryFolder), "unused", new ImmediateProgress(_ => { }), _ => { }, CancellationToken.None);
        check(resultRetry.Downloaded == 8 && resultRetry.Skipped == 7 && retry.Calls.SequenceEqual(new[] { "90002" }) && File.GetLastWriteTimeUtc(existingFile) == timestamp, "retry downloads only the failed post and leaves seven completed files untouched");

        var cookies = Path.Combine(root, "session.txt");
        const string originalCookies = "# Netscape HTTP Cookie File\n.test\tTRUE\t/\tTRUE\t0\ttest\tvalue\n";
        File.WriteAllText(cookies, originalCookies);
        var cookieRunner = new ControlledRunner(7, 3);
        var cookieJob = new DownloadService(cookieRunner).DownloadAsync(profile, new(Path.Combine(root, "parallel-cookies"), cookies), "unused", new ImmediateProgress(_ => { }), _ => { }, CancellationToken.None);
        await cookieRunner.Saturated.Task.WaitAsync(TimeSpan.FromSeconds(10));
        check(cookieRunner.CookiePaths.Count == 3 && !cookieRunner.CookiePaths.Contains(cookies), "parallel engine processes use isolated cookie files");
        cookieRunner.Release.TrySetResult(); await cookieJob;
        check(File.ReadAllText(cookies) == originalCookies && cookieRunner.CookiePaths.All(p => !File.Exists(p) && !Directory.Exists(Path.GetDirectoryName(p))), "worker cookie changes do not overwrite user cookies and temporary jars are removed");

        var cancelRunner = new ControlledRunner(12, 3);
        using var cancel = new CancellationTokenSource();
        var cancelled = new DownloadService(cancelRunner).DownloadAsync(profile, new(Path.Combine(root, "parallel-cancel"), cookies), "unused", new ImmediateProgress(_ => { }), _ => { }, cancel.Token);
        await cancelRunner.Saturated.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancel.Cancel();
        try { await cancelled; check(false, "parallel cancellation must propagate"); }
        catch (OperationCanceledException)
        {
            check(cancelRunner.Active == 0 && cancelRunner.Calls.Count == 3, "cancel drains all active workers and does not start queued posts");
            check(cancelRunner.StagingPaths.All(p => !File.Exists(p)) && cancelRunner.CookiePaths.All(p => !File.Exists(p)), "cancel cleans every worker's partial file and cookie jar before returning");
        }

        var mixed = new MixedPosts();
        var mixedUpdates = new ConcurrentQueue<DownloadProgress>();
        var mixedResult = await new DownloadService(mixed, mixed).DownloadAsync(profile, new(Path.Combine(root, "parallel-mixed")), "unused", new ImmediateProgress(mixedUpdates.Enqueue), _ => { }, CancellationToken.None);
        check(mixed.Peak is > 1 and <= 3 && mixedResult.Videos == 2 && mixedResult.Images == 6 && mixedResult.Downloaded == 8, "mixed videos and photo albums share the configured post limit");
        check(mixed.Slides.GroupBy(s => s.Post).All(g => g.Select(s => s.Slide).SequenceEqual(new[] { 1, 2, 3 })), "each concurrent slideshow still downloads in slide order");
        check(mixedUpdates.Last().Completed == 8 && mixedUpdates.Last().ActivePosts == 0 && mixedUpdates.Last().Fraction == 0, "mixed-post completion has no active workers or leftover fractional progress");
    }

    private sealed class ControlledRunner(int count, int expected) : IProcessRunner
    {
        private readonly object gate = new();
        public readonly TaskCompletionSource Saturated = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ConcurrentQueue<string> Calls = new();
        public readonly ConcurrentBag<string> StagingPaths = [];
        public readonly HashSet<string> CookiePaths = [];
        public string? FailId;
        public int Active, Peak;
        public async Task<int> RunAsync(string executable, IEnumerable<string> arguments, Action<string> output, Action<string> error, CancellationToken token)
        {
            var args = arguments.ToList();
            if (args.Contains("--flat-playlist"))
            {
                output(JsonSerializer.Serialize(new { scrapper_schema = 1, entries = Enumerable.Range(0, count).Select(i => new { id = (90000 + i).ToString() }) })); return 0;
            }
            var id = args[^1].Split('/')[^1]; Calls.Enqueue(id);
            var path = args[args.IndexOf("--output") + 1].Replace("%%", "%"); StagingPaths.Add(path);
            File.WriteAllText(path, "partial transfer");
            using var cookie = args.Contains("--cookies") ? new FileStream(args[args.IndexOf("--cookies") + 1], FileMode.Open, FileAccess.ReadWrite, FileShare.None) : null;
            if (cookie is not null)
            {
                lock (gate) CookiePaths.Add(args[args.IndexOf("--cookies") + 1]);
                cookie.SetLength(0); cookie.Write("worker-refreshed cookies"u8); cookie.Flush();
            }
            lock (gate) { Active++; Peak = Math.Max(Peak, Active); }
            try
            {
                output("__PROGRESS__:{\"downloaded_bytes\":50,\"total_bytes\":100}");
                lock (gate) if (Active >= expected) Saturated.TrySetResult();
                await Release.Task.WaitAsync(token);
                token.ThrowIfCancellationRequested();
                if (id == FailId) { error("Simulated missing post"); return 1; }
                var bytes = new byte[100]; "ftyp"u8.CopyTo(bytes.AsSpan(4)); File.WriteAllBytes(path, bytes);
                output("__PROGRESS__:{\"downloaded_bytes\":100,\"total_bytes\":100}");
                return 0;
            }
            finally { lock (gate) Active--; }
        }
    }

    private sealed class MixedPosts : IProcessRunner, IPhotoDownloader
    {
        private readonly object gate = new();
        private int active;
        public int Peak;
        public readonly ConcurrentQueue<(string Post, int Slide)> Slides = new();
        private static object[] Images(string id) => Enumerable.Range(1, 3).Select(i => (object)new { urls = new[] { $"https://p16.tiktokcdn.com/{id}/{i}.jpg" } }).ToArray();
        private async Task Transfer(string path, byte[] bytes, Action<double> progress, CancellationToken token)
        {
            lock (gate) { active++; Peak = Math.Max(Peak, active); }
            try { progress(.4); await Task.Delay(35, token); File.WriteAllBytes(path, bytes); }
            finally { lock (gate) active--; }
        }
        public async Task<int> RunAsync(string executable, IEnumerable<string> arguments, Action<string> output, Action<string> error, CancellationToken token)
        {
            var args = arguments.ToList();
            if (args.Contains("--flat-playlist"))
            {
                output(JsonSerializer.Serialize(new { scrapper_schema = 1, entries = Enumerable.Range(0, 4).Select(i => new { id = (80000 + i).ToString(), scrapper_photo = i < 2, scrapper_images = i < 2 ? Images((80000 + i).ToString()) : [] }) })); return 0;
            }
            var id = args[^1].Split('/')[^1];
            if (args.Contains("--dump-single-json")) { output(JsonSerializer.Serialize(new { id, scrapper_images = Images(id) })); return 0; }
            var bytes = new byte[100]; "ftyp"u8.CopyTo(bytes.AsSpan(4));
            await Transfer(args[args.IndexOf("--output") + 1], bytes, _ => output("__PROGRESS__:{\"downloaded_bytes\":40,\"total_bytes\":100}"), token); return 0;
        }
        public async Task<string> DownloadAsync(IReadOnlyList<string> urls, string referer, string temporary, Action<double> progress, CancellationToken token)
        {
            var uri = new Uri(urls[0]);
            Slides.Enqueue((uri.Segments[^2].TrimEnd('/'), int.Parse(Path.GetFileNameWithoutExtension(uri.Segments[^1]))));
            var bytes = new byte[100]; bytes[0] = 255; bytes[1] = 216; bytes[2] = 255; bytes[^2] = 255; bytes[^1] = 217;
            await Transfer(temporary, bytes, progress, token); return ".jpg";
        }
    }
}
