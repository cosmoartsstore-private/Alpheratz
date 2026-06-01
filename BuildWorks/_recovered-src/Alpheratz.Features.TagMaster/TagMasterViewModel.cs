using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Alpheratz.Core;
using Alpheratz.Core.Database;
using Alpheratz.Shared.Models;
using Alpheratz.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using WinRT;
using WinRT.Alpheratz_FrontendVtableClasses;

namespace Alpheratz.Features.TagMaster;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.Data.INotifyPropertyChanged")]
[WinRTExposedType(typeof(Alpheratz_Features_Gallery_PhotoGridItemWinRTTypeDetails))]
public class TagMasterViewModel : UiThreadSafeObservableObject
{
	private const int MAX_TAG_LENGTH = 20;

	private readonly AlpheratzDb db;

	private readonly ToastService toastService;

	[ObservableProperty]
	private string tagDraft = string.Empty;

	public UiObservableCollection<string> masterTags { get; } = new UiObservableCollection<string>();

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string TagDraft
	{
		get
		{
			return tagDraft;
		}
		[MemberNotNull("tagDraft")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(tagDraft, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.TagDraft);
				tagDraft = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.TagDraft);
			}
		}
	}

	public TagMasterViewModel(AlpheratzDb db, ToastService toastService)
	{
		this.db = db;
		this.toastService = toastService;
	}

	public async Task loadTags()
	{
		try
		{
			IReadOnlyList<string> source = await db.GetAllTagsAsync();
			masterTags.Clear();
			foreach (string item in source.OrderBy((string tag) => tag))
			{
				masterTags.Add(item);
			}
		}
		catch (Exception value)
		{
			AppLogger.Error($"TagMasterViewModel.loadTags: threw: {value}");
			throw;
		}
	}

	public async Task createTag()
	{
		string text = TagDraft.Trim();
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		if (text.Length > 20)
		{
			toastService.addToast($"タグは{20}文字以内で入力してください。", ToastType.error);
			return;
		}
		try
		{
			await db.CreateTagMasterAsync(text);
			TagDraft = string.Empty;
			await loadTags();
			toastService.addToast("タグを追加しました。");
		}
		catch (Exception value)
		{
			AppLogger.Error($"TagMasterViewModel.createTag: threw: {value}");
			toastService.addToast($"タグの追加に失敗しました: {value}", ToastType.error);
		}
	}

	public async Task deleteTag(string tag)
	{
		_ = 1;
		try
		{
			await db.DeleteTagMasterAsync(tag);
			await loadTags();
			toastService.addToast("タグを削除しました。");
		}
		catch (Exception value)
		{
			AppLogger.Error($"TagMasterViewModel.deleteTag: threw: {value}");
			toastService.addToast($"タグの削除に失敗しました: {value}", ToastType.error);
		}
	}
}
