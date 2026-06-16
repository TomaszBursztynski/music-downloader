namespace MusicDownloader.Services;

public sealed record DownloadRequest(
    string PlaylistOrTrackUrl,
    string OutputDirectory,
    AudioFormatChoice Format,
    bool EmbedThumbnail = true,
    bool EmbedMetadata = true
);

public sealed record TrackProgress(
    string Title,
    double Percent,
    string Status,
    string? FilePath = null,
    string? Error = null
);

public sealed record DownloadResult(
    int Succeeded,
    int Failed,
    string OutputDirectory,
    IReadOnlyList<string> Warnings
)
{
    public DownloadResult(int succeeded, int failed, string outputDirectory)
        : this(succeeded, failed, outputDirectory, Array.Empty<string>()) { }
}
