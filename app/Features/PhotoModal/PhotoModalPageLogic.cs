using System.Collections.Generic;
using System.Linq;
using Alpheratz.Features.Gallery;
using Windows.System;

namespace Alpheratz.Features.PhotoModal;

/// <summary>
/// PhotoModalPage の表示文言、キー操作、タグ操作を UI 要素なしで決める補助ロジック。
/// Page 本体はここで決まった値を TextBlock、Image、callback に反映する。
/// </summary>
internal static class PhotoModalPageLogic
{
    public const int ModalDecodePixelWidth = 1920;
    public const long NavigationInitialIntervalMs = 80;
    public const long NavigationLongHoldThresholdMs = 900;
    public const long NavigationLongHoldIntervalMs = 180;
    public const long NavigationBurstResetMs = 450;
    public const string UnknownWorldName = "ワールド不明";
    public const string BottomActionHoverBrushKey = "ASurfaceHover";

    /// <summary>SelectedPhoto の変更だけを、モーダル表示全体の再同期対象として扱う。</summary>
    public static bool ShouldSyncForPropertyChanged(string? propertyName)
        => propertyName == nameof(PhotoModalState.SelectedPhoto);

    /// <summary>選択中 PhotoThumbnailItem の内部変更で、コードビハインド表示を再同期すべきもの。</summary>
    public static bool ShouldSyncForSelectedPhotoProperty(string? propertyName)
        => propertyName is nameof(PhotoThumbnailItem.Tags)
            or nameof(PhotoThumbnailItem.WorldName)
            or nameof(PhotoThumbnailItem.MatchSource)
            or nameof(PhotoThumbnailItem.EffectiveDisplayPath);

    /// <summary>PhotoModal 画像として表示するパスとデコード幅を返す。パスが空なら null。</summary>
    public static PhotoModalImageRequest? ModalImageRequest(string? effectiveDisplayPath, char directorySeparatorChar)
    {
        if (string.IsNullOrEmpty(effectiveDisplayPath)) return null;
        return new PhotoModalImageRequest(
            effectiveDisplayPath.Replace('/', directorySeparatorChar),
            ModalDecodePixelWidth);
    }

    /// <summary>写真のワールド名が空の場合に、モーダル用の不明表示へ置き換える。</summary>
    public static string WorldNameText(string? worldName)
        => string.IsNullOrEmpty(worldName) ? UnknownWorldName : worldName;

    /// <summary>match_source から補完元チップの表示可否とラベルを返す。</summary>
    public static MatchSourceDisplay MatchSource(string? source)
        => source switch
        {
            "polaris_archive" => new MatchSourceDisplay(true, "archive ログから補完"),
            "phash" => new MatchSourceDisplay(true, "類似写真から推測"),
            "phash_confirmed" => new MatchSourceDisplay(true, "類似写真から確認済み"),
            _ => new MatchSourceDisplay(false, null),
        };

    /// <summary>マスタタグのうち、現在の写真にまだ付いていないタグ数と追加欄の表示可否を返す。</summary>
    public static TagAddDisplay TagAddDisplay(IEnumerable<string>? masterTags, IReadOnlyCollection<string>? currentTags)
    {
        if (masterTags is null) return new TagAddDisplay(false, 0);
        var currentSet = currentTags?.ToHashSet(System.StringComparer.OrdinalIgnoreCase);
        var availableCount = currentSet is null
            ? masterTags.Count(tag => !string.IsNullOrWhiteSpace(tag))
            : masterTags.Count(tag => !string.IsNullOrWhiteSpace(tag) && !currentSet.Contains(tag));
        return new TagAddDisplay(availableCount > 0, availableCount);
    }

