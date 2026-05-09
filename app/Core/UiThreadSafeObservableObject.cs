using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alpheratz.Core;

public abstract class UiThreadSafeObservableObject : ObservableObject
{
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        UiThread.Run(() => base.OnPropertyChanged(e));
    }

    protected override void OnPropertyChanging(PropertyChangingEventArgs e)
    {
        UiThread.Run(() => base.OnPropertyChanging(e));
    }
}
