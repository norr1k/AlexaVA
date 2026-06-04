using System;
using System.IO;

namespace Alexa.Services;

/// <summary>
/// Пишет диагностические события приложения в rolling log-файлы.
/// </summary>
public static class AppLogger
{
    private const long MaxLogFileSizeBytes = 10 * 1024 * 1024;
    private const int MaxLogFiles = 5;
    private const string LogFileName = "app.log";

    private static readonly object SyncRoot = new();
    private static string? _logFilePath;

    /// <summary>
    /// Инициализирует каталог логов и основной log-файл.
    /// </summary>
    public static void Initialize()
    {
        Directory.CreateDirectory(AppPaths.LogsDirectory);
        _logFilePath = Path.Combine(AppPaths.LogsDirectory, LogFileName);
        Info("Logger initialized");
    }

    /// <summary>
    /// Пишет информационное событие.
    /// </summary>
    public static void Info(string message)
    {
        Write("INFO", message, null);
    }

    /// <summary>
    /// Пишет предупреждение.
    /// </summary>
    public static void Warning(string message)
    {
        Write("WARN", message, null);
    }

    /// <summary>
    /// Пишет ошибку с исключением.
    /// </summary>
    public static void Error(Exception exception, string message)
    {
        Write("ERROR", message, exception);
    }

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            lock (SyncRoot)
            {
                _logFilePath ??= Path.Combine(AppPaths.LogsDirectory, LogFileName);
                Directory.CreateDirectory(AppPaths.LogsDirectory);
                RotateIfNeeded();

                var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}";
                if (exception is not null)
                    line += $"{Environment.NewLine}{exception}";

                File.AppendAllText(_logFilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never break the main application flow.
        }
    }

    private static void RotateIfNeeded()
    {
        if (_logFilePath is null ||
            !File.Exists(_logFilePath) ||
            new FileInfo(_logFilePath).Length < MaxLogFileSizeBytes)
        {
            return;
        }

        for (var index = MaxLogFiles - 1; index >= 1; index--)
        {
            var source = GetArchivePath(index);
            var destination = GetArchivePath(index + 1);

            if (!File.Exists(source))
                continue;

            if (index + 1 >= MaxLogFiles)
                File.Delete(source);
            else
                File.Move(source, destination, true);
        }

        File.Move(_logFilePath, GetArchivePath(1), true);
    }

    private static string GetArchivePath(int index)
    {
        return Path.Combine(AppPaths.LogsDirectory, $"app.{index}.log");
    }
}