    /// <summary>テキスト入力へフォーカスがない場合に、モーダルが処理するキー操作を返す。</summary>
    public static PhotoModalPageKeyAction ResolveKeyAction(bool isTextInputFocused, VirtualKey key)
    {
        if (isTextInputFocused) return PhotoModalPageKeyAction.None;
        return key switch
        {
            VirtualKey.Escape => PhotoModalPageKeyAction.Close,
            VirtualKey.Left => PhotoModalPageKeyAction.GoPrevious,
            VirtualKey.Right => PhotoModalPageKeyAction.GoNext,
            VirtualKey.Back => PhotoModalPageKeyAction.GoBack,
            _ => PhotoModalPageKeyAction.None,
        };
    }

    /// <summary>矢印キー長押し中の連続ナビゲーションを間引くための判定を返す。</summary>
    public static PhotoModalNavigationGateDecision NavigationGate(
        long nowTicks,
        long lastAcceptedTicks,
        long burstStartedTicks,
        bool sameDirection)
    {
        var elapsedSinceLast = lastAcceptedTicks <= 0
            ? long.MaxValue
            : System.Math.Max(0, nowTicks - lastAcceptedTicks);
        var resetBurst = !sameDirection
            || burstStartedTicks <= 0
            || elapsedSinceLast >= NavigationBurstResetMs;
        var effectiveBurstStart = resetBurst ? nowTicks : burstStartedTicks;

        if (lastAcceptedTicks <= 0 || resetBurst)
        {
            return new PhotoModalNavigationGateDecision(true, effectiveBurstStart, nowTicks);
        }

        var burstDuration = System.Math.Max(0, nowTicks - effectiveBurstStart);
        var minInterval = burstDuration >= NavigationLongHoldThresholdMs
            ? NavigationLongHoldIntervalMs
            : NavigationInitialIntervalMs;
        var allowed = elapsedSinceLast >= minInterval;
        return new PhotoModalNavigationGateDecision(
            allowed,
            effectiveBurstStart,
            allowed ? nowTicks : lastAcceptedTicks);
    }

    /// <summary>既存タグコンボボックスからタグ追加 callback へ渡す request を作る。</summary>
    public static PhotoModalTagMutationRequest? AddExistingTagRequest(object? selectedItem, string? photoPath, bool canAddTag)
    {
        if (!canAddTag || selectedItem is not string tag || string.IsNullOrEmpty(tag) || photoPath is null)
            return null;
        return new PhotoModalTagMutationRequest(photoPath, tag);
    }

    /// <summary>タグ chip の Tag 値からタグ削除 callback へ渡す request を作る。</summary>
    public static PhotoModalTagMutationRequest? RemoveTagRequest(object? tagValue, string? photoPath, bool canRemoveTag)
    {
        if (!canRemoveTag || tagValue is not string tag || string.IsNullOrEmpty(tag) || photoPath is null)
            return null;
        return new PhotoModalTagMutationRequest(photoPath, tag);
    }
}

/// <summary>PhotoModal の表示画像に渡す正規化済みパスとデコード幅。</summary>
internal sealed record PhotoModalImageRequest(string NormalizedPath, int DecodePixelWidth);

/// <summary>矢印キー長押しナビゲーションの受理可否と次回判定用タイミング。</summary>
internal sealed record PhotoModalNavigationGateDecision(bool Allowed, long BurstStartedTicks, long LastAcceptedTicks);

/// <summary>写真モーダルの前後ナビゲーション方向。</summary>
internal enum PhotoModalNavigationDirection
{
    Previous,
    Next,
}

/// <summary>match_source 補完元チップの表示状態。</summary>
internal sealed record MatchSourceDisplay(bool Visible, string? Label);

/// <summary>タグ追加欄の表示状態と追加可能タグ数。</summary>
internal sealed record TagAddDisplay(bool HasAvailable, int AvailableCount);

/// <summary>PhotoModalPage が処理するキーボード操作。</summary>
internal enum PhotoModalPageKeyAction
{
    None,
    Close,
    GoPrevious,
    GoNext,
    GoBack,
}

/// <summary>PhotoModal のタグ追加・削除 callback に渡す値。</summary>
internal sealed record PhotoModalTagMutationRequest(string PhotoPath, string Tag);
