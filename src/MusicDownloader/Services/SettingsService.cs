using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MusicDownloader.Services;

public sealed class AppSettings
{
    public string? OutputDirectory { get; set; }
    public AudioFormatChoice? Format { get; set; }
}

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;
    private readonly object _gate = new();

    public SettingsService(string? overrideFilePath = null)
    {
        _filePath =
            overrideFilePath
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MusicDownloader",
                "settings.json"
            );
    }

    public AppSettings Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_filePath))
                    return new AppSettings();
                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                    ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                var tmp = _filePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
                if (File.Exists(_filePath))
                    File.Delete(_filePath);
                File.Move(tmp, _filePath);
            }
            catch { }
        }
    }
}
