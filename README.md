# Music Downloader

Tiny cross-platform desktop app (Avalonia + .NET 10) that wraps `yt-dlp` +
`ffmpeg`. Paste a YouTube link, pick a format, hit **Pobierz**.

`yt-dlp` and `ffmpeg` are downloaded automatically on first run.

## Run from source

```bash
dotnet run --project src/MusicDownloader/MusicDownloader.csproj
```

## Format

```bash
dotnet tool restore        # first time only
dotnet csharpier format .
```

## Release

```bash
./scripts/release.sh       # builds win-x64, osx-arm64, osx-x64 zips into ./dist
```

Or push a tag to build & publish a GitHub Release with all three zips:

```bash
git tag v0.1.0 && git push origin v0.1.0
```

First-launch warnings to tell friends about:

- **Windows**: SmartScreen → *More info → Run anyway*
- **macOS**: Right-click → **Open** → **Open**

## Legal

Only download content you have the right to download.
