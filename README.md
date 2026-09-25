<p align="center">
  <img src="Assets/logo.png" alt="TikTok Scrapper logo" width="128" height="128">
</p>

<h1 align="center">TikTok Scrapper</h1>

<p align="center">
  Download videos, photo posts, and avatars.<br>
  <strong>v1.100a</strong> · Windows x64<br>
  By <strong>Karmikal Apps</strong>
</p>

## What is TikTok Scrapper?

TikTok Scrapper is a portable Windows application that downloads videos and all images from photo slideshow posts available on a TikTok profile. Its purpose is to make it easy to back up your own posts or archive content you have permission to save, through a simple desktop interface.

Paste a profile link, choose a destination, and start the download. By default, the app downloads up to three posts at once. Videos are saved as the highest-resolution playable MP4 stream TikTok exposes; photo slideshows are saved as individual images in their original order. Images retain the format supplied by TikTok, such as JPG, PNG, or WebP. Media is not re-encoded or upscaled, except when an avatar needs JPEG conversion.

The application is developed with C# and WPF and uses yt-dlp as its download engine. It is an independent application and is not affiliated with or endorsed by TikTok.

## Features

- Profile-link validation before starting a download.
- Adjustable concurrent post downloads: 1–10, with 3 enabled by default.
- Combined progress and completed-file counts across all active downloads.
- Every available slideshow image, numbered in slide order using TikTok's full image URLs rather than preview thumbnails.
- The profile avatar saved as `_profile.jpeg` in the same profile folder.
- A configurable save folder, with a separate `@profile_name` directory for each profile.
- Existing completed files are skipped and counted as downloaded; repeat a profile to retry missing or incomplete files.
- Cancellation that stops the current job and preserves completed downloads.
- A dedicated completion screen showing video/image totals and clear reporting of individual failed files.
- An optional completion ding and Windows notification, disabled by default.
- A download-engine updater and optional support for a user-supplied TikTok cookies file.

## Requirements for running the app

| Requirement | Details |
| --- | --- |
| Operating system | 64-bit Windows. This build targets Windows x64 and uses the .NET 10 Windows Desktop runtime. |
| Internet | Required to retrieve profile information, download videos/images, and update the engine. |
| Download engine | Included as `tools/yt-dlp.exe` in the portable ZIP. When running the standalone `.exe`, the app downloads and verifies the engine automatically on the first download if it is not already available. |
| Storage | A writable application/download location and enough free space for the videos and images. |

The published executable is self-contained: **you do not need to install .NET, Python, Node.js, or FFmpeg separately** for video or photo downloads. The included photo extractor runs inside the existing download engine. Administrator privileges are not required for normal use.

## Install and run

1. Download **TikTok Scrapper.exe** from the release assets and place it in a folder where you have write access. Alternatively, extract the entire portable ZIP there.
2. For the ZIP version, keep the included `tools` folder beside the app. Do not run the app from inside the ZIP archive.
3. Double-click **TikTok Scrapper.exe**. With the standalone executable, allow the first download to prepare the engine; it is saved under `%LOCALAPPDATA%\TikTok Scrapper\tools`.

No installer is needed. If Windows displays an unknown-publisher notice, note that this personal build is unsigned; verify the source of the package before running it.

## How to use it

1. Copy the URL of a TikTok profile whose posts you are authorized to download.
2. Paste it into **URL Profile**. **Download** becomes available when the link has a valid profile format.
3. Optionally open the gear button to choose a **Save location** and adjust the **Concurrent downloads** slider, then select **Save settings**.
4. Select **Download**. The URL form is hidden while the app finds and downloads posts. The progress panel counts files: the profile avatar counts as one, one video counts as one, and a slideshow with five images counts as five. Completed files already on disk count toward the downloaded total and appear as **already saved**. Active post counts, the latest slide, and failures are shown during downloading.
5. Use **Cancel** to stop the job, or wait for **Download Completed**.
6. Select **Open folder** to view the files, **Scrap New Profile** to start another job, or **Exit** to close the app.

