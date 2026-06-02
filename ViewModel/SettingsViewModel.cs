using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Alexa.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Alexa.ViewModel;

/// <summary>
/// ViewModel окна настроек: управляет общими, аудио- и серверными настройками.
/// </summary>
public partial class SettingsViewModel : BaseViewModel
{
    private bool _isApplyingSettings;

    #region Static option lists

    /// <summary>
    /// Список языков, доступных для выбора в настройках.
    /// </summary>
    public ObservableCollection<string> Languages { get; } = new(["Русский", "English"]);

    /// <summary>
    /// Список устройств ввода, полученный из аудиосервиса.
    /// </summary>
    public ObservableCollection<string> InputDevices { get; } = new(AudioDeviceService.GetInputDevices());

    /// <summary>
    /// Список устройств вывода, полученный из аудиосервиса.
    /// </summary>
    public ObservableCollection<string> OutputDevices { get; } = new(AudioDeviceService.GetOutputDevices());

    #endregion

    #region Page selection state

    /// <summary>
    /// Показывает, выбрана ли вкладка "Общие".
    /// </summary>
    public bool IsGeneralSettingsSelected => SelectedSettingsPage == SettingsPage.General;

    /// <summary>
    /// Показывает, выбрана ли вкладка "Аудио".
    /// </summary>
    public bool IsAudioSettingsSelected => SelectedSettingsPage == SettingsPage.Audio;

    /// <summary>
    /// Показывает, выбрана ли вкладка "Сервер".
    /// </summary>
    public bool IsServerSettingsSelected => SelectedSettingsPage == SettingsPage.Server;

