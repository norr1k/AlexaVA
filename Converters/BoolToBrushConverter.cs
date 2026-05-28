using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Alexa.Converters;

/// <summary>
/// По признаку "IsMine" красит сообщения в чате
/// </summary>
public class BoolToBrushConverter : IValueConverter
{
    /// <summary>
    /// Синее - пользователь, серое - сервер
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isMine = (bool)value!;

        return isMine
            ? new SolidColorBrush(Color.Parse("#3B82F6"))
            : new SolidColorBrush(Color.Parse("#1A1A1A"));
    }

    /// <summary>
    /// Заглушка
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