Minimize keeps an active job running. Closing the app cancels the current job before exiting.

## Files and repeat downloads

The default destination is the `scrapped` folder beside the executable. Files are organized as follows; `profile_name`, `VIDEO_ID`, and `PHOTO_POST_ID` below are placeholders:

```text
scrapped/
└── @profile_name/
    ├── _profile.jpeg
    ├── VIDEO_ID.mp4
    ├── PHOTO_POST_ID_001.jpg
    ├── PHOTO_POST_ID_002.jpg
    ├── PHOTO_POST_ID_003.webp
    └── ...
```

The post ID provides a stable filename; image names also include a three-digit slide number starting at `001`. Downloading the same profile into the same output location **skips completed files with matching names**, leaving their bytes and timestamps unchanged. Skipped files count as downloaded. Only missing files or files that fail the app's basic media checks are downloaded, so you can retry a run after failures or cancellation without downloading everything again.

New transfers use temporary paths on the same drive. Incomplete files are replaced only after a successful transfer and media check. Cancelled partial transfers restart from the beginning on retry; completed files are reused. To intentionally fetch a completed file again, move or remove that specific file first.

The app saves every available slide separately, retaining JPG, PNG, WebP, GIF, or AVIF as supplied. It does not turn a slideshow into a video or save its background music. If one image fails, the remaining images are still attempted and that failure is shown; the job cannot claim a fully successful download. Image transfers are limited to 128 MB per file.

The largest available profile avatar is saved as `_profile.jpeg`. A completed `_profile.jpeg` already in the folder is skipped. Newly downloaded JPEG avatars are retained without re-encoding; other supported avatar formats are converted to JPEG at their existing dimensions. Transparent avatars use a white background. An unavailable avatar is reported without stopping the post downloads.

Files with other names are not removed. Changing the save location applies to future jobs; it does not move earlier downloads.

## Settings and profile access

**Save location:** Choose the destination in Settings, or use **Reset to ./scrapped** to restore the default.

**Concurrent downloads:** Use the slider to select 1–10 active posts, then save settings. The default is 3, including after upgrading from an older build. Select 1 for sequential downloads. Each photo post occupies one slot and saves its slides in order; the avatar is handled before the post queue. Increasing the value allows more transfers at once, but actual speed depends on your connection and TikTok's response. Cancel stops all workers and completion is shown only after all workers finish.

**Completion alert:** Enable **Play a ding and show a notification when downloads finish**, then save settings. It is off by default, including when upgrading from an older build. When enabled, the app plays a system ding and requests a native Windows notification after a completed run, including one with failed files. Cancellations and runs stopped before completion remain quiet. This works while the app is minimized; Windows notification settings and system volume can suppress the banner or sound.

**Download engine:** Select **Check for update** to fetch the latest official yt-dlp release. The app verifies its SHA-256 checksum against the official release manifest before installing it. Updated engines, settings, and the app's embedded photo extractor are stored in `%LOCALAPPDATA%\TikTok Scrapper`. Signed image URLs are refreshed when each photo post is reached.

**Optional TikTok session:** If TikTok requires login, select a Netscape-format `cookies.txt` file exported from your own authorized TikTok session. The app does not automatically read browser cookies. The profile scan may refresh the selected file; parallel workers use separate temporary copies so concurrent processes do not overwrite it. Those copies are removed when workers stop. Treat cookies as sensitive, and do not share or commit them.

TikTok may restrict profiles, require a session, block a request, or change its endpoints. Updating the engine can help with compatibility issues, but access is not guaranteed. The app cannot guarantee that TikTok exposes every post or the original upload resolution. Unavailable posts, unsupported image responses, and missing slides appear as failures. An incomplete job is reported separately from a fully successful download.

## Working on the project

### Development requirements

