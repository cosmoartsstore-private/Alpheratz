using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Alpheratz.Core;

/// <summary>
/// UI スレッドセーフな ObservableCollection。
/// OnCollectionChanged / OnPropertyChanged を UiThread.Run 経由で発火することで、
/// バックグラウンドスレッドからの Add/Remove でも UI バインディングがクラッシュしない。
/// </summary>
public class UiObservableCollection<T> : ObservableCollection<T>
{
    public UiObservableCollection() { }
    public UiObservableCollection(IEnumerable<T> collection) : base(collection) { }
    public UiObservableCollection(List<T> list) : base(list) { }

    /// <summary>
    /// コレクションを一括差し替える。個別 Add/Remove と異なり Reset イベント1回のみ発火する。
    /// MasonryView では Reset を受けて全カード破棄→再構築するため、
    /// 逐次ロード (loadMorePhotos) では個別 Add を使い RebuildLayout の軽量パスを通す。
    /// </summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        // Items.Clear / Items.Add は ObservableCollection<T> 内部の List<T> を直接いじるため、
        // バックグラウンドスレッドから呼ぶとバインドされた UI が CollectionChanged 発火前に
        // 中間状態を読んでクラッシュする。差し替え自体を UI スレッドへマーシャリングして
        // Reset イベント発火までを 1 アトミックに行う。
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

    /// <summary>UI スレッドにマーシャリングしてからイベントを発火する。</summary>
    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        UiThread.Run(() => base.OnCollectionChanged(e));
    }

    /// <summary>UI スレッドにマーシャリングしてからイベントを発火する。</summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        UiThread.Run(() => base.OnPropertyChanged(e));
    }
}