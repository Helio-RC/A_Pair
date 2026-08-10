using System;
using System.Globalization;
using SeatFlow.Core.Enums;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace SeatFlow.Presentation.Avalonia.Converters;

/// <summary>
/// Gender? ↔ ComboBox SelectedIndex (0=空, 1=男, 2=女, 3=其他)
/// </summary>
public class GenderIndexConverter : IValueConverter
{
    public object? Convert (object? value , Type targetType , object? parameter , CultureInfo culture)
    {
        return value switch
        {
            Gender.Male => 1,
            Gender.Female => 2,
            Gender.Other => 3,
            _ => 0
        };
    }

    public object? ConvertBack (object? value , Type targetType , object? parameter , CultureInfo culture)
    {
        if (value is int i)
        {
            return i switch
            {
                1 => Gender.Male,
                2 => Gender.Female,
                3 => Gender.Other,
                _ => (Gender?)null
            };
        }
        return null;
    }
}

/// <summary>
/// 安全转换 float? ↔ string，无效输入返回 null。
/// </summary>
public class HeightConverter : IValueConverter
{
    public object? Convert (object? value , Type targetType , object? parameter , CultureInfo culture)
    {
        return value?.ToString() ?? "";
    }

    public object? ConvertBack (object? value , Type targetType , object? parameter , CultureInfo culture)
    {
        var s = value as string;
        if (string.IsNullOrWhiteSpace(s))
            return null;

        return float.TryParse(s , out var h) ? h : null;
    }
}

/// <summary>
/// 文件路径 → Bitmap 转换，null 或无效路径返回 null。
/// </summary>
public class FilePathToBitmapConverter : IValueConverter
{
    public object? Convert (object? value , Type targetType , object? parameter , CultureInfo culture)
    {
        if (value is string path && System.IO.File.Exists(path))
        {
            try { return new Bitmap(path); }
            catch { return null; }
        }
        return null;
    }

    public object? ConvertBack (object? value , Type targetType , object? parameter , CultureInfo culture)
        => throw new NotSupportedException();
}
