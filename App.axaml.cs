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
    private readonly WakeWordService _wakeWordService = new();
    private readonly ServerConnectionService _serverConnectionService = new();
    private MainView? _mainView;
    private TrayIcon? _trayIcon;
    private string _serverConnectionStatusText = "Проверка подключения к серверу...";
    private bool _isServerConnected;
    private bool _isMicrophoneAvailable = true;
    private bool _isVoiceRecording;

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
        AppLogger.Initialize();
        AppLogger.Info("Application initialization started");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // OnExplicitShutdown нужен, чтобы скрытое окно не завершало процесс автоматически.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _mainView = new MainView();
            _mainView.VoiceRecordingStateChanged += OnVoiceRecordingStateChanged;
            CreateTrayIcon(desktop);
            _serverConnectionService.StateChanged += OnServerConnectionStateChanged;
            ConfigureGlobalHotkeys(AppSettingsStorage.Load());
            ConfigureWakeWord(AppSettingsStorage.Load());
            ConfigureServerConnection(AppSettingsStorage.Load());
            ConfigureMicrophoneStatus(AppSettingsStorage.Load());
            AppSettingsStorage.SettingsSaved += ConfigureGlobalHotkeys;
            AppSettingsStorage.SettingsSaved += ConfigureWakeWord;
            AppSettingsStorage.SettingsSaved += ConfigureServerConnection;
            AppSettingsStorage.SettingsSaved += ConfigureMicrophoneStatus;
            _wakeWordService.WakeWordDetected += OnWakeWordDetected;
            SettingsWindow.HotkeyCaptureChanged += OnHotkeyCaptureChanged;
        }

        AppLogger.Info("Application initialization completed");
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
            AppLogger.Info("Application exit requested from tray");

            if (_mainView is not null)
            {
                _mainView.VoiceRecordingStateChanged -= OnVoiceRecordingStateChanged;
                await _mainView.ExitApplicationAsync();
            }

            _trayIcon?.Dispose();
            AppSettingsStorage.SettingsSaved -= ConfigureGlobalHotkeys;
            AppSettingsStorage.SettingsSaved -= ConfigureWakeWord;
            AppSettingsStorage.SettingsSaved -= ConfigureServerConnection;
            AppSettingsStorage.SettingsSaved -= ConfigureMicrophoneStatus;
            _serverConnectionService.StateChanged -= OnServerConnectionStateChanged;
            _wakeWordService.WakeWordDetected -= OnWakeWordDetected;
            SettingsWindow.HotkeyCaptureChanged -= OnHotkeyCaptureChanged;
            _globalHotkeyService.Dispose();
            _wakeWordService.Dispose();
            _serverConnectionService.Dispose();
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
            Icon = LoadTrayIcon("error.ico"),
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
        return LoadTrayIcon("icon.ico");
    }

    /// <summary>
    /// Загружает .ico-файл из Avalonia resources для отображения в системном трее.
    /// </summary>
    private static WindowIcon LoadTrayIcon(string iconName)
    {
        var iconUri = new Uri($"avares://Alexa/Assets/{iconName}");
        using var iconStream = AssetLoader.Open(iconUri);
        return new WindowIcon(iconStream);
    }

    /// <summary>
    /// Обновляет иконку и tooltip в трее по текущему состоянию приложения.
    /// </summary>
    private void UpdateTrayIcon()
    {
        if (_trayIcon is null)
            return;

        var iconName = GetTrayIconName();
        _trayIcon.Icon = LoadTrayIcon(iconName);
        _trayIcon.ToolTipText = GetTrayTooltip();
    }

    /// <summary>
    /// Выбирает имя tray-иконки по приоритету состояний.
    /// </summary>
    private string GetTrayIconName()
    {
        if (_isVoiceRecording)
            return "microphone.ico";

        if (!_isMicrophoneAvailable)
            return "warning.ico";

        return _isServerConnected ? "correct.ico" : "error.ico";
    }

    /// <summary>
    /// Формирует текст tooltip для tray-иконки.
    /// </summary>
    private string GetTrayTooltip()
    {
        if (_isVoiceRecording)
            return "Alexa: идет запись голоса";

        if (!_isMicrophoneAvailable)
            return "Alexa: микрофон недоступен";

        return $"Alexa: {_serverConnectionStatusText}";
    }

    #endregion

    #region Global hotkeys

    /// <summary>
    /// Перерегистрирует глобальные горячие клавиши по сохраненным настройкам.
    /// </summary>
    private void ConfigureGlobalHotkeys(AppSettings settings)
    {
        _globalHotkeyService.HotkeyPressed -= OnGlobalHotkeyPressed;
        _globalHotkeyService.Configure(settings);
        _globalHotkeyService.HotkeyPressed += OnGlobalHotkeyPressed;
    }

    /// <summary>
    /// Обрабатывает сработавшую глобальную горячую клавишу в UI-потоке приложения.
    /// </summary>
    private void OnGlobalHotkeyPressed(GlobalHotkeyAction action)
    {
        AppLogger.Info($"Global hotkey pressed: {action}");

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

    /// <summary>
    /// Временно отключает глобальные хоткеи на время записи новой комбинации в настройках.
    /// </summary>
    private void OnHotkeyCaptureChanged(bool isCapturing)
    {
        _globalHotkeyService.SetSuspended(isCapturing);
    }

    #endregion

    #region Wake word

    /// <summary>
    /// Перезапускает прослушивание wake-word с актуальным устройством ввода из настроек.
    /// </summary>
    private void ConfigureWakeWord(AppSettings settings)
    {
        AppLogger.Info($"Configuring wake-word listener. InputDevice='{settings.SelectedInputDevice ?? "default"}'");
        _wakeWordService.Configure(settings);
    }

    /// <summary>
    /// Запускает запись голосового сообщения после распознавания wake-word.
    /// </summary>
    private void OnWakeWordDetected()
    {
        AppLogger.Info("Wake-word detected");

        Dispatcher.UIThread.Post(async () =>
        {
            if (_mainView is null)
                return;

            var started = await _mainView.StartVoiceRecordingFromWakeWordAsync();
            if (!started)
                _wakeWordService.SetSuspended(false);
        });
    }

    /// <summary>
    /// Ставит прослушивание wake-word на паузу во время активной записи голосового сообщения.
    /// </summary>
    private void OnVoiceRecordingStateChanged(bool isVoiceRecording)
    {
        AppLogger.Info($"Voice recording state changed: {isVoiceRecording}");
        _isVoiceRecording = isVoiceRecording;
        UpdateTrayIcon();
        _wakeWordService.SetSuspended(isVoiceRecording);
    }

    #endregion

    #region Server connection

    /// <summary>
    /// Запускает фоновую проверку подключения к серверу с актуальными настройками.
    /// </summary>
    private void ConfigureServerConnection(AppSettings settings)
    {
        AppLogger.Info("Configuring server connection monitor");
        _mainView?.SetServerConnectionStatus("Проверка подключения к серверу...");
        _serverConnectionService.Start(settings, AuthTokenStorage.Load());
    }

    /// <summary>
    /// Обновляет состояние сервера в UI и tray после очередной проверки /api/health.
    /// </summary>
    private void OnServerConnectionStateChanged(ServerConnectionSnapshot snapshot)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _isServerConnected = snapshot.IsConnected;
            _serverConnectionStatusText = snapshot.Text;
            _mainView?.SetServerConnectionStatus(snapshot.Text);
            UpdateTrayIcon();
        });
    }

    /// <summary>
    /// Проверяет доступность выбранного микрофона и обновляет tray-иконку.
    /// </summary>
    private void ConfigureMicrophoneStatus(AppSettings settings)
    {
        _isMicrophoneAvailable = AudioDeviceService.IsInputDeviceAvailable(settings.SelectedInputDevice);
        AppLogger.Info($"Microphone status checked. Available={_isMicrophoneAvailable}; InputDevice='{settings.SelectedInputDevice ?? "default"}'");
        UpdateTrayIcon();
    }

    #endregion
}
