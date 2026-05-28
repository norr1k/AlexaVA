using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alexa.Models;
using Alexa.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Alexa.ViewModel;

/// <summary>
/// ViewModel главного окна чата: хранит сообщения, управляет отправкой текста и записью голоса.
/// </summary>
public partial class MainViewModel : BaseViewModel
{
    private static readonly string VoiceTempDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Temp",
        "Alexa");

    private readonly Collection<string> _sessionAudioFiles = new();
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private CancellationTokenSource? _audioPlaybackCts;
    private string? _playingAudioFilePath;
    private VoiceMessageRecorder? _voiceRecorder;
    private string? _currentVoiceFilePath;

    #region Bindable state

    [ObservableProperty]
    private bool _isSearchActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasText))]
    private string _message = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMessageInputEnabled))]
    private bool _isVoiceRecording;

    /// <summary>
    /// Показывает, есть ли текст в поле ввода; используется для смены иконки кнопки отправки
    /// </summary>
    public bool HasText => !string.IsNullOrWhiteSpace(Message);

    /// <summary>
    /// Блокирует поле ввода во время записи голосового сообщения
    /// </summary>
    public bool IsMessageInputEnabled => !IsVoiceRecording;

    /// <summary>
    /// Коллекция сообщений, отображаемых в ListBox чата
    /// </summary>
    public ObservableCollection<ChatMessage> Messages { get; } = new();

    #endregion

    #region Commands

    /// <summary>
    /// Переключает видимость строки поиска в верхней панели
    /// </summary>
    [RelayCommand]
    private void ToggleSearch()
    {
        IsSearchActive = !IsSearchActive;
    }

    /// <summary>
    /// Обрабатывает основную кнопку отправки: отправляет текст или стартует/останавливает запись голоса
    /// </summary>
    [RelayCommand]
    private async Task SendMessage()
    {
        if (IsVoiceRecording)
        {
            await StopVoiceRecordingAndSendAsync();
            return;
        }

        if (!HasText)
        {
            StartVoiceRecording();
            return;
        }

        await SendTextMessageAsync();
    }

    #endregion

    #region Session cleanup

    /// <summary>
    /// Останавливает активную запись и удаляет все временные аудиофайлы текущей сессии
    /// </summary>
    public async Task CleanupSessionAudioFilesAsync()
    {
        StopAudioPlayback();

        if (IsVoiceRecording)
            await StopCurrentRecordingAsync(deleteRecordedFile: true);

        foreach (var filePath in _sessionAudioFiles)
            TryDeleteFile(filePath);

        _sessionAudioFiles.Clear();
        CleanupVoiceTempDirectory();
    }

    public async Task ToggleVoiceRecordingFromHotkeyAsync()
    {
        if (IsVoiceRecording)
        {
            await StopVoiceRecordingAndSendAsync();
            return;
        }

        Message = string.Empty;
        StartVoiceRecording();
    }

    #endregion

    #region Text messages

    /// <summary>
    /// Добавляет сообщение пользователя в чат и отправляет его на POST /api/chat
    /// </summary>
    private async Task SendTextMessageAsync()
    {
        var userText = Message.Trim();
        Message = string.Empty;

        Messages.Add(new ChatMessage
        {
            Text = userText,
            IsMine = true,
            Time = DateTime.Now
        });

        try
        {
            using var apiClient = CreateApiClient();
            var response = await apiClient.SendChatAsync(userText, _sessionId);
            AddServerMessage(response.Text, response.Attachments);
        }
        catch (Exception ex)
        {
            AddServerMessage($"Ошибка отправки сообщения: {ex.Message}");
        }
    }

    #endregion

    #region Voice messages

    /// <summary>
    /// Создает временный WAV-файл и запускает запись голосового сообщения
    /// </summary>
    private void StartVoiceRecording()
    {
        var settings = AppSettingsStorage.Load();
        Directory.CreateDirectory(VoiceTempDirectory);

        _currentVoiceFilePath = Path.Combine(VoiceTempDirectory, $"voice-message-{Guid.NewGuid():N}.wav");
        _voiceRecorder = new VoiceMessageRecorder(
            _currentVoiceFilePath,
            settings.SelectedInputDevice,
            settings.RecordingSensitivity);

        // Поле ввода блокируется через IsVoiceRecording, чтобы пользователь не менял текст во время записи.
        Message = string.Empty;
        IsVoiceRecording = true;
        _voiceRecorder.Start();
    }

    /// <summary>
    /// Останавливает запись, добавляет голосовое сообщение в чат и отправляет файл на POST /api/voice.
    /// </summary>
    private async Task StopVoiceRecordingAndSendAsync()
    {
        var filePath = _currentVoiceFilePath;
        await StopCurrentRecordingAsync(deleteRecordedFile: false);

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return;

        _sessionAudioFiles.Add(filePath);
        Messages.Add(new ChatMessage
        {
            Text = "Голосовое сообщение",
            IsVoiceMessage = true,
            AudioFilePath = filePath,
            IsMine = true,
            Time = DateTime.Now
        });

        try
        {
            using var apiClient = CreateApiClient();
            var response = await apiClient.SendVoiceAsync(filePath);
            TryDeleteFile(filePath);
            _sessionAudioFiles.Remove(filePath);
            var serverAudioFilePath = await DownloadVoiceResponseAudioAsync(apiClient, response);
            if (serverAudioFilePath is not null)
            {
                AddServerVoiceMessage(response.Text, serverAudioFilePath);
                await PlayAudioFileAsync(serverAudioFilePath);
            }
            else
            {
                AddServerMessage(FormatVoiceResponse(response));
            }
        }
        catch (Exception ex)
        {
            AddServerMessage($"Ошибка отправки голосового сообщения: {ex.Message}");
        }
    }

    /// <summary>
    /// Останавливает текущий рекордер и при необходимости удаляет незавершенный файл
    /// </summary>
    private async Task StopCurrentRecordingAsync(bool deleteRecordedFile)
    {
        var recorder = _voiceRecorder;
        var filePath = _currentVoiceFilePath;

        _voiceRecorder = null;
        _currentVoiceFilePath = null;
        IsVoiceRecording = false;

        if (recorder is null)
            return;

        try
        {
            await recorder.StopAsync();
        }
        finally
        {
            recorder.Dispose();

            // При закрытии во время записи файл еще не отправлен, поэтому удаляем его сразу.
            if (deleteRecordedFile && !string.IsNullOrWhiteSpace(filePath))
                TryDeleteFile(filePath);
        }
    }

    #endregion

    #region Audio playback

    public async Task ToggleMessageAudioPlaybackAsync(ChatMessage message)
    {
        if (!message.IsVoiceMessage ||
            string.IsNullOrWhiteSpace(message.AudioFilePath) ||
            !File.Exists(message.AudioFilePath))
        {
            return;
        }

        if (_playingAudioFilePath == message.AudioFilePath)
        {
            StopAudioPlayback();
            return;
        }

        StopAudioPlayback();
        await PlayAudioFileAsync(message.AudioFilePath);
    }

    private async Task PlayAudioFileAsync(string audioFilePath)
    {
        StopAudioPlayback();

        var playbackCts = new CancellationTokenSource();
        _audioPlaybackCts = playbackCts;
        _playingAudioFilePath = audioFilePath;

        try
        {
            var settings = AppSettingsStorage.Load();
            await AudioDeviceService.PlayFileAsync(audioFilePath, settings.SelectedOutputDevice, playbackCts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            playbackCts.Dispose();

            if (_audioPlaybackCts == playbackCts)
            {
                _audioPlaybackCts = null;
                _playingAudioFilePath = null;
            }
        }
    }

    private void StopAudioPlayback()
    {
        _audioPlaybackCts?.Cancel();
    }

    #endregion

    #region API and formatting helpers

    /// <summary>
    /// Создает API-клиент из сохраненных серверных настроек и DPAPI-токена
    /// </summary>
    private static AlexaApiClient CreateApiClient()
    {
        var settings = AppSettingsStorage.Load();
        var token = AuthTokenStorage.Load();
        return new AlexaApiClient(settings.ServerAddress, settings.ServerPort, token);
    }

    /// <summary>
    /// Добавляет ответ сервера в чат, включая список вложений, если он есть
    /// </summary>
    private void AddServerMessage(string? text, IReadOnlyList<string>? attachments = null)
    {
        var attachmentText = attachments is { Count: > 0 }
            ? $"{Environment.NewLine}{string.Join(Environment.NewLine, attachments.Select(item => $"- {item}"))}"
            : string.Empty;

        Messages.Add(new ChatMessage
        {
            Text = string.IsNullOrWhiteSpace(text) ? $"Ответ сервера{attachmentText}" : $"{text}{attachmentText}",
            IsMine = false,
            Time = DateTime.Now
        });
    }

    private void AddServerVoiceMessage(string? text, string audioFilePath)
    {
        Messages.Add(new ChatMessage
        {
            Text = string.IsNullOrWhiteSpace(text) ? "Audio response" : text,
            IsVoiceMessage = true,
            AudioFilePath = audioFilePath,
            IsMine = false,
            Time = DateTime.Now
        });
    }

    /// <summary>
    /// Превращает ответ /api/voice в текст для отображения в чате.
    /// </summary>
    private static string FormatVoiceResponse(VoiceResponse response)
    {
        if (!string.IsNullOrWhiteSpace(response.Text))
            return response.Text;

        if (!string.IsNullOrWhiteSpace(response.EffectiveAudioUrl))
            return $"Аудио ответа: {response.AudioUrl}";

        return "Голосовое сообщение отправлено";
    }

    /// <summary>
    /// Пытается удалить временный файл, не прерывая закрытие приложения при ошибке файловой системы.
    /// </summary>
    private static async Task<string?> DownloadVoiceResponseAudioAsync(AlexaApiClient apiClient, VoiceResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.EffectiveAudioUrl))
            return null;

        Directory.CreateDirectory(VoiceTempDirectory);
        var audioFilePath = Path.Combine(
            VoiceTempDirectory,
            $"voice-response-{Guid.NewGuid():N}{GetAudioFileExtension(response.EffectiveAudioUrl)}");

        await apiClient.DownloadAudioAsync(response.EffectiveAudioUrl, audioFilePath);
        return audioFilePath;
    }

    private static string GetAudioFileExtension(string audioUrl)
    {
        var path = Uri.TryCreate(audioUrl, UriKind.Absolute, out var absoluteUri)
            ? absoluteUri.LocalPath
            : audioUrl;

        var extension = Path.GetExtension(path);
        return string.IsNullOrWhiteSpace(extension) ? ".wav" : extension;
    }

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch
        {
            // Очистка сессии не должна блокировать закрытие приложения.
        }
    }

    private static void CleanupVoiceTempDirectory()
    {
        try
        {
            if (!Directory.Exists(VoiceTempDirectory))
                return;

            foreach (var filePath in Directory.EnumerateFiles(VoiceTempDirectory))
                TryDeleteFile(filePath);
        }
        catch
        {
            // Cleanup should not block application shutdown.
        }
    }

    #endregion
}
