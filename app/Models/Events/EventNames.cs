namespace Alpheratz.Models.Events;

/// <summary>
/// LocalEventBus で使われるイベント名の定数集約。
/// 文字列リテラルが publisher と subscriber に散らばっていると、片方をタイポしても
/// コンパイラが検出できず「イベントが発火されたが誰も受信しない」状態になるため、
/// ここに集めて参照させる。
///
/// 命名規則：
///   - スキャナ系: "scan:..." (コロン区切り、subaction を持つもの)
///   - その他: snake_case (Phash / Orientation 系)
/// 既存実装が混在しているのは歴史的経緯。新規追加は snake_case を推奨。
/// </summary>
public static class EventNames
{
    // ===== Scan (PhotoScanner) =====
    /// <summary>スキャン進捗。payload = ScanProgressDto。</summary>
    public const string ScanProgress = "scan:progress";
    /// <summary>スキャン完了 (写真メタデータの取込/更新が一段落)。payload = null。</summary>
    public const string ScanCompleted = "scan:completed";
    /// <summary>スキャンを継続できる警告。payload = string (利用者向けメッセージ)。</summary>
    public const string ScanWarning = "scan:warning";
    /// <summary>利用者または画面遷移による正常なスキャン中止。payload = string。</summary>
    public const string ScanCancelled = "scan:cancelled";
    /// <summary>
    /// スキャン後の archive 解決、orientation・PDQ 更新、フィルタ再読込が完了したとき。
    /// payload = null。
    /// </summary>
    public const string ScanEnrichCompleted = "scan:enrich_completed";
    /// <summary>スキャン中のエラー。payload = string (エラーメッセージ)。</summary>
    public const string ScanError = "scan:error";

    // ===== Phash (PhashService) =====
    /// <summary>PDQ ハッシュ計算の進捗。payload = PhashProgressEvent。</summary>
    public const string PhashProgress = "phash_progress";
    /// <summary>PDQ ハッシュ計算完了。payload = null。</summary>
    public const string PhashComplete = "phash_complete";
    /// <summary>PDQ ハッシュ計算エラー。payload = string (エラーメッセージ)。</summary>
    public const string PhashError = "phash_error";

    // ===== Orientation (OrientationService) =====
    /// <summary>orientation / 寸法補完の進捗。payload = OrientationProgressEvent。</summary>
    public const string OrientationProgress = "orientation_progress";
    /// <summary>orientation / 寸法補完の完了。payload = null。</summary>
    public const string OrientationComplete = "orientation_complete";
}
