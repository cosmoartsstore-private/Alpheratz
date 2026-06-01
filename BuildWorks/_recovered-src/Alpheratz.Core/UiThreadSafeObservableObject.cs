using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alpheratz.Core;

public abstract class UiThreadSafeObservableObject : ObservableObject
{
	protected override void OnPropertyChanged(PropertyChangedEventArgs e)
	{
		UiThread.Run(delegate
		{
			base.OnPropertyChanged(e);
		});
	}

	protected override void OnPropertyChanging(PropertyChangingEventArgs e)
	{
		UiThread.Run(delegate
		{
			base.OnPropertyChanging(e);
		});
	}
}
