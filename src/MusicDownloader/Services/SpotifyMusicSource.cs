using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;

namespace MusicDownloader.Services;

public sealed class SpotifyMusicSource : IMusicSource
{
    private const string SpotDlVersion = "4.5.0";

    private readonly YtDlpMusicSource _ytdlp;
    private string _spotDlPath = string.Empty;
    private bool _ready;

    public SpotifyMusicSource(YtDlpMusicSource ytdlp)
    {
        _ytdlp = ytdlp;
    }

    public string DisplayName => "Spotify";

    public bool CanHandle(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;
        var trimmed = url.Trim();
        if (trimmed.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return false;
        return uri.Host.ToLowerInvariant().Contains("spotify");
    }

    public async Task EnsureReadyAsync(
        IProgress<string>? status,
        CancellationToken cancellationToken
    )
    {
        await _ytdlp.EnsureReadyAsync(status, cancellationToken).ConfigureAwait(false);

        if (_ready)
            return;

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var toolsDir = _ytdlp.ToolsDirectory;
        Directory.CreateDirectory(toolsDir);

        _spotDlPath = isWindows
            ? Path.Combine(toolsDir, "spotdl.exe")
            : FindOnPath("spotdl") ?? Path.Combine(toolsDir, "spotdl");

        if (!File.Exists(_spotDlPath))
        {
            status?.Report("Pobieranie spotDL (jednorazowa konfiguracja, ok. 45 MB)...");
            cancellationToken.ThrowIfCancellationRequested();
            await DownloadSpotDlAsync(_spotDlPath, isWindows, cancellationToken)
                .ConfigureAwait(false);
            if (!isWindows)
                EnsureExecutable(_spotDlPath);
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

        var outputTemplate = Path.Combine(
            request.OutputDirectory,
            "{list-name}",
            "{artist} - {title}.{output-ext}"
        );

        var args = new List<string>
        {
            "download",
            request.PlaylistOrTrackUrl,
            "--output",
            outputTemplate,
            "--overwrite",
            "skip",
            "--ffmpeg",
            _ytdlp.FfmpegPath,
        };

        AppendFormatArgs(args, request.Format);

        log.Report($"$ spotdl {string.Join(' ', args)}");

        var (exitCode, succeeded, failed) = await RunSpotDlAsync(args, log, cancellationToken)
            .ConfigureAwait(false);

        if (exitCode != 0 && succeeded == 0 && failed == 0)
        {
            return new DownloadResult(
                0,
                1,
                request.OutputDirectory,
                new[] { $"spotdl zakończyło się błędem (kod {exitCode})." }
            );
        }

        return new DownloadResult(
            succeeded,
            failed,
            request.OutputDirectory,
            Array.Empty<string>()
        );
    }

    private static void AppendFormatArgs(List<string> args, AudioFormatChoice format)
    {
        switch (format)
        {
            case AudioFormatChoice.Mp3_320:
                args.AddRange(new[] { "--format", "mp3", "--bitrate", "320k" });
                break;
            case AudioFormatChoice.M4a:
                args.AddRange(new[] { "--format", "m4a" });
                break;
            case AudioFormatChoice.Wav:
                args.AddRange(new[] { "--format", "wav" });
                break;
            case AudioFormatChoice.Flac:
                args.AddRange(new[] { "--format", "flac" });
                break;
            case AudioFormatChoice.Aiff:
                args.AddRange(new[] { "--format", "wav" });
                break;
            case AudioFormatChoice.BestOriginal:
                args.AddRange(new[] { "--format", "m4a" });
                break;
        }
    }

    private async Task<(int ExitCode, int Succeeded, int Failed)> RunSpotDlAsync(
        List<string> args,
        IProgress<string> log,
        CancellationToken cancellationToken
    )
    {
        var psi = new ProcessStartInfo
        {
            FileName = _spotDlPath,
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

        var succeeded = 0;
        var failed = 0;

        proc.Start();

        var stdoutTask = PumpStreamAsync(
            proc.StandardOutput,
            line =>
            {
                if (line.Contains("Downloaded", StringComparison.OrdinalIgnoreCase))
                    succeeded++;
                else if (
                    line.Contains("Skipping", StringComparison.OrdinalIgnoreCase)
                    && line.Contains("already exists", StringComparison.OrdinalIgnoreCase)
                )
                    succeeded++;
                else if (
                    line.Contains("LookupError", StringComparison.Ordinal)
                    || line.Contains("could not find", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("No results", StringComparison.OrdinalIgnoreCase)
                )
                    failed++;
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

        return (proc.ExitCode, succeeded, failed);
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

    private static async Task DownloadSpotDlAsync(
        string destination,
        bool isWindows,
        CancellationToken cancellationToken
    )
    {
        var assetName = isWindows
            ? $"spotdl-{SpotDlVersion}-win32.exe"
            : $"spotdl-{SpotDlVersion}-darwin";
        var url =
            $"https://github.com/spotDL/spotify-downloader/releases/download/v{SpotDlVersion}/{assetName}";

        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromMinutes(10);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MusicDownloader/1.0");

        using var response = await http.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            )
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var src = await response
            .Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var dst = File.Create(destination);
        await src.CopyToAsync(dst, cancellationToken).ConfigureAwait(false);
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
}
