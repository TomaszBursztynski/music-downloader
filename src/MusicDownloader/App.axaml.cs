using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using MusicDownloader.Services;
using MusicDownloader.ViewModels;
using MusicDownloader.Views;

namespace MusicDownloader;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var ytdlp = new YtDlpMusicSource();
            var sources = new IMusicSource[] { new SpotifyMusicSource(ytdlp), ytdlp };
            var settingsService = new SettingsService();
            var vm = new MainViewModel(sources, settingsService);
            vm.ApplyPersistedSettings(settingsService.Load());

            var window = new MainWindow { DataContext = vm };
            desktop.MainWindow = window;

            vm.PickFolderAsync = async () =>
            {
                var result = await window.StorageProvider.OpenFolderPickerAsync(
                    new FolderPickerOpenOptions
                    {
                        Title = "Wybierz folder, w którym zapisać muzykę",
                        AllowMultiple = false,
                    }
                );
                var folder = result is { Count: > 0 } ? result[0] : null;
                return folder?.TryGetLocalPath();
            };

            vm.OpenFolder = path =>
            {
                try
                {
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = path,
                            UseShellExecute = true,
                        }
                    );
                }
                catch { }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
