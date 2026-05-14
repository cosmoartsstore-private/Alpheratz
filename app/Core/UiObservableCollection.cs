using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Alpheratz.Core;

/// <summary>
/// UI スレッドセーフな ObservableCollection&lt;T&gt;。
/// 標準 ObservableCollection はバックグラウンドスレッドから Add/Remove を呼ぶと、
/// XAML がバインディング更新中に内部 List の中間状態を読んで InvalidOperationException や
/// IndexOutOfRangeException を投げる。本クラスは OnCollectionChanged /
/// OnPropertyChanged を <see cref="UiThread.Run"/> 経由で発火することでこれを防ぐ。
/// </summary>
public class UiObservableCollection<T> : ObservableCollection<T>
{
    public UiObservableCollection() { }
    public UiObservableCollection(IEnumerable<T> collection) : base(collection) { }
    public UiObservableCollection(List<T> list) : base(list) { }

    /// <summary>
    /// コレクションを一括差し替える。
    /// Items.Clear + foreach Add の組合せは複数の OnCollectionChanged を発火させ、
    /// UI が中間状態 (空リスト → 1 件 → 2 件 ...) を見てクラッシュする可能性がある。
    /// このメソッドは差し替えそのものを UI スレッドへマーシャリングし、最後に
    /// Reset イベント 1 回だけ発火させて 1 アトミックな更新にする。
    /// MasonryView では Reset を受けて全カード破棄→再構築する重い処理になるため、
    /// 増分追加用途には使わず loadMorePhotos 等は個別 Add で軽量パスを通すこと。
    /// </summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        UiThread.Run(() =>
        {
            Items.Clear();
            foreach (var item in items)
                Items.Add(item);
            base.OnPropertyChanged(new PropertyChangedEventArgs("Count"));
            base.OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            base.OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        });
    }

    /// <summary>CollectionChanged を UI スレッドにマーシャリングしてから発火する。</summary>
    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        UiThread.Run(() => base.OnCollectionChanged(e));
    }

    /// <summary>PropertyChanged を UI スレッドにマーシャリングしてから発火する。</summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        UiThread.Run(() => base.OnPropertyChanged(e));
    }
}
