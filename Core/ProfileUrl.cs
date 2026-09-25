using System.Text.RegularExpressions;

namespace TikTokScrapper.Core;

public sealed record ProfileUrl(string Username, string Url)
{
    public string FolderName => "@" + Username;

    public static bool TryParse(string? value, out ProfileUrl? profile)
    {
        profile = null;
        var text = value?.Trim() ?? "";
        if (!text.Contains("://", StringComparison.Ordinal)) text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || (uri.Scheme != "https" && uri.Scheme != "http")
            || !uri.IsDefaultPort || uri.UserInfo.Length != 0
            || !new[] { "tiktok.com", "www.tiktok.com", "m.tiktok.com" }.Contains(uri.Host.ToLowerInvariant())) return false;
        var match = Regex.Match(uri.AbsolutePath, @"^/@([A-Za-z0-9_][A-Za-z0-9_.]{0,23})/?$", RegexOptions.CultureInvariant);
        if (!match.Success || match.Groups[1].Value.EndsWith('.')) return false;
        var username = match.Groups[1].Value;
        profile = new(username, $"https://www.tiktok.com/@{username}");
        return true;
    }
}
