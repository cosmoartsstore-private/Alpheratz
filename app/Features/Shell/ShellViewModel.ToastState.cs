using Alpheratz.Shared.Services;

namespace Alpheratz.Features.Shell;

public partial class ShellViewModel
{
    // WinUI バインディング名に合わせて小文字のまま公開する。
    // Toast の所有は ViewModel に置き、ToastHost はこのサービスを表示だけに使う。
    public ToastService toastState => toastService;
}
