using System;
using System.Collections.Generic;
using System.Linq;

namespace Alpheratz.Features.Gallery;

/// <summary>
/// GalleryPhotosState のコレクション同期とサムネイル対象抽出を UI/worker から切り離した補助ロジック。
/// DB 取得や実サムネイル生成は扱わず、PhotoThumbnailItem の並びから必要な辞書と対象リストだけを作る。
/// </summary>
internal static class GalleryPhotosStateLogic
{
    public const string UnknownWorldGroupKey = "unknown";

    /// <summary>ワールド名をグループキーへ正規化する。空白だけの値は未解決ワールドとして扱う。</summary>
    public static string BuildWorldGroupKey(string? worldName)
    {
        var trimmed = worldName?.Trim();
        return string.IsNullOrEmpty(trimmed) ? UnknownWorldGroupKey : trimmed;
    }

    /// <summary>写真差し替え時に displayItems の参照同期へ使う photo_path キーの辞書を作る。</summary>
    public static IReadOnlyDictionary<string, PhotoThumbnailItem> BuildReplacementMap(IEnumerable<PhotoThumbnailItem> photos)
        => photos.ToDictionary(photo => photo.PhotoPath, photo => photo, StringComparer.Ordinal);

    /// <summary>サムネイル生成要求に使う photo_path キーの辞書を作る。空パスと重複パスは無視する。</summary>
    public static IReadOnlyDictionary<string, PhotoThumbnailItem> BuildThumbnailRequestMap(
        IEnumerable<PhotoThumbnailItem> photos,
        bool requireMissingGridThumb)
    {
        var map = new Dictionary<string, PhotoThumbnailItem>(StringComparer.Ordinal);
        foreach (var photo in photos)
        {
            if (string.IsNullOrEmpty(photo.PhotoPath)) continue;
            if (requireMissingGridThumb && !string.IsNullOrEmpty(photo.GridThumbPath)) continue;
            map.TryAdd(photo.PhotoPath, photo);
        }

        return map;
    }

    /// <summary>サムネイル生成 worker へ渡す未生成写真の path/slot リストを作る。</summary>
    public static IReadOnlyList<(string path, long slot)> BuildThumbnailTargets(IEnumerable<PhotoThumbnailItem> photos)
        => photos
            .Where(photo => string.IsNullOrEmpty(photo.GridThumbPath) && !string.IsNullOrEmpty(photo.PhotoPath))
            .Select(photo => (path: photo.PhotoPath, slot: photo.SourceSlot))
            .ToList();

    /// <summary>既存 PhotoGridItem 内の Photo と GroupPhotos を新しい写真インスタンスへ差し替える。</summary>
    public static PhotoGridItem SyncDisplayItem(PhotoGridItem item, IReadOnlyDictionary<string, PhotoThumbnailItem> map)
    {
        if (map.TryGetValue(item.Photo.PhotoPath, out var nextPhoto))
        {
            item.Photo = nextPhoto;
        }

        if (item.GroupPhotos is not null)
        {
            item.GroupPhotos = item.GroupPhotos
                .Select(photo => map.TryGetValue(photo.PhotoPath, out var replacement) ? replacement : photo)
                .ToArray();
        }

        return item;
    }
}
