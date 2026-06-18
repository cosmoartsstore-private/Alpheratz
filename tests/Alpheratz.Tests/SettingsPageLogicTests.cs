using System.IO;
using Alpheratz.Features.Settings;
using Alpheratz.Features.Template;
using Windows.System;

namespace Alpheratz.Tests;

/// <summary>
/// SettingsPage の入力状態と表示ラベルの純粋ロジックを検証するテスト。
///
/// SettingsPage 本体は Page、TextBox、Border、TemplateList を操作するが、
/// 空状態、キー判定、テンプレート編集ラベル、カードのテーマキーは UI なしで決められる。
/// ここではそれらを SettingsPageLogic として固定し、画面側には実際の Visibility/Brush 反映だけを残す。
/// </summary>
public sealed class SettingsPageLogicTests
{
    /// <summary>
    /// タグ一覧の空状態が件数 0 のときだけ表示対象になることを確認する。
    /// タグが1件でもあれば空状態プレースホルダは隠す。
    /// </summary>
    [Fact]
    public void IsTagEmpty_ReturnsTrueOnlyForZeroCount()
    {
        Assert.True(SettingsPageLogic.IsTagEmpty(0));
        Assert.False(SettingsPageLogic.IsTagEmpty(1));
        Assert.False(SettingsPageLogic.IsTagEmpty(12));
    }

    /// <summary>
    /// 設定モーダルのキー操作が Escape で閉じ、タグ入力が Enter で追加になることを確認する。
    ///
    /// 両方とも Page 側では KeyRoutedEventArgs を受けるが、実際の判定はキー種別だけで決まる。
    /// </summary>
    [Fact]
    public void KeyHelpers_DistinguishModalCloseAndTagSubmit()
    {
        Assert.True(SettingsPageLogic.ShouldCloseModal(VirtualKey.Escape));
        Assert.False(SettingsPageLogic.ShouldCloseModal(VirtualKey.Enter));
        Assert.True(SettingsPageLogic.ShouldSubmitTag(VirtualKey.Enter));
        Assert.False(SettingsPageLogic.ShouldSubmitTag(VirtualKey.Escape));
    }

    /// <summary>
    /// テンプレート編集状態から、キャンセル表示、保存ボタン文言、モードラベルが決まることを確認する。
    ///
    /// 編集対象が null のときは新規登録モード、文字列が入っているときは既存テンプレート更新モードになる。
    /// </summary>
    [Fact]
    public void TemplateEditor_ReturnsLabelsForCreateAndEditModes()
    {
        Assert.Equal(new TemplateEditorDisplay(false, "登録", "新規テンプレート"),
            SettingsPageLogic.TemplateEditor(null));
        Assert.Equal(new TemplateEditorDisplay(true, "更新", "テンプレート編集"),
            SettingsPageLogic.TemplateEditor("hello world"));
    }

    /// <summary>
    /// テンプレートカードの active 状態から、表示ラベルとテーマリソースキーが選ばれることを確認する。
    ///
    /// 使用中カードは水色の solid 背景と白文字を使い、
    /// 通常カードは標準枠・薄い面色・通常文字色へ戻す。
    /// </summary>
    [Fact]
    public void TemplateCard_ReturnsThemeKeysForActiveAndRestCards()
    {
        Assert.Equal(
            new TemplateCardDisplay("使用中", "APrimary", "ASurface", "APrimary", "AText"),
            SettingsPageLogic.TemplateCard(true));
        Assert.Equal(
            new TemplateCardDisplay("テンプレート", "ABorder", "ASurface", "ATextDim", "AText"),
            SettingsPageLogic.TemplateCard(false));
    }

    /// <summary>単一テーマボタンが現在テーマ名と次の切替先を返すことを確認する。</summary>
    [Fact]
    public void ThemeButtonHelpers_ReturnCurrentLabelAndNextMode()
    {
        Assert.Equal("ライト", SettingsPageLogic.ThemeButtonText(isDark: false));
        Assert.Equal("ダーク", SettingsPageLogic.ThemeButtonText(isDark: true));
        Assert.Contains("ダーク", SettingsPageLogic.ThemeButtonTooltip(isDark: false));
        Assert.Contains("ライト", SettingsPageLogic.ThemeButtonTooltip(isDark: true));
        Assert.True(SettingsPageLogic.NextThemeIsDark(isDark: false));
        Assert.False(SettingsPageLogic.NextThemeIsDark(isDark: true));
    }

    /// <summary>ワールド分析が使用可能なときは進捗表示を隠す。</summary>
    [Fact]
    public void WorldAnalysisProgress_HidesWhenAnalysisCanStart()
    {
        Assert.Equal(
            WorldAnalysisProgressDisplay.Hidden,
            SettingsPageLogic.WorldAnalysisProgress(
                done: 10,
                total: 10,
                current: null,
                isRunning: false,
                canStartWorldAnalysis: true));
    }

    /// <summary>PDQ 総数が未確定の待機中は不定進行を表示する。</summary>
    [Fact]
    public void WorldAnalysisProgress_UsesIndeterminateStateBeforeTotalIsKnown()
    {
        var display = SettingsPageLogic.WorldAnalysisProgress(
            done: 0,
            total: 0,
            current: null,
            isRunning: true,
            canStartWorldAnalysis: false);

        Assert.True(display.ProgressVisible);
        Assert.True(display.IsIndeterminate);
        Assert.Equal(1, display.Maximum);
        Assert.Equal(0, display.Value);
        Assert.Contains("開始", display.SummaryText);
    }

