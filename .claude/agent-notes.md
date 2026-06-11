# Agent Notes

These notes prevent future sessions from redoing already-audited recovery work.

## Completed Decisions

- `BuildWorks/_recovered-src` is generated/recovery material, not the active development source. Do not re-add it.
- Files ending in `.recovered-0516` under `app/` are historical recovery snapshots. They may appear in `rg`, but they are not the active XAML/code-behind implementation.
- WinUI code-behind is treated as the OS/UI framework boundary. Behavior should be covered through extracted `*Logic.cs` classes and service tests.
- Use `ExcludeFromCodeCoverage(Justification = "WinUI/OS framework boundary; behavior is covered through extracted logic and service tests.")` consistently for framework-boundary UI code.
- The gallery filter panel is intentionally hosted inside `ShellPage.FilterOverlay`. Do not move the existing instance between parents at show time; that can break inherited `ActualTheme`.
- The filter panel width is intentionally fixed at 612px as of 2026-06-06. Long tag/world values should wrap or ellipsize inside that width instead of stretching the panel.
- Header search is intentionally plain text only as of 2026-06-06. Search execution is Enter-submit only; do not restore input-time debounce search. Do not reintroduce command search, `cmd:` UI, a command quick menu, or `SearchCommandParser`.
- Photo modal layout has already been adjusted for long world names. Keep the right details pane bounded and wrap detail text.
- Match source/source slot badges were removed from the photo modal because their user-facing meaning was unclear. Reintroduce only with clearer labels.
- The old photo-modal history back circle is intentionally hidden. Photo-to-photo movement is exposed through hidden-by-default image-edge navigation and keyboard left/right handling. The image-edge navigation should match `F:\planetes-atelier\bk\Alpheratz-tauri`: no circular/oval button chrome; on left/right edge hover, show a subtle black-to-transparent gradient plus `<` / `>` chevrons, then hide again on pointer exit.
- `openWorldLinkOnPost` / `OpenWorldLinkOnPost` is the persisted setting for opening the selected photo's VRChat world link when launching the tweet/post intent.
- `AppLogger` intentionally persists only `Fatal` by default. `Error`, `Warn`, `Info`, and `Trace` are opt-in through `ALPHERATZ_VERBOSE_LOGS=1`; the queue is bounded and `info.log` rotates at 1MB to avoid user storage growth from repeated sync/media failures.
- Settings modal layout is intentionally single-column in the order General, Tag master, Template, Credits, with `MaxWidth=760`.
- `MVVMTK0045` is suppressed in `app/Alpheratz.Frontend.csproj` to keep build/test output usable. Do not reintroduce warning noise unless doing the full CommunityToolkit source-generator migration.
- `TextBlock` default `IsTextSelectionEnabled` is set to `False` to avoid text boxes looking like focused inputs unless selection is explicitly needed.
- World grouping is normalized by `GalleryPhotosStateLogic.BuildWorldGroupKey`: null, empty, and whitespace-only names are all grouped as the unknown-world group.
- Group drill-down should prefer preserved `GroupPhotos` from the clicked card. Use DB fallback only when `GroupPhotos` is absent.
- Favorite-star handlers must also run on `DataContextChanged`, not only `Loaded`, because virtualized cards are reused.
- The favorite toggle argument name `currentIsFavorite` is intentional: it represents the current state before calculating the next state.

## Known Follow-Up Work

- Migration from `[ObservableProperty]` fields to WinUI/AOT-compatible partial properties is still a separate task; the warning noise itself is already suppressed.
- Existing code still has many lowerCamel method names. Broad naming cleanup is a separate task, not part of the UI/bug-fix pass.
- Do not create a new visual design direction unless explicitly requested. Preserve the current design and repair concrete breakage.
- Audit status from the 2026-06-06 recheck:
  - Fixed in the follow-up pass: UI-bound mutations after `ConfigureAwait(false)` now use `DispatcherService` in the main Gallery / PhotoModal / WorldResolve / Settings / Tag / Toast paths touched by the audit.
  - Fixed in the follow-up pass: `PhotoModalPage` subscribes to the selected `PhotoThumbnailItem` and resyncs derived UI for `Tags`, `WorldName`, `MatchSource`, and `EffectiveDisplayPath`.
  - Fixed in the follow-up pass: `WorldResolveViewModel` thumbnail callbacks dispatch item property updates back to the UI thread.
  - Fixed in the follow-up pass: date preset/custom date apply paths use a batched `applyDateRange` path and raise one `BatchCompleted`.
  - Still true by design: `UiObservableCollection` and `UiThreadSafeObservableObject` only marshal notifications; they do not make mutation itself UI-thread-owned. Continue to prefer explicit `DispatcherService.RunOnUiThread(...)` for UI-bound state mutations.
  - Fixed in the 2026-06-06 visual pass: narrow-window layout for Settings, PhotoModal, GroupDrillDown, WorldResolve, and the Gallery bulk bar. Keep future changes compatible with 640px snapped-window captures.
  - Fixed in the 2026-06-06 visual pass: Settings -> "分析" no longer awaits WorldResolve initialization on the UI path. The modal is shown first; DB/PDQ initialization runs asynchronously and starts by leaving the UI thread.
  - Fixed in the 2026-06-06 visual pass: `CopyXbfToSubfolder` excludes the generated assembly-named subfolder and removes stale direct recursion, preventing repeated `Alpheratz.Frontend\Alpheratz.Frontend\...` output growth.
  - Visual comparison report for that pass is generated at `artifacts/visual-regression/report.html` with embedded screenshots.
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
- Before changing visual design, inspect relevant real running services or official product docs first and record the design basis in the handoff. Do not ship visual changes based only on intuition.
- When a user references old Alpheratz behavior or a backup source such as `bk`, inspect that source before recreating the UI. If the referenced source cannot be found, report that explicitly and separate inference from verified behavior.
- `F:\planetes-atelier\bk\Alpheratz-tauri` exists and is the relevant old Alpheratz/Tauri reference for photo-modal edge navigation and the shared `menu` sliders icon. Check it before recreating those interactions.
- When a user lists multiple requirements separated by `｜`, split each block into trackable plan items so later sessions do not collapse distinct requirements into one broad task.
