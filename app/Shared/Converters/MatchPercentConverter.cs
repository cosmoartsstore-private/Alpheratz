using System;
using System.Globalization;
using Microsoft.UI.Xaml.Data;
using static Alpheratz.Messages.MessageCatalog;

namespace Alpheratz.Shared.Converters;

/// <summary>
/// PDQ ハミング距離 (0-256) を一致率% 文字列に換算する。
/// P = round((1 − distance/256) × 100)（距離0→100% / 75→71% / 256→0%）。
/// int / int? の両方を受け、null は "一致率: —" と表示する。
/// 注意: 同一アバターのアップ等は別ワールドでも高% になり得るため、目視併用前提。
/// </summary>
public sealed class MatchPercentConverter : IValueConverter
{
    private const double PdqMaxDistance = 256.0;

    /// <summary>PDQ 距離を 0-100% の一致率文字列へ変換する。</summary>
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        // int? は boxing で int として届く。値が無い場合のみ "—" を返す。
        if (value is int distance)
        {
            var percent = (int)Math.Round((1.0 - distance / PdqMaxDistance) * 100.0);
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            return getMsg("MatchPercentConverter.value", ("percent", percent.ToString(CultureInfo.InvariantCulture)));
        }
        return getMsg("MatchPercentConverter.empty");
    }

    /// <summary>表示専用の変換なので逆変換は提供しない。</summary>
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
