using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace Alexa.Services;

/// <summary>
/// Управляет автозапуском приложения через Run-ключ текущего пользователя
/// </summary>
public static class AutostartService
{
    private const string AppName = "AlexaVA";
    private const string LegacyAppName = "Alexa";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// Проверяет, включен ли автозапуск приложения для текущего пользователя
    /// </summary>
    public static bool IsEnabled()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        return key?.GetValue(AppName) is string || key?.GetValue(LegacyAppName) is string;
    }

    /// <summary>
    /// Включает или выключает автозапуск приложения
    /// </summary>
    public static void SetEnabled(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
        if (key is null)
            return;

        key.DeleteValue(LegacyAppName, false);

        if (!enabled)
        {
            key.DeleteValue(AppName, false);
            return;
        }

        // В Run-ключ записывается текущий exe, чтобы настройка работала и после публикации приложения.
        var executablePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrWhiteSpace(executablePath))
            key.SetValue(AppName, $"\"{executablePath}\"");
    }
}
