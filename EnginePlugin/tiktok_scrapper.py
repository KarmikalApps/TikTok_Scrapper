"""Expose TikTok's original ordered photo URLs alongside yt-dlp's video metadata."""
from yt_dlp.extractor.tiktok import TikTokIE, TikTokUserIE


def _photo_metadata(result, aweme_detail):
    photo = aweme_detail.get('imagePost')
    result['scrapper_photo'] = photo is not None
    result['scrapper_images'] = []
    if isinstance(photo, dict):
        images = photo.get('images') or []
        if isinstance(images, list):
            for image in images:
                # Keep every slide, including missing URLs, so an incomplete album fails visibly.
                source = image.get('imageURL') if isinstance(image, dict) else None
                urls = source.get('urlList') if isinstance(source, dict) else None
                result['scrapper_images'].append({
                    'urls': [url for url in urls if isinstance(url, str)]
                    if isinstance(urls, list) else [],
                })
    result['scrapper_schema'] = 1
    return result


class _ScrapperTikTokUserIE(TikTokUserIE, plugin_name='scrapper_photos'):
    def _parse_aweme_video_web(self, aweme_detail, *args, **kwargs):
        return _photo_metadata(super()._parse_aweme_video_web(aweme_detail, *args, **kwargs), aweme_detail)

    def _real_extract(self, url):
        self._scrapper_avatar = []
        result = super()._real_extract(url)
        result['scrapper_schema'] = 1
        result['scrapper_avatar'] = self._scrapper_avatar
        return result

    def _get_universal_data(self, webpage, display_id):
        data = super()._get_universal_data(webpage, display_id)
        user = ((data.get('webapp.user-detail') or {}).get('userInfo') or {}).get('user') or {}
        self._scrapper_avatar = [user[key] for key in ('avatarLarger', 'avatarMedium', 'avatarThumb')
                                 if isinstance(user.get(key), str) and user[key]]
        return data


class _ScrapperTikTokIE(TikTokIE, plugin_name='scrapper_photos'):
    def _parse_aweme_video_web(self, aweme_detail, *args, **kwargs):
        return _photo_metadata(super()._parse_aweme_video_web(aweme_detail, *args, **kwargs), aweme_detail)
