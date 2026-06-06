# Agent Notes

These notes prevent future sessions from redoing already-audited recovery work.

## Completed Decisions

- `BuildWorks/_recovered-src` is generated/recovery material, not the active development source. Do not re-add it.
- Files ending in `.recovered-0516` under `app/` are historical recovery snapshots. They may appear in `rg`, but they are not the active XAML/code-behind implementation.
- WinUI code-behind is treated as the OS/UI framework boundary. Behavior should be covered through extracted `*Logic.cs` classes and service tests.
- Use `ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")` consistently for framework-boundary UI code.
- The gallery filter panel is intentionally hosted inside `ShellPage.FilterOverlay`. Do not move the existing instance between parents at show time; that can break inherited `ActualTheme`.
- The filter panel width is intentionally fixed. Long tag/world values should wrap or ellipsize inside that width instead of stretching the panel.
- Photo modal layout has already been adjusted for long world names. Keep the right details pane bounded and wrap detail text.
- Match source/source slot badges were removed from the photo modal because their user-facing meaning was unclear. Reintroduce only with clearer labels.
- `TextBlock` default `IsTextSelectionEnabled` is set to `False` to avoid text boxes looking like focused inputs unless selection is explicitly needed.
- World grouping is normalized by `GalleryPhotosStateLogic.BuildWorldGroupKey`: null, empty, and whitespace-only names are all grouped as the unknown-world group.
- Group drill-down should prefer preserved `GroupPhotos` from the clicked card. Use DB fallback only when `GroupPhotos` is absent.
- Favorite-star handlers must also run on `DataContextChanged`, not only `Loaded`, because virtualized cards are reused.
- The favorite toggle argument name `currentIsFavorite` is intentional: it represents the current state before calculating the next state.

## Known Follow-Up Work

- `MVVMTK0045` warnings still exist. Treat migration from `[ObservableProperty]` fields to WinUI/AOT-compatible partial properties as a separate task.
- Existing code still has many lowerCamel method names. Broad naming cleanup is a separate task, not part of the UI/bug-fix pass.
- Do not create a new visual design direction unless explicitly requested. Preserve the current design and repair concrete breakage.
- Audit status from the 2026-06-06 recheck:
  - Fixed in the follow-up pass: UI-bound mutations after `ConfigureAwait(false)` now use `DispatcherService` in the main Gallery / PhotoModal / WorldResolve / Settings / Tag / Toast paths touched by the audit.
  - Fixed in the follow-up pass: `PhotoModalPage` subscribes to the selected `PhotoThumbnailItem` and resyncs derived UI for `Tags`, `WorldName`, `MatchSource`, and `EffectiveDisplayPath`.
  - Fixed in the follow-up pass: `WorldResolveViewModel` thumbnail callbacks dispatch item property updates back to the UI thread.
  - Fixed in the follow-up pass: date preset/custom date apply paths use a batched `applyDateRange` path and raise one `BatchCompleted`.
  - Still true by design: `UiObservableCollection` and `UiThreadSafeObservableObject` only marshal notifications; they do not make mutation itself UI-thread-owned. Continue to prefer explicit `DispatcherService.RunOnUiThread(...)` for UI-bound state mutations.
  - Still pending: narrow-window layout remains a separate UI pass. Current fixed modal/filter/settings widths are intentional enough for desktop, but not robust for snapped/narrow windows.
  - Still pending: Shell overlay state is split across local flags and `ShellStage`; current guards are tested, but a single overlay state model would reduce future drift.
- Rechecked non-issues:
  - `ShellStage` already clears modal content with version guards after close animations. Do not re-fix modal content cleanup unless new evidence appears.
  - `SettingsPage` already reattaches subscriptions on `Loaded` and detaches on `Unloaded`.
  - `PhotoService` mutation methods accepting `sourceSlot` while updating by `photo_path` are not currently a functional bug because `photo_path` is the primary key; treat broader slot-key redesign as a separate schema/API decision.

## Work Rules

- Check `git status --short` before edits.
- If the running app locks build artifacts, stop the `Alpheratz.Frontend` process before build/test.
- Use `apply_patch` for manual file edits.
- Run at least `dotnet test tests\Alpheratz.Tests\Alpheratz.Tests.csproj` before handing off source changes.
- For UI checks, include long tag names, long world names, empty/unknown worlds, and virtualized reused photo cards.
