## About

MediaProxy is an Azure Functions app that proxies HTTP Live Streaming (HLS) content.

It was written to download [France TV](https://www.france.tv) content outside of France with [yt-dlp](https://github.com/yt-dlp/yt-dlp).

## Usage

1. Deploy the function app to Azure in the *France Central* region
2. Patch the [francetv.py](https://github.com/yt-dlp/yt-dlp/blob/e3ce2b385ec1f03fac9d4210c57fda77134495fc/yt_dlp/extractor/francetv.py) extractor to replace the video URL with the proxy URL (replace `<DEPLOYMENT_URL>` with the actual deployed URL and `<FUNCTION_KEY>` with the actual function key)

```diff
--- a/yt_dlp/extractor/francetv.py
+++ b/yt_dlp/extractor/francetv.py
@@ -1,3 +1,5 @@
+import urllib.parse
+
 from .common import InfoExtractor
 from .dailymotion import DailymotionIE
 from ..utils import (
@@ -144,6 +146,8 @@ def _extract_video(self, video_id, catalogue=None):
                 video_url = video.get('url')
 
             ext = determine_ext(video_url)
+            video_url = (f'https://<DEPLOYMENT_URL>.azurewebsites.net/?url={urllib.parse.quote(video_url)}'
+                         f'&code=<FUNCTION_KEY>')
             if ext == 'f4m':
                 formats.extend(self._extract_f4m_formats(
                     video_url, video_id, f4m_id=format_id, fatal=False))
```

3. Download some content, make sure to explicitly specify the HLS streams (PROTO = m3u8) with the `-f` option and download 50 fragments concurrently to speed up the download using the `-N 50` option

```
./yt-dlp.sh -f hls-5398+hls-audio-aacl-96-Audio_Français -N 50 https://www.france.tv/france-2/astrid-et-raphaelle/saison-4/5418627-l-oeil-du-dragon.html
```

ℹ️ The formats can be listed with the `-F` option:

```
./yt-dlp.sh -F https://www.france.tv/france-2/astrid-et-raphaelle/saison-4/5418627-l-oeil-du-dragon.html
```

```
ID                                  EXT   RESOLUTION │   FILESIZE PROTO │ MORE INFO
───────────────────────────────────────────────────────────────────────────────────────────────────
spritesheets                        mhtml unknown    │            mhtml │ storyboard
hls-audio-aacl-96-Audio_Description mp4   audio only │            m3u8  │ [qad] audio description
dash-audio_qad=96000                m4a   audio only │ ~ 36.43MiB dash  │ [qad] audio description
hls-audio-aacl-96-Audio_Français    mp4   audio only │            m3u8  │ [fr] Audio Français
dash-audio_fre=96000                m4a   audio only │ ~ 36.43MiB dash  │ [fr] DASH audio, m4a_dash
dash-video=400000                   mp4   384x216    │ ~151.81MiB dash  │ DASH video, mp4_dash
hls-522                             mp4   384x216    │ ~198.11MiB m3u8  │
dash-video=950000                   mp4   640x360    │ ~360.54MiB dash  │ DASH video, mp4_dash
hls-1105                            mp4   640x360    │ ~419.37MiB m3u8  │
dash-video=1400000                  mp4   960x540    │ ~531.32MiB dash  │ DASH video, mp4_dash
hls-1582                            mp4   960x540    │ ~600.40MiB m3u8  │
dash-video=2000000                  mp4   1280x720   │ ~759.03MiB dash  │ DASH video, mp4_dash
hls-2218                            mp4   1280x720   │ ~841.77MiB m3u8  │
dash-video=5000000                  mp4   1920x1080  │ ~  1.85GiB dash  │ DASH video, mp4_dash
hls-5398                            mp4   1920x1080  │ ~  2.00GiB m3u8  │
```

### During development

While developing, using a (SOCKS5) proxy in France can be achieved thanks to the the [Tor Browser](https://www.torproject.org/download/). The torrc file (found at `~/Library/Application Support/TorBrowser-Data/Tor/torrc` on macOS) must be [configured with exit nodes](https://www.optimizationcore.com/security/set-tor-exit-node-tor-browser-country-code-specific-node/) in France by adding this line:

```
ExitNodes {fr} StrictNodes 1
```

Then the `HttpClientProxy` environment variable must be set to `socks5://127.0.0.1:9150`.

This allows to test whether the code proxying the HLS content is correct, but with mostly slow download speed due to the nature of the Tor network.

## Alternative

After writing MediaProxy I realized that it would be much easier to use a SOCKS proxy directly since yt-dlp supports the [--proxy option](https://github.com/yt-dlp/yt-dlp/?tab=readme-ov-file#network-options).

It would probably have been easier to deploy the `rastasheep/ubuntu-sshd` container to Azure and create a tunnel instead, see https://gist.github.com/noelbundick/be9bf7bcaa6c6bcee4b65da841c620a3

```sh
#!/bin/bash
# Create a container in Azure in the region you want
az group create -n temp-proxy -l southcentralus
az container create -g temp-proxy -n proxy --image rastasheep/ubuntu-sshd --ip-address Public --ports 22 --dns-name-label myproxy
ssh -D 1337 -C -N root@myproxy.southcentralus.azurecontainer.io

# Go to your network connection, set your SOCKS proxy to localhost:1337
# Browse around, your traffic is being tunneled through southcentralus
# When you're done, unset your proxy

# Clean up
az group delete -n temp-proxy -y --no-wait
```
