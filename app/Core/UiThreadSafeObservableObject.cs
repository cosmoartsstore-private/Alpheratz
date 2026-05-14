using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alpheratz.Core;

/// <summary>
/// CommunityToolkit.Mvvm の ObservableObject を UI スレッドセーフ化した基底クラス。
/// バックグラウンドスレッドからのプロパティ変更通知を <see cref="UiThread.Run"/> 経由で
/// 発火させることで、XAML バインディングが別スレッド更新を読んでクラッシュするのを防ぐ。
/// ViewModel / State クラスはすべてこれを継承するのがプロジェクト規約。
/// </summary>
public abstract class UiThreadSafeObservableObject : ObservableObject
{
    /// <summary>PropertyChanged を UI スレッドにマーシャリングしてから発火する。</summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        UiThread.Run(() => base.OnPropertyChanged(e));
    }

    /// <summary>PropertyChanging を UI スレッドにマーシャリングしてから発火する。</summary>
    protected override void OnPropertyChanging(PropertyChangingEventArgs e)
    {
        UiThread.Run(() => base.OnPropertyChanging(e));
    }
}
