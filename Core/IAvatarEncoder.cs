namespace TikTokScrapper.Core;

public interface IAvatarEncoder
{
    void WriteJpeg(string input, string output);
}
