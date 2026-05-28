using System;

namespace Alexa.Models;

/// <summary>
/// Модель одного сообщения в списке чата.
/// </summary>
public class ChatMessage
{
    /// <summary>Текст, который отображается в пузыре сообщения</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Признак того, что сообщение голосовое</summary>
    public bool IsVoiceMessage { get; set; }

    /// <summary>Путь к временному аудиофайлу голосового сообщения текущей сессии</summary>
    public string? AudioFilePath { get; set; }

    /// <summary>Признак сообщения пользователя; влияет на выравнивание и цвет</summary>
    public bool IsMine { get; set; }

    /// <summary>Время создания сообщения</summary>
    public DateTime Time { get; set; } = DateTime.Now;
}
