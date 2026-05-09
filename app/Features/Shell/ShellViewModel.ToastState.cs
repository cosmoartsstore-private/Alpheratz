using Alpheratz.Shared.Services;

namespace Alpheratz.Features.Shell;

public partial class ShellViewModel
{
    // Kept as a lower-case property to match the existing WinUI binding style in this migration.
    // This exposes the legacy App.tsx/useToasts responsibility to ToastHost without moving toast ownership into the view.
    public ToastService toastState => toastService;
}
