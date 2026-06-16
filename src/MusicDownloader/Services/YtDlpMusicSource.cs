using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace MusicDownloader.Services;

public sealed class YtDlpMusicSource : IMusicSource
{
    private readonly string _toolsDirectory;
    private string _ytDlpPath = string.Empty;
    private string _ffmpegPath = string.Empty;
    private bool _ready;

    public YtDlpMusicSource(string? toolsDirectory = null)
    {
        _toolsDirectory =
            toolsDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MusicDownloader",
                "bin"
            );
    }

    public string DisplayName => "YouTube / SoundCloud";

    public bool CanHandle(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        var host = uri.Host.ToLowerInvariant();
        return host.Contains("youtube.com")
            || host.Contains("youtu.be")
            || host.Contains("music.youtube.com")
            || host.Contains("soundcloud.com")
            || host.Contains("snd.sc");
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
            await YoutubeDLSharp.Utils.DownloadYtDlp(_toolsDirectory).ConfigureAwait(false);
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
            await YoutubeDLSharp.Utils.DownloadFFmpeg(_toolsDirectory).ConfigureAwait(false);
            EnsureExecutable(_ffmpegPath);
        }

        _ready = true;
    }

    public async Task<DownloadResult> DownloadAsync(
        DownloadRequest request,
        IProgress<string> log,
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

        var args = BuildDownloadArgs(request, outputFolder);

        log.Report($"$ yt-dlp {string.Join(' ', args)}");

        var (exitCode, downloadedFiles) = await RunYtDlpAsync(args, log, cancellationToken)
            .ConfigureAwait(false);

        if (exitCode != 0)
        {
            return new DownloadResult(0, 1, outputFolder, Array.Empty<string>());
        }

        return new DownloadResult(downloadedFiles.Count, 0, outputFolder, Array.Empty<string>());
    }

    private List<string> BuildDownloadArgs(DownloadRequest request, string outputFolder)
    {
        var args = new List<string>
        {
            "--ffmpeg-location",
            _ffmpegPath,
            "--paths",
            outputFolder,
            "--output",
            "%(title)s.%(ext)s",
            "--ignore-errors",
            "--no-overwrites",
            "--no-warnings",
            "--newline",
            "--progress",
            "--extract-audio",
            "--format",
            "bestaudio/best",
        };

        if (request.EmbedThumbnail)
            args.Add("--embed-thumbnail");
        if (request.EmbedMetadata)
            args.Add("--embed-metadata");

        switch (request.Format)
        {
            case AudioFormatChoice.Mp3_320:
                args.AddRange(
                    new[]
                    {
                        "--audio-format",
                        "mp3",
                        "--audio-quality",
                        "0",
                        "--postprocessor-args",
                        "ffmpeg:-b:a 320k",
                    }
                );
                break;
            case AudioFormatChoice.M4a:
                args.AddRange(new[] { "--audio-format", "m4a", "--audio-quality", "0" });
                break;
            case AudioFormatChoice.Wav:
                args.AddRange(new[] { "--audio-format", "wav" });
                break;
            case AudioFormatChoice.Flac:
                args.AddRange(new[] { "--audio-format", "flac" });
                break;
            case AudioFormatChoice.Aiff:
                args.AddRange(new[] { "--audio-format", "aiff" });
                break;
            case AudioFormatChoice.BestOriginal:
                args.AddRange(new[] { "--audio-format", "best" });
                break;
        }

        args.Add("--print");
        args.Add("after_move:filepath");

        args.Add(request.PlaylistOrTrackUrl);
        return args;
    }

    private async Task<(int ExitCode, List<string> Files)> RunYtDlpAsync(
        List<string> args,
        IProgress<string> log,
        CancellationToken cancellationToken
    )
    {
        var psi = new ProcessStartInfo
        {
            FileName = _ytDlpPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var files = new List<string>();

        proc.Start();

        var stdoutTask = PumpStreamAsync(
            proc.StandardOutput,
            line =>
            {
                if (LooksLikeMediaPath(line) && File.Exists(line))
                {
                    files.Add(line);
                    log.Report($"✓ {Path.GetFileName(line)}");
                    return;
                }
                log.Report(line);
            },
            cancellationToken
        );

        var stderrTask = PumpStreamAsync(
            proc.StandardError,
            line => log.Report(line),
            cancellationToken
        );

        try
        {
            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!proc.HasExited)
                    proc.Kill(entireProcessTree: true);
            }
            catch { }
            throw;
        }

        return (proc.ExitCode, files);
    }

    private static async Task PumpStreamAsync(
        StreamReader reader,
        Action<string> onLine,
        CancellationToken cancellationToken
    )
    {
        var buffer = new char[1024];
        var current = new System.Text.StringBuilder();

        while (!cancellationToken.IsCancellationRequested)
        {
            int read;
            try
            {
                read = await reader
                    .ReadAsync(buffer.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (read == 0)
                break;

            for (int i = 0; i < read; i++)
            {
                var ch = buffer[i];
                if (ch == '\n' || ch == '\r')
                {
                    if (current.Length > 0)
                    {
                        onLine(current.ToString());
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(ch);
                }
            }
        }

        if (current.Length > 0)
            onLine(current.ToString());
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
    };

    private static bool LooksLikeMediaPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var lower = value.Trim().ToLowerInvariant();
        foreach (var ext in MediaExtensions)
            if (lower.EndsWith(ext, StringComparison.Ordinal))
                return true;
        return false;
    }
}
