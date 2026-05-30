namespace Alexa.Services;

/// <summary>
/// DTO пользовательских настроек, которые можно безопасно хранить в открытом JSON.
/// </summary>
public sealed class AppSettings
{
    public bool HotkeysEnabled { get; set; } = true;

    public bool ShowWindowHotkeyEnabled { get; set; } = true;

    /// <summary>Хоткей для показа окна</summary>
    public string ShowWindowHotkey { get; set; } = "Ctrl+Shift+A";

    public bool VoiceRecordHotkeyEnabled { get; set; } = true;

    /// <summary>Хоткей для записи голосового сообщения</summary>
    public string VoiceRecordHotkey { get; set; } = "Ctrl+Shift+R";

    /// <summary>Выбранный язык интерфейса</summary>
    public string SelectedLanguage { get; set; } = "Русский";

    /// <summary>Название микрофона</summary>
    public string? SelectedInputDevice { get; set; }

    /// <summary>Наушники</summary>
    public string? SelectedOutputDevice { get; set; }

    /// <summary>Чувствительность записи в процентах</summary>
    public double RecordingSensitivity { get; set; } = 50;

    /// <summary>Громкость записи в процентах</summary>
    public double RecordingVolume { get; set; } = 100;

    /// <summary>Громкость воспроизведения в процентах</summary>
    public double PlaybackVolume { get; set; } = 100;

    /// <summary>Адрес сервера</summary>
    public string ServerAddress { get; set; } = string.Empty;

    /// <summary>Порт сервера</summary>
    public string ServerPort { get; set; } = string.Empty;
}
