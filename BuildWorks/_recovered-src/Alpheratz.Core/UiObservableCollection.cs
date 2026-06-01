using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Alpheratz.Core;

public class UiObservableCollection<T> : ObservableCollection<T>
{
	public UiObservableCollection()
	{
	}

	public UiObservableCollection(IEnumerable<T> collection)
		: base(collection)
	{
	}

	public UiObservableCollection(List<T> list)
		: base(list)
	{
	}

	public void ReplaceAll(IEnumerable<T> items)
	{
		UiThread.Run(delegate
		{
			base.Items.Clear();
			foreach (T item in items)
			{
				base.Items.Add(item);
			}
			base.OnPropertyChanged(new PropertyChangedEventArgs("Count"));
			base.OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
			base.OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
		});
	}

	protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
	{
		UiThread.Run(delegate
		{
			base.OnCollectionChanged(e);
		});
	}

	protected override void OnPropertyChanged(PropertyChangedEventArgs e)
	{
		UiThread.Run(delegate
		{
			base.OnPropertyChanged(e);
		});
	}
}
