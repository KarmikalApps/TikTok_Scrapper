using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using TikTokScrapper.Core;

internal static class PhotoTests
{
    public static async Task RunAsync(string root, ProfileUrl profile, Action<bool, string> check, string[] args)
    {
        const string listing = """{"scrapper_schema":1,"entries":[{"id":"33333"},{"id":"44444","scrapper_photo":true,"scrapper_images":[{"urls":["https://p16.tiktokcdn.com/stale1.jpg"]},{"urls":["https://p16.tiktokcdn.com/stale2.jpg"]},{"urls":["https://p16.tiktokcdn.com/stale3.jpg"]}]}]}""";
        const string details = """{"id":"44444","scrapper_photo":true,"scrapper_images":[{"urls":["https://p16.tiktokcdn.com/fresh1.jpg"]},{"urls":["https://p16.tiktokcdn.com/fresh2.jpg"]},{"urls":["https://p16.tiktokcdn.com/fresh3.jpg"]}]}""";
        var parsed = DownloadService.ParseClips(listing, profile);
        check(parsed.Count == 2 && !parsed[0].IsPhoto && parsed[1].Photos!.Count == 3, "mixed posts preserve all three slides");
        check(parsed[1].Photos!.Select(p => p.Index).SequenceEqual(new[] { 1, 2, 3 }), "slide order is retained");
        try { DownloadService.ParseClips("{\"entries\":[]}", profile); check(false, "missing extractor must fail"); }
        catch (IOException) { check(true, "missing photo extractor fails explicitly"); }
        check(!PhotoDownloader.IsMediaUrl("file:///test.jpg") && !PhotoDownloader.IsMediaUrl("http://127.0.0.1/a.jpg") && !PhotoDownloader.IsMediaUrl("https://tiktokcdn.com.evil.test/a.jpg") && !PhotoDownloader.IsMediaUrl("https://user:password@p16.tiktokcdn.com/a.jpg"), "photo URLs reject local paths, credentials, and lookalike hosts");
        var photos = new FakePhotos();
        var runner = new FakeRunner { Listing = listing, PhotoDetails = details };
        var service = new DownloadService(runner, photos);
        var updates = new List<DownloadProgress>();
        var result = await service.DownloadAsync(profile, new(root), "unused", new ImmediateProgress(updates.Add), _ => { }, CancellationToken.None);
        check(result.Total == 4 && result.Downloaded == 4 && result.Videos == 1 && result.Images == 3 && result.Failed.Count == 0, "one video plus three images gives four successful files");
        check(photos.Calls.SequenceEqual(new[] { "https://p16.tiktokcdn.com/fresh1.jpg", "https://p16.tiktokcdn.com/fresh2.jpg", "https://p16.tiktokcdn.com/fresh3.jpg" }), "signed photo URLs refresh and download in slide order");
        check(Enumerable.Range(1, 3).All(i => File.Exists(Path.Combine(result.Folder, $"44444_{i:000}.jpg"))) && !File.Exists(Path.Combine(result.Folder, "44444.mp4")), "stable numbered image filenames; no fake slideshow MP4");
        check(updates.All(u => u.Total is 0 or 4) && updates.Last().Completed == 4 && updates.Last().Processed == 4 && updates.Any(u => u.Fraction == .5), "progress counts each image and video exactly once");
        photos.Marker = 9;
        result = await service.DownloadAsync(profile, new(root), "unused", new ImmediateProgress(_ => { }), _ => { }, CancellationToken.None);
        var second = Path.Combine(result.Folder, "44444_002.jpg");
        check(result.Skipped == 4 && photos.Calls.Count == 3 && File.ReadAllBytes(second)[50] == 1, "repeat slideshow skips all existing images without transferring them again");
        photos.Marker = 10; photos.Fail = "fresh2";
        File.WriteAllText(second, "incomplete slide");
        File.Delete(Path.Combine(result.Folder, "44444_003.jpg"));
        result = await service.DownloadAsync(profile, new(root), "unused", new ImmediateProgress(_ => { }), _ => { }, CancellationToken.None);
        check(result.Downloaded == 3 && result.Skipped == 2 && result.Failed.SequenceEqual(new[] { "44444_002" }) && result.Images == 2, "retry skips complete files while a failed slide does not stop later slides");
        check(File.ReadAllText(second) == "incomplete slide" && File.ReadAllBytes(Path.Combine(result.Folder, "44444_003.jpg"))[50] == 10, "failed retry keeps incomplete original while later missing slide downloads");
        using (var cancel = new CancellationTokenSource())
        {
            photos.Fail = null; photos.Marker = 77; photos.AfterWrite = cancel.Cancel;
            File.WriteAllText(Path.Combine(result.Folder, "44444_001.jpg"), "incomplete first slide");
            try { await service.DownloadAsync(profile, new(root), "unused", new ImmediateProgress(_ => { }), _ => { }, cancel.Token); check(false, "photo cancellation must throw"); }
            catch (OperationCanceledException) { check(File.ReadAllText(Path.Combine(result.Folder, "44444_001.jpg")) == "incomplete first slide", "cancelled image retry keeps the previous file"); }
        }
        check(!Directory.EnumerateFiles(result.Folder).Any(f => f.Contains(".download.")), "failed and cancelled image staging files are removed");
        photos.AfterWrite = null;
        runner.Listing = """{"scrapper_schema":1,"entries":[{"id":"55555","scrapper_photo":true,"scrapper_images":[{"urls":[]},{"urls":["https://p16.tiktokcdn.com/fresh2.jpg"]}]}]}""";
        runner.PhotoDetails = null;
        result = await service.DownloadAsync(profile, new(root), "unused", new ImmediateProgress(_ => { }), _ => { }, CancellationToken.None);
        check(result.Total == 2 && result.Images == 1 && result.Failed.SequenceEqual(new[] { "55555_001" }) && File.Exists(Path.Combine(result.Folder, "55555_002.jpg")), "missing slide URL remains a failure without shifting later filenames");
        runner.Listing = """{"scrapper_schema":1,"entries":[{"id":"66666","scrapper_photo":true}]}""";
        result = await service.DownloadAsync(profile, new(root), "unused", new ImmediateProgress(_ => { }), _ => { }, CancellationToken.None);
        check(result.Downloaded == 0 && result.Failed.Count == 1, "photo post without images cannot claim success");

        runner.Listing = """{"scrapper_schema":1,"scrapper_avatar":["https://p16.tiktokcdn.com/avatar.jpg"],"entries":[{"id":"77777"}]}""";
        photos.Marker = 24;
        result = await service.DownloadAsync(profile, new(root), "unused", new ImmediateProgress(_ => { }), _ => { }, CancellationToken.None);
        var avatarFile = Path.Combine(result.Folder, "_profile.jpeg");
        check(result.AvatarSaved && result.Total == 2 && result.Downloaded == 2 && result.Videos == 1 && PhotoDownloader.DetectExtension(avatarFile) == ".jpg", "avatar saved as actual JPEG and counted alongside post files");
        photos.Marker = 25;
        result = await service.DownloadAsync(profile, new(root), "unused", new ImmediateProgress(_ => { }), _ => { }, CancellationToken.None);
        check(result.Skipped == 2 && File.ReadAllBytes(avatarFile)[50] == 24, "existing _profile.jpeg is skipped and unchanged on the next download");
        photos.Fail = "avatar";
        File.WriteAllText(avatarFile, "incomplete avatar");
        result = await service.DownloadAsync(profile, new(root), "unused", new ImmediateProgress(_ => { }), _ => { }, CancellationToken.None);
        check(!result.AvatarSaved && result.Videos == 1 && result.Failed.SequenceEqual(new[] { "_profile.jpeg" }) && File.ReadAllText(avatarFile) == "incomplete avatar", "avatar failure preserves old data and continues with posts");
        photos.Fail = null;
        using (var cancel = new CancellationTokenSource())
        {
            photos.AfterWrite = cancel.Cancel;
            try { await service.DownloadAsync(profile, new(root), "unused", new ImmediateProgress(_ => { }), _ => { }, cancel.Token); check(false, "avatar cancellation must throw"); }
            catch (OperationCanceledException) { check(File.ReadAllText(avatarFile) == "incomplete avatar" && !Directory.EnumerateFiles(result.Folder).Any(f => f.Contains(".download.")), "avatar cancellation preserves previous data and cleans staging files"); }
        }
        photos.AfterWrite = null;
        runner.Listing = """{"scrapper_schema":1,"scrapper_avatar":[],"entries":[{"id":"77777"}]}""";
        result = await service.DownloadAsync(profile, new(root), "unused", new ImmediateProgress(_ => { }), _ => { }, CancellationToken.None);
        check(result.Failed.Contains("_profile.jpeg") && result.Videos == 1, "unavailable avatar is reported without blocking videos");
        check(!new AppSettings(root).NotifyWhenFinished && !JsonSerializer.Deserialize<AppSettings>("{\"SaveFolder\":\"C:/test\"}")!.NotifyWhenFinished, "completion alert defaults off for both new and existing settings");
        var enabled = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings(root, NotifyWhenFinished: true)))!;
        check(enabled.NotifyWhenFinished && CompletionNotice.Create(enabled, result)!.Message.Contains("failed"), "enabled alert persists and distinguishes incomplete downloads");
        check(CompletionNotice.Create(new(root), result) is null, "disabled alert produces no notification request");
        check(CompletionNotice.Create(enabled, new(2, 2, 0, [], root, Videos: 1, AvatarSaved: true))!.Title == "Download Completed", "successful run gets completion notification text");

        var temporary = Path.Combine(root, "photo-http-test.image");
        var requests = new List<string>();
        using var http = new HttpClient(new FakeHttp(request =>
        {
            requests.Add(request.RequestUri!.AbsolutePath);
            return request.RequestUri.AbsolutePath == "/backup.jpg"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(FakePhotos.Jpeg(7)) }
                : new HttpResponseMessage(HttpStatusCode.Forbidden);
        }));
        var extension = await new PhotoDownloader(http).DownloadAsync(["https://p16.tiktokcdn.com/expired.jpg", "https://p19.tiktokcdn.com/backup.jpg"], profile.Url, temporary, _ => { }, CancellationToken.None);
        check(extension == ".jpg" && File.ReadAllBytes(temporary)[50] == 7 && requests.Contains("/backup.jpg"), "HTTP photo downloader retries an alternate original-image URL");
        using var invalid = new HttpClient(new FakeHttp(_ => new(HttpStatusCode.OK) { Content = new StringContent("<html>not an image, please log in</html>") }));
        try { await new PhotoDownloader(invalid).DownloadAsync(["https://p16.tiktokcdn.com/bad.jpg"], profile.Url, temporary, _ => { }, CancellationToken.None); check(false, "HTML must fail"); }
        catch (IOException) { check(true, "HTTP success containing HTML is rejected"); }
        using var truncated = new HttpClient(new FakeHttp(_ => { var content = new ByteArrayContent(FakePhotos.Jpeg(8)); content.Headers.ContentLength = 200; return new(HttpStatusCode.OK) { Content = content }; }));
        try { await new PhotoDownloader(truncated).DownloadAsync(["https://p16.tiktokcdn.com/short.jpg"], profile.Url, temporary, _ => { }, CancellationToken.None); check(false, "truncated transfer must fail"); }
        catch (IOException) { check(true, "truncated image response is rejected"); }
        using var redirect = new HttpClient(new FakeHttp(_ => { var response = new HttpResponseMessage(HttpStatusCode.Redirect); response.Headers.Location = new Uri("http://127.0.0.1/private"); return response; }));
        try { await new PhotoDownloader(redirect).DownloadAsync(["https://p16.tiktokcdn.com/redirect.jpg"], profile.Url, temporary, _ => { }, CancellationToken.None); check(false, "unsafe redirect must fail"); }
        catch (IOException) { check(true, "redirect to a local or untrusted host is rejected"); }
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aX1sAAAAASUVORK5CYII=");
        File.WriteAllBytes(temporary, png);
        check(PhotoDownloader.DetectExtension(temporary) == ".png", "image extension follows PNG bytes, not URL suffix");
        var webp = new byte[32]; "RIFF"u8.CopyTo(webp); BitConverter.GetBytes(24).CopyTo(webp, 4); "WEBP"u8.CopyTo(webp.AsSpan(8));
        File.WriteAllBytes(temporary, webp);
        check(PhotoDownloader.DetectExtension(temporary) == ".webp", "original WebP images retain their format");

        if (args.Length == 2 && args[0] == "--engine")
        {
            var productionPlugin = ExtractorPlugin.EnsureDirectory(root);
            var fixtureRoot = Path.Combine(root, "fixture");
            var fixtureFile = Path.Combine(fixtureRoot, "tests", "yt_dlp_plugins", "extractor", "scrapper_fixture.py");
            Directory.CreateDirectory(Path.GetDirectoryName(fixtureFile)!);
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("TikTokScrapper.EngineFixture")!)
            using (var destination = File.Create(fixtureFile)) resource.CopyTo(destination);
            foreach (var mode in new[] { "profile", "photo" })
            {
                var json = new StringBuilder(); var errors = new StringBuilder();
                var engineArgs = new List<string> { "--ignore-config", "--no-plugin-dirs", "--plugin-dirs", productionPlugin, "--plugin-dirs", fixtureRoot, "--flat-playlist", "--dump-single-json", "--skip-download", "--ignore-no-formats-error", "--", "scrapperfixture:" + mode };
                var code = await new ProcessRunner().RunAsync(args[1], engineArgs, line => json.AppendLine(line), line => errors.AppendLine(line), CancellationToken.None);
                if (code != 0) throw new Exception("Engine fixture: " + errors);
                using var doc = JsonDocument.Parse(json.ToString());
                if (mode == "profile")
                {
                    var posts = DownloadService.ParseClips(json.ToString(), profile);
                    check(posts.Count == 2 && posts[1].IsPhoto && posts[1].Photos!.Count == 3 && posts[1].Photos![0].Urls.Count == 2 && posts[1].Photos![2].Urls.Count == 0, "real bundled engine retains full slide metadata through profile JSON");
                    check(doc.RootElement.GetProperty("scrapper_avatar")[0].GetString()!.Contains("avatar-large"), "real bundled engine exposes largest profile avatar first");
                }
                else check(doc.RootElement.GetProperty("scrapper_images").GetArrayLength() == 3 && doc.RootElement.GetProperty("scrapper_photo").GetBoolean(), "real bundled engine retains photo metadata when refreshing a post");
            }
        }
    }

    private sealed class FakePhotos : IPhotoDownloader
    {
        public byte Marker = 1;
        public string? Fail;
        public Action? AfterWrite;
        public readonly List<string> Calls = [];
        public Task<string> DownloadAsync(IReadOnlyList<string> urls, string referer, string temporary, Action<double> progress, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (urls.Count == 0) throw new IOException("No image URL.");
            Calls.Add(urls[0]);
            File.WriteAllBytes(temporary, Jpeg(Marker)); progress(.5);
            if (Fail is not null && urls[0].Contains(Fail)) throw new IOException("Simulated image failure.");
            AfterWrite?.Invoke();
            return Task.FromResult(".jpg");
        }
        public static byte[] Jpeg(byte marker) { var bytes = new byte[100]; bytes[0] = 0xff; bytes[1] = 0xd8; bytes[2] = 0xff; bytes[50] = marker; bytes[^2] = 0xff; bytes[^1] = 0xd9; return bytes; }
    }
    private sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
