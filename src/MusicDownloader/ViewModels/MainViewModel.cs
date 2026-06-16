using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicDownloader.Services;

namespace MusicDownloader.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IReadOnlyList<IMusicSource> _sources;
    private readonly SettingsService? _settings;
    private CancellationTokenSource? _cts;
    private bool _isHydratingSettings;

    public Func<Task<string?>>? PickFolderAsync { get; set; }
    public Action<string>? OpenFolder { get; set; }

    public MainViewModel(IEnumerable<IMusicSource> sources, SettingsService? settings = null)
    {
        _sources = sources.ToList();
        _settings = settings;
        _outputDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "Music Downloader"
        );
    }

    public void ApplyPersistedSettings(AppSettings settings)
    {
        _isHydratingSettings = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(settings.OutputDirectory))
                OutputDirectory = settings.OutputDirectory!;
            if (settings.Format is { } f)
                Format = f;
        }
        finally
        {
            _isHydratingSettings = false;
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    private string _playlistUrl = string.Empty;

    [ObservableProperty]
    private string _outputDirectory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLosslessFormatSelected))]
    private AudioFormatChoice _format = AudioFormatChoice.Mp3_320;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Wklej link do playlisty lub filmu z YouTube, aby zacząć.";

    public ObservableCollection<TrackProgress> Tracks { get; } = new();

    public IReadOnlyList<AudioFormatChoice> Formats { get; } =
        new[]
        {
            AudioFormatChoice.Mp3_320,
            AudioFormatChoice.M4a,
            AudioFormatChoice.BestOriginal,
            AudioFormatChoice.Wav,
            AudioFormatChoice.Flac,
            AudioFormatChoice.Aiff,
        };

    public bool IsLosslessFormatSelected =>
        Format is AudioFormatChoice.Wav or AudioFormatChoice.Flac or AudioFormatChoice.Aiff;

    partial void OnOutputDirectoryChanged(string value) => PersistSettings();

    partial void OnFormatChanged(AudioFormatChoice value) => PersistSettings();

    private void PersistSettings()
    {
        if (_isHydratingSettings || _settings is null)
            return;
        _settings.Save(new AppSettings { OutputDirectory = OutputDirectory, Format = Format });
    }

    [RelayCommand]
    private async Task BrowseOutputDirectoryAsync()
    {
        if (PickFolderAsync is null)
            return;
        var picked = await PickFolderAsync().ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(picked))
            OutputDirectory = picked!;
    }

    [RelayCommand]
    private void OpenOutputDirectory()
    {
        if (Directory.Exists(OutputDirectory))
            OpenFolder?.Invoke(OutputDirectory);
    }

    private bool CanDownload() => !IsBusy && !string.IsNullOrWhiteSpace(PlaylistUrl);

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAsync()
    {
        var source = _sources.FirstOrDefault(s => s.CanHandle(PlaylistUrl));
        if (source is null)
        {
            StatusText = "Ten link nie jest jeszcze obsługiwany. Wklej link z YouTube.";
            return;
        }

        Tracks.Clear();
        IsBusy = true;
        StatusText = "Łączenie z YouTube, pobieranie listy utworów…";
        _cts = new CancellationTokenSource();

        try
        {
            await source.EnsureReadyAsync(null, _cts.Token);

            StatusText = $"Pobieranie z {source.DisplayName}…";

            var trackProgress = new Progress<TrackProgress>(UpsertTrack);
            var request = new DownloadRequest(PlaylistUrl.Trim(), OutputDirectory, Format);
            var result = await source.DownloadAsync(request, trackProgress, _cts.Token);

            StatusText =
                result.Failed == 0
                    ? $"Gotowe! Zapisano {result.Succeeded} utwor(ów) w {result.OutputDirectory}"
                    : $"Zakończono z błędami. Zapisano: {result.Succeeded}, nieudane: {result.Failed}.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Anulowano.";
        }
        catch (Exception ex)
        {
            StatusText = $"Coś poszło nie tak: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private bool CanCancel() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cts?.Cancel();

    private void UpsertTrack(TrackProgress tp)
    {
        for (int i = 0; i < Tracks.Count; i++)
        {
            if (string.Equals(Tracks[i].Title, tp.Title, StringComparison.OrdinalIgnoreCase))
            {
                Tracks[i] = tp;
                return;
            }
        }
        Tracks.Add(tp);
    }
}
