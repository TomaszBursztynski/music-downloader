# Music Downloader

A simple, cross-platform desktop app for downloading music from public YouTube
playlists (and, later, sources like Tidal). Built with **Avalonia + .NET 10**,
so the same codebase runs on **macOS, Windows, and Linux**.

Designed for non-technical users:

- Paste a link → click **Download**. That's it.
- No installs to fight with: `yt-dlp` and `ffmpeg` are fetched automatically on
  first run into the user's local app data folder.
- Sensible defaults (MP3 at the highest available quality, embedded album art
  - metadata), with M4A and "keep original best" as alternatives.

## How it works

| Layer                            | What it does                                                                                                                 |
| -------------------------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| `Services/IMusicSource.cs`       | Abstraction over a music provider. Add Tidal/SoundCloud/etc. by writing one new class.                                       |
| `Services/YouTubeMusicSource.cs` | yt-dlp + ffmpeg wrapper via [`YoutubeDLSharp`](https://github.com/Bluegrams/YoutubeDLSharp). Cross-platform binary handling. |
| `ViewModels/MainViewModel.cs`    | MVVM glue (CommunityToolkit.Mvvm). UI-framework-agnostic — folder picker is injected as a delegate.                          |
| `Views/MainWindow.axaml`         | Single-page Avalonia UI: link box, folder picker, format dropdown, big download button, per-track progress list.             |
| `App.axaml.cs`                   | Composes the sources and wires Avalonia's `StorageProvider` into the view model.                                             |

## Requirements

- **Build & run:** [.NET 10 SDK](https://dotnet.microsoft.com/download). Works on
  macOS, Linux, and Windows.
- On first launch the app downloads `yt-dlp` (~10 MB) and `ffmpeg` (~90 MB) into
  the appropriate per-user folder. Internet required for that first run only.

## Run

```bash
dotnet run --project src/MusicDownloader/MusicDownloader.csproj
```

## Publish a single-file binary

**Windows (.exe)**

```bash
dotnet publish src/MusicDownloader/MusicDownloader.csproj \
    -c Release -r win-x64 --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

**macOS (Apple Silicon)**

```bash
dotnet publish src/MusicDownloader/MusicDownloader.csproj \
    -c Release -r osx-arm64 --self-contained true \
    -p:PublishSingleFile=true
```

**macOS (Intel)**

```bash
dotnet publish src/MusicDownloader/MusicDownloader.csproj \
    -c Release -r osx-x64 --self-contained true \
    -p:PublishSingleFile=true
```

Output lives under `src/MusicDownloader/bin/Release/net10.0/<rid>/publish/`.

## Adding a new source (e.g. Tidal)

1. Implement `IMusicSource` in `src/MusicDownloader/Services/`.
2. Register it in `App.axaml.cs`:

   ```csharp
   var sources = new IMusicSource[]
   {
       new YouTubeMusicSource(),
       new TidalMusicSource(/* creds */),
   };
   ```

The view model automatically routes URLs to the first source whose
`CanHandle` returns `true`.

## Legal

Only download content you have the right to download. YouTube's Terms of
Service generally prohibit downloading without explicit permission unless a
download button is provided by the service. This tool is for personal use with
content you own or that is licensed for download.
