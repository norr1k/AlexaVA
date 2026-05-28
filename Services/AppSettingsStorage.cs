using System;
using System.IO;
using System.Text.Json;

namespace Alexa.Services;

/// <summary>
/// Загружает и сохраняет настройки приложения в JSON-файл
/// </summary>
public static class AppSettingsStorage
{
    public static event Action<AppSettings>? SettingsSaved;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Возвращает каталог настроек
    /// </summary>
    private static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Alexa");

    /// <summary>
    /// Возвращает путь к файлу настроек
    /// </summary>
    private static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    /// <summary>
    /// Читает настройки из файла или возвращает значения по умолчанию, если с файлом проблемы
    /// </summary>
    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// Сохраняет настройки в JSON`е, создавая каталог приложения при необходимости
    /// </summary>
    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        settings.ShowWindowHotkey = settings.ShowWindowHotkey.Trim();
        settings.VoiceRecordHotkey = settings.VoiceRecordHotkey.Trim();
        settings.ServerAddress = settings.ServerAddress.Trim();
        settings.ServerPort = settings.ServerPort.Trim();

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
        SettingsSaved?.Invoke(settings);
    }
}
