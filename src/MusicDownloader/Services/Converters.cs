using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace MusicDownloader.Services;

public sealed class AudioFormatLabelConverter : IValueConverter
{
    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => value is AudioFormatChoice f ? Label(f) : value?.ToString() ?? string.Empty;

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => BindingOperations.DoNothing;

    public static string Label(AudioFormatChoice f) =>
        f switch
        {
            AudioFormatChoice.Wav => "WAV — bezstratny  ⚠ duży plik, bez wzrostu jakości",
            AudioFormatChoice.Flac => "FLAC — bezstratny, mniejszy  ⚠ bez wzrostu jakości",
            AudioFormatChoice.Aiff =>
                "AIFF — bezstratny (Apple / Logic)  ⚠ duży plik, bez wzrostu jakości",
            AudioFormatChoice.Mp3_320 => "MP3 320 kbps — uniwersalny (zalecane)",
            AudioFormatChoice.M4a => "M4A / AAC — urządzenia Apple",
            AudioFormatChoice.BestOriginal => "Oryginał (bez konwersji, najmniejszy plik)",
            _ => f.ToString(),
        };
}

public sealed class NotConverter : IValueConverter
{
    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => value is bool b ? !b : true;

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => value is bool b ? !b : false;
}

public sealed class CountToEmptyConverter : IValueConverter
{
    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => value is int i && i == 0;

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => BindingOperations.DoNothing;
}

public sealed class BusyLabelConverter : IValueConverter
{
    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => value is bool b && b ? "Pobieranie…" : "Pobierz";

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => BindingOperations.DoNothing;
}
