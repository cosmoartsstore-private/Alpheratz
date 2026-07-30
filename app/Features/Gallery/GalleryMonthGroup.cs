using System;
using System.Collections.Generic;
using System.Globalization;
using static Alpheratz.Messages.MessageCatalog;

namespace Alpheratz.Features.Gallery;

/// <summary>月ナビゲーション用のグループ情報。FirstIndex は photos 配列上のインデックス。</summary>
public sealed record GalleryMonthGroup(string Key, int Year, int Month, string Label, int FirstIndex, int Count);

/// <summary>
/// 写真リストからタイムスタンプを解析し、月ごとのグループ境界を特定する。
/// 結果は月ナビゲーション（サイドバーの年月リスト）に使用される。
/// </summary>
public static class GalleryMonthsCalculator
{
    /// <summary>写真リストを走査し、月の変わり目でグループを切る。</summary>
    public static IReadOnlyList<GalleryMonthGroup> Build(IReadOnlyList<PhotoThumbnailItem> photos)
    {
        if (photos.Count == 0) return [];

        var groups = new List<GalleryMonthGroup>();
        string currentKey = string.Empty;
        var pendingYear = 0;
        var pendingMonth = 0;
        var pendingFirstIndex = 0;
        var pendingCount = 0;

        for (var i = 0; i < photos.Count; i++)
        {
            var (year, month) = ParseYearMonth(photos[i].Timestamp);
            var key = $"{year:D4}-{month:D2}";

            if (key != currentKey)
            {
                if (currentKey.Length > 0)
                {
                    groups.Add(new GalleryMonthGroup(
                        currentKey,
                        pendingYear,
                        pendingMonth,
                        getMsg("GalleryMonthGroup.monthLabel", ("month", pendingMonth)),
                        pendingFirstIndex,
                        pendingCount));
                }
                currentKey = key;
                pendingYear = year;
                pendingMonth = month;
                pendingFirstIndex = i;
                pendingCount = 1;
            }
            else
            {
                pendingCount++;
            }
        }
        if (currentKey.Length > 0)
        {
            groups.Add(new GalleryMonthGroup(
                currentKey,
                pendingYear,
                pendingMonth,
                getMsg("GalleryMonthGroup.monthLabel", ("month", pendingMonth)),
                pendingFirstIndex,
                pendingCount));
        }

        return groups;
    }

    /// <summary>タイムスタンプ文字列から年・月を抽出する。パース失敗時は (0, 1) を返す。</summary>
    private static (int year, int month) ParseYearMonth(string timestamp)
    {
        if (string.IsNullOrEmpty(timestamp)) return (0, 1);
        var normalized = timestamp.Replace(' ', 'T');
        if (DateTime.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            return (dt.Year, dt.Month);
        }
        return (0, 1);
    }
}
