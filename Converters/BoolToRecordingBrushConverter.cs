using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Alexa.Converters;

/// <summary>
/// Конвертер для цвето микрофона во время записи
/// </summary>
public sealed class BoolToRecordingBrushConverter : IValueConverter
{
    private static readonly IBrush RecordingBrush = new SolidColorBrush(Color.Parse("#22C55E"));
    private static readonly IBrush IdleBrush = Brushes.White;

    /// <summary>
    /// Зелёный для записи, сервый для бездействия
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? RecordingBrush : IdleBrush;
    }

    /// <summary>
    /// Заглушка
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