    /// <summary>
    /// Возвращает заголовок текущей страницы настроек.
    /// </summary>
    public string SelectedSettingsTitle => SelectedSettingsPage switch
    {
        SettingsPage.Audio => "Аудио",
        SettingsPage.Server => "Сервер",
        _ => "Общие"
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneralSettingsSelected))]
    [NotifyPropertyChangedFor(nameof(IsAudioSettingsSelected))]
    [NotifyPropertyChangedFor(nameof(IsServerSettingsSelected))]
    [NotifyPropertyChangedFor(nameof(SelectedSettingsTitle))]
    private SettingsPage _selectedSettingsPage = SettingsPage.General;

    #endregion

    #region Editable settings

    [ObservableProperty]
    private bool _isAutostartEnabled;

    [ObservableProperty]
    private bool _hotkeysEnabled = true;

    [ObservableProperty]
    private bool _showWindowHotkeyEnabled = true;

    [ObservableProperty]
    private string _showWindowHotkey = string.Empty;

    [ObservableProperty]
    private bool _voiceRecordHotkeyEnabled = true;

    [ObservableProperty]
    private string _voiceRecordHotkey = string.Empty;

    [ObservableProperty]
    private string _selectedLanguage = string.Empty;

    [ObservableProperty]
    private string? _selectedInputDevice;

    [ObservableProperty]
    private string? _selectedOutputDevice;

    [ObservableProperty]
    private double _recordingSensitivity;

    [ObservableProperty]
    private double _recordingVolume;

    [ObservableProperty]
    private double _playbackVolume;

    [ObservableProperty]
    private double _silenceToSendSeconds;

    [ObservableProperty]
    private string _serverAddress = string.Empty;

    [ObservableProperty]
    private string _serverPort = string.Empty;

    [ObservableProperty]
    private string _authenticationToken = string.Empty;

    #endregion

    #region Status text

    [ObservableProperty]
    private string _serverHealthStatus = "Соединение не проверялось";

    [ObservableProperty]
    private string _microphoneTestStatus = "Готово к тесту";

    [ObservableProperty]
    private string _settingsStatus = "Измените настройки и нажмите \"Сохранить\"";

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    #endregion

    #region Initialization

    /// <summary>
    /// Загружает сохраненные настройки и заполняет форму.
    /// </summary>
    public SettingsViewModel()
    {
        LoadSavedSettings();
    }

    #endregion

    #region Navigation commands

    /// <summary>
    /// Переключает окно настроек на раздел "Общие".
    /// </summary>
    [RelayCommand]
    private void SelectGeneralSettings()
    {
        SelectedSettingsPage = SettingsPage.General;
    }

    /// <summary>
    /// Переключает окно настроек на раздел "Аудио".
    /// </summary>
    [RelayCommand]
    private void SelectAudioSettings()
    {
        SelectedSettingsPage = SettingsPage.Audio;
    }

    /// <summary>
    /// Переключает окно настроек на раздел "Сервер".
    /// </summary>
    [RelayCommand]
    private void SelectServerSettings()
    {
        SelectedSettingsPage = SettingsPage.Server;
    }

    #endregion

    #region Save/reset commands

    /// <summary>
    /// Сохраняет обычные настройки в JSON, токен в DPAPI-хранилище и применяет автозапуск.
    /// </summary>
    [RelayCommand]
    private void SaveSettings()
    {
        try
        {
            // Автозапуск влияет на реестр Windows, поэтому применяем его только по явному нажатию "Сохранить".
            TrimEditableSettings();
            AutostartService.SetEnabled(IsAutostartEnabled);

            AuthTokenStorage.Save(AuthenticationToken);

            AppSettingsStorage.Save(new AppSettings
            {
                ShowWindowHotkey = ShowWindowHotkey,
                VoiceRecordHotkey = VoiceRecordHotkey,
                HotkeysEnabled = HotkeysEnabled,
                ShowWindowHotkeyEnabled = ShowWindowHotkeyEnabled,
                VoiceRecordHotkeyEnabled = VoiceRecordHotkeyEnabled,
                SelectedLanguage = SelectedLanguage,
                SelectedInputDevice = SelectedInputDevice,
                SelectedOutputDevice = SelectedOutputDevice,
                RecordingSensitivity = RecordingSensitivity,
                RecordingVolume = RecordingVolume,
                PlaybackVolume = PlaybackVolume,
                SilenceToSendSeconds = SilenceToSendSeconds,
                ServerAddress = ServerAddress,
                ServerPort = ServerPort
            });

            HasUnsavedChanges = false;
            SettingsStatus = "Настройки сохранены";
        }
        catch (Exception ex)
        {
            SettingsStatus = $"Не удалось сохранить настройки: {ex.Message}";
        }
    }

    /// <summary>
    /// Возвращает значения формы к настройкам по умолчанию без немедленной записи на диск.
    /// </summary>
    [RelayCommand]
    private void ResetSettingsToDefault()
    {
        // Для фактической записи сброшенных значений пользователь должен нажать "Сохранить".
        ApplySettings(new AppSettings());
        AuthenticationToken = string.Empty;
        ServerHealthStatus = "Соединение не проверялось";
        IsAutostartEnabled = false;
        HotkeysEnabled = true;
        ShowWindowHotkeyEnabled = true;
        VoiceRecordHotkeyEnabled = true;
        SettingsStatus = "Возвращены значения по умолчанию. Нажмите \"Сохранить\", чтобы применить";
    }

    #endregion

    #region Server checks

    /// <summary>
    /// Проверяет сервер через GET /api/health, используя текущие значения из формы.
    /// </summary>
    [RelayCommand]
    private async Task CheckServerHealth()
    {
        try
        {
            ServerHealthStatus = "Проверка соединения...";
            TrimServerSettings();
            using var apiClient = new AlexaApiClient(ServerAddress, ServerPort, AuthenticationToken);
            await apiClient.CheckHealthAsync();
            ServerHealthStatus = "Соединение установлено";
        }
        catch (Exception ex)
        {
            ServerHealthStatus = $"Ошибка соединения: {ex.Message}";
        }
    }

    #endregion

    #region Audio test

    /// <summary>
    /// Запускает тест микрофона: записывает короткий фрагмент, воспроизводит его и удаляет временный файл.
    /// </summary>
    [RelayCommand]
    private async Task StartMicrophoneTest()
    {
        var tempDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp",
            "Alexa");
        Directory.CreateDirectory(tempDirectory);

        var tempFile = Path.Combine(tempDirectory, $"microphone-test-{Guid.NewGuid():N}.wav");

        try
        {
            MicrophoneTestStatus = "Запись 3 секунды...";
            await AudioDeviceService.RecordAndPlayEchoAsync(
                tempFile,
                SelectedInputDevice,
                SelectedOutputDevice,
                RecordingSensitivity,
                RecordingVolume,
                PlaybackVolume);
            MicrophoneTestStatus = "Эхо воспроизведено, временный файл удален";
        }
        catch (Exception ex)
        {
            MicrophoneTestStatus = $"Ошибка теста: {ex.Message}";
        }
        finally
        {
            TryDeleteTempFile(tempFile);
        }
    }

    #endregion

    #region Load/apply helpers

    /// <summary>
    /// Загружает настройки из JSON, токен из DPAPI и реальное состояние автозапуска из реестра.
    /// </summary>
    private void LoadSavedSettings()
    {
        _isApplyingSettings = true;
        ApplySettings(AppSettingsStorage.Load());
        AuthenticationToken = TrimEditableText(AuthTokenStorage.Load());
        IsAutostartEnabled = AutostartService.IsEnabled();
        _isApplyingSettings = false;
        HasUnsavedChanges = false;
    }

    /// <summary>
    /// Применяет объект настроек к свойствам ViewModel.
    /// </summary>
    private void ApplySettings(AppSettings settings)
    {
        ShowWindowHotkey = TrimEditableText(settings.ShowWindowHotkey);
        VoiceRecordHotkey = TrimEditableText(settings.VoiceRecordHotkey);
        HotkeysEnabled = settings.HotkeysEnabled;
        ShowWindowHotkeyEnabled = settings.ShowWindowHotkeyEnabled;
        VoiceRecordHotkeyEnabled = settings.VoiceRecordHotkeyEnabled;
        SelectedLanguage = Languages.Contains(settings.SelectedLanguage)
            ? settings.SelectedLanguage
            : Languages[0];
        SelectedInputDevice = SelectStoredOrFirst(InputDevices, settings.SelectedInputDevice);
        SelectedOutputDevice = SelectStoredOrFirst(OutputDevices, settings.SelectedOutputDevice);
        RecordingSensitivity = Math.Clamp(settings.RecordingSensitivity, 0, 100);
        RecordingVolume = Math.Clamp(settings.RecordingVolume, 0, 100);
        PlaybackVolume = Math.Clamp(settings.PlaybackVolume, 0, 100);
        SilenceToSendSeconds = Math.Clamp(settings.SilenceToSendSeconds, 1, 10);
        ServerAddress = TrimEditableText(settings.ServerAddress);
        ServerPort = TrimEditableText(settings.ServerPort);
    }

    #endregion

    #region Utility helpers

    /// <summary>
    /// Удаляет временный WAV-файл теста микрофона, не ломая UI при ошибке удаления.
    /// </summary>
    private static void TryDeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Ошибка удаления временного WAV не должна ломать окно настроек.
        }
    }

    /// <summary>
    /// Очищает все редактируемые строковые поля перед сохранением.
    /// </summary>
    private void TrimEditableSettings()
    {
        ShowWindowHotkey = TrimEditableText(ShowWindowHotkey);
        VoiceRecordHotkey = TrimEditableText(VoiceRecordHotkey);
        TrimServerSettings();
    }

    /// <summary>
    /// Очищает серверные поля от пробелов и переводов строк по краям.
    /// </summary>
    private void TrimServerSettings()
    {
        ServerAddress = TrimEditableText(ServerAddress);
        ServerPort = TrimEditableText(ServerPort);
        AuthenticationToken = TrimEditableText(AuthenticationToken);
    }

    /// <summary>
    /// Возвращает строку без пробелов по краям или пустую строку для null.
    /// </summary>
    private static string TrimEditableText(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// Возвращает сохраненное значение, если оно есть в текущем списке устройств, иначе первое доступное.
    /// </summary>
    private static string? SelectStoredOrFirst(ObservableCollection<string> values, string? storedValue)
    {
        if (!string.IsNullOrWhiteSpace(storedValue) && values.Contains(storedValue))
            return storedValue;

        return values.Count > 0 ? values[0] : null;
    }

    /// <summary>
    /// Помечает настройки как измененные, если сейчас не выполняется загрузка значений.
    /// </summary>
    private void MarkSettingsChanged()
    {
        if (!_isApplyingSettings)
            HasUnsavedChanges = true;
    }

    partial void OnIsAutostartEnabledChanged(bool value) => MarkSettingsChanged();
    partial void OnHotkeysEnabledChanged(bool value) => MarkSettingsChanged();
    partial void OnShowWindowHotkeyEnabledChanged(bool value) => MarkSettingsChanged();
    partial void OnShowWindowHotkeyChanged(string value) => MarkSettingsChanged();
    partial void OnVoiceRecordHotkeyEnabledChanged(bool value) => MarkSettingsChanged();
    partial void OnVoiceRecordHotkeyChanged(string value) => MarkSettingsChanged();
    partial void OnSelectedLanguageChanged(string value) => MarkSettingsChanged();
    partial void OnSelectedInputDeviceChanged(string? value) => MarkSettingsChanged();
    partial void OnSelectedOutputDeviceChanged(string? value) => MarkSettingsChanged();
    partial void OnRecordingSensitivityChanged(double value) => MarkSettingsChanged();
    partial void OnRecordingVolumeChanged(double value) => MarkSettingsChanged();
    partial void OnPlaybackVolumeChanged(double value) => MarkSettingsChanged();
    partial void OnSilenceToSendSecondsChanged(double value) => MarkSettingsChanged();
    partial void OnServerAddressChanged(string value) => MarkSettingsChanged();
    partial void OnServerPortChanged(string value) => MarkSettingsChanged();
    partial void OnAuthenticationTokenChanged(string value) => MarkSettingsChanged();

    #endregion
}

/// <summary>
/// Разделы окна настроек.
/// </summary>
public enum SettingsPage
{
    General,
    Audio,
    Server
}