    /// <summary>PDQ 総数がある場合は処理済み枚数、割合、現在ファイル名を表示する。</summary>
    [Fact]
    public void WorldAnalysisProgress_ReturnsDeterminateCountAndCurrentFile()
    {
        var display = SettingsPageLogic.WorldAnalysisProgress(
            done: 12,
            total: 30,
            current: "avatar.png",
            isRunning: true,
            canStartWorldAnalysis: false);

        Assert.True(display.ProgressVisible);
        Assert.False(display.IsIndeterminate);
        Assert.Equal(30, display.Maximum);
        Assert.Equal(12, display.Value);
        Assert.Equal("類似画像解析 12/30 枚 (40%)", display.SummaryText);
        Assert.Equal("処理中: avatar.png", display.DetailText);
    }

    /// <summary>外部遷移やサイドバーで扱う設定セクション ID を固定する。</summary>
    [Fact]
    public void IsKnownSection_AllowsOnlySettingsSections()
    {
        Assert.True(SettingsPageLogic.IsKnownSection("general"));
        Assert.True(SettingsPageLogic.IsKnownSection("tags"));
        Assert.True(SettingsPageLogic.IsKnownSection("templates"));
        Assert.True(SettingsPageLogic.IsKnownSection("credits"));
        Assert.False(SettingsPageLogic.IsKnownSection("unknown"));
        Assert.False(SettingsPageLogic.IsKnownSection(null));
    }

    /// <summary>
    /// 写真フォルダ操作で使う slot 番号が、UI 上の 1st/2nd と一致していることを確認する。
    ///
    /// ShellPage 側のハンドラは slot=1 をメイン、slot=2 をセカンダリとして処理する。
    /// code-behind に数値直書きを残すと呼び出し箇所ごとに意味が読みにくいため、定数として固定する。
    /// </summary>
    [Fact]
    public void FolderSlotConstants_MatchPrimaryAndSecondarySlots()
    {
        Assert.Equal(1, SettingsPageLogic.PrimaryFolderSlot);
        Assert.Equal(2, SettingsPageLogic.SecondaryFolderSlot);
    }

    /// <summary>
    /// StellaRecord が利用できる場合だけ登録 request を作り、icon.png の配置パスを返すことを確認する。
    ///
    /// ランチャー未導入の環境では registry 登録を試みず null を返す。
    /// 導入済みの場合は現在の exe パスと、アプリ配置ディレクトリ配下の Assets/icon.png を
    /// StellaRecordRegistration.Register へ渡す値としてまとめる。
    /// </summary>
    [Fact]
    public void StellaRecordRegistration_ReturnsRequestOnlyWhenAvailable()
    {
        Assert.Null(SettingsPageLogic.StellaRecordRegistration(false, "C:/app/Alpheratz.exe", "C:/app"));

        Assert.Equal(
            new StellaRecordRegistrationRequest(
                "C:/app/Alpheratz.exe",
                Path.Combine("C:/app", "Assets", "icon.png")),
            SettingsPageLogic.StellaRecordRegistration(true, "C:/app/Alpheratz.exe", "C:/app"));
        Assert.Null(SettingsPageLogic.StellaRecordRegistration(true, null, "C:/app"));
    }

    /// <summary>
    /// TemplatePageViewModel の変更通知から、SettingsPage が実行する UI 更新種別を返すことを確認する。
    ///
    /// 編集対象の変更では保存ボタンやキャンセルボタンを更新し、
    /// アクティブテンプレートの変更ではカードの active 表示だけを再描画する。
    /// それ以外のプロパティ通知では不要な VisualTree 走査を行わない。
    /// </summary>
    [Fact]
    public void TemplatePropertyAction_ReturnsEditorOrCardRefreshActions()
    {
        Assert.Equal(TemplatePropertyUpdate.UpdateEditor,
            SettingsPageLogic.TemplatePropertyAction(nameof(TemplatePageViewModel.EditingTweetTemplate)));
        Assert.Equal(TemplatePropertyUpdate.RefreshCards,
            SettingsPageLogic.TemplatePropertyAction(nameof(TemplatePageViewModel.ActiveTweetTemplate)));
        Assert.Equal(TemplatePropertyUpdate.None,
            SettingsPageLogic.TemplatePropertyAction(nameof(TemplatePageViewModel.TweetTemplateDraft)));
        Assert.Equal(TemplatePropertyUpdate.None,
            SettingsPageLogic.TemplatePropertyAction(null));
    }

    /// <summary>
    /// ItemsControl の DataContext から、タグ名やテンプレート本文として扱える string だけを取り出すことを確認する。
    ///
    /// 削除・編集・選択ボタンは sender の DataContext を通じて対象を受け取る。
    /// string 以外を拒否することで、誤ったテンプレート削除やタグ削除を callback へ渡さない。
    /// </summary>
    [Fact]
    public void StringItem_ReturnsOnlyStringDataContext()
    {
        Assert.Equal("template", SettingsPageLogic.StringItem("template"));
        Assert.Null(SettingsPageLogic.StringItem(123));
        Assert.Null(SettingsPageLogic.StringItem(null));
    }

    /// <summary>
    /// テンプレート削除 callback が未配線のときだけ、画面内コレクションから直接削除することを確認する。
    ///
    /// 通常は ShellPage から渡される OnDeleteTemplate が永続化を担当する。
    /// callback が無い fallback 経路では、存在するテンプレートだけを一時的にコレクションから除去する。
    /// </summary>
    [Fact]
    public void ShouldRemoveTemplateLocally_AllowsOnlyFallbackExistingTemplateDelete()
    {
        Assert.True(SettingsPageLogic.ShouldRemoveTemplateLocally(hasDeleteCallback: false, templateExists: true));
        Assert.False(SettingsPageLogic.ShouldRemoveTemplateLocally(hasDeleteCallback: true, templateExists: true));
        Assert.False(SettingsPageLogic.ShouldRemoveTemplateLocally(hasDeleteCallback: false, templateExists: false));
    }
}
