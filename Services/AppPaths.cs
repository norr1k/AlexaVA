using System;
using System.IO;

namespace Alexa.Services;

/// <summary>
/// Централизует пользовательские каталоги приложения.
/// </summary>
public static class AppPaths
{
    public const string AppDirectoryName = "AlexaVA";

    /// <summary>
    /// Каталог настроек приложения в LocalAppData.
    /// </summary>
    public static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppDirectoryName);

    /// <summary>
    /// Временный каталог приложения в LocalAppData\Temp.
    /// </summary>
    public static string TempDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Temp",
        AppDirectoryName);

    /// <summary>
    /// Каталог логов приложения в Roaming AppData.
    /// </summary>
    public static string LogsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppDirectoryName,
        "logs");
}
