using Alpheratz.Core;
using Alpheratz.Features.TagMaster;
using Alpheratz.Features.Template;

namespace Alpheratz.Features.Settings;

/// <summary>
/// 設定モーダルの DataContext。3 つの ViewModel (一般設定 / タグマスタ / テンプレート) を
/// ネストプロパティとして公開し、SettingsPage の XAML から
/// <c>{Binding Settings.PhotoFolderPath}</c> のような階層バインディングで参照させる。
///
/// なぜ複合 VM にするか：
/// 設定モーダルは「3 セクションを 1 画面で同時表示する」ため、DataContext を 1 つしか
/// 持てない単一 Page に対して 3 種類の VM を同居させる必要がある。各セクションごとに
/// ContentControl の DataContext を切る方法もあるが、ネスト VM の方が XAML がシンプルで、
/// 1 つの Page インスタンスで完結する利点がある。
/// </summary>
public sealed class SettingsCompositeViewModel : UiThreadSafeObservableObject
{
    public SettingsViewModel Settings { get; }
    public TagMasterViewModel TagMaster { get; }
    public TemplatePageViewModel Template { get; }

    public SettingsCompositeViewModel(
        SettingsViewModel settings,
        TagMasterViewModel tagMaster,
        TemplatePageViewModel template)
    {
        Settings = settings;
        TagMaster = tagMaster;
        Template = template;
    }
}
