using System;
using Avalonia;

namespace Alexa;

internal static class Program
{
    /// <summary>
    /// Точка входа приложения: запускает Avalonia в desktop-режиме.
    /// </summary>
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    /// <summary>
    /// Собирает конфигурацию Avalonia: платформенный backend, инструменты отладки и шрифт Inter.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
