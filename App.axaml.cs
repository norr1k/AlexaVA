using System;
using Alexa.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Alexa;

/// <summary>
/// Главный класс Avalonia-приложения: инициализирует ресурсы, главное окно и системный трей.
/// </summary>
public partial class App : Application
{
    private readonly GlobalHotkeyService _globalHotkeyService = new();
    private MainView? _mainView;
    private TrayIcon? _trayIcon;

    #region Avalonia lifecycle

    /// <summary>
    /// Загружает XAML-ресурсы приложения.
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Настраивает desktop lifecycle так, чтобы приложение запускалось в трее без открытого окна.
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // OnExplicitShutdown нужен, чтобы скрытое окно не завершало процесс автоматически.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _mainView = new MainView();
            CreateTrayIcon(desktop);
            ConfigureGlobalHotkeys(AppSettingsStorage.Load());
            AppSettingsStorage.SettingsSaved += ConfigureGlobalHotkeys;
            SettingsWindow.HotkeyCaptureChanged += OnHotkeyCaptureChanged;
        }

        base.OnFrameworkInitializationCompleted();
    }

    #endregion

    #region Tray

    /// <summary>
    /// Создает иконку системного трея и подключает пункты контекстного меню.
    /// </summary>
    private void CreateTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var openChatItem = new NativeMenuItem("Открыть чат");
        openChatItem.Click += (_, _) => _mainView?.ShowChatWindow();

        var settingsItem = new NativeMenuItem("Настройки");
        settingsItem.Click += (_, _) => _mainView?.ShowSettingsWindow();

        var exitItem = new NativeMenuItem("Выход");
        exitItem.Click += async (_, _) =>
        {
            if (_mainView is not null)
                await _mainView.ExitApplicationAsync();

            _trayIcon?.Dispose();
            AppSettingsStorage.SettingsSaved -= ConfigureGlobalHotkeys;
            SettingsWindow.HotkeyCaptureChanged -= OnHotkeyCaptureChanged;
            _globalHotkeyService.Dispose();
            desktop.Shutdown();
        };

        var menu = new NativeMenu
        {
            Items =
            {
                openChatItem,
                settingsItem,
                new NativeMenuItemSeparator(),
                exitItem
            }
        };

        _trayIcon = new TrayIcon
        {
            Icon = LoadTrayIcon(),
            ToolTipText = "Alexa",
            Menu = menu,
            IsVisible = true
        };

        // Стандартное поведение для ЛКМ по tray-иконке: открыть чат.
        _trayIcon.Clicked += (_, _) => _mainView?.ShowChatWindow();
    }

    /// <summary>
    /// Загружает .ico-файл из Avalonia resources для отображения в системном трее.
    /// </summary>
    private static WindowIcon LoadTrayIcon()
    {
        var iconUri = new Uri("avares://Alexa/Assets/icon.ico");
        using var iconStream = AssetLoader.Open(iconUri);
        return new WindowIcon(iconStream);
    }

    #endregion

    #region Global hotkeys

    private void ConfigureGlobalHotkeys(AppSettings settings)
    {
        _globalHotkeyService.HotkeyPressed -= OnGlobalHotkeyPressed;
        _globalHotkeyService.Configure(settings);
        _globalHotkeyService.HotkeyPressed += OnGlobalHotkeyPressed;
    }

    private void OnGlobalHotkeyPressed(GlobalHotkeyAction action)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            if (_mainView is null)
                return;

            switch (action)
            {
                case GlobalHotkeyAction.ShowWindow:
                    _mainView.ShowChatWindow();
                    break;
                case GlobalHotkeyAction.ToggleVoiceRecording:
                    await _mainView.ToggleVoiceRecordingFromHotkeyAsync();
                    break;
            }
        });
    }

    private void OnHotkeyCaptureChanged(bool isCapturing)
    {
        _globalHotkeyService.SetSuspended(isCapturing);
    }

    #endregion
}