- Windows, because the UI and icon-generation script use WPF.
- The **.NET 10 SDK**, including its Windows desktop build support. A runtime-only installation is not sufficient to build the project.
- Windows PowerShell, or PowerShell 7 on Windows. The regression checks also invoke `powershell.exe`.
- Internet access for the initial SDK package restore and official yt-dlp engine download.
- A text editor or IDE capable of editing C#, XAML, and PowerShell files.

The application does not require a Python or Node.js development environment. Git is optional for version control.

### Build a portable executable

Open a PowerShell terminal in the folder containing **TikTokScrapper.csproj**. This is the repository root when you clone the source repository, or the **source** folder in the full development package. Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

The script regenerates the icon, downloads and checksum-verifies the official engine, runs the regression checks, and publishes a self-contained Windows x64 build to `dist/` beside the project file. That output contains the executable, download engine, README, logo asset, and default save folder.

If the project's `tools/` folder already contains a verified engine, you can reuse it:

```powershell
.\build.ps1 -SkipEngine
```

To compile or run the existing regression checks independently, use these commands from the folder containing **TikTokScrapper.csproj**:

```powershell
dotnet build .\TikTokScrapper.csproj -c Release
dotnet run --project .\Tests\TikTokScrapper.Tests.csproj -c Release
dotnet run --project .\Tests\Ui\TikTokScrapper.UiTests.csproj -c Release
```

The tests cover profile validation, mixed video/photo posts, ordered image filenames, HTTP responses, concurrency limits of 1/3/10, combined progress, isolated cookie files, skipping completed media, retrying missing files, cancellation of all active workers, and temporary-file cleanup. The build also runs an offline compatibility fixture through the actual bundled yt-dlp executable to verify the photo extractor's profile listing and single-post metadata. To run that fixture manually, append `-- --engine .\tools\yt-dlp.exe` to the core test command. Offscreen WPF checks cover URL text/caret layout, the slider's range/default/value display, and JPEG avatar conversion; they do not open a window or take desktop focus.

### Project layout

The full development package has the layout below. The source repository contains the contents of `source/` at its root; the executable and portable release ZIP are distributed as Release assets. Public binary downloads do not include the project source.

```text
TikTok Scrapper.exe          Portable Windows application
README.md                   This guide
THIRD-PARTY-NOTICES.md       Dependency credits and licensing references
assets/logo.png             Logo displayed in this README
tools/                      Bundled yt-dlp executable and release information
scrapped/                   Default download destination
design/                     Original logo files and interface previews
source/                     C# / WPF project and build scripts
  Core/                     Download, process, settings, and engine services
  EnginePlugin/             Embedded yt-dlp extension for ordered photo metadata
  Assets/                   Source logo and Windows icon
  Tests/                    Regression checks
  scripts/                  Icon and engine preparation scripts
```

Interface previews can be exported with `--render-preview <folder>`. This mode renders offscreen without opening a desktop window or making download requests. Counts in its downloading/completed images are simulated for design review.

## Repository and releases

Commit the contents of `source/` as the repository root, including `.gitignore`, `README.md`, `THIRD-PARTY-NOTICES.md`, the project file, XAML/C# files, assets, scripts, and tests. Build outputs, executable binaries, local settings, downloads, and cookies should not be committed.

For a release, create the version tag **v1.100a** and attach **TikTok Scrapper.exe** as a binary asset. You may also attach the portable Windows x64 ZIP, which includes the engine and user documentation without the source tree. Users can choose the executable directly instead of downloading the repository.

## License and use

**Author: Karmikal Apps**<br>
**Copyright © 2026 Karmikal Apps**

This package does not currently include an open-source license grant for the original application code or branding. Source availability by itself is not a license grant. Contact Karmikal Apps for application licensing or redistribution permissions.

Third-party components retain their own licenses. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for yt-dlp, the .NET runtime, and the relevant upstream license references. Application licensing does not replace those components' terms.

Use the app only for content you own or have permission to download, and follow the applicable platform terms and content rights. Downloading a file does not transfer ownership or grant permission to redistribute it. The application is intended for authorized archiving and backup.
