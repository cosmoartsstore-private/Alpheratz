using Alpheratz.Messages;
using System.Text.RegularExpressions;

namespace Alpheratz.Tests;

/// <summary>properties ファイルを正本とする利用者向け文言の解決契約を検証する。</summary>
public sealed class MessageCatalogTests
{
    // catalogの誤変更を検知するため、期待文言と変数の値は独立literalとして保持する。
    private static readonly (string Key, (string Name, object? Value)[] Parameters, string Expected)[] ExpectedMessageLiterals =
    [
        ("common.add", [], "追加"),
        ("common.apply", [], "適用"),
        ("common.cancel", [], "キャンセル"),
        ("common.change", [], "変更"),
        ("common.clear", [], "クリア"),
        ("common.close", [], "閉じる"),
        ("common.copy", [], "コピー"),
        ("common.delete", [], "削除"),
        ("common.edit", [], "編集"),
        ("common.emptyMarker", [], "—"),
        ("common.favoriteAdd", [], "お気に入りに追加"),
        ("common.favoriteRemove", [], "お気に入りから削除"),
        ("common.register", [], "登録"),
        ("common.reset", [], "リセット"),
        ("common.unknownWorld", [], "ワールド不明"),
        ("MainWindow.title", [], "Alpheratz"),
        ("EmptyState.heading", [], "写真が見つかりません"),
        ("EmptyState.guidance", [], "写真フォルダの設定または検索条件を変更してください。"),
        ("BootstrapPage.copyright", [], "© CosmoArtsStore"),
        ("BootstrapPage.startupFailedTitle", [], "Alpheratzを起動できませんでした"),
        ("BootstrapPage.startupFailedMessage", [], "必要なデータを準備できませんでした。アプリを終了して、もう一度起動してください。繰り返す場合は販売ページからお問い合わせください。"),
        ("BootstrapPage.exit", [], "アプリを終了"),
        ("ScanningOverlay.heading", [], "写真を読み込んでいます…"),
        ("ScanningOverlay.cancel", [], "読み込みを中止"),
        ("ScanningOverlay.progress", [], "写真の読み込み進捗"),
        ("ShellHeaderBar.filterButton", [], "検索条件"),
        ("ShellHeaderBar.filterButtonAutomation", [], "検索条件を開閉"),
        ("ShellHeaderBar.filterButtonHelp", [], "写真の検索条件を開閉します。"),
        ("ShellHeaderBar.searchPlaceholder", [], "ワールド名を検索…"),
        ("ShellHeaderBar.searchAutomation", [], "ワールド名を検索"),
        ("ShellHeaderBar.viewModeAutomation", [], "写真の表示方法を切り替え"),
        ("ShellHeaderBar.viewModeTooltip", [], "グリッド表示とギャラリー表示を切り替えます。ギャラリーではCtrl+ホイール、W/S、↑/↓で写真サイズを変更できます。"),
        ("ShellHeaderBar.galleryModeLabel", [], "ギャラリーモード"),
        ("ShellHeaderBar.viewModeToGrid", [], "グリッド表示に切り替え"),
        ("ShellHeaderBar.viewModeToGallery", [], "ギャラリー表示に切り替え"),
        ("ShellHeaderBar.groupingAutomation", [], "ワールドごとのグループ表示を切り替え"),
        ("ShellHeaderBar.groupingTooltip", [], "写真をワールドごとにまとめる表示を切り替えます。"),
        ("ShellHeaderBar.groupPhotosLabel", [], "写真をまとめる"),
        ("ShellHeaderBar.groupingEnable", [], "ワールドごとにまとめて表示"),
        ("ShellHeaderBar.groupingDisable", [], "ワールドごとのグループ表示を解除"),
        ("ShellHeaderBar.groupingUnavailable", [], "ギャラリー表示ではワールドごとのグループ表示を利用できません"),
        ("ShellHeaderBar.multiSelectAutomation", [], "複数選択を切り替え"),
        ("ShellHeaderBar.multiSelectTooltip", [], "複数の写真を選択するモードを切り替えます。"),
        ("ShellHeaderBar.multiSelectLabel", [], "複数選択"),
        ("ShellHeaderBar.multiSelectEndLabel", [], "選択を終了"),
        ("ShellHeaderBar.multiSelectStart", [], "写真の複数選択を開始"),
        ("ShellHeaderBar.multiSelectEnd", [], "写真の複数選択を終了"),
        ("ShellHeaderBar.settings", [], "設定を開く"),
        ("ShellHeaderBar.settingsLabel", [], "設定"),
        ("ShellHeaderBar.worldPhotoCount", [("count", "【値:count】日本語\n\\\"={原文}")], "【値:count】日本語\n\\\"={原文}枚"),
        ("ShellPage.folderChangeTitle", [], "写真フォルダの変更"),
        ("ShellPage.folderChangeMessage", [("folder", "【値:folder】日本語\n\\\"={原文}"), ("path", "【値:path】日本語\n\\\"={原文}")], "【値:folder】日本語\n\\\"={原文}を「【値:path】日本語\n\\\"={原文}」へ変更します。【値:folder】日本語\n\\\"={原文}から読み込んだ写真の情報、お気に入り、写真に付けたタグ、解析結果は削除され、変更後のフォルダから読み込み直されます。写真ファイルとタグマスタは削除されません。"),
        ("ShellPage.folderResetTitle", [], "写真フォルダのリセット"),
        ("ShellPage.folderResetMessage", [("folder", "【値:folder】日本語\n\\\"={原文}")], "【値:folder】日本語\n\\\"={原文}の設定を解除します。【値:folder】日本語\n\\\"={原文}から読み込んだ写真の情報、お気に入り、写真に付けたタグ、解析結果は削除されます。写真ファイルとタグマスタは削除されません。"),
        ("ShellPage.primaryPhotoFolder", [], "写真フォルダ1"),
        ("ShellPage.secondaryPhotoFolder", [], "写真フォルダ2"),
        ("ShellPage.worldInfoApplied", [], "ワールド情報を適用しました。"),
        ("ShellViewModel.folderCleanupPending", [], "写真フォルダの変更処理が完了していないため、写真の読み込みを保留しています。アプリを再起動すると再試行します。"),
        ("ShellViewModel.scanCancelFailed", [], "写真の読み込みを中止できませんでした。読み込みが続いている場合は、中止をもう一度選択してください。"),
        ("ShellViewModel.tagRefreshFailed", [], "タグは削除されましたが、写真一覧を更新できませんでした。アプリを再起動してください。"),
        ("ShellViewModel.phashComplete", [], "類似画像の解析が完了しました。"),
        ("ShellViewModel.folderCleanupFailed", [], "写真フォルダの設定は保存されましたが、以前の写真情報を整理できませんでした。次回の起動時に再試行します。"),
        ("ShellViewModel.folderCleanupComplete", [], "以前の写真情報を整理しました。"),
        ("ShellViewModel.folderUpdated", [], "写真フォルダを変更しました。"),
        ("ShellViewModel.folderMissing", [], "写真フォルダが見つかりません。設定からフォルダを選び直してください。"),
        ("ShellViewModel.foldersOverlap", [], "写真フォルダ1と写真フォルダ2には、同じフォルダや親子関係のフォルダを設定できません。"),
        ("ShellViewModel.folderUpdateFailed", [], "写真フォルダを変更できませんでした。設定で写真フォルダを選び直してください。"),
        ("ShellViewModel.folderReset", [("slot", "【値:slot】日本語\n\\\"={原文}")], "写真フォルダ【値:slot】日本語\n\\\"={原文}をリセットしました。"),
        ("ShellViewModel.folderResetFailed", [], "写真フォルダをリセットできませんでした。設定でリセットをもう一度実行してください。"),
        ("ShellViewModel.viewModeUpdateFailed", [], "写真の表示方法を変更できませんでした。表示方法をもう一度切り替えてください。"),
        ("ShellViewModel.themeUpdateFailed", [], "テーマを変更できませんでした。テーマをもう一度切り替えてください。"),
        ("ShellViewModel.worldLinkSettingUpdateFailed", [], "投稿時のワールドリンク設定を変更できませんでした。設定をもう一度切り替えてください。"),
        ("ShellViewModel.startupEnabled", [], "Alpheratzをログイン時に起動するよう設定しました。"),
        ("ShellViewModel.startupDisabled", [], "Alpheratzのログイン時起動を無効にしました。"),
        ("ShellViewModel.startupUpdateFailed", [], "ログイン時起動の設定を変更できませんでした。設定をもう一度切り替えてください。"),
        ("ShellViewModel.photoUserDataBackupCreated", [("fileCount", "【値:fileCount】日本語\n\\\"={原文}"), ("photoCount", "【値:photoCount】日本語\n\\\"={原文}")], "写真【値:photoCount】日本語\n\\\"={原文}枚を、ファイル名【値:fileCount】日本語\n\\\"={原文}件としてバックアップしました。"),
        ("ShellViewModel.photoUserDataBackupNoPhotos", [], "バックアップできる写真がありません。以前のバックアップは変更していません。"),
        ("ShellViewModel.photoUserDataBackupFailed", [], "お気に入りとタグをバックアップできませんでした。もう一度実行してください。"),
        ("ShellViewModel.photoUserDataBackupMissing", [], "復元に使えるバックアップがありません。先にバックアップを作成してください。"),
        ("ShellViewModel.photoUserDataRestoreNoMatches", [], "バックアップとファイル名が一致する写真はありませんでした。"),
        ("ShellViewModel.photoUserDataRestored", [("photoCount", "【値:photoCount】日本語\n\\\"={原文}"), ("tagCount", "【値:tagCount】日本語\n\\\"={原文}")], "写真【値:photoCount】日本語\n\\\"={原文}枚へお気に入りとタグを復元しました。タグマスタへ【値:tagCount】日本語\n\\\"={原文}件を追加しました。"),
        ("ShellViewModel.photoUserDataRestoreRefreshFailed", [], "復元は完了しましたが、画面を更新できませんでした。アプリを再起動すると反映されます。"),
        ("ShellViewModel.photoUserDataRestoreFailed", [], "お気に入りとタグを復元できませんでした。もう一度実行してください。"),
        ("PhotoScanner.cancelled", [], "写真の読み込みを中止しました。"),
        ("PhotoScanner.failed", [], "写真の読み込みに失敗しました。設定した写真フォルダを確認してください。フォルダを変更すると、写真を読み込み直します。"),
        ("PhotoScanner.primaryFolderMissing", [], "写真フォルダ1が見つかりません。設定からフォルダを選び直してください。"),
        ("PhotoScanner.secondaryFolderMissing", [], "写真フォルダ2が見つかりません。設定からフォルダを選び直してください。"),
        ("PhotoScanner.folderUnconfigured", [], "写真フォルダが設定されていません。設定からフォルダを選択してください。"),
        ("PhotoScanner.foldersOverlap", [], "写真フォルダ1と写真フォルダ2が重複しています。設定から別々のフォルダを選択してください。"),
        ("PhotoScanner.collecting", [], "写真を確認しています…"),
        ("PhotoScanner.updatesFound", [("count", "【値:count】日本語\n\\\"={原文}")], "更新が必要な写真を【値:count】日本語\n\\\"={原文}件確認しました。"),
        ("PhotoScanner.incompleteFolders", [("folders", "【値:folders】日本語\n\\\"={原文}")], "【値:folders】日本語\n\\\"={原文}を完全に読み取れませんでした。フォルダへのアクセス権を確認してください。見つからない写真の整理は次回の読み込みまで延期します。"),
        ("PhotoScanner.sourceSlot", [("slot", "【値:slot】日本語\n\\\"={原文}")], "写真フォルダ【値:slot】日本語\n\\\"={原文}"),
        ("PhashService.cancelled", [], "類似画像の解析を中断しました。"),
        ("PhashService.failed", [], "類似画像の解析に失敗しました。アプリを再起動すると、未解析の写真をもう一度解析します。"),
        ("WorldService.browserLaunchFailed", [], "既定のブラウザを起動できません。"),
        ("PhotoGridItemsView.imageLoadFailed", [], "写真を表示できません"),
        ("PhotoService.operationCancelled", [], "操作をキャンセルしました。"),
        ("PhotoService.operationFailed", [], "操作を完了できませんでした。"),
        ("PhotoModalPage.previousPhotoAutomation", [], "前の写真"),
        ("PhotoModalPage.nextPhotoAutomation", [], "次の写真"),
        ("PhotoModalPage.closeHelp", [], "写真詳細を閉じる（Esc）"),
        ("PhotoModalPage.tagsHeading", [], "タグ"),
        ("PhotoModalPage.noAssignedTags", [], "タグはありません"),
        ("PhotoModalPage.removeTagHelp", [], "この写真からタグを削除"),
        ("PhotoModalPage.addTags", [], "タグを追加"),
        ("PhotoModalPage.noAvailableTags", [], "追加できるタグはありません。"),
        ("PhotoModalPage.openTagMaster", [], "タグマスタを開く"),
        ("PhotoModalPage.favoriteToggleAutomation", [], "お気に入りを切り替え"),
        ("PhotoModalPage.favoriteToggleHelp", [], "お気に入りに追加またはお気に入りから削除します。"),
        ("PhotoModalPage.favoriteLabel", [], "お気に入り"),
        ("PhotoModalPage.postAutomation", [], "投稿画面を開く"),
        ("PhotoModalPage.postHelp", [], "画像をコピーして投稿画面を開きます。"),
        ("PhotoModalPage.postUnavailableHelp", [], "投稿テンプレートが選択されていないため、投稿できません。設定の「投稿テンプレート」でテンプレートを選択してください。"),
        ("PhotoModalPage.postLabel", [], "投稿"),
        ("PhotoModalPage.openWorldHelp", [], "VRChatのワールドページを開く"),
        ("PhotoModalPage.worldLabel", [], "ワールド"),
        ("PhotoModalPage.openExplorerAutomation", [], "エクスプローラーで写真を表示"),
        ("PhotoModalPage.openExplorerHelp", [], "写真ファイルの場所をエクスプローラーで開きます。"),
        ("PhotoModalPage.openExplorerLabel", [], "表示"),
        ("PhotoModalPage.clipboardCopied", [], "画像をクリップボードにコピーしました"),
        ("PhotoModalPage.tagDialogTitle", [], "タグを追加"),
        ("PhotoModalPage.tagDialogHelp", [], "この写真へ追加するタグを選択します。"),
        ("PhotoModalPage.cancelTagAdditionAutomation", [], "タグの追加をキャンセル"),
        ("PhotoModalPage.confirmTagAdditionAutomation", [], "選択したタグを追加"),
        ("PhotoModalPage.worldLinkUnavailableLabel", [], "リンクなし"),
        ("PhotoModalPage.worldLinkUnavailableHelp", [], "ワールドページの情報がないため開けません"),
        ("PhotoModalPage.archiveMatchSource", [], "過去の記録から補完"),
        ("PhotoModalPage.similarPhotoMatchSource", [], "類似写真から推測"),
        ("PhotoModalPage.confirmedSimilarPhotoMatchSource", [], "類似写真から確認済み"),
        ("PhotoModalPage.tagChoiceSelectAutomation", [("tag", "【値:tag】日本語\n\\\"={原文}")], "タグ「【値:tag】日本語\n\\\"={原文}」を選択"),
        ("PhotoModalPage.tagChoiceDeselectAutomation", [("tag", "【値:tag】日本語\n\\\"={原文}")], "タグ「【値:tag】日本語\n\\\"={原文}」の選択を解除"),
        ("PhotoModalPage.tagChoiceHelp", [], "追加するタグを選択または解除します。"),
        ("PhotoModalViewModel.worldPageOpenFailed", [], "VRChatのワールドページを開けませんでした。既定のブラウザを確認してください。"),
        ("PhotoModalViewModel.explorerOpenFailed", [], "写真をエクスプローラーで表示できませんでした。写真ファイルが移動または削除されていないか確認してください。"),
        ("PhotoModalState.tagLoadFailed", [], "写真のタグを読み込めませんでした。写真詳細を閉じて、写真を開き直してください。"),
        ("GalleryDisplayState.groupedPhotoLabel", [], "ワールド"),
        ("GalleryFilterPanel.allDates", [], "すべての期間"),
        ("GalleryFilterPanel.allTags", [], "すべてのタグ"),
        ("GalleryFilterPanel.allWorlds", [], "すべてのワールド"),
        ("GalleryFilterPanel.applyDateRange", [], "期間を適用"),
        ("GalleryFilterPanel.calendarDay", [("day", "【値:day】日本語\n\\\"={原文}")], "【値:day】日本語\n\\\"={原文}"),
        ("GalleryFilterPanel.calendarDayAutomationName", [("day", "【値:day】日本語\n\\\"={原文}"), ("month", "【値:month】日本語\n\\\"={原文}"), ("year", "【値:year】日本語\n\\\"={原文}")], "【値:year】日本語\n\\\"={原文}年【値:month】日本語\n\\\"={原文}月【値:day】日本語\n\\\"={原文}日"),
        ("GalleryFilterPanel.calendarMonth", [("month", "【値:month】日本語\n\\\"={原文}"), ("year", "【値:year】日本語\n\\\"={原文}")], "【値:year】日本語\n\\\"={原文}年【値:month】日本語\n\\\"={原文}月"),
        ("GalleryFilterPanel.clearDateFilter", [], "撮影日の絞り込みを解除"),
        ("GalleryFilterPanel.dateFrom", [("from", "【値:from】日本語\n\\\"={原文}")], "【値:from】日本語\n\\\"={原文}以降"),
        ("GalleryFilterPanel.dateHeading", [], "撮影日"),
        ("GalleryFilterPanel.dateRange", [("from", "【値:from】日本語\n\\\"={原文}"), ("to", "【値:to】日本語\n\\\"={原文}")], "【値:from】日本語\n\\\"={原文}～【値:to】日本語\n\\\"={原文}"),
        ("GalleryFilterPanel.dateRangeGuidance", [], "開始日と終了日を選択してください。"),
        ("GalleryFilterPanel.dateTo", [("to", "【値:to】日本語\n\\\"={原文}")], "【値:to】日本語\n\\\"={原文}以前"),
        ("GalleryFilterPanel.endDate", [], "終了日"),
        ("GalleryFilterPanel.favoriteHeading", [], "お気に入り"),
        ("GalleryFilterPanel.favoritesOnly", [], "お気に入りのみ"),
        ("GalleryFilterPanel.filteredCount", [("count", "【値:count】日本語\n\\\"={原文}")], "【値:count】日本語\n\\\"={原文}枚の写真が該当"),
        ("GalleryFilterPanel.heading", [], "検索条件"),
        ("GalleryFilterPanel.nextMonth", [], "次の月を表示"),
        ("GalleryFilterPanel.orientationAll", [], "すべて"),
        ("GalleryFilterPanel.orientationHeading", [], "向き"),
        ("GalleryFilterPanel.orientationLandscape", [], "横長"),
        ("GalleryFilterPanel.orientationPortrait", [], "縦長"),
        ("GalleryFilterPanel.photoCount", [("count", "【値:count】日本語\n\\\"={原文}")], "【値:count】日本語\n\\\"={原文}枚"),
        ("GalleryFilterPanel.presetLast7Days", [], "直近7日"),
        ("GalleryFilterPanel.presetLastMonth", [], "先月"),
        ("GalleryFilterPanel.presetLastSixMonths", [], "過去6か月"),
        ("GalleryFilterPanel.presetLastYear", [], "過去1年"),
        ("GalleryFilterPanel.presetThisMonth", [], "今月"),
        ("GalleryFilterPanel.presetToday", [], "今日"),
        ("GalleryFilterPanel.previousMonth", [], "前の月を表示"),
        ("GalleryFilterPanel.selectedFilterCount", [("count", "【値:count】日本語\n\\\"={原文}")], "【値:count】日本語\n\\\"={原文}件を選択中"),
        ("GalleryFilterPanel.sortHeading", [], "表示順"),
        ("GalleryFilterPanel.sortNewest", [], "新しい順"),
        ("GalleryFilterPanel.sortWorldName", [], "ワールド名順"),
        ("GalleryFilterPanel.startDate", [], "開始日"),
        ("GalleryFilterPanel.tagHeading", [], "タグ"),
        ("GalleryFilterPanel.tagOptionCount", [("count", "【値:count】日本語\n\\\"={原文}")], "登録タグ：【値:count】日本語\n\\\"={原文}件"),
        ("GalleryFilterPanel.tagSearchPlaceholder", [], "タグ名で絞り込む…"),
        ("GalleryFilterPanel.visitedWorldsHeading", [], "訪問済みワールド"),
        ("GalleryFilterPanel.weekdayFriday", [], "金"),
        ("GalleryFilterPanel.weekdayMonday", [], "月"),
        ("GalleryFilterPanel.weekdaySaturday", [], "土"),
        ("GalleryFilterPanel.weekdaySunday", [], "日"),
        ("GalleryFilterPanel.weekdayThursday", [], "木"),
        ("GalleryFilterPanel.weekdayTuesday", [], "火"),
        ("GalleryFilterPanel.weekdayWednesday", [], "水"),
        ("GalleryFilterPanel.worldHeading", [], "ワールド"),
        ("GalleryFilterPanel.worldOptionCount", [("count", "【値:count】日本語\n\\\"={原文}")], "ワールド：【値:count】日本語\n\\\"={原文}件"),
        ("GalleryFilterPanel.worldSearchPlaceholder", [], "ワールド名で絞り込む…"),
        ("GalleryGridStage.loadingPhotos", [], "写真を読み込んでいます…"),
        ("GalleryMonthGroup.monthLabel", [("month", "【値:month】日本語\n\\\"={原文}")], "【値:month】日本語\n\\\"={原文}月"),
        ("GalleryPage.addTags", [], "タグを追加"),
        ("GalleryPage.bulkTagChoiceAddAutomationName", [("tag", "【値:tag】日本語\n\\\"={原文}")], "タグ「【値:tag】日本語\n\\\"={原文}」を追加対象に選択"),
        ("GalleryPage.bulkTagChoiceRemoveAutomationName", [("tag", "【値:tag】日本語\n\\\"={原文}")], "タグ「【値:tag】日本語\n\\\"={原文}」を追加対象から外す"),
        ("GalleryPage.bulkTagDialogTitle", [], "選択した写真にタグを追加"),
        ("GalleryPage.bulkTagSummary", [("photoCount", "【値:photoCount】日本語\n\\\"={原文}"), ("tagCount", "【値:tagCount】日本語\n\\\"={原文}")], "【値:photoCount】日本語\n\\\"={原文}枚の写真に【値:tagCount】日本語\n\\\"={原文}件のタグを追加します。"),
        ("GalleryPage.clearSelection", [], "選択を解除"),
        ("GalleryPage.copyToAnotherFolder", [], "別フォルダへコピー"),
        ("GalleryPage.noBulkTags", [], "追加できるタグがありません。タグマスタでタグを登録してください。"),
        ("GalleryPage.selectedPhotoCount", [("count", "【値:count】日本語\n\\\"={原文}")], "【値:count】日本語\n\\\"={原文}枚の写真を選択しています。"),
        ("GalleryPage.selectedTags", [("tags", "【値:tags】日本語\n\\\"={原文}")], "追加するタグ：【値:tags】日本語\n\\\"={原文}"),
        ("GalleryPage.selectionLabel", [("count", "【値:count】日本語\n\\\"={原文}")], "【値:count】日本語\n\\\"={原文}枚を選択中"),
        ("GalleryPage.selectTagsPrompt", [], "追加するタグを選択してください。"),
        ("GalleryPhotosState.photoLoadFailed", [], "写真一覧を読み込めませんでした。"),
        ("GallerySelectionState.selectedPhotoInfoLoadFailed", [], "選択した写真の情報を取得できないため、一括操作を実行できません。"),
        ("GalleryViewModel.additionalPhotoCount", [("count", "【値:count】日本語\n\\\"={原文}")], "ほか【値:count】日本語\n\\\"={原文}枚"),
        ("GalleryViewModel.bulkFavoriteAdded", [("count", "【値:count】日本語\n\\\"={原文}")], "【値:count】日本語\n\\\"={原文}枚をお気に入りに追加しました。"),
        ("GalleryViewModel.bulkFavoriteRemoved", [("count", "【値:count】日本語\n\\\"={原文}")], "【値:count】日本語\n\\\"={原文}枚をお気に入りから削除しました。"),
        ("GalleryViewModel.bulkSingleTagAdded", [("photoCount", "【値:photoCount】日本語\n\\\"={原文}"), ("tag", "【値:tag】日本語\n\\\"={原文}")], "タグ「【値:tag】日本語\n\\\"={原文}」を【値:photoCount】日本語\n\\\"={原文}枚の写真に追加しました。"),
        ("GalleryViewModel.bulkTagsAdded", [("photoCount", "【値:photoCount】日本語\n\\\"={原文}"), ("tagCount", "【値:tagCount】日本語\n\\\"={原文}")], "【値:tagCount】日本語\n\\\"={原文}件のタグを【値:photoCount】日本語\n\\\"={原文}枚の写真に追加しました。"),
        ("GalleryViewModel.bulkUpdateCanceled", [("operation", "【値:operation】日本語\n\\\"={原文}")], "【値:operation】日本語\n\\\"={原文}を中断しました。"),
        ("GalleryViewModel.bulkUpdateCompleted", [("count", "【値:count】日本語\n\\\"={原文}")], "【値:count】日本語\n\\\"={原文}枚は更新済みです。"),
        ("GalleryViewModel.bulkUpdateFailed", [("operation", "【値:operation】日本語\n\\\"={原文}")], "【値:operation】日本語\n\\\"={原文}に失敗した写真があります。"),
        ("GalleryViewModel.bulkUpdateFailureSummary", [("completed", "【値:completed】日本語\n\\\"={原文}"), ("failedCount", "【値:failedCount】日本語\n\\\"={原文}"), ("names", "【値:names】日本語\n\\\"={原文}"), ("status", "【値:status】日本語\n\\\"={原文}")], "【値:status】日本語\n\\\"={原文}【値:completed】日本語\n\\\"={原文}未完了の【値:failedCount】日本語\n\\\"={原文}枚を選択したままにしています：【値:names】日本語\n\\\"={原文}。同じ操作を実行できます。"),
        ("GalleryViewModel.bulkUpdateFailureSelectionChanged", [("completed", "1枚は更新済みです。"), ("failedCount", 2), ("names", "写真A.jpg、写真B.jpg"), ("status", "タグの追加に失敗した写真があります。")], "タグの追加に失敗した写真があります。1枚は更新済みです。未完了の2枚：写真A.jpg、写真B.jpg。現在の選択は維持しています。再試行する場合は、未完了の写真を選択してください。"),
        ("GalleryViewModel.favoriteOperationLabel", [], "お気に入りの更新"),
        ("GalleryViewModel.favoriteUpdateFailed", [], "お気に入りを更新できませんでした。お気に入りをもう一度変更してください。"),
        ("GalleryViewModel.photoCopyFailed", [], "写真をコピーできませんでした。コピー元とコピー先のフォルダを確認してください。"),
        ("GalleryViewModel.photosCopied", [("count", "【値:count】日本語\n\\\"={原文}")], "【値:count】日本語\n\\\"={原文}枚の写真をコピーしました。"),
        ("GalleryViewModel.photosCopiedWithFailures", [("copied", "【値:copied】日本語\n\\\"={原文}"), ("failed", "【値:failed】日本語\n\\\"={原文}")], "【値:copied】日本語\n\\\"={原文}枚をコピーし、【値:failed】日本語\n\\\"={原文}枚はコピーできませんでした。"),
        ("GalleryViewModel.tagAdded", [], "写真にタグを追加しました。"),
        ("GalleryViewModel.tagAddFailed", [], "写真にタグを追加できませんでした。タグをもう一度追加してください。"),
        ("GalleryViewModel.tagOperationLabel", [], "タグの追加"),
        ("GalleryViewModel.tagRemoved", [], "写真からタグを削除しました。"),
        ("GalleryViewModel.tagRemoveFailed", [], "写真からタグを削除できませんでした。タグをもう一度削除してください。"),
        ("GalleryViewModel.tagsAdded", [("count", "【値:count】日本語\n\\\"={原文}")], "写真に【値:count】日本語\n\\\"={原文}件のタグを追加しました。"),
        ("GalleryViewModel.tagTooLong", [("maxLength", "【値:maxLength】日本語\n\\\"={原文}")], "タグは【値:maxLength】日本語\n\\\"={原文}文字以内で入力してください。"),
        ("GalleryViewModel.unknownPhotoName", [], "ファイル名不明"),
        ("GalleryViewModel.unknownWorldAnalysisStarted", [], "ワールド不明の写真の分析を開始しました。"),
        ("GalleryViewModel.unknownWorldAnalysisStartFailed", [], "ワールド不明の写真の分析を開始できませんでした。しばらくしてから分析をもう一度開始してください。"),
        ("GalleryViewModel.worldInfoApplied", [], "ワールド情報を反映しました。"),
        ("GalleryViewModel.worldInfoApplyFailed", [], "ワールド情報を反映できませんでした。ワールド情報をもう一度反映してください。"),
        ("GroupDrillDownPage.closeAutomationName", [], "グループ表示を閉じる"),
        ("GroupDrillDownPage.closeTooltip", [], "グループ表示を閉じる（Esc）"),
        ("GroupDrillDownPage.title", [("count", "【値:count】日本語\n\\\"={原文}"), ("groupName", "【値:groupName】日本語\n\\\"={原文}")], "【値:groupName】日本語\n\\\"={原文}（【値:count】日本語\n\\\"={原文}枚）"),
        ("MonthNav.jumpToMonth", [("month", "【値:month】日本語\n\\\"={原文}"), ("year", "【値:year】日本語\n\\\"={原文}")], "【値:year】日本語\n\\\"={原文}年【値:month】日本語\n\\\"={原文}月へ移動"),
        ("MonthNav.monthLabel", [("month", "【値:month】日本語\n\\\"={原文}")], "【値:month】日本語\n\\\"={原文}月"),
        ("MonthNav.yearLabel", [("year", "【値:year】日本語\n\\\"={原文}")], "【値:year】日本語\n\\\"={原文}年"),
        ("PhotoGridItem.groupCount", [("count", "【値:count】日本語\n\\\"={原文}")], "【値:count】日本語\n\\\"={原文}枚"),
        ("MatchPercentConverter.value", [("percent", "【値:percent】日本語\n\\\"={原文}")], "一致率：【値:percent】日本語\n\\\"={原文}%"),
        ("MatchPercentConverter.empty", [], "一致率：—"),
        ("AlpheratzSetting.defaultTweetTemplateGreeting", [], "おは{world-name}\n\n#{タグを追加}"),
        ("AlpheratzSetting.defaultTweetTemplateWorld", [], "World: {world-name}\nAuthor:\n\n#VRChat_world紹介"),
        ("AlpheratzSetting.defaultTweetTemplatePhotography", [], "World: {world-name}\nAuthor:\nCloth:\n\n#VRChatPhotography"),
        ("SettingsPage.title", [], "設定"),
        ("SettingsPage.closeAutomationName", [], "設定を閉じる"),
        ("SettingsPage.closeTooltip", [], "設定を閉じる（Esc）"),
        ("SettingsPage.generalTitle", [], "全般"),
        ("SettingsPage.similarPhotosTitle", [], "類似の写真"),
        ("SettingsPage.tagMasterTitle", [], "タグマスタ"),
        ("SettingsPage.templateTitle", [], "投稿テンプレート"),
        ("SettingsPage.creditsTitle", [], "クレジット"),
        ("SettingsPage.primaryPhotoFolderLabel", [], "写真フォルダ1"),
        ("SettingsPage.secondaryPhotoFolderLabel", [], "写真フォルダ2"),
        ("SettingsPage.changePrimaryPhotoFolderAutomationName", [], "写真フォルダ1を変更"),
        ("SettingsPage.resetPrimaryPhotoFolderAutomationName", [], "写真フォルダ1をリセット"),
        ("SettingsPage.changeSecondaryPhotoFolderAutomationName", [], "写真フォルダ2を変更"),
        ("SettingsPage.resetSecondaryPhotoFolderAutomationName", [], "写真フォルダ2をリセット"),
        ("SettingsPage.startupLabel", [], "ログイン時に起動"),
        ("SettingsPage.startupDescription", [], "Windowsへのログイン時にAlpheratzを自動で起動します。"),
        ("SettingsPage.openWorldLinkLabel", [], "投稿時にワールドページを開く"),
        ("SettingsPage.openWorldLinkDescription", [], "投稿時、選択した写真にワールド情報があればVRChatのワールドページも開きます。"),
        ("SettingsPage.themeLabel", [], "テーマ"),
        ("SettingsPage.themeDescription", [], "アプリの表示をライトまたはダークに切り替えます。"),
        ("SettingsPage.stellaRecordIntegrationLabel", [], "StellaRecord連携"),
        ("SettingsPage.stellaRecordIntegrationDescription", [], "StellaRecordのランチャーにAlpheratzを登録します。"),
        ("SettingsPage.worldNameInferenceLabel", [], "ワールド名補完"),
        ("SettingsPage.worldNameInferenceDescription", [], "写真の類似分析結果をもとに、ワールド名が不明な写真の補完候補を表示します。"),
        ("SettingsPage.startWorldNameInferenceButton", [], "補完を開始"),
        ("SettingsPage.photoUserDataBackupLabel", [], "お気に入り・タグのバックアップ"),
        ("SettingsPage.photoUserDataBackupDescription", [], "現在読み込んでいるすべての写真から、ファイル名、お気に入り、タグを保存します。同じファイル名の写真は1件にまとめます。"),
        ("SettingsPage.photoUserDataRestoreDescription", [], "復元すると、同じファイル名の写真へお気に入りを戻し、保存済みのタグを追加します。現在付いているタグは削除しません。"),
        ("SettingsPage.createPhotoUserDataBackupButton", [], "バックアップを更新"),
        ("SettingsPage.restorePhotoUserDataBackupButton", [], "バックアップから復元"),
        ("SettingsPage.photoUserDataBackupWorking", [], "お気に入りとタグを処理しています…"),
        ("SettingsPage.photoUserDataBackupConfirmTitle", [], "お気に入りとタグのバックアップ"),
        ("SettingsPage.photoUserDataBackupConfirmMessage", [], "現在読み込んでいるすべての写真からバックアップを作成します。以前のバックアップは置き換わります。"),
        ("SettingsPage.photoUserDataBackupConfirmButton", [], "バックアップを更新"),
        ("SettingsPage.photoUserDataRestoreConfirmTitle", [], "お気に入りとタグの復元"),
        ("SettingsPage.photoUserDataRestoreConfirmMessage", [], "バックアップとファイル名が一致するすべての写真へ反映します。お気に入りは保存時の状態へ戻り、保存済みのタグは現在のタグを残したまま追加されます。"),
        ("SettingsPage.photoUserDataRestoreConfirmButton", [], "復元"),
        ("SettingsPage.duplicatePhotosHeading", [], "重複写真の整理"),
        ("SettingsPage.duplicatePhotosDescription", [], "既存の類似分析結果から候補を絞り込み、ファイル内容が完全に一致する写真だけを検出します。"),
        ("SettingsPage.duplicatePhotosDeletionNotice", [], "削除した写真は、保存場所に応じてごみ箱へ移動するか、完全に削除されます。"),
        ("SettingsPage.duplicatePhotosInitialGuidance", [], "「重複写真を検出」を押すと、現在読み込まれている写真の分析結果を利用して比較します。"),
        ("SettingsPage.duplicatePhotosWorking", [], "重複写真を処理しています…"),
        ("SettingsPage.duplicatePhotosSelectionGuidance", [], "各グループで、削除する写真を選択してください。少なくとも1枚は残ります。"),
        ("SettingsPage.duplicatePhotosRetainedLabel", [], "この写真を残します"),
        ("SettingsPage.deleteSelectedDuplicatePhotosButton", [], "選択した写真を削除"),
        ("SettingsPage.deleteDuplicatePhotosConfirmTitle", [], "重複写真の削除"),
        ("SettingsPage.deleteDuplicatePhotosConfirmMessage", [("count", "【値:count】日本語\n\\\"={原文}"), ("size", "【値:size】日本語\n\\\"={原文}")], "選択した【値:count】日本語\n\\\"={原文}枚（【値:size】日本語\n\\\"={原文}）を削除します。各グループには同じ内容の写真を1枚以上残します。削除した写真は、保存場所に応じてごみ箱へ移動するか、完全に削除されます。"),
        ("SettingsPage.addTagHeading", [], "タグを追加"),
        ("SettingsPage.tagNamePlaceholder", [], "タグ名"),
        ("SettingsPage.tagLengthLimitReached", [("maxLength", "【値:maxLength】日本語\n\\\"={原文}")], "タグ名は【値:maxLength】日本語\n\\\"={原文}文字が上限です。これ以上は入力できません。"),
        ("SettingsPage.savedTagsHeading", [], "登録済みタグ"),
        ("SettingsPage.noSavedTags", [], "登録済みのタグはありません。"),
        ("SettingsPage.templatePlaceholder", [], "例：\nWorld: {world-name}\n\n#VRChat_world紹介"),
        ("SettingsPage.worldNameToken", [], "{world-name}"),
        ("SettingsPage.quickTagsLabel", [], "クイックタグ"),
        ("SettingsPage.worldNameQuickTag", [], "ワールド名"),
        ("SettingsPage.templateLengthCount", [("count", "【値:count】日本語\n\\\"={原文}"), ("max", "【値:max】日本語\n\\\"={原文}")], "X換算：【値:count】日本語\n\\\"={原文}/【値:max】日本語\n\\\"={原文}"),
        ("SettingsPage.templateLengthLimitReached", [("max", "【値:max】日本語\n\\\"={原文}")], "Xの標準上限は【値:max】日本語\n\\\"={原文}文字です。これ以上は入力できません。"),
        ("SettingsPage.templatePlaceholderMayOverflow", [("remaining", "【値:remaining】日本語\n\\\"={原文}")], "残り【値:remaining】日本語\n\\\"={原文}文字です。写真の情報を入れた後に上限を超えると、投稿準備を中止します。"),
        ("SettingsPage.savedTemplatesHeading", [], "登録済みテンプレート"),
        ("SettingsPage.creatorBrand", [], "CosmoArtsStore"),
        ("SettingsPage.creatorName", [], "制作者：ぷらねっと / planet"),
        ("SettingsPage.litLinkLabel", [], "Lit.Link"),
        ("SettingsPage.xProfileLabel", [], "X / Twitter"),
        ("SettingsPage.boothLabel", [], "BOOTH"),
        ("SettingsPage.materialsHeading", [], "使用素材"),
        ("SettingsPage.uiIconsLabel", [], "UIアイコン"),
        ("SettingsPage.bootstrapIconsCredit", [], "Bootstrap Icons / MIT License / Copyright (c) 2019-2024 The Bootstrap Authors"),
        ("SettingsPageLogic.templateUpdateButton", [], "更新"),
        ("SettingsPageLogic.templateEditHeading", [], "テンプレートを編集"),
        ("SettingsPageLogic.templateNewHeading", [], "テンプレートを追加"),
        ("SettingsPageLogic.templateActiveLabel", [], "使用中"),
        ("SettingsPageLogic.templateInactiveLabel", [], "テンプレート"),
        ("SettingsPageLogic.themeDark", [], "ダーク"),
        ("SettingsPageLogic.themeLight", [], "ライト"),
        ("SettingsPageLogic.switchToLightTheme", [], "ライトテーマに切り替える"),
        ("SettingsPageLogic.switchToDarkTheme", [], "ダークテーマに切り替える"),
        ("SettingsPageLogic.comparisonPreparationStarting", [], "写真の比較準備を開始しています…"),
        ("SettingsPageLogic.comparisonPreparationChecking", [], "写真の比較準備を確認しています…"),
        ("SettingsPageLogic.comparisonPreparationGuidance", [], "準備が完了すると、ワールド名を補完できます。"),
        ("SettingsPageLogic.comparisonPreparationCurrent", [("current", "【値:current】日本語\n\\\"={原文}")], "処理中：【値:current】日本語\n\\\"={原文}"),
        ("SettingsPageLogic.comparisonPreparationRunning", [], "写真を分析しています…"),
        ("SettingsPageLogic.comparisonPreparationWaitingToStart", [], "写真の分析開始を待っています。"),
        ("SettingsPageLogic.comparisonPreparationWaitingToFinish", [], "写真の分析完了を待っています。"),
        ("SettingsPageLogic.comparisonPreparationProgress", [("done", "【値:done】日本語\n\\\"={原文}"), ("percent", "【値:percent】日本語\n\\\"={原文}"), ("total", "【値:total】日本語\n\\\"={原文}")], "写真の比較準備：【値:done】日本語\n\\\"={原文}/【値:total】日本語\n\\\"={原文}枚（【値:percent】日本語\n\\\"={原文}%）"),
        ("DuplicatePhotosViewModel.unknownDate", [], "撮影日時不明"),
        ("DuplicatePhotosViewModel.photoDetails", [("size", "【値:size】日本語\n\\\"={原文}"), ("source", "【値:source】日本語\n\\\"={原文}"), ("timestamp", "【値:timestamp】日本語\n\\\"={原文}")], "【値:timestamp】日本語\n\\\"={原文}・【値:size】日本語\n\\\"={原文}・【値:source】日本語\n\\\"={原文}"),
        ("DuplicatePhotosViewModel.selectForDeletionAutomationName", [("filename", "【値:filename】日本語\n\\\"={原文}"), ("folder", "【値:folder】日本語\n\\\"={原文}")], "【値:filename】日本語\n\\\"={原文}（【値:folder】日本語\n\\\"={原文}）を削除対象にする"),
        ("DuplicatePhotosViewModel.groupSummary", [("count", "【値:count】日本語\n\\\"={原文}"), ("size", "【値:size】日本語\n\\\"={原文}")], "同じ内容の写真：【値:count】日本語\n\\\"={原文}枚（1枚 【値:size】日本語\n\\\"={原文}）"),
        ("DuplicatePhotosViewModel.detectButton", [], "重複写真を検出"),
        ("DuplicatePhotosViewModel.detectAgainButton", [], "もう一度検出"),
        ("DuplicatePhotosViewModel.selectPhotosGuidance", [], "削除する写真を選択してください。"),
        ("DuplicatePhotosViewModel.selectionSummary", [("count", "【値:count】日本語\n\\\"={原文}"), ("size", "【値:size】日本語\n\\\"={原文}")], "【値:count】日本語\n\\\"={原文}枚（【値:size】日本語\n\\\"={原文}）を削除対象に選択しています。"),
        ("DuplicatePhotosViewModel.preparingDetection", [], "写真の類似分析結果を確認しています…"),
        ("DuplicatePhotosViewModel.inspectingFiles", [("processed", "【値:processed】日本語\n\\\"={原文}"), ("total", "【値:total】日本語\n\\\"={原文}")], "写真を確認しています… 【値:processed】日本語\n\\\"={原文}/【値:total】日本語\n\\\"={原文}枚"),
        ("DuplicatePhotosViewModel.comparingContents", [("processed", "【値:processed】日本語\n\\\"={原文}"), ("total", "【値:total】日本語\n\\\"={原文}")], "候補のファイル内容を比較しています… 【値:processed】日本語\n\\\"={原文}/【値:total】日本語\n\\\"={原文}枚"),
        ("DuplicatePhotosViewModel.detectionCanceled", [], "重複写真の検出を中止しました。"),
        ("DuplicatePhotosViewModel.detectionFailed", [], "重複写真を検出できませんでした。写真フォルダを開けることを確認してから、もう一度検出してください。"),
        ("DuplicatePhotosViewModel.noPhotos", [], "比較できる写真がありません。写真フォルダを設定し、写真の読み込みが完了してから検出してください。"),
        ("DuplicatePhotosViewModel.noDuplicates", [], "ファイル内容が完全に一致する写真はありません。"),
        ("DuplicatePhotosViewModel.noDuplicatesWithUnreadable", [("count", "【値:count】日本語\n\\\"={原文}")], "ファイル内容が完全に一致する写真はありません。【値:count】日本語\n\\\"={原文}枚は読み取れなかったため比較していません。"),
        ("DuplicatePhotosViewModel.unreadablePhotos", [("count", "【値:count】日本語\n\\\"={原文}")], "【値:count】日本語\n\\\"={原文}枚は読み取れなかったため、検出結果に含まれていません。"),
        ("DuplicatePhotosViewModel.detectionSummary", [("deletableCount", "【値:deletableCount】日本語\n\\\"={原文}"), ("groupCount", "【値:groupCount】日本語\n\\\"={原文}"), ("photoCount", "【値:photoCount】日本語\n\\\"={原文}"), ("size", "【値:size】日本語\n\\\"={原文}")], "【値:groupCount】日本語\n\\\"={原文}組・【値:photoCount】日本語\n\\\"={原文}枚の重複写真が見つかりました。最大【値:deletableCount】日本語\n\\\"={原文}枚（【値:size】日本語\n\\\"={原文}）を削除できます。"),
        ("DuplicatePhotosViewModel.deletingPhotos", [("count", "【値:count】日本語\n\\\"={原文}")], "【値:count】日本語\n\\\"={原文}枚の写真を削除しています…"),
        ("DuplicatePhotosViewModel.deletionCanceled", [], "重複写真の削除を中止しました。"),
        ("DuplicatePhotosViewModel.deletionFailed", [], "重複写真の削除処理を完了できませんでした。選択内容は画面に残っています。"),
        ("DuplicatePhotosViewModel.galleryRefreshFailed", [], "写真は削除されましたが、ギャラリーを更新できませんでした。アプリを再起動すると反映されます。"),
        ("DuplicatePhotosViewModel.deletedDatabaseUpdateFailed", [("count", "【値:count】日本語\n\\\"={原文}")], "【値:count】日本語\n\\\"={原文}枚の元ファイルは削除しましたが、管理情報を更新できませんでした。次回の写真スキャンで管理情報が整理されます。"),
        ("DuplicatePhotosViewModel.partiallyDeleted", [("deleted", "【値:deleted】日本語\n\\\"={原文}"), ("failed", "【値:failed】日本語\n\\\"={原文}")], "【値:deleted】日本語\n\\\"={原文}枚を削除しました。【値:failed】日本語\n\\\"={原文}枚は現在の写真一覧から外れた、内容が変わった、使用中、または削除権限がないため削除していません。"),
        ("DuplicatePhotosViewModel.noneDeleted", [("count", "【値:count】日本語\n\\\"={原文}")], "【値:count】日本語\n\\\"={原文}枚は現在の写真一覧から外れた、内容が変わった、使用中、または削除権限がないため削除していません。"),
        ("DuplicatePhotosViewModel.deleted", [("count", "【値:count】日本語\n\\\"={原文}")], "【値:count】日本語\n\\\"={原文}枚の写真を削除しました。"),
        ("StellaRecordRegistration.appDescription", [], "VRChat写真のギャラリー表示とワールドページの閲覧を支援するアプリ"),
        ("TagMasterViewModel.tagTooLong", [("maxLength", "【値:maxLength】日本語\n\\\"={原文}")], "タグは【値:maxLength】日本語\n\\\"={原文}文字以内で入力してください。"),
        ("TagMasterViewModel.tagNameRequired", [], "タグ名を入力してください。"),
        ("TagMasterViewModel.tagAlreadyExists", [], "同じタグが登録されています。"),
        ("TagMasterViewModel.tagSaved", [], "タグを保存しました。"),
        ("TagMasterViewModel.tagAddFailed", [], "タグを追加できませんでした。入力内容は保持されています。"),
        ("TagMasterViewModel.tagDeleted", [], "タグと写真への設定を削除しました。"),
        ("TagMasterViewModel.tagDeleteFailed", [], "タグを削除できませんでした。写真への設定は変更されていません。"),
        ("TemplatePageViewModel.postScreenOpenFailed", [], "画像はクリップボードにコピーしましたが、投稿画面を開けませんでした。既定のブラウザを確認してください。"),
        ("TemplatePageViewModel.postPreparationFailed", [], "投稿の準備を完了できませんでした。写真ファイルと既定のブラウザを確認してください。"),
        ("TemplatePageViewModel.worldPageOpenFailed", [], "投稿画面は開きましたが、VRChatのワールドページを開けませんでした。既定のブラウザを確認してください。"),
        ("TemplatePageViewModel.activeTemplateRequired", [], "投稿テンプレートが選択されていません。設定の「投稿テンプレート」で選択してください。"),
        ("TemplatePageViewModel.postTextBuildFailed", [], "投稿文を作成できませんでした。テンプレートを確認してください。"),
        ("TemplatePageViewModel.postTextTooLong", [("count", "【値:count】日本語\n\\\"={原文}"), ("max", "【値:max】日本語\n\\\"={原文}")], "写真の情報を入れた投稿文が上限を超えています（【値:count】日本語\n\\\"={原文}/【値:max】日本語\n\\\"={原文}）。タグまたはテンプレートを短くしてください。"),
        ("TemplatePageViewModel.templateRequired", [], "テンプレートを入力してください。"),
        ("TemplatePageViewModel.templateAlreadyExists", [], "同じテンプレートが登録されています。"),
        ("TemplatePageViewModel.templateTooLong", [("count", "【値:count】日本語\n\\\"={原文}"), ("max", "【値:max】日本語\n\\\"={原文}")], "Xの標準上限を超えています（【値:count】日本語\n\\\"={原文}/【値:max】日本語\n\\\"={原文}）。内容を短くしてください。"),
        ("TemplatePageViewModel.templateSaved", [], "テンプレートを保存しました。"),
        ("TemplatePageViewModel.templateSaveFailed", [], "テンプレートを保存できませんでした。変更内容は画面に残っています。もう一度保存してください。"),
        ("TemplatePageViewModel.templateDeleteFailed", [], "テンプレートを削除できませんでした。保存済みの内容と編集中の入力は変更されていません。もう一度削除してください。"),
        ("TemplatePageViewModel.templateSelectionFailed", [], "使用するテンプレートを変更できませんでした。使用中のテンプレートは変更されていません。もう一度選択してください。"),
        ("WorldResolvePage.title", [], "ワールド名補完"),
        ("WorldResolvePage.applyAllButton", [], "すべて適用"),
        ("WorldResolvePage.skipAllButton", [], "すべて適用しない"),
        ("WorldResolvePage.chooseAnotherCandidate", [], "候補を選ぶ／手動入力"),
        ("WorldResolvePage.emptyState", [], "ワールド名を補完できる写真はありません"),
        ("WorldResolvePage.applyCount", [("count", "【値:count】日本語\n\\\"={原文}")], "適用予定：【値:count】日本語\n\\\"={原文}件"),
        ("WorldResolvePage.applyConfirmed", [("count", "【値:count】日本語\n\\\"={原文}")], "選択した【値:count】日本語\n\\\"={原文}件を適用"),
        ("WorldResolvePage.searchingCandidates", [], "候補を検索しています…"),
        ("WorldResolvePage.checkingTargetPhotos", [], "対象の写真を確認しています…"),
        ("WorldResolvePage.searchingCandidatesStatus", [("progress", "【値:progress】日本語\n\\\"={原文}")], "候補を検索しています…（【値:progress】日本語\n\\\"={原文}）"),
        ("WorldResolvePage.unresolvedPhotoCount", [("count", "【値:count】日本語\n\\\"={原文}")], "ワールド名不明の写真：【値:count】日本語\n\\\"={原文}件"),
        ("WorldResolvePage.backButton", [], "戻る"),
        ("WorldResolvePage.candidateGuidance", [], "一致率71%以上の候補を、高い順に表示しています。"),
        ("WorldResolvePage.manualCompletionHeading", [], "手動で補完"),
        ("WorldResolvePage.manualCompletionDescription", [], "候補にない場合は、ワールド名を入力して使用できます。"),
        ("WorldResolvePage.manualWorldNamePlaceholder", [], "ワールド名を入力"),
        ("WorldResolvePage.useManualWorldName", [], "この名前を使う"),
        ("WorldResolvePage.noAutomaticCandidates", [], "一致率71%以上の候補がありません"),
        ("WorldResolvePage.manualCompletionRequired", [], "一致率70%以下は自動補完できません。上の入力欄から手動で補完してください。"),
        ("WorldResolvePage.previewPhotoHelp", [], "写真を大きく表示します。"),
        ("WorldResolvePage.previewTargetPhotoAutomation", [], "補完対象の写真をプレビュー"),
        ("WorldResolvePage.closePreviewAutomation", [], "写真プレビューを閉じる"),
        ("WorldResolveViewModel.checkingTargetPhotos", [], "対象の写真を確認しています…"),
        ("WorldResolveViewModel.searchProgress", [("processed", "【値:processed】日本語\n\\\"={原文}"), ("total", "【値:total】日本語\n\\\"={原文}")], "【値:processed】日本語\n\\\"={原文}/【値:total】日本語\n\\\"={原文}件"),
        ("WorldResolveViewModel.initializationFailed", [], "ワールド名補完を開始できませんでした。画面を閉じてから、もう一度実行してください。"),
        ("WorldResolveViewModel.applyFailed", [], "ワールド名の保存を完了できませんでした。一部が保存されている場合があります。もう一度適用してください。"),
        ("WorldResolveViewModel.candidateLoadFailed", [], "候補写真を読み込めませんでした。もう一度候補を開いてください。"),
        ("AppConfig.settingDirectoryUnavailable", [], "設定ファイルの保存先を取得できません"),
        ("AppConfig.startupUpdateAndRestoreFailed", [], "自動起動設定の更新と以前の登録への復元に失敗しました"),
        ("AppConfig.startupRegistryUnavailable", [], "Windows の自動起動設定を開けません"),
        ("AppConfig.launcherMissing", [], "自動起動へ登録する Alpheratz launcher が見つかりません"),
        ("AppConfig.startupRegistryRestoreFailed", [], "Windows の自動起動設定を復元できません"),
        ("AlpheratzDb.pathUnavailable", [], "Alpheratz DB の保存先を取得できません"),
        ("common.emptyTagName", [], "タグ名が空です"),
        ("common.invalidSourceSlot", [], "source_slot は 1 または 2 である必要があります"),
        ("PdqImageReader.pooledBufferOwnershipRequired", [], "プール配列は所有権を切り離せません。"),
        ("ThumbnailService.cacheDirectoryUnavailable", [], "imgCache フォルダを取得できません"),
        ("ThumbnailService.incompleteJpeg", [], "生成したサムネイルが完全な JPEG ではありません"),
        ("ThumbnailService.lockReferenceLimitReached", [], "サムネイル生成ロックの参照数が上限に達しました"),
        ("SettingsPage.undefinedSection", [], "未定義の設定セクションです。"),
        ("ShellViewModel.corruptedFolderCleanupRequest", [], "写真フォルダ整理要求が破損しています"),
        ("common.overlappingPhotoFolders", [], "1st と 2nd の写真フォルダには、同じフォルダや親子関係のフォルダを設定できません"),
        ("SettingsService.folderCleanupPending", [], "前回の写真フォルダ整理が完了していません"),
        ("SettingsService.photoFolderChanged", [], "写真フォルダ設定が確認後に変更されました。設定画面を開き直してください"),
        ("SettingsService.folderCleanupRequestChanged", [], "別の写真フォルダ整理要求へ更新されています"),
        ("WorldService.invalidWorldId", [], "VRChat ワールドIDの形式が不正です。"),
        ("WorldService.invalidTweetIntentUrl", [], "Tweet intent URL の形式が不正です。"),
        ("WorldService.emptyPhotoPath", [], "Explorer で表示する写真パスが空です。"),
        ("WorldService.windowsDirectoryUnavailable", [], "Windows ディレクトリを特定できないため Explorer を起動できません。"),
        ("DispatcherService.queueUnavailable", [], "DispatcherQueue が利用できません"),
        ("common.photoNotFound", [("path", "【値:path】日本語\n\\\"={原文}")], "写真が見つかりません: 【値:path】日本語\n\\\"={原文}"),
        ("ShellViewModel.photoDirectoryMissing", [("path", "【値:path】日本語\n\\\"={原文}")], "写真フォルダが見つかりません: 【値:path】日本語\n\\\"={原文}"),
        ("PhotoService.destinationDirectoryMissing", [("path", "【値:path】日本語\n\\\"={原文}")], "コピー先フォルダが見つかりません: 【値:path】日本語\n\\\"={原文}"),
        ("Launcher.runtimeMissing", [("path", "【値:path】日本語\n\\\"={原文}")], "Alpheratz runtime executable was not found.\n\n【値:path】日本語\n\\\"={原文}"),
        ("Launcher.startFailedWithLog", [], "Alpheratzを起動できませんでした。詳細は Data\\logs\\launcher_error.log を確認してください。"),
        ("Launcher.startFailedWithoutLog", [], "Alpheratzを起動できませんでした。エラー情報も保存できませんでした。"),
    ];

