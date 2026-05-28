using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Layout;

namespace Alexa.Converters;

/// <summary>
/// По признаку "IsMine" выравнивает сообщения в чате
/// </summary>
public class BoolToAlignmentConverter : IValueConverter
{
    /// <summary>
    /// Правое - пользователь, левое - сервер
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isMine = (bool)value!;

        return isMine
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Left;
    }

    /// <summary>
    /// Заглушка
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
