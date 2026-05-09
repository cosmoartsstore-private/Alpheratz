using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Alpheratz.Shared.Converters;

/// <summary>
/// ファイルパス文字列を BitmapImage に変換する XAML バインディングコンバータ。
/// DecodePixelWidth でデコード解像度を制限し、メモリ消費を抑える。
/// </summary>
public sealed class ThumbnailSourceConverter : IValueConverter
{
    public int DecodePixelWidth { get; set; } = 260;

    /// <summary>パス文字列 → BitmapImage。null/空文字の場合は null を返す。</summary>
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string path || string.IsNullOrEmpty(path))
            return null;
        try
        {
            var bmp = new BitmapImage { CreateOptions = BitmapCreateOptions.IgnoreImageCache, UriSource = new Uri(path, UriKind.Absolute) };
            if (DecodePixelWidth > 0)
            {
                bmp.DecodePixelWidth = DecodePixelWidth;
                bmp.DecodePixelType = DecodePixelType.Logical;
            }
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>逆変換は非対応。</summary>
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
