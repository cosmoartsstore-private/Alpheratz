using System;
using System.Globalization;
using Microsoft.UI.Xaml.Data;

namespace Alpheratz.Shared.Converters;

/// <summary>
/// PDQ ハミング距離 (0-256) を一致率% 文字列に換算する。
/// P = round((1 − distance/256) × 100)（距離0→100% / 124→約52% / 256→0%）。
/// int / int? の両方を受け、null は "一致率: —" と表示する。
/// 注意: 同一アバターのアップ等は別ワールドでも高% になり得るため、目視併用前提。
/// </summary>
public sealed class MatchPercentConverter : IValueConverter
{
    private const double PdqMaxDistance = 256.0;

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        // int? は boxing で int として届く。値が無い場合のみ "—" を返す。
        if (value is int distance)
        {
            var percent = (int)Math.Round((1.0 - distance / PdqMaxDistance) * 100.0);
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            return $"一致率: {percent.ToString(CultureInfo.InvariantCulture)}%";
        }
        return "一致率: —";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
