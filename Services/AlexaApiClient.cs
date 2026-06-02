using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Alexa.Services;

/// <summary>
/// API
/// </summary>
public sealed class AlexaApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    #region Initialization

    /// <summary>
    /// HttpClient по адресу сервера и с заголовком Authorization.
    /// </summary>
    public AlexaApiClient(string serverAddress, string serverPort, string authorizationToken)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = BuildBaseAddress(serverAddress, serverPort),
            Timeout = TimeSpan.FromSeconds(20)
        };

        if (!string.IsNullOrWhiteSpace(authorizationToken))
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authorizationToken);
    }

    #endregion

    #region API methods

    /// <summary>
    /// Отправляет текстовое сообщение в /api/chat и возвращает ответ сервера
    /// </summary>
    public async Task<ChatResponse> SendChatAsync(
        string text,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var request = new ChatRequest(text, sessionId);
        using var response = await _httpClient.PostAsJsonAsync("api/chat", request, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ChatResponse>(JsonOptions, cancellationToken)
               ?? new ChatResponse(string.Empty, []);
    }

    /// <summary>
    /// Отправляет WAV в /api/voice и возвращает ответ сервера
    /// </summary>
    public async Task<VoiceResponse> SendVoiceAsync(
        string audioFilePath,
        CancellationToken cancellationToken = default)
    {
        await using var audioStream = File.OpenRead(audioFilePath);
        using var form = new MultipartFormDataContent();
        using var audioContent = new StreamContent(audioStream);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");

        // Имя поля "file" согласовано с типичным backend-контрактом multipart/form-data.
        form.Add(audioContent, "file", Path.GetFileName(audioFilePath));

        using var response = await _httpClient.PostAsync("api/voice", form, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<VoiceResponse>(JsonOptions, cancellationToken)
               ?? new VoiceResponse();
    }

    /// <summary>
    /// Скачивает аудиофайл по URL из ответа сервера во временный файл приложения.
    /// </summary>
    public async Task DownloadAudioAsync(
        string audioUrl,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var uri = BuildAudioUri(audioUrl);
        await using var sourceStream = await _httpClient.GetStreamAsync(uri, cancellationToken);
        await using var destinationStream = File.Create(destinationPath);
        await sourceStream.CopyToAsync(destinationStream, cancellationToken);
    }

    /// <summary>
    /// GET /api/health.
    /// </summary>
    public async Task CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("api/health", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Освобождает внутренний HttpClient
    /// </summary>
    public void Dispose()
    {
        _httpClient.Dispose();
    }

    /// <summary>
    /// Собирает базовый URI сервера из адреса и порта, добавляя http:// при необходимости
    /// </summary>
    public static Uri BuildBaseAddress(string serverAddress, string serverPort)
    {
        if (string.IsNullOrWhiteSpace(serverAddress))
            throw new InvalidOperationException("Адрес сервера не указан.");

        var normalizedAddress = serverAddress.Trim();
        if (!normalizedAddress.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !normalizedAddress.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            normalizedAddress = $"http://{normalizedAddress}";
        }

        var builder = new UriBuilder(normalizedAddress);
        if (int.TryParse(serverPort, out var port))
            builder.Port = port;

        if (!builder.Path.EndsWith('/'))
            builder.Path += "/";

        return builder.Uri;
    }

    /// <summary>
    /// Преобразует абсолютную или относительную ссылку на аудио в URI для скачивания.
    /// </summary>
    private Uri BuildAudioUri(string audioUrl)
    {
        if (Uri.TryCreate(audioUrl, UriKind.Absolute, out var absoluteUri))
            return absoluteUri;

        return new Uri(_httpClient.BaseAddress!, audioUrl);
    }

    #endregion
}

/// <summary>
/// Тело запроса POST /api/chat
/// </summary>
public sealed record ChatRequest(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("session_id")] string SessionId);

/// <summary>
/// Ответ POST /api/chat с текстом и вложениями
/// </summary>
public sealed record ChatResponse(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("attachments")] IReadOnlyList<string> Attachments);

/// <summary>
/// Ответ POST /api/voice: сервер может вернуть ссылку на аудио или текст
/// </summary>
public sealed class VoiceResponse
{
    /// <summary>Ссылка на аудиофайл в стандартном snake_case поле.</summary>
    [JsonPropertyName("audio_url")]
    public string? AudioUrl { get; init; }

    /// <summary>Ссылка на аудиофайл в компактном поле без подчеркивания.</summary>
    [JsonPropertyName("audiourl")]
    public string? AudioUrlCompact { get; init; }

    /// <summary>Текстовый ответ сервера.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    /// <summary>Возвращает первую доступную ссылку на аудиофайл из поддерживаемых полей ответа.</summary>
    [JsonIgnore]
    public string? EffectiveAudioUrl => string.IsNullOrWhiteSpace(AudioUrl) ? AudioUrlCompact : AudioUrl;
}
