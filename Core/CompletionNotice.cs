namespace TikTokScrapper.Core;

public sealed record CompletionNotice(string Title, string Message)
{
    public static CompletionNotice? Create(AppSettings settings, DownloadResult result)
    {
        if (!settings.NotifyWhenFinished) return null;
        return new(result.Failed.Count == 0 ? "Download Completed" : "Download finished with failed files",
            $"{result.Videos} videos and {result.Images} images ready" + (result.AvatarSaved ? ", plus the profile avatar." : ".") +
            (result.Skipped > 0 ? $" {result.Skipped} files were already saved." : "") +
            (result.Failed.Count > 0 ? $" {result.Failed.Count} files failed; open TikTok Scrapper for details." : " Your posts are ready."));
    }
}
