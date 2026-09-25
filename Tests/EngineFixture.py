"""Offline compatibility fixture: exercise the installed engine's real TikTok parser."""
from yt_dlp.extractor.common import InfoExtractor
import json
from yt_dlp_plugins.extractor.tiktok_scrapper import _ScrapperTikTokIE, _ScrapperTikTokUserIE


class ScrapperFixtureIE(InfoExtractor):
    _VALID_URL = r'scrapperfixture:(?P<id>profile|photo)'

    def _real_extract(self, url):
        photo = {
            'id': '22222', 'desc': 'Photo fixture', 'createTime': 1700000000,
            'author': {'uniqueId': 'profile_name'},
            'imagePost': {'images': [
                {'imageURL': {'urlList': ['https://p16.tiktokcdn.com/first.jpeg', 'https://p19.tiktokcdn.com/first.jpeg']}},
                {'imageURL': {'urlList': ['https://p16.tiktokcdn.com/second.webp']}},
                {'imageURL': {'urlList': []}},
            ]},
        }
        if self._match_id(url) == 'photo':
            post = _ScrapperTikTokIE(self._downloader)
            post._extract_web_data_and_status = lambda *args, **kwargs: (photo, 0)
            return post._real_extract('https://www.tiktok.com/@profile_name/video/22222')
        user = _ScrapperTikTokUserIE(self._downloader)
        profile = {'__DEFAULT_SCOPE__': {'webapp.user-detail': {'userInfo': {
            'stats': {'videoCount': 2},
            'user': {'secUid': 'MS4wLjABAAAA' + 'a' * 64,
                     'avatarLarger': 'https://p16.tiktokcdn.com/avatar-large.jpeg',
                     'avatarThumb': 'https://p16.tiktokcdn.com/avatar-thumb.jpeg'},
        }}}}
        user._download_webpage = lambda *args, **kwargs: '<script id="__UNIVERSAL_DATA_FOR_REHYDRATION__">' + json.dumps(profile) + '</script>'
        user._download_json = lambda *args, **kwargs: {
            'itemList': [
                {'id': '11111', 'desc': 'Video fixture', 'createTime': 1700000001,
                 'author': {'uniqueId': 'profile_name'}, 'video': {}},
                photo,
            ],
            'hasMorePrevious': False,
        }
        return user._real_extract('https://www.tiktok.com/@profile_name')
