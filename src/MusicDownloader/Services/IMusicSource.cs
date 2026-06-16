namespace MusicDownloader.Services;

public interface IMusicSource
{
    string DisplayName { get; }

    bool CanHandle(string url);

    Task EnsureReadyAsync(IProgress<string>? status, CancellationToken cancellationToken);

    Task<DownloadResult> DownloadAsync(
        DownloadRequest request,
        IProgress<TrackProgress> progress,
        CancellationToken cancellationToken
    );
}
