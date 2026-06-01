using Alpheratz.Core;
using Alpheratz.Features.TagMaster;
using Alpheratz.Features.Template;

namespace Alpheratz.Features.Settings;

public sealed class SettingsCompositeViewModel : UiThreadSafeObservableObject
{
	public SettingsViewModel Settings { get; }

	public TagMasterViewModel TagMaster { get; }

	public TemplatePageViewModel Template { get; }

	public SettingsCompositeViewModel(SettingsViewModel settings, TagMasterViewModel tagMaster, TemplatePageViewModel template)
	{
		Settings = settings;
		TagMaster = tagMaster;
		Template = template;
	}
}
