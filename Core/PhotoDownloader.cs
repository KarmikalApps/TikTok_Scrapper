using System.Buffers.Binary;
using System.IO;
using System.Net.Http;

namespace TikTokScrapper.Core;

public interface IPhotoDownloader
{
    Task<string> DownloadAsync(IReadOnlyList<string> urls, string referer, string temporary, Action<double> progress, CancellationToken token);
}

public sealed class PhotoDownloader(HttpClient? client = null) : IPhotoDownloader
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(90) };
    private static readonly string[] MediaDomains = ["tiktokcdn.com", "tiktokcdn-us.com", "tiktokcdn-eu.com", "byteoversea.com", "ibytedtos.com", "byteimg.com", "tiktok.com", "tiktokv.com"];

    public static bool IsMediaUrl(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
        MediaDomains.Any(domain => uri.IdnHost.Equals(domain, StringComparison.OrdinalIgnoreCase) || uri.IdnHost.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));

    public async Task<string> DownloadAsync(IReadOnlyList<string> urls, string referer, string temporary, Action<double> progress, CancellationToken token)
    {
        Exception? last = null;
        foreach (var url in urls.Where(IsMediaUrl).Distinct())
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var current = new Uri(url);
                    for (var redirects = 0; ; redirects++)
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Get, current);
                        request.Headers.Referrer = new Uri(referer);
                        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0.0.0 Safari/537.36");
                        using var response = await (client ?? Http).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                        if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
                        {
                            current = new Uri(current, location);
                            if (redirects >= 5 || !IsMediaUrl(current.AbsoluteUri)) throw new IOException("Unexpected image redirect.");
                            continue;
                        }
                        if (!response.IsSuccessStatusCode) throw new IOException($"Image server returned HTTP {(int)response.StatusCode}.");
                        var length = response.Content.Headers.ContentLength;
                        const long maximumSize = 128 * 1024 * 1024;
                        if (length > maximumSize) throw new IOException("The image exceeds the supported 128 MB file size.");
                        // ResponseHeadersRead stops HttpClient's timeout after headers; cover the body too.
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                        timeout.CancelAfter(TimeSpan.FromMinutes(2));
                        await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
                        await using (var output = File.Create(temporary))
                        {
                            var buffer = new byte[81920];
                            long received = 0;
                            int count;
                            while ((count = await input.ReadAsync(buffer, timeout.Token)) > 0)
                            {
                                received += count;
                                if (received > maximumSize) throw new IOException("The image exceeds the supported 128 MB file size.");
                                await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
                                progress(length is > 0 ? Math.Min(.99, (double)received / length.Value) : 0);
                            }
                            if (length.HasValue && received != length.Value) throw new IOException("The image transfer was incomplete.");
                        }
                        return DetectExtension(temporary) ?? throw new IOException("The server did not return a complete supported image.");
                    }
                }
                catch (Exception ex) when (ex is IOException or HttpRequestException || ex is OperationCanceledException && !token.IsCancellationRequested)
                {
                    last = ex;
                    if (attempt == 0) await Task.Delay(400, token);
                }
            }
        }
        throw new IOException(last is null ? "No original image URL was available for this slide." : "Could not download this image: " + last.Message, last);
    }

    public static string? DetectExtension(string file)
    {
        if (!File.Exists(file)) return null;
        using var stream = File.OpenRead(file);
        if (stream.Length < 16) return null;
        Span<byte> head = stackalloc byte[16];
        stream.ReadExactly(head);
        Span<byte> tail = stackalloc byte[12];
        stream.Seek(-12, SeekOrigin.End); stream.ReadExactly(tail);
        if (head[0] == 0xff && head[1] == 0xd8 && head[2] == 0xff && tail[^2] == 0xff && tail[^1] == 0xd9) return ".jpg";
        if (head[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) && tail[4..8].SequenceEqual("IEND"u8)) return ".png";
        if (head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8) && BinaryPrimitives.ReadUInt32LittleEndian(head[4..8]) + 8L == stream.Length) return ".webp";
        if ((head[..6].SequenceEqual("GIF87a"u8) || head[..6].SequenceEqual("GIF89a"u8)) && tail[^1] == 0x3b) return ".gif";
        if (head[4..8].SequenceEqual("ftyp"u8) && (head[8..12].SequenceEqual("avif"u8) || head[8..12].SequenceEqual("avis"u8))) return ".avif";
        return null;
    }
}
