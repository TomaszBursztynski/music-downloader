using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using YoutubeDLSharp;
using YoutubeDLSharp.Options;

namespace MusicDownloader.Services;

public sealed class YouTubeMusicSource : IMusicSource
{
    private static readonly string[] MediaExtensions =
    {
        ".mp3",
        ".m4a",
        ".aac",
        ".opus",
        ".ogg",
        ".webm",
        ".mp4",
        ".flac",
        ".wav",
        ".aiff",
        ".aif",
        ".mka",
        ".mkv",
        ".part",
    };

    private readonly string _toolsDirectory;
    private string _ytDlpPath = string.Empty;
    private string _ffmpegPath = string.Empty;
    private bool _ready;

    public YouTubeMusicSource(string? toolsDirectory = null)
    {
        _toolsDirectory =
            toolsDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MusicDownloader",
                "bin"
            );
    }

    public string DisplayName => "YouTube";

    public bool CanHandle(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        var host = uri.Host.ToLowerInvariant();
        return host.Contains("youtube.com")
            || host.Contains("youtu.be")
            || host.Contains("music.youtube.com");
    }

    public async Task EnsureReadyAsync(
        IProgress<string>? status,
        CancellationToken cancellationToken
    )
    {
        if (_ready)
            return;

        Directory.CreateDirectory(_toolsDirectory);

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        _ytDlpPath = isWindows
            ? Path.Combine(_toolsDirectory, "yt-dlp.exe")
            : FindOnPath("yt-dlp") ?? Path.Combine(_toolsDirectory, "yt-dlp");

        _ffmpegPath = isWindows
            ? Path.Combine(_toolsDirectory, "ffmpeg.exe")
            : FindOnPath("ffmpeg") ?? Path.Combine(_toolsDirectory, "ffmpeg");

        if (!File.Exists(_ytDlpPath))
        {
            if (!isWindows)
            {
                throw new InvalidOperationException(
                    "yt-dlp nie zostało znalezione. Zainstaluj je w terminalu:\n\n"
                        + "    brew install yt-dlp\n\n"
                        + "(lub: pipx install yt-dlp), a następnie spróbuj ponownie."
                );
            }
            status?.Report("Pobieranie yt-dlp (jednorazowa konfiguracja)...");
            cancellationToken.ThrowIfCancellationRequested();
            await Utils.DownloadYtDlp(_toolsDirectory).ConfigureAwait(false);
            EnsureExecutable(_ytDlpPath);
        }

        if (!File.Exists(_ffmpegPath))
        {
            if (!isWindows)
            {
                throw new InvalidOperationException(
                    "ffmpeg nie zostało znalezione. Zainstaluj je w terminalu:\n\n"
                        + "    brew install ffmpeg\n\n"
                        + "a następnie spróbuj ponownie."
                );
            }
            status?.Report("Pobieranie ffmpeg (jednorazowa konfiguracja)...");
            cancellationToken.ThrowIfCancellationRequested();
            await Utils.DownloadFFmpeg(_toolsDirectory).ConfigureAwait(false);
            EnsureExecutable(_ffmpegPath);
        }

        _ready = true;
    }

    public async Task<DownloadResult> DownloadAsync(
        DownloadRequest request,
        IProgress<TrackProgress> progress,
        CancellationToken cancellationToken
    )
    {
        await EnsureReadyAsync(null, cancellationToken).ConfigureAwait(false);

        Directory.CreateDirectory(request.OutputDirectory);

        var outputFolder = await ResolveOutputFolderAsync(
                request.PlaylistOrTrackUrl,
                request.OutputDirectory,
                cancellationToken
            )
            .ConfigureAwait(false);

        Directory.CreateDirectory(outputFolder);

        var ytdl = new YoutubeDL
        {
            YoutubeDLPath = _ytDlpPath,
            FFmpegPath = _ffmpegPath,
            OutputFolder = outputFolder,
            OutputFileTemplate = "%(title)s.%(ext)s",
            RestrictFilenames = false,
            OverwriteFiles = false,
            IgnoreDownloadErrors = true,
        };

        var conversion = request.Format switch
        {
            AudioFormatChoice.Wav => AudioConversionFormat.Wav,
            AudioFormatChoice.Flac => AudioConversionFormat.Flac,
            AudioFormatChoice.Aiff => AudioConversionFormat.Best,
            AudioFormatChoice.Mp3_320 => AudioConversionFormat.Mp3,
            AudioFormatChoice.M4a => AudioConversionFormat.M4a,
            AudioFormatChoice.BestOriginal => AudioConversionFormat.Best,
            _ => AudioConversionFormat.Best,
        };

        var overrides = new OptionSet
        {
            Format = "bestaudio/best",
            AudioQuality = 0,
            EmbedThumbnail = request.EmbedThumbnail,
            EmbedMetadata = request.EmbedMetadata,
            IgnoreErrors = true,
            NoOverwrites = true,
            PostprocessorArgs =
                request.Format == AudioFormatChoice.Mp3_320 ? "ffmpeg:-b:a 320k" : null,
        };

        if (request.Format == AudioFormatChoice.Aiff)
        {
            overrides.ExtractAudio = true;
            overrides.AudioFormat = AudioConversionFormat.Best;
            overrides.AddCustomOption("--audio-format", "aiff");
        }

        string? lastReportedTitle = null;

        var ytProgress = new Progress<DownloadProgress>(p =>
        {
            if (p.State != DownloadState.Downloading && p.State != DownloadState.PostProcessing)
                return;

            var candidate = LooksLikeMediaPath(p.Data) ? p.Data : null;
            if (candidate is null && lastReportedTitle is null)
                return;

            string title;
            if (candidate is not null)
            {
                title = Path.GetFileNameWithoutExtension(candidate);
                if (string.IsNullOrWhiteSpace(title))
                    title = candidate;
                lastReportedTitle = title;
            }
            else
            {
                title = lastReportedTitle!;
            }

            var status = p.State == DownloadState.Downloading ? "Pobieranie" : "Konwertowanie";
            var pct = Math.Clamp(p.Progress * 100.0, 0.0, 100.0);
            progress.Report(new TrackProgress(title, pct, status));
        });

        var result = await ytdl.RunAudioPlaylistDownload(
                request.PlaylistOrTrackUrl,
                format: conversion,
                ct: cancellationToken,
                progress: ytProgress,
                overrideOptions: overrides
            )
            .ConfigureAwait(false);

        if (!result.Success)
        {
            var err = string.Join(Environment.NewLine, result.ErrorOutput ?? Array.Empty<string>());
            progress.Report(new TrackProgress(lastReportedTitle ?? "", 0, "Błąd", Error: err));
            return new DownloadResult(0, 1, request.OutputDirectory, Array.Empty<string>());
        }

        var paths = result.Data ?? Array.Empty<string>();
        foreach (var path in paths)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            progress.Report(new TrackProgress(name, 100, "Gotowe", FilePath: path));
        }

        return new DownloadResult(paths.Length, 0, request.OutputDirectory, Array.Empty<string>());
    }

    private async Task<string> ResolveOutputFolderAsync(
        string url,
        string baseFolder,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _ytDlpPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--flat-playlist");
            psi.ArgumentList.Add("--dump-single-json");
            psi.ArgumentList.Add("--no-warnings");
            psi.ArgumentList.Add("--skip-download");
            psi.ArgumentList.Add(url);

            using var proc = Process.Start(psi);
            if (proc is null)
                return baseFolder;

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(cancellationToken);
            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var json = await stdoutTask.ConfigureAwait(false);

            if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(json))
                return baseFolder;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var isPlaylist =
                root.TryGetProperty("_type", out var typeEl)
                && typeEl.ValueKind == JsonValueKind.String
                && string.Equals(
                    typeEl.GetString(),
                    "playlist",
                    StringComparison.OrdinalIgnoreCase
                );

            if (!isPlaylist)
                return baseFolder;

            if (
                !root.TryGetProperty("title", out var titleEl)
                || titleEl.ValueKind != JsonValueKind.String
            )
                return baseFolder;

            var playlistTitle = titleEl.GetString();
            if (string.IsNullOrWhiteSpace(playlistTitle))
                return baseFolder;

            return Path.Combine(baseFolder, SanitizeFolderName(playlistTitle));
        }
        catch
        {
            return baseFolder;
        }
    }

    private static string? FindOnPath(string binaryName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var separator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
        foreach (var dir in pathEnv.Split(separator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, binaryName);
            if (File.Exists(candidate))
                return candidate;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string[] brewDirs = { "/opt/homebrew/bin", "/usr/local/bin" };
            foreach (var dir in brewDirs)
            {
                var candidate = Path.Combine(dir, binaryName);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static void EnsureExecutable(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;
        if (!File.Exists(path))
            return;
        try
        {
            var mode = File.GetUnixFileMode(path);
            File.SetUnixFileMode(
                path,
                mode
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherExecute
            );
        }
        catch { }
    }

    private static string SanitizeFolderName(string name)
    {
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars())
        {
            '/',
            '\\',
            ':',
            '*',
            '?',
            '"',
            '<',
            '>',
            '|',
        };

        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var ch in name)
            sb.Append(invalid.Contains(ch) ? '_' : ch);

        var cleaned = sb.ToString().Trim().TrimEnd('.', ' ');
        return string.IsNullOrWhiteSpace(cleaned) ? "Playlist" : cleaned;
    }

    private static bool LooksLikeMediaPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var s = value.Trim();
        if (s.StartsWith("[", StringComparison.Ordinal))
            return false;
        if (s.StartsWith("WARNING:", StringComparison.OrdinalIgnoreCase))
            return false;
        if (s.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
            return false;

        var lower = s.ToLowerInvariant();
        foreach (var ext in MediaExtensions)
        {
            if (lower.EndsWith(ext, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
