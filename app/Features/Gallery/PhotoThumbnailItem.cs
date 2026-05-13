using System.Collections.Generic;
using Alpheratz.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using Alpheratz.Models;

namespace Alpheratz.Features.Gallery;

/// <summary>
/// 1枚の写真のサムネイル表示用 ViewModel。
/// DB レコード (PhotoRecordDto) から生成され、ギャラリーと PhotoModal の両方で使用される。
/// EffectiveSourcePath / EffectiveDisplayPath はバインディング用の読み取り専用プロパティ。
/// </summary>
public partial class PhotoThumbnailItem : UiThreadSafeObservableObject
{
    private string photo_filename = string.Empty;
    private string photo_path = string.Empty;
    private string? resolved_photo_path;
    private string? grid_thumb_path;
    private string? display_thumb_path;
    private string? world_id;
    private string? world_name;
    [ObservableProperty] private string timestamp = string.Empty;
    [ObservableProperty] private string? phash;
    [ObservableProperty] private string? orientation;
    private long? image_width;
    private long? image_height;
    private long source_slot = 1;
    private bool is_favorite;
    [ObservableProperty] private IReadOnlyList<string> tags = [];
    private string? match_source;
    private bool is_missing;
    [ObservableProperty] private bool isSelected;

    public string PhotoFilename
    {
        get => photo_filename;
        set => SetProperty(ref photo_filename, value);
    }

    /// <summary>写真の絶対パス。変更時に EffectiveSourcePath / EffectiveDisplayPath も通知する。</summary>
    public string PhotoPath
    {
        get => photo_path;
        set
        {
            if (SetProperty(ref photo_path, value))
            {
                OnPropertyChanged(nameof(EffectiveSourcePath));
                OnPropertyChanged(nameof(EffectiveDisplayPath));
            }
        }
    }

    /// <summary>シンボリックリンク等で解決された実パス。</summary>
    public string? ResolvedPhotoPath
    {
        get => resolved_photo_path;
        set
        {
            if (SetProperty(ref resolved_photo_path, value))
            {
                OnPropertyChanged(nameof(EffectiveSourcePath));
                OnPropertyChanged(nameof(EffectiveDisplayPath));
            }
        }
    }

    /// <summary>グリッド表示用サムネイルのパス。ThumbnailWorker が生成完了後にセットする。</summary>
    public string? GridThumbPath
    {
        get => grid_thumb_path;
        set
        {
            if (SetProperty(ref grid_thumb_path, value))
            {
                OnPropertyChanged(nameof(EffectiveSourcePath));
            }
        }
    }

    /// <summary>PhotoModal 用の表示サムネイルパス。</summary>
    public string? DisplayThumbPath
    {
        get => display_thumb_path;
        set
        {
            if (SetProperty(ref display_thumb_path, value))
            {
                OnPropertyChanged(nameof(EffectiveDisplayPath));
            }
        }
    }

    public string? WorldId
    {
        get => world_id;
        set => SetProperty(ref world_id, value);
    }

    public string? WorldName
    {
        get => world_name;
        set => SetProperty(ref world_name, value);
    }

    public long? ImageWidth
    {
        get => image_width;
        set => SetProperty(ref image_width, value);
    }

    public long? ImageHeight
    {
        get => image_height;
        set => SetProperty(ref image_height, value);
    }

    public long SourceSlot
    {
        get => source_slot;
        set => SetProperty(ref source_slot, value);
    }

    public bool IsFavorite
    {
        get => is_favorite;
        set => SetProperty(ref is_favorite, value);
    }

    public string? MatchSource
    {
        get => match_source;
        set => SetProperty(ref match_source, value);
    }

    public bool IsMissing
    {
        get => is_missing;
        set => SetProperty(ref is_missing, value);
    }

    /// <summary>
    /// グリッド表示用のソースパス。grid_thumb_path のみを返す。
    /// サムネイル未生成時は元画像パスにフォールバック（コンバータが DecodePixelWidth=260 でデコードする）。
    /// </summary>
    public string? EffectiveSourcePath
    {
        get
        {
            var path = !string.IsNullOrEmpty(grid_thumb_path) ? grid_thumb_path : photo_path;
            return string.IsNullOrEmpty(path) ? null : path.Replace('/', '\\');
        }
    }

    /// <summary>
    /// PhotoModal 用の表示パス。display_thumb_path → resolved_photo_path → photo_path の優先順。
    /// </summary>
    public string? EffectiveDisplayPath
    {
        get
        {
            var path = !string.IsNullOrEmpty(display_thumb_path) ? display_thumb_path
                     : !string.IsNullOrEmpty(resolved_photo_path) ? resolved_photo_path
                     : photo_path;
            return string.IsNullOrEmpty(path) ? null : path.Replace('/', '\\');
        }
    }

    /// <summary>DB の DTO から ViewModel インスタンスを生成する。</summary>
    public static PhotoThumbnailItem FromDto(PhotoRecordDto photo) => new()
    {
        photo_filename = photo.photo_filename,
        photo_path = photo.photo_path,
        resolved_photo_path = photo.resolved_photo_path,
        grid_thumb_path = photo.grid_thumb_path,
        display_thumb_path = photo.display_thumb_path,
        world_id = photo.world_id,
        world_name = photo.world_name,
        Timestamp = photo.timestamp,
        Phash = photo.phash,
        Orientation = photo.orientation,
        image_width = photo.image_width,
        image_height = photo.image_height,
        source_slot = photo.source_slot,
        is_favorite = photo.is_favorite,
        Tags = photo.tags,
        match_source = photo.match_source,
        is_missing = photo.is_missing,
    };

    /// <summary>ViewModel の現在値を DTO に変換する。</summary>
    public PhotoRecordDto ToDto() => new()
    {
        photo_filename = photo_filename,
        photo_path = photo_path,
        resolved_photo_path = resolved_photo_path,
        grid_thumb_path = grid_thumb_path,
        display_thumb_path = display_thumb_path,
        world_id = world_id,
        world_name = world_name,
        timestamp = Timestamp,
        phash = Phash,
        orientation = Orientation,
        image_width = image_width,
        image_height = image_height,
        source_slot = source_slot,
        is_favorite = is_favorite,
        tags = Tags,
        match_source = match_source,
        is_missing = is_missing,
    };
}
