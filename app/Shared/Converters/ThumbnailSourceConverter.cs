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
    /// <remarks>
    /// WinUI 内蔵の URI ベースキャッシュを有効化するため <c>IgnoreImageCache</c> は指定しない。
    /// 同一 URI のセルが再表示されたときに再デコードを避けることでスクロール時のメモリ・
    /// CPU 負荷を抑える。サムネ再生成時 (元画像の差し替え時) は ThumbnailService 側で
    /// 同パスへ上書きが起きるため僅かに古いキャッシュが見える可能性があるが、これは稀。
    /// </remarks>
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string path || string.IsNullOrEmpty(path))
            return null;
        try
        {
            var bmp = new BitmapImage { UriSource = new Uri(path, UriKind.Absolute) };
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