    private static readonly Regex MessageDefinitionPattern = new(
        @"^(?<key>[A-Za-z][A-Za-z0-9]*(?:\.[A-Za-z][A-Za-z0-9]*)+)\s*[=:]",
        RegexOptions.CultureInvariant);

    private static readonly Regex LiteralMessageCallPattern = new(
        "\\bgetMsg\\s*\\(\\s*['\"](?<key>[A-Za-z][A-Za-z0-9]*(?:\\.[A-Za-z][A-Za-z0-9]*)+)['\"]",
        RegexOptions.CultureInvariant);

    /// <summary>実際に埋め込まれた全キーを固定した契約と照合し、追加・欠落・重複を検知する。</summary>
    [Fact]
    public void EmbeddedCatalogKeysMatchIndependentLiteralContract()
    {
        using var stream = typeof(MessageCatalog).Assembly.GetManifestResourceStream("Alpheratz.Messages.messages.ja.properties");
        Assert.NotNull(stream); using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        var keys = reader.ReadToEnd().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => MessageDefinitionPattern.Match(line.TrimStart())).Where(match => match.Success).Select(match => match.Groups["key"].Value).ToArray();
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(ExpectedMessageLiterals.Select(row => row.Key).Order(StringComparer.Ordinal), keys.Order(StringComparer.Ordinal));
    }

    /// <summary>全文言と変数展開を独立literalで照合し、値内の改行・引用符・波括弧を再展開しない。</summary>
    [Fact]
    public void EntireCatalogResolvesToIndependentLiteralMessages()
    {
        foreach (var row in ExpectedMessageLiterals) Assert.Equal(row.Expected, MessageCatalog.getMsg(row.Key, row.Parameters));
    }

    [Fact]
    public void GetMsg_ReturnsKnownMessage()
    {
        Assert.Equal("キャンセル", MessageCatalog.getMsg("common.cancel"));
    }

    [Fact]
    public void GetMsg_ReplacesNamedVariables()
    {
        var message = MessageCatalog.getMsg(
            "WorldResolveViewModel.searchProgress",
            ("processed", 2),
            ("total", 5));

        Assert.Equal("2/5件", message);
    }

    [Fact]
    public void GetMsg_PreservesDoubleBracesAsLiteralBraces()
    {
        Assert.Equal("{world-name}", MessageCatalog.getMsg("SettingsPage.worldNameToken"));
    }

    [Fact]
    public void GetMsg_ThrowsWhenKeyIsMissing()
    {
        Assert.Throws<KeyNotFoundException>(() => MessageCatalog.getMsg("Missing.messageKey"));
    }

    [Fact]
    public void GetMsg_ThrowsWhenRequiredVariableIsMissing()
    {
        Assert.Throws<ArgumentException>(() =>
            MessageCatalog.getMsg("PhotoGridItem.groupCount"));
    }

    [Fact]
    public void GetMsg_ThrowsWhenVariableIsSpecifiedMoreThanOnce()
    {
        Assert.Throws<ArgumentException>(() => MessageCatalog.getMsg(
            "PhotoGridItem.groupCount",
            ("count", 1),
            ("count", 2)));
    }

    /// <summary>ソース内で直接指定した文言キーが properties にすべて定義されていることを確認する。</summary>
    [Fact]
    public void LiteralGetMsgKeys_AreDefinedInJapaneseProperties()
    {
        var root = FindRepositoryRoot();
        var appRoot = Path.Combine(root, "app");
        var propertiesPath = Path.Combine(appRoot, "Messages", "messages.ja.properties");
        var definedKeys = File.ReadLines(propertiesPath)
            .Select(line => MessageDefinitionPattern.Match(line.TrimStart()))
            .Where(match => match.Success)
            .Select(match => match.Groups["key"].Value)
            .ToHashSet(StringComparer.Ordinal);
        var usedKeys = Directory.EnumerateFiles(appRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".cs" or ".xaml")
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => LiteralMessageCallPattern.Matches(File.ReadAllText(path)).Select(match => match.Groups["key"].Value))
            .ToHashSet(StringComparer.Ordinal);
        var undefinedKeys = usedKeys.Except(definedKeys, StringComparer.Ordinal).Order().ToArray();

        Assert.True(
            undefinedKeys.Length == 0,
            $"properties に未定義の文言キーがあります: {string.Join(", ", undefinedKeys)}");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "app", "Alpheratz.Frontend.csproj")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Alpheratz.Frontend.csproj を含むリポジトリルートが見つかりません。");
    }
}
