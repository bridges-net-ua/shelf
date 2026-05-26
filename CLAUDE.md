# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

**Shelf** — a Windows dock-bar that lives on the right (or left) edge of the screen, holds user-configurable widgets (clock, notes, todo lists, photo slideshow, internet radio, weather, timer/stopwatch, holidays), and reserves screen real estate via the Win32 AppBar API so that maximized windows lay out beside it instead of under it.

`Shelf` is the **technical / project name** (single binary, all paths and namespaces). The **user-facing brand** comes through localization: Ukrainian users see «Поличка», English users see «ShelfDesk» — driven by the `App_Name` key in `Strings.uk.xaml` / `Strings.en.xaml`.

WPF + WinForms (for `NotifyIcon`), .NET 8, dark monochrome theme throughout. UI strings are localized — Ukrainian and English, chosen in Settings (see **Localization** below). All csproj/sln/namespace/assembly names are now ASCII (`Shelf`, `Shelf.Sdk`, `Shelf.Widgets.<Name>`).

The app is split into **eleven projects**:
- **`Shelf`** (host, WPF .exe) — the panel itself, settings UI, AppBar/virtual-desktop logic. References the SDK and each of the 9 widget projects directly via `<ProjectReference>` (no runtime plugin loading).
- **`Shelf.Sdk`** (class library) — public surface for widget authors: `IWidget`, `WidgetBase`, `IWidgetHost`/`WidgetServices`, `WindowChrome`, `DarkMessageBox`, `Loc` (localization), `Theme` (theme service), **all** theme styles (`Theme.xaml`), the colour palettes (`Themes/Theme.Dark.xaml` and `Themes/Theme.Light.xaml`), and the string dictionaries (`Strings.uk.xaml`/`Strings.en.xaml`).
- **`Shelf.Widgets.Clock` / .Notes / .Todo / .Photos / .Radio / .Weather / .Timer / .Stopwatch / .Holidays** (WPF class libraries under `WidgetPlugins/`) — each compiles to a DLL that lands next to the host `.exe`. The `WidgetPlugins/` directory name is historical; widgets are first-class, statically referenced projects, not runtime-loaded plugins.

## Release process

The end-to-end release procedure (versioning, `dotnet publish`, signing, installer, distribution) lives in [`RELEASE.md`](RELEASE.md). When the user asks about "як випустити", "release", "installer", "signed exe" etc., consult that document first.

## Build / run

```powershell
cd "d:\project\Shelf"
dotnet build "Shelf.sln" -c Debug
Start-Process "bin\Debug\net8.0-windows\Shelf.exe"
```

The `.sln` is saved with UTF-8 **BOM** (first three bytes `EF BB BF`). Since the rename to `Shelf` all sln/csproj paths are ASCII, so the BOM is no longer strictly required — but keep it to avoid surprises if the legacy `Помічник.*` paths ever reappear in a branch.

**Stop the running instance before rebuilding** — the `.exe` is locked while running. Single-instance via named mutex `Shelf_SingleInstance_E94F12C7`. To kill: `Get-Process -Name Shelf -ErrorAction SilentlyContinue | Stop-Process`.

`dotnet` is at `C:\Program Files\dotnet\dotnet.exe` and may not be on PATH in some shells — call it by full path if `dotnet` errors with "command not found".

No test suite, no linter config, no CI. `Resources\shelf.ico` is multi-size — referenced by `<ApplicationIcon>` and (via pack URI) by `App.xaml.cs → LoadAppIcon` which feeds `WindowChrome.DefaultIcon` for every window.

**Do not run `dotnet publish`, PyInstaller, or any executable-packaging step** unless the user types `ЗІБРАТИ EXE` exactly. (Per user's global rule — packaging is slow and produces a large bundle the user didn't ask for.)

## UI text conventions

- **All user-visible strings come from `Strings.uk.xaml` / `Strings.en.xaml`** (see **Localization**). Never hardcode Ukrainian or English text in XAML or code — add a key to both dictionaries.
- **Use ASCII hyphen `-`, not em-dash `—`**, in any user-visible string (window titles, tooltips, placeholders, MessageBox content, settings text). The user explicitly requested this throughout the UI. Code comments and `CLAUDE.md` can still use `—` — the rule applies to *what the user sees*. If you grep `—` and find it in a string dictionary value, a `<TextBlock>`, `Title="…"`, `ToolTip="…"`, error message, etc., replace it.
- **Ellipsis (`…`) for "open a dialog" menu items** stays as is ("Налаштування…", "Перейменувати…"). It's a real ellipsis (U+2026), not three dots.
- **Use `«»` for quotes in UI text** ("Видалити віджет «Покупки»?"), not `""`.

## Architecture

### Lifecycle (`App.xaml.cs`)

1. Single-instance mutex check — second launches `Shutdown()` immediately.
2. **Wire SDK services**: `WindowChrome.DefaultIcon` ← host icon loaded via pack URI; `WidgetServices.Host` ← `HostAdapter` (forwards `RequestSaveStates()` to `App.Widgets.SaveStates()`). **Do this before any widget code runs** — the SDK is otherwise a passive library.
3. `SettingsService.Load()` — reads `%APPDATA%\Shelf\settings.json`. **One-time migration**: if the new file is missing but `%APPDATA%\Помічник\settings.json` exists (legacy install), it is copied into the new location.
4. `Loc.Initialize(Settings.Current.Language)` — merges `Strings.uk.xaml` or `Strings.en.xaml` into `Application.Resources`, so `{DynamicResource Key}` and `Loc.Get(...)` resolve UI strings. Must run before any window is shown.
5. `AutoStartService.MigrateLegacyValue()` — removes the legacy `HKCU\...\Run\Помічник` entry (if any) and promotes it to a new `Shelf` value, so users with autostart enabled before the rename keep autostart.
6. `WidgetRegistry.Initialize()` — force-loads every `Shelf.Widgets.*.dll` sitting next to the host `.exe` (`Assembly.LoadFrom`), then scans `AppDomain.CurrentDomain.GetAssemblies()` for types implementing `IWidget`, instantiates one "prototype" per type to read metadata (`Id`, `DisplayName`, `Description`, `HasSettings`). No manifest, no broken-plugin reporting — the only failure mode is a bug in a widget's prototype constructor (silently skipped).
7. `WidgetManager.Sync()` — migrates legacy `WidgetEntry.Id` → `TypeId`, generates missing `InstanceId` GUIDs. **Entries with unknown TypeId are kept in settings.json** but skipped when creating instances — so removing a widget project doesn't wipe user state.
8. `WidgetManager.LoadStates()` → `MainWindow.Show()` → `TrayIconService.Show()`.

On exit: `Widgets.SaveStates()`, `Tray.Dispose()`, `Settings.Save()`, mutex release.

Unhandled exceptions append to `%TEMP%\Shelf.crash.log`. Virtual-desktop tracker writes to `%TEMP%\Shelf.vd.log`. Neither rotates.

### SDK (`Shelf.Sdk/`)

The shared contract — everything widgets need to integrate, nothing more:
- **`IWidget`** — interface: `Id`/`DisplayName`/`Description`/`HasSettings`, `CreateView()` returns the `UserControl`, `ShowSettings(owner)`, `SaveState()`/`LoadState(json)`, **`InstanceLabel` (default interface method = DisplayName)**. Widgets supporting per-instance rename override `InstanceLabel` to return the user-set title (Notes, Todo, Photos, Radio do this).
- **`WidgetBase`** — optional abstract `UserControl : IWidget` with sensible defaults (including `virtual InstanceLabel => DisplayName`); widgets can also implement `IWidget` directly (current built-ins do that).
- **`IWidgetHost` + `WidgetServices`** — static service locator. Widgets call `WidgetServices.RequestSaveStates()` to ask the host to persist state. **Widgets must not reference the host assembly directly.**
- **`WindowChrome.Apply(window)`** — full one-call window setup: assigns the default icon, swaps in the `ChromeWindow` style (custom 32-px caption bar in the app's theme, see **Custom window chrome** below), rounds the window corners on Win11, registers the `SystemCommands.CloseWindowCommand` binding for the chrome's ✕ button, and sets the DWM dark/light title-bar flag (kept for safety even though the system caption is hidden). Every Window in the app — Settings, `DarkMessageBox`, widget settings dialogs — calls this in its constructor. **Skipped for windows that explicitly set `WindowStyle="None"` in XAML** (e.g. `MainWindow` the AppBar panel) — they already supply their own chrome.
- **`Theme` (`Shelf.Sdk/Theme.cs`)** — runtime theme service mirroring `Loc`. `AppTheme` enum (`Dark`, `Light`); `Initialize(theme)` is called once at startup; `Apply(theme)` swaps the active colour dictionary live and raises `ThemeChanged`. DynamicResource consumers re-resolve immediately; the host subscribes to update the WinForms tray menu and re-apply DWM flags on all open windows.
- **`DarkMessageBox`** — drop-in replacement for `MessageBox.Show(...)`. Same API surface (`Show(owner, text, title, buttons, image)` → `MessageBoxResult`). Renders inside a real WPF Window so the theme applies — standard Win32 `MessageBox` is white and can't be styled. Always use `DarkMessageBox.Show(...)`, never `MessageBox.Show(...)`. Custom Path-geometry icons for Info/Question/Warning/Error. Class name kept as `DarkMessageBox` despite now supporting both themes — it's a public SDK API surface; renaming would ripple through callers.
- **`Loc` + `Strings.uk.xaml`/`Strings.en.xaml`** — localization helper and string dictionaries. See **Localization** below.
- **`Theme.xaml` + `Themes/Theme.Dark.xaml` + `Themes/Theme.Light.xaml`** — the visual style is split: the 15 `SolidColorBrush` palette entries live in two colour-only dictionaries (`Theme.Dark.xaml` / `Theme.Light.xaml`); `Theme.xaml` holds **only the styles** (Button, TextBox, ContextMenu, ScrollBar, etc.) and references brushes via `{DynamicResource ...}` so theme switching is instantaneous. `App.xaml` merges `Theme.xaml`; `Theme.Initialize/Apply` merges the active colour dictionary as the **first** entry in `Application.Resources.MergedDictionaries`. The Dark palette is fully monochrome (`AccentBrush`/`AccentHoverBrush` = `#5C5C62`/`#6E6E76`); Light palette pairs are inverted with care (white surfaces, dark text) — see **Themes** section. Key names stay `Accent*` for source-compat; in Light theme the same key holds a darker grey so highlights remain visible on white. Widget XAML must use `{DynamicResource ...}`, not `{StaticResource}` — widget assemblies can't statically see resources from another assembly's App.xaml at compile time.

Implicit styles (no `x:Key`) cover `TextBlock`, `Button`, `CheckBox`, `RadioButton`, `Slider`, `ScrollBar`, `TabControl`, `TabItem`, `ContextMenu`, `MenuItem`, `Separator`, `ToolTip`, `TextBox`, `ComboBox`, `ComboBoxItem`. Named: `IconButton`, `IconButtonSubtle`, `ComboBoxToggleButton`, `ChromeWindow`, `WindowCaptionCloseButton`, `OverlayPanelScrollViewer`. Non-obvious bits:
- The thin scrollbar style is **global** (no `x:Key`); per-widget overrides are forbidden by convention (captured in user memory). The one exception is the named `OverlayPanelScrollViewer` applied to `MainWindow.PanelScroll`/`PinnedScroll` — see **Overlay panel scrollbar** below.
- WPF menu separators need `{x:Static MenuItem.SeparatorStyleKey}` — we register one `BasedOn` the global `Separator` style so submenu dashes render correctly.
- Default `TextBox` style ships a localized Cut/Copy/Paste/SelectAll context menu wired to `ApplicationCommands` with `CommandTarget` bound to the menu's `PlacementTarget`; each item carries an icon.
- The `MenuItem` template has a dedicated left **icon column** (`ContentPresenter ContentSource="Icon"`, `Auto` width — collapses to nothing when an item has no icon). See **Unified context menu**.
- `ComboBox` is fully custom-templated (WPF's default Aero template ignores `Background`, so a partial style is useless). Both the closed-state ToggleButton and the dropdown `Popup` are themed; selected `ComboBoxItem` uses `AccentBrush` background with white foreground, hover uses `HoverBrush`. Any plugin using `<ComboBox>` automatically gets this — don't redefine.

### Localization (`Shelf.Sdk/Loc.cs`)

The app ships Ukrainian and English. `AppLanguage` enum (`Uk`, `En`); the choice persists in `AppSettings.Language` (default `Uk`).

- Two `ResourceDictionary` files in the SDK — `Strings.uk.xaml` and `Strings.en.xaml` — hold every user-visible string, keyed identically (e.g. `Btn_OK`, `Radio_Settings_Title`, `Photos_Folder`). **Keep the two files in lockstep — every key must exist in both.**
- At startup `App.xaml.cs` calls `Loc.Initialize(Settings.Current.Language)`, which merges the matching dictionary into `Application.Resources`.
- **XAML** resolves strings via `{DynamicResource Key}` — exactly like theme brushes.
- **Code** resolves strings via `Loc.Get("Key")` (returns the key itself if the string is missing) or `Loc.Format("Key", args...)`.
- `Loc.Culture` gives the `CultureInfo` (uk-UA / en-US) for date/number formatting that should follow the UI language.
- The language is **fixed for the process lifetime**. Settings → language radio buttons (`RbLangUk`/`RbLangEn`) write `AppSettings.Language`, but the change only takes effect after an app restart (Settings prompts for one, same flow as a plugin install).
- `TrayIconService` picks the tray glyph from `Loc.Current` — `П` for Ukrainian («Поличка»), `S` for English («ShelfDesk»). Used only as a fallback when `shelf.ico` fails to load from pack URI.

**Adding or changing UI text:** never hardcode a user-visible string. Add the key to **both** `Strings.uk.xaml` and `Strings.en.xaml`, then reference it via `{DynamicResource Key}` (XAML) or `Loc.Get`/`Loc.Format` (code). Widget plugins use the same mechanism — the dictionaries live in the SDK and are merged app-wide.

### Themes (`Shelf.Sdk/Theme.cs`)

Two themes ship: **Dark** (legacy default, near-black background) and **Light** (off-white background, dark text). `AppTheme` enum (`Dark`, `Light`); persists in `AppSettings.Theme` (default `Dark`).

- Palette = 15 `SolidColorBrush` keys: `BackgroundBrush`, `SurfaceBrush`, `ElevatedSurfaceBrush`, `BorderBrush`, `PrimaryTextBrush`, `SecondaryTextBrush`, `MutedTextBrush`, `AccentBrush`, `AccentHoverBrush`, `HoverBrush`, `CheckBoxFillBrush`, `CheckBoxFillHoverBrush`, `ScrollThumbBrush`, `ScrollThumbHoverBrush`, `ScrollThumbDragBrush`. Both `Themes/Theme.Dark.xaml` and `Themes/Theme.Light.xaml` define all 15 keys — keep them in lockstep.
- `App.xaml.cs` calls `Theme.Initialize(Settings.Current.Theme)` **before** any window is shown, then subscribes `Theme.ThemeChanged += WindowChrome.ReapplyTitleBarThemeToAll` so DWM dark-mode flag updates everywhere on the fly. `TrayIconService` self-subscribes to rebuild its WinForms color table.
- **Live-switch**: clicking the theme `ComboBox` in Settings calls `Theme.Apply(newTheme)` immediately — DynamicResource consumers re-resolve, panel and all open dialogs change colours without restart. The choice is also saved to `settings.json` right away.
- `Theme.Apply` removes the previous colour dictionary from `Application.Resources.MergedDictionaries` and inserts the new one at index 0 (so it's resolved before `Theme.xaml`'s styles).
- **Adding a third theme**: add an `AppTheme` enum value, a `Themes/Theme.<Name>.xaml` colour-only dictionary with all 15 keys, a branch in `Theme.Apply` for the URI, a `Settings_Theme_<Name>` localization key in both string dictionaries, a `<ComboBoxItem>` in `SettingsWindow.xaml`, the matching `SelectedIndex` case in `SettingsWindow.xaml.cs LoadCurrent`/`CbTheme_SelectionChanged`, and a `TrayPalette.<Name>()` factory in `TrayIconService`.

### Custom window chrome (`ChromeWindow` style)

System-drawn Windows title bars look chromed-out even with `DWMWA_USE_IMMERSIVE_DARK_MODE` on Win11 24H2+. The app hides them and paints its own:

- `Theme.xaml` defines `Style x:Key="ChromeWindow" TargetType="Window"` — sets `WindowStyle="None"`, `AllowsTransparency="False"`, attaches `System.Windows.Shell.WindowChrome` (CaptionHeight=32 for native drag/snap, all other thicknesses 0, `UseAeroCaptionButtons=False`), and replaces the control template with a 32-px caption strip (icon + window Title binding + ✕) over a `ContentPresenter` for the window content.
- `WindowCaptionCloseButton` named style: 46×32, hover `#E81123` red with white ✕, marked `shell:WindowChrome.IsHitTestVisibleInChrome="True"` so clicks don't bubble as drag.
- The ✕ uses `SystemCommands.CloseWindowCommand` and `WindowChrome.Apply` registers a per-window `CommandBinding` that calls `window.Close()`. We use `UseAeroCaptionButtons=False`, so the shell does NOT auto-bind the command — without our explicit binding the button silently no-ops.
- Win11 rounded outer corners: `WindowChrome.Apply` calls `DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE=33, DWMWCP_ROUND=2)`. No-op on older Windows.
- `WindowChrome.Apply` is **skipped** for windows whose XAML explicitly sets `WindowStyle="None"` (currently only `MainWindow`). The assumption is they already have their own chrome and overriding with `ChromeWindow` would break them.
- Local Window attributes that conflict with the style's `WindowStyle="None"` Setter (`Background`, `Foreground`, `FontFamily`) are **kept** on each Window XAML because they have higher precedence than style setters; the chrome's `ControlTemplate` reads `Background` via `{TemplateBinding}` so per-window backgrounds still work.

### Overlay panel scrollbar (`OverlayPanelScrollViewer` style)

A normal WPF `ScrollViewer` with `VerticalScrollBarVisibility="Auto"` reserves layout width for the scrollbar when it appears — widgets in the panel would shrink the moment content overflows. Fix: `OverlayPanelScrollViewer` (named style in `Theme.xaml`, applied only to `MainWindow.PanelScroll` and `PinnedScroll`):

- Custom template puts the `ScrollContentPresenter` and the `ScrollBar` in the same `Grid` cell. The scrollbar floats over content at `HorizontalAlignment="Right"`, `Width="6"`, never affects layout width.
- `Opacity="0"` by default with a `DataTrigger` on `IsMouseOver` of the parent `ScrollViewer` — fade-in 200ms, fade-out 300ms. Wheel scrolling still works while invisible (internally `Visibility="Visible"`).
- `Style="{DynamicResource OverlayPanelScrollViewer}"` + `Padding="4,0,4,0"` on both ScrollViewers — symmetric 4-px breathing room so the floating thumb doesn't overlap content visually.
- The implicit `ScrollBar` style (thin-thumb) is unchanged and used everywhere else (Settings, message boxes, widget settings dialogs). The overlay style inherits from it via `BasedOn="{StaticResource {x:Type ScrollBar}}"`.

### Widget architecture (static `ProjectReference`, no runtime loading)

Each widget is its own csproj under `WidgetPlugins/Shelf.Widgets.<Name>/`. The host (`Shelf.csproj`) declares a `<ProjectReference>` to every widget project, so MSBuild builds them together and drops the resulting DLLs (`Shelf.Widgets.*.dll`) next to `Shelf.exe`. There is no `.hwidget` package format, no `widget.json` manifest, no SDK-version negotiation, no install/uninstall flow, no `%LOCALAPPDATA%\Shelf\Widgets\` directory. Removing the `PluginInstallService` and the manifest-scanning paths was deliberate — it keeps the build a single signed artifact, simplifies Microsoft Store packaging, and means new widgets ship as part of a normal app update.

Discovery at runtime is reflection-based but trivial: `WidgetRegistry.Initialize()` force-loads every `Shelf.Widgets.*.dll` from `AppDomain.CurrentDomain.BaseDirectory`, then walks loaded assemblies for non-abstract types that implement `IWidget`, instantiates one prototype per type, and registers it. Adding a project to the host csproj is enough; no other host code changes.

### Adding a new widget

1. Create `WidgetPlugins/Shelf.Widgets.<Name>/` with a csproj, the `UserControl` (XAML + cs implementing `IWidget`), and an optional settings dialog `Window`.
2. csproj must:
   - target `net8.0-windows`, `<UseWPF>true</UseWPF>`, `<OutputType>Library</OutputType>`
   - set `<AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>` (so `bin/Debug/` is the output, not `bin/Debug/net8.0-windows/`)
   - reference SDK with `<Private>false</Private>` (so `Shelf.Sdk.dll` isn't double-copied — at runtime the host already supplies it).
   - Use the existing widget csprojs as templates. There is no post-build copy `Target` any more — the host's `<ProjectReference>` to the widget puts the DLL where it needs to be.
3. Add the project to `Shelf.sln` (preserve the BOM!) and add a matching `<ProjectReference>` line to `Shelf.csproj`.
4. Widget XAML must use `{DynamicResource ...}` for theme keys and string keys, **not** `{StaticResource}`. The widget assembly doesn't have its own App.xaml and StaticResource won't find symbols defined in another assembly's merged dictionaries.
5. Widget code calls `WidgetServices.RequestSaveStates()` after mutating state (use a `DispatcherTimer` to debounce frequent edits). Never reference `App.*` from the host — that's a circular dependency.
6. Settings dialog calls `Shelf.Sdk.WindowChrome.Apply(this)` after `InitializeComponent()`.
7. **Widget csproj does NOT have `UseWindowsForms=true`** — only the host does. So widgets can't use `System.Windows.Forms.Clipboard` (which has retry-count overloads). Use WPF `Clipboard.SetDataObject(text, copy: true)` instead. For high-traffic clipboard operations (e.g., Ctrl+X) the host has helpers in `MainWindow` that do dispatched, retry-limited writes.
8. **Localize every user-visible string** — add the key to both `Strings.uk.xaml` and `Strings.en.xaml` in the SDK, reference via `{DynamicResource Key}` / `Loc.Get`. Don't hardcode text.

### Settings shape (`Models/AppSettings.cs`)

```
AppSettings
├── Side, Width, AutoHide, AutoStart, WidgetOrderLocked, Language, Theme, InitializedWithDefaults
└── Widgets: List<WidgetEntry>
    ├── InstanceId  (GUID, unique per instance)
    ├── TypeId      (plugin id, e.g. "clock" / "notes" / "todo" / "photos" / "radio" / "stopwatch")
    ├── Enabled
    ├── Pinned      (bool, default false — see "Pinned widgets" below)
    ├── State       (JSON blob owned by the widget — opaque to host)
    └── Id          (legacy alias for TypeId, migrated then nulled)
```

`Language` (`AppLanguage`, default `Uk`) — the UI language; see **Localization**.
`Theme` (`AppTheme`, default `Dark`) — UI theme; see **Themes**.

`SettingsService.Save` writes prettified JSON. `Save()` does not fire `Changed`; `NotifyChanged()` does — call the latter only when **panel** layout (position, side, width, autohide) needs to react, not for widget autosaves.

For widget-list changes (add/remove/reorder/lock), use `App.Widgets.ActiveWidgetsChanged` event (fired by `WidgetManager`). `MainWindow.RebuildPanel` subscribes to it. `WidgetManager.NotifyOrderLockChanged()` is a no-arg helper that just raises `ActiveWidgetsChanged` so the panel rebuilds.

### Settings window — three tabs

`Views/SettingsWindow.xaml` has a `TabControl` with three tabs:

1. **Налаштування панелі** — Position (Left/Right side + width slider), Behavior (AutoHide, AutoStart, LockOrder), **Theme** (`CbTheme` ComboBox with Dark/Light; live-applied via `Theme.Apply` on `SelectionChanged`; `_suppressThemeApply` flag guards initial population), **Language** (radio buttons — needs restart, prompts via `DarkMessageBox`).
2. **Віджети** — single section **Екземпляри**: add/enable/reorder/remove widget instances. Adding a widget shows a context menu populated from `WidgetRegistry.Types`. No install/uninstall UI.
3. **Про програму** — vertical layout: app icon (96×96 from `Resources/shelf.png` via pack URI), localized app name (`{DynamicResource App_Name}`, 28 pt SemiBold), version line auto-generated from `Assembly.GetExecutingAssembly().GetName().Version` (`{Major}.{Minor}`), single-line description, then a footer with «Програма розроблена: Bridges Community», a clickable `mailto:`-Hyperlink (current address: `shelf@bridges.net.ua` via `About_Email` key), and «© 2026 Bridges Community». The version `TextBlock` and the developer/contact `Inlines` are populated in `SettingsWindow.xaml.cs SetupAbout()`; do **not** hardcode the version or the email in XAML.

**Restart** (used by the language switcher) = `Process.Start(Environment.ProcessPath)` + `Application.Current.Shutdown()`, with `RestartApp()` defined locally in `SettingsWindow`. The single-instance mutex on the old process must be released first (`OnExit` does this), so there's a brief race window — practically harmless because the new process retries instantly. Theme switching does **not** need a restart.

### AppBar registration (`Services/AppBarService.cs`)

`SHAppBarMessage(ABM_NEW)` registers the panel HWND as a system AppBar; `ABM_QUERYPOS` then `ABM_SETPOS` reserve a strip on the chosen edge (Left/Right only). Windows then auto-resizes maximized windows around it. AppBar coordinates are physical pixels; WPF uses DIPs — `HwndSource.CompositionTarget.TransformToDevice` does the conversion.

**Auto-hide mode does not register as an AppBar.** Instead the panel slides out to a 3-pixel trigger strip and back via `DoubleAnimation` on `Window.Left` (`MainWindow.SlideIn`/`SlideOut`). `ApplySettings` switches modes by calling `_appBar.Register`/`Unregister` and animating `LeftProperty`.

**Width-change ordering matters.** In non-AutoHide mode, `ApplySettings` must set `Top`/`Height`/`Width`/`Left` **before** calling `_appBar.Register(side, width)` — assigning Width alone would leave the window briefly at the old Left, visually shifting it for one frame. Then `AppBar.SetPosition` re-confirms the coords from `SHAppBarMessage`. Don't optimize this away.

**Trust our own rect, not `ABM_QUERYPOS`'s response.** `SetPosition` issues `ABM_QUERYPOS` for protocol politeness (so other AppBars see us), but then **overwrites `data.rc` with our own desired coordinates** before `ABM_SETPOS`. Reason: under race conditions (notably right after a virtual-desktop move + Hide/Show cycle), `ABM_QUERYPOS` returns a rect anchored to the **left** edge regardless of the `uEdge` we requested — which used to leave the panel detached from the intended screen edge. Forcing our rect makes this deterministic. `%TEMP%\Shelf.vd.log` records `QUERYPOS returned: ...` and `SETPOS final: ...` for diagnosis.

### Virtual desktops: pin-first, mover as fallback

Two services, used in priority order:

1. **`Services/VirtualDesktopPinService.cs` (preferred)** — pins the panel HWND to **every** virtual desktop at once, so it never has to move. Uses the **undocumented** `IVirtualDesktopPinnedApps.PinView` interface obtained through the immersive shell's `IServiceProvider` (`CLSID_ImmersiveShell = C2F03A33-21F5-47FA-B4BB-156362A2F239`). To get an `IApplicationView` for the HWND we go through `IApplicationViewCollection.GetViewForHwnd`. Two non-obvious GUID facts learned the hard way:
   - The Win11-era ApplicationViewCollection CLSID (`2c08adf0-a386-4b35-9250-0fe183476fcc`) returns `E_NOTIMPL` on current Win11 builds — use the Win10 CLSID (`1841c6d7-4f9d-42c0-af41-8747538f10e5`) which is still wired up.
   - `IVirtualDesktopPinnedApps` IID has remained stable: `4CE81583-1E4C-4632-A621-07A53543148F`.
   - `GetViewForHwnd` returns `TYPE_E_ELEMENTNOTFOUND (0x8002802B)` if called too early — Windows hasn't yet registered the WPF window in the Application View Collection. Workaround: defer the pin to `DispatcherPriority.ApplicationIdle` *and* loop `TryPinWithRetry` (up to ~1s of 100ms attempts). At startup this typically succeeds on attempt 2.

2. **`Services/VirtualDesktopService.cs` (fallback)** — the older approach. Polls `IVirtualDesktopManager.GetWindowDesktopId` every 150ms; on desktop change it fires `BeforeMove` (host unregisters AppBar) → `ShowWindow(SW_HIDE)` → `MoveWindowToDesktop` → `ShowWindow(SW_SHOWNOACTIVATE)` → `SetWindowPos(SWP_FRAMECHANGED)` → `AfterMove` (host re-registers AppBar via `Dispatcher.BeginInvoke(..., Background)` so WPF has time to rebuild `CompositionTarget`). This sequence visibly works but produces a brief blink on every switch and is the origin of the now-fixed "panel offset from screen edge" bug.

**Startup order** in `MainWindow.OnSourceInitialized`: start the mover *first* (so we always have a working strategy), then queue the pin attempt at `ApplicationIdle`. On pin success, dispose the mover and null the field; the polling stops. On pin failure (future Windows breaks the GUIDs), the mover keeps running — graceful degradation.

Both services log to `%TEMP%\Shelf.vd.log`. Pin attempts are prefixed with `[Pin]`.

### Drag-and-drop widget reordering (`MainWindow.xaml.cs`)

Each widget in the panel is wrapped in a `Grid` container. There is **no separate drag handle** — a widget is reordered by dragging it from its **header strip** (the top `HeaderDragZonePx` = 34px of the container, where the title sits). The host wires `PreviewMouseLeftButtonDown/Move/Up` + `LostMouseCapture` directly on the container `Grid` (non-pinned only).

Drag mechanics:
- `WidgetContainer_MouseDown`: ignored if `WidgetOrderLocked`, if the press is below `HeaderDragZonePx`, or if `IsInteractiveOriginalSource` finds a `ButtonBase`/`TextBoxBase`/`Thumb`/`ScrollBar`/`ComboBox`/`Slider`/`MenuItem` between the pressed element and the container. Otherwise sets `_dragArmed = true`, records start position. **No mouse capture and no `e.Handled` here** — a plain click, double-click rename, or context menu in the header must still work. Threshold = 4px.
- On movement past threshold (`WidgetContainer_MouseMove`): `StartDrag()` — **captures the mouse now** (`_dragCaptured = true`), renders the source container to a `RenderTargetBitmap`, creates an `Image` ghost (Opacity=0.75) in the `DragOverlay` Canvas. Source container dimmed to Opacity=0.3. Insertion line (3px AccentBrush Border) inserted into `WidgetsHost` children at the source's current index.
- While not dragging, `WidgetContainer_MouseMove` also sets the container `Cursor` to `Hand` when hovering the header strip — the discoverability hint that replaced the old dots.
- During drag: ghost follows cursor, insertion line moves to the gap closest to cursor Y, auto-scroll engages within 36px of viewport top/bottom.
- On drop (`WidgetContainer_MouseUp`): **compute anchor BEFORE releasing capture** — `ReleaseMouseCapture()` fires `LostMouseCapture` synchronously, which would otherwise null out drag state and the reorder would never execute. Anchor = first widget container after insertion line that isn't the source. Then `CleanupDrag()`, then `ReleaseMouseCapture()` (only if `_dragCaptured`), then `App.Widgets.MoveBeforeEnabled(instanceId, anchor)`.

`WidgetManager.MoveBeforeEnabled(instanceId, anchorInstanceId)` keeps disabled widgets at their absolute positions in `settings.json` — only the relative order of *enabled* widgets is rearranged. Anchor=null means "move to end of enabled run".

**Lock state** (`AppSettings.WidgetOrderLocked`):
- Toggle from two places: right-click on panel → "Заблокувати/Розблокувати порядок віджетів"; and Settings → Налаштування панелі → "Заблокувати порядок віджетів" checkbox (saves immediately, not on OK).
- When locked: header drag is disabled (`WidgetContainer_MouseDown` bails out, no `Hand` cursor hint); ↑/↓ buttons in Settings → Віджети become disabled. Add/remove/configure still work.

### Pinned widgets (sticky-top zone)

`WidgetEntry.Pinned` (per-instance bool, default false). Pinned widgets render in a separate `PinnedHost` StackPanel above the scrollable `WidgetsHost`. They keep their relative order from the full widget list (pinned items just "rise" to the top), do **not** participate in drag-and-drop, and have no visible badge — the only signal is the "Відкріпити" menu entry. Toggling is via the per-widget context menu only.

`MainWindow.xaml` structure: `RootGrid` has two rows — `PinnedBorder` (Auto height, `Collapsed` when no pinned widgets) containing its own `ScrollViewer` with `MaxHeight = ActualHeight * 0.6` (updated on `SizeChanged` so pinned can't starve the scroll zone), and the main `PanelScroll` row below it.

`RebuildPanel` is the single redraw entry point. It uses a **FLIP animation** to make pinned↔unpinned transitions feel smooth: snapshot every wrapper's visual `Y` relative to `RootGrid` *before* clearing the hosts, repopulate, force `UpdateLayout()`, then for each wrapper apply `TranslateTransform.Y = oldY - newY` and animate to 0 over 280ms (`CubicEase EaseOut`). This is the same FLIP pattern Todo uses for check/uncheck reorders (see below).

### Unified context menu

There is **one** context-menu architecture, surfaced via right-click on widgets and on the empty panel background. The bottom-of-panel gear button that used to open Settings was removed — Settings open from this menu now.

- `BuildPanelContextMenu()` creates a menu containing only the **panel section** (`AppendPanelMenuItems`), assigned to `MainWindow.ContextMenu` so it shows on empty-area right-click.
- `BuildWidgetContextMenu(widget, entry)` creates a per-widget menu with widget-section items (Закріпити/Відкріпити, Налаштування віджета…, Видалити віджет), a `Separator`, then **the same** `AppendPanelMenuItems` appended at the end. The whole menu is shared by the widget body and is also the source of the menu shown from a right-click anywhere on the widget.
- `AppendPanelMenuItems(menu)` is the shared block: section header + Додати віджет ▶ + Налаштування програми… + Заблокувати/Розблокувати порядок + Вийти з програми. Both dynamic-text items (lock toggle, pin toggle) refresh their headers on `menu.Opened` so external toggles are reflected.

**Section headers** are visually distinct, non-interactive labels styled as a `Separator` with a custom `ControlTemplate` containing a centered `TextBlock` (FontSize=11, SemiBold, MutedTextBrush). Using `Separator` rather than `MenuItem` is intentional — in a `Menu`/`ContextMenu`, `Separator` is intrinsically non-interactive (no hover/focus highlight), which is what we want for a label. A `MenuItem` with `IsHitTestVisible=false` still gets visually highlighted because `MenuBase` wraps non-`MenuItem` children in a container that reacts to mouse.

**Menu icons.** Every `MenuItem` carries an `Icon` — a 16px monochrome `Path` (Material Design geometry) in a `Viewbox`. The themed `MenuItem` template has a dedicated left icon column (`ContentSource="Icon"`, `Auto` width — collapses when an item has no icon). The host builds icons with the `MenuIcon(geometry)` helper; the "Додати віджет" submenu gives each widget type its own icon via `WidgetTypeIconGeometry(typeId)`. The TextBox edit menu (Theme.xaml) and the per-widget "Перейменувати…" menus also carry icons. **Never set `Stretch` on an icon `Path` inside a `Viewbox`** — that combination collapses the shape to zero size; leave the `Path` with default `Stretch` and let the `Viewbox` scale it.

### Per-instance widget titles (rename support)

`Notes`, `Todo`, `Photos` and `Radio` widgets support renaming via inline edit. Pattern (used in all four):
- `WidgetState.Title` field (`string`, default `""`). Empty = use default `DefaultTitle` constant ("Замітка" / "Задачі" / "Фото" / "Радіо").
- `InstanceLabel` override returns `Title` if non-empty, else `DefaultTitle`.
- XAML header has a `Grid` with two layers: `StackPanel` containing display `TextBlock`(s) (and a separate count `TextBlock` for Todo), and an invisible `TextBox` for editing. The display `TextBlock` shows an `IBeam` cursor (it reads as editable text); the rest of the header strip shows `Hand` (drag-to-reorder).
- Trigger: double-click (`MouseLeftButtonDown` with `e.ClickCount == 2`) on the title `TextBlock`, or right-click → "Перейменувати…" from the `TextBlock`'s own `ContextMenu`.
- Edit flow: `BeginRenameTitle()` swaps visibility, focuses TextBox, `SelectAll()`. `PreviewKeyDown` handles Enter (commit) and Escape (cancel via `_editTitleCanceled` flag). `LostFocus` also commits if not already done.
- Commit: trims input; if empty or matches default → store `""` (treated as "use default"). `WidgetServices.RequestSaveStates()`.

`SettingsWindow.BuildRow` uses `widget.InstanceLabel` for the row label (so renamed widgets show their custom name in the list). `MainWindow.ConfirmAndDeleteWidget(instanceId)` looks up `App.Widgets.GetInstance(instanceId).InstanceLabel` **fresh at click time** — don't capture the label at menu-build time, because the user can rename after the menu was built.

### Tray icon menu — themed (`Services/TrayIconService.cs`)

The tray menu is a WinForms `ContextMenuStrip` (NotifyIcon limitation — must be WinForms). WPF DynamicResource doesn't reach it. We style it via a `TrayPalette` (Dark/Light factories mirroring the WPF brush palette in `System.Drawing.Color`) and a `ThemedColorTable : ProfessionalColorTable` that holds a reference to the active palette.

- Two palette factories: `TrayPalette.Dark()` and `TrayPalette.Light()`. Six colors each — Surface, Hover, Border, Text, Separator, plus two for the fallback icon background/foreground. **Keep them in sync with `Themes/Theme.Dark.xaml` and `Themes/Theme.Light.xaml` if you tweak the WPF palette.**
- Constructor picks the palette from `Theme.Current` and self-subscribes to `Theme.ThemeChanged`. On change: disposes the old `ContextMenuStrip`, builds a new one with the new palette, recreates the fallback icon (only used if `shelf.ico` fails to load) so the embedded letter (`П`/`S`) re-renders in the new theme colors.
- `ShowImageMargin = false`, `DropShadowEnabled = false`, `RoundedEdges = false` — these remove vestiges of the default light style that leak through otherwise. The tray icon glyph (`П` / `S`) is drawn from `Loc.Current`.
- `Dispose` unsubscribes from `Theme.ThemeChanged` before hiding the NotifyIcon.

### Cut/Copy/Paste/SelectAll keyboard handling

WPF's default routing for Ctrl+X/C/V/A doesn't reliably reach `TextBox` instances inside our borderless, non-taskbar, Topmost panel window — the shortcuts silently no-op. We work around it in `MainWindow.xaml.cs`:

- Static constructor registers a **class-level handler** via `EventManager.RegisterClassHandler(typeof(TextBox), UIElement.PreviewKeyDownEvent, ...)`. Fires for every TextBox in the app regardless of where it is in the tree.
- For X/C/A the handler bypasses `ApplicationCommands` and does **direct `TextBox` manipulation** (`tb.SelectedText = ""`, `tb.Text.Remove(...)`, `tb.SelectAll()`).
- **For V the handler calls `tb.Paste()`** (not direct text insertion). This is critical because `Paste()` runs the full WPF paste pipeline — including `DataObject.Pasting` handlers, which Todo's multiline-split feature subscribes to. Earlier the V case did direct manipulation like X/C, and the Todo plugin's pasting handler never fired.
- **Cut deletes synchronously first**, then queues clipboard set via `Dispatcher.BeginInvoke(..., DispatcherPriority.Background)`. This ensures the user sees instant feedback even if a misbehaving clipboard hook makes `SetDataObject` slow.
- Host uses `System.Windows.Forms.Clipboard.SetDataObject(text, copy: true, retryTimes: 3, retryDelay: 50)` — max 150ms hang instead of WPF Clipboard's 1-second default. Plugins (no `UseWindowsForms`) must use `System.Windows.Clipboard.SetDataObject(text, true)`.

### Per-widget copy-to-clipboard convention

Notes, Todo, and Photos all have a copy button (`📋` icon) in their header. Pattern:
- Button styled `IconButton`/`IconButtonSubtle`, 20-24×same, with `Viewbox` containing a `Path` of the document icon.
- Click handler builds the payload, calls clipboard (`SetDataObject` for text or `SetImage` for Photos), then flashes `CopyIconPath.Fill = AccentBrush` for 700ms as feedback.
- Todo formats as `<title>:\n- task1\n- task2\n...` (active tasks only, done skipped). Button is disabled when there are no active tasks.
- Photos copies the **currently visible front layer** `BitmapSource` via `Clipboard.SetImage`.
- Header buttons (copy etc.) sit flush at the right content edge — no right-margin clearance (the 22px clearance for the old drag handle was removed once the handle went away).

### Todo: multiline paste → multiple tasks

The "Нова задача" input has a `DataObject.AddPastingHandler` (`OnNewTaskPasting`) attached on `Loaded`/detached on `Unloaded`. If the clipboard text contains `\r` or `\n`, the handler splits it on `\r\n|\r|\n`, strips a leading list marker per line via regex `^\s*(?:\d+[.\)]\s+|[-*•–]\s+)`, drops empty lines, takes the first 500, and inserts each as a new `TodoItem` at position 0 (in **reverse** so the first pasted line ends up on top). The original paste is cancelled via `e.CancelCommand()`. Single-line pastes fall through to WPF default behavior. This handler relies on the host's Ctrl+V class handler calling `tb.Paste()` (see Cut/Copy/Paste section) — direct text-insert in the host would bypass it.

### Todo: reorder animation on check/uncheck

Checking an item moves it to the bottom of `_state.Items`; un-checking moves it back to the bottom of the *active* run (i.e. just above the first completed item). The visual reorder uses a FLIP animation similar to the panel reorder above, but local to the Todo list:
- `AnimateReorder(movedRow, movedItem)` snapshots current visual Y of each row using `LayoutInformation.GetLayoutSlot(child).Y + (TranslateTransform.Y ?? 0)`. Computes the new target index from `_state.Items.IndexOf(movedItem)`. Moves the existing row UIElement in `Children.Remove/Insert` (no rebuild — same instances preserved so focus etc. survive). `UpdateLayout()`. For each row, seeds `TranslateTransform.Y = oldY - newLayoutY` and animates to 0 over 280ms CubicEase EaseOut.
- When `HideCompleted=true` and the item was just checked, the row instead does a 200ms opacity fade-out, then `BuildList()` rebuilds without it.
- Spam-clicking is OK: snapshotting uses the **visual** Y (which includes any in-flight transform), so consecutive animations stack correctly.

### Photos widget (`WidgetPlugins/Shelf.Widgets.Photos`)

Slideshow widget pointing at a user-chosen folder. Two-layer `Image` setup (`PhotoImageBack` / `PhotoImageFront`) both at `Stretch="UniformToFill"` with `RenderTransformOrigin="0.5,0.5"` and `RenderTransform = TransformGroup(ScaleTransform, TranslateTransform)`. The back layer holds the previous photo at its last visual state during transitions, the front layer holds the current and runs Ken Burns.

**Ken Burns** (`StartKenBurnsIfEnabled`):
- `coverScale = max(imgAspect/contAspect, contAspect/imgAspect)` — the scale at which Uniform-fit becomes UniformToFill-cover for *this specific photo's aspect ratio*. Animation target is `coverScale * 1.10` (10% overcrop), always zoom-in from `1.0`. Gives a consistent "fully visible → close-up" feel regardless of orientation.
- Pan ±3% of container dimensions, random direction per photo.
- Duration = `IntervalSeconds` (or 60s if interval is "no change"). `SineEase EaseInOut`.

**Transitions** (`TransitionTo`): four modes — `CrossFade` (default), `FadeBlack`, `Slide`, `None`. The back layer is fixed at the front's last `ScaleX/Y` and `TranslateX/Y` during the 800ms fade so it doesn't visually snap.

**Darkening**: `DarkenOverlay` is a solid-black `Border` sibling that covers the whole photo block; its `Opacity` is driven by `DarkenPercent` (0-100%). The photo container itself has no border (`BorderThickness=0`).

**State**: `Title`, `FolderPath`, `IncludeSubfolders`, `Order` (Random/DateNewest/NameAscending), `Transition`, `KenBurnsEnabled`, `IntervalSeconds`, `Height`, `Grayscale`, `DarkenPercent` (0-100; replaced an older on/off `Darken` bool — old `Darken` JSON is ignored, everyone starts at 0). An older `Fill` enum and `ZoomBlend` transition value have been removed/migrated — `LoadState` maps any persisted `ZoomBlend` to `CrossFade` at load time.

**File scanning**: `Directory.EnumerateFiles` with extension filter `.jpg/.jpeg/.png/.gif/.bmp/.webp/.tiff/.tif`; refreshed when the folder's `LastWriteTime` changes between ticks. `BitmapImage` decodes with `DecodePixelHeight = state.Height * 2.5` and `CacheOption.OnLoad` (immediate file unlock + Ken Burns headroom).

**Folder picker**: `Microsoft.Win32.OpenFolderDialog` (.NET 8) — no `UseWindowsForms` needed.

### Radio widget (`WidgetPlugins/Shelf.Widgets.Radio`)

Internet-radio player. Playback uses WPF `System.Windows.Media.MediaPlayer` — zero extra dependencies, the plugin stays a single DLL.

- **Panel UI:** renameable header; a station row of `◀` / station `ComboBox` / `▶`; a round Play/Stop button (icon only — triangle / square, custom round `ControlTemplate`); a volume row (mute toggle + slider + percent). An error line is hidden in normal states and appears only on failure ("Помилка потоку", "Хибне посилання", "Виберіть станцію").
- **State**: `Title`, `Stations` (`List<RadioStation>` of `Id`/`Name`/`Url`), `SelectedStationId`, `Volume` (0-100), `Muted`, `Initialized`. On first run a built-in list of Ukrainian stations is seeded once; `Initialized` guards against re-seeding after the user clears the list.
- **Volume taper**: the slider 0-100 maps to amplitude as `(pct/100)²` — a linear map made low slider values far too loud (5% sounded like 50%).
- **Playlist resolution**: `.pls`/`.m3u` URLs are fetched over HTTP and the first direct stream URL extracted before `MediaPlayer.Open` (MediaPlayer can't parse playlists). A `_playGeneration` counter discards a stale resolve if the user switched station while it was loading.
- **Lifecycle**: audio keeps playing across panel rebuilds. `Unloaded` defers a check to `DispatcherPriority.Background` and stops playback only if the widget wasn't re-added — i.e. real removal, not a transient `RebuildPanel`.
- The settings dialog manages the station list (`ListBox` + add/remove + name/URL fields).

### Weather widget (`WidgetPlugins/Shelf.Widgets.Weather`)

Current weather plus a one-day forecast, styled like the Radio widget. Data comes from **Open-Meteo** - a free, no-API-key service - over `HttpClient`; JSON is parsed with the built-in `System.Text.Json`, so the plugin stays a single DLL.

- **Panel UI:** renameable header with a small refresh button and a last-update `HH:mm` stamp; a compact content block with the resolved city name, a 2-line **Today** row (weather icon + current temperature + description + a combined `hi/lo · feels-like` line in `TodayMetaText`), a separator, and a single-line **Tomorrow** row (icon + `tomorrow · description` + hi/lo). An error line is hidden in normal states and appears only on failure **and only when there is no cached data to show** (see Cache below).
- **State**: `Title`, `City` (default `"Київ"`), `Latitude`/`Longitude` (`double?` - cached after a successful geocode so each refresh skips geocoding; cleared when the city changes), `ResolvedName`, `Cached` (`CachedSnapshot?` — see Cache).
- **API**: geocoding via `geocoding-api.open-meteo.com/v1/search` (city name → coordinates), forecast via `api.open-meteo.com/v1/forecast` (`current` + `daily` with `forecast_days=2`, `timezone=auto`). A `_fetchGeneration` counter discards a stale response if the city changed or a newer refresh started.
- **Icons**: monochrome vector `Path` geometries (Material Design), one per condition group, mapped from the WMO `weather_code` by `Describe(code)`. `SetIcon` wraps `Geometry.Parse` in try/catch so a malformed path degrades gracefully.
- **Refresh**: auto every 30 minutes via `DispatcherTimer`, plus the manual refresh button. The settings dialog has a single City field; changing it clears the cached coordinates AND `_state.Cached` (the old snapshot is for a different city) and triggers an immediate refresh.
- **Cache (`CachedSnapshot`)**: every successful `RenderWeather(d)` persists a full snapshot (current temp/feels/code, today max/min, tomorrow code/max/min, timestamp) into `_state.Cached` and triggers `ScheduleSave`. On widget load (`ApplyState`), `TryRenderCache()` paints the cached snapshot **before** the async fetch starts, so the user sees data immediately after restart. If the cache is older than `CacheMaxAge` (24h), it's ignored and the widget shows "Завантаження..." as before. When a fetch fails (network timeout, 5xx, etc.), the error line is suppressed if `_hasData` is true — the user keeps seeing the cached snapshot with its original `HH:mm` timestamp; the next successful fetch overwrites. `RenderWeather(d, snapshotTimestamp)` is the single render method: a fresh fetch passes `null` (writes cache, stamps with `DateTime.Now`); rendering an existing cache passes the snapshot's `CachedAt` (no cache rewrite).

### Timer widget (`WidgetPlugins/Shelf.Widgets.Timer`)

Countdown-only widget. **Was previously combined with Stopwatch via a two-button mode toggle; split into two separate widgets** (see Stopwatch widget below) — `enum Mode`, `_state.Mode`, the segmented toggle row, and all stopwatch fields/methods were removed. Self-contained: `HasSettings` is `false`, there is no settings dialog.

- **UI**: idle shows editable `hh:mm:ss` fields (▲▼ steppers + digit-only `TextBox`es) plus quick-preset buttons (1/5/10/25 min); running/paused shows a large `HH:MM:SS` countdown. On reaching zero it plays `System.Media.SystemSounds.Exclamation` a few times and flashes the display (looping opacity `DoubleAnimation`) until **Reset** is pressed.
- **Font**: `FontFamily="{DynamicResource UiFont}"` (Segoe UI) with `Typography.NumeralAlignment="Tabular"` — large 41 pt digits don't jitter while counting because tabular numerals are fixed-width. The previous monospace `Cascadia Mono, Consolas, Segoe UI` was replaced for consistency with the rest of the app.
- **Timing**: `System.Diagnostics.Stopwatch` measures elapsed time for accuracy; a single 50ms `DispatcherTimer` only refreshes the display and runs only while counting.
- **State**: `Title`, `TimerH`/`TimerM`/`TimerS` (configured duration). A running countdown is **not** persisted — restarts idle; the configured duration is kept.
- **Lifecycle**: counting survives panel rebuilds (the deferred-`Unloaded` check stops everything only on real removal, same pattern as Radio).
- **Backward compat**: users who previously had a Timer widget set to "Stopwatch" mode now see the countdown side (the `Mode` field is ignored when deserialising older `WidgetState` JSON). They have to manually add the **Stopwatch** widget to the panel — there is no automatic migration in `WidgetManager.Sync`.

### Stopwatch widget (`WidgetPlugins/Shelf.Widgets.Stopwatch`)

Stopwatch-only widget. `Id="stopwatch"`, `HasSettings=false`. Same visual frame as Timer.

- **UI**: large `MM:SS.cc` display (centiseconds; `H:MM:SS.cc` past an hour); while running buttons are **Lap** / **Pause**, while paused/idle **Reset** / **Start**. Laps newest-first in a scrollable box.
- **Same UiFont + Tabular** as Timer for consistent look.
- **State**: `Title` only. Running elapsed and lap list are **not** persisted — restart resets the stopwatch.
- **Lifecycle**: same Unloaded-deferred pattern as Timer/Radio.
- **Namespace clash**: the widget's own namespace is `Shelf.Widgets.Stopwatch`, which shadows `System.Diagnostics.Stopwatch`. The file aliases `using SwClock = System.Diagnostics.Stopwatch;` to refer to the .NET type unambiguously. Keep this alias; renaming the namespace would change the assembly name and break the existing `<ProjectReference>` line.

### Holidays widget (`WidgetPlugins/Shelf.Widgets.Holidays`)

Holiday + birthday calendar over a configurable date range. Self-contained, no network. Styled like the Weather/Timer widgets.

**Significantly expanded from the original 3-day yesterday/today/tomorrow design.** The widget now carries two parallel user-managed lists (holidays and birthdays), configurable display range, per-day limit, optional icon glyph, optional birth year (for anniversary "(N р.)" suffix), optional free-form note, JSON + TXT export/import. The historical "3 days, all holidays each" behaviour is reproduced by the default state values.

#### Models (`HolidaysWidget.xaml.cs`)

```
WidgetState
├── Title
├── UserHolidays: List<UserHoliday>
├── Birthdays:    List<Birthday>          // parallel category
├── ShowPast, ShowFuture        (bool, both true by default) - toggle past/future blocks
├── DaysBefore, DaysAfter       (int, both 1 by default)    - days to scan on each side
├── MaxPerDay                   (int, default 0 = unlimited) - cap per sub-block
├── ShowHolidays, ShowBirthdays (bool, both true by default) - category visibility

UserHoliday               Birthday                  (parallel structures)
├── Month, Day             ├── Month, Day
├── Name                   ├── Name
├── Year (int?, opt)       ├── Year (int?, opt = birth year)
├── Emoji (string?, opt)   ├── Emoji (string?, default "gift")
└── Note (string?, opt)    └── Note (string?, opt)
```

`Birthday` is a separate class (not a flag on `UserHoliday`) — same shape but distinct semantics (Year is birth year, default Emoji is gift, separate localised editor titles, separate delete-confirm messages). `WidgetManager` does not migrate between them.

#### Built-in dataset (`HolidaysData.cs`)

- ~120 fixed-date Ukrainian holidays (state, religious, professional, international) plus three movable feasts (Palm Sunday / Easter / Trinity) computed per year from Gregorian Easter via `ComputeGregorianEaster` (Gauss algorithm; OCU uses Gregorian Easter since September 2023). Movable feasts cached per year.
- `Holiday` carries `Month`, `Day`, `Name`, `Type`, plus optional `Year`/`Emoji`/`Note` that are only populated for the user-holiday subset (built-in entries leave them null). User holidays are mapped to `Holiday` by `RefreshDisplay` before merging with the built-in dataset via `GetForDate(date, userHolidays)`.
- **Holiday names are Ukrainian only** — even in English UI mode, the dataset stays Ukrainian (it's Ukraine-specific). Localised UI scaffolding (day labels, "Today", "Yesterday", category section headers) does follow the UI language.
- `Birthday` entries are kept separate from the `Holiday` merge — they render in their own sub-block beneath holidays for the same day.

#### Icon palette (`HolidayIcons.cs`)

Ten Material Design Icon glyphs (`gift`, `cake`, `heart`, `star`, `candle`, `cross`, `flag`, `tree`, `balloon`, `flower`) plus a "no icon" choice. Stored as the string ID in `UserHoliday.Emoji`/`Birthday.Emoji`. Rendered as 14-px (or 16-px in the emphasised today row) `Path` with `Fill="{DynamicResource PrimaryTextBrush}"` / `SecondaryTextBrush` so they follow theme switches. `HolidayIcons.GetGeometry(id)` returns `null` for empty/unknown IDs (defensive against future data).

#### Render (`RefreshDisplay` → `BuildDayBlock` → `AppendSubBlock` → `BuildEntryRow`)

For each offset in `[-DaysBefore .. +DaysAfter]` (clamped to 0 when ShowPast/ShowFuture is off), gather:
- **Holidays**: built-in + user merged via `HolidaysData.GetForDate(date, userHolidays)`, only if `ShowHolidays`.
- **Birthdays**: filter `_state.Birthdays` by month/day, only if `ShowBirthdays`.

Skip days where both sub-blocks are empty. Otherwise render: day label (special-cased for −1/0/+1 → ВЧОРА/СЬОГОДНІ/ЗАВТРА; other offsets → `dddd · d MMMM` localised), then the holiday sub-block, then a thin sub-block separator (`Opacity=0.6`) if **both** sub-blocks have entries, then the birthday sub-block. Days are separated by the normal `BuildSeparator()`. Today block (offset=0) uses larger/bolder type.

`AppendSubBlock` applies `MaxPerDay` independently to each sub-block: shows the first N items, then a "+N" badge whose `ToolTip` lists the hidden entries (with notes if any). `BuildEntryRow` returns:
- a flat `TextBlock` if there is no icon AND no note;
- a 2-column `Grid` (icon column Auto + text column *) where the text column is a vertical `StackPanel` of the main name `TextBlock` and an optional note `TextBlock` (smaller, MutedTextBrush).

`FormatHolidayName`/`FormatBirthdayName` append " ({N} р.)" anniversary suffix when `Year` is set and `today.Year - Year >= 1`.

#### Settings dialog (`HolidaysSettingsDialog.xaml`) — three tabs

1. **Налаштування**: section КАТЕГОРІЇ (toggles for `ShowHolidays` / `ShowBirthdays`); ВІДОБРАЖЕННЯ (`CbShowPast` + `DaysBeforeSlider` 1..14, gated by the checkbox; same for future); ЛІМІТ НА ДЕНЬ (`MaxPerDaySlider` 0..15 with 0 displaying as "без обмежень").
2. **Свята**: `ListBox` of user holidays (sorted Month/Day/Name; the display string includes anniversary suffix). Buttons: **Додати**, **Змінити** (enabled on selection), **Експорт…**, **Імпорт…**. Double-click on a row opens the editor.
3. **Дні народження**: same shape with `_birthdayItems`, **Експорт…** and **Імпорт…** for birthdays.

The dialog takes a `WidgetState` snapshot via constructor, works on deep copies of the lists (Cancel discards every edit), and exposes a `ResultState` set on OK. Single editor window (`HolidayEditorWindow`) handles both Add and Edit for both categories via `HolidayEditorKind` enum (Holiday/Birthday) and `HolidayEditorData` DTO. Edit mode also shows a **Видалити** button that confirms with `DarkMessageBox` (`Confirm_DeleteHoliday`/`Confirm_DeleteBirthday`) and returns `HolidayEditorAction.Removed`. The editor includes a Name TextBox, Month/Day ComboBoxes (day clamps to month length on month change), Year ComboBox (first entry = "Без року", then current..1900 descending), Note TextBox, and a `ListBox`-based 6×2 icon palette where the first item is "×" (no icon) and the rest are 10 `Path`-rendered glyphs. Default initial values: today's date; default Emoji is `gift` for new birthdays, null for new holidays.

#### Import / Export

Two buttons per list tab. **Export**: `SaveFileDialog` with filter `JSON (*.json)|JSON (*.json)|*.json|Text (*.txt)|*.txt`, default filename `shelf-holidays-{date}` / `shelf-birthdays-{date}` (yyyy-MM-dd). JSON serialised with `JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }` so Ukrainian characters stay readable. UTF-8 **without BOM**. TXT export is lossy: holidays drop Year (the holiday TXT parser doesn't read a year column), all entries drop Emoji — `DoExport` warns via `DarkMessageBox` when there are entries with an Emoji set before writing TXT.

**Import** (single button per list): `OpenFileDialog` filter is `(*.json;*.txt)|...|*.json|*.txt|*.*`. After `DecodeAuto(bytes)`, `LooksLikeJson(text)` checks whether the trimmed content starts with `[` or `{`. If yes, dispatch to `ImportHolidaysJson` / `ImportBirthdaysJson` (deserialise to the model list, defensively validate each entry — clamp month/day, range-check year, dedup); otherwise fall back to the legacy regex TXT parser. Regex for holidays: `^\s*(\d{1,2})[/\.\-](\d{1,2})\s+(.+?)\s*$`; for birthdays: same plus an optional `[/\.\-]\d{4}` year group. The trailing `\s*\(...)\s*$` content of the name is split off into the Note field by `SplitNameAndNote`. UTF-8 auto-detected first (BOM stripped if present), Windows-1251 fallback via the hand-rolled `Cp1251` table (the .NET 8 desktop runtime ships only UTF and ASCII encodings — keeps the plugin a single DLL). Dedup key = `month+day+name (trimmed, lowered)`. A `DarkMessageBox` summarises imported / duplicates / invalid lines (broken JSON raises the same error dialog instead of a TXT fallback — half-broken JSON is not silently retried as TXT).

#### Refresh

`DispatcherTimer` ticks every 15 minutes; on each tick, if `DateTime.Today` changed (e.g. after midnight) the display re-renders. The widget does **not** persist running state — list edits go through the settings dialog's OK button.

## Conventions worth knowing

- **All menus live in right-click,** there is no chrome (no bottom gear button, no per-widget ⚙ in the Settings window). Right-click on a widget → unified menu (widget section + panel section). Right-click on empty panel area → panel section only. The Settings window opens from the "Налаштування програми…" entry. Don't add back chrome buttons.
- **Per-widget operations** (rename, settings, delete, pin) are exclusively in the right-click menu of the widget. `DarkMessageBox` confirms deletion — never the Win32 `MessageBox`.
- **Never hardcode user-visible strings** — they live in `Strings.uk.xaml` / `Strings.en.xaml` (see **Localization**); every key must exist in both.
- **Notes/Todo state saves are debounced** (`DispatcherTimer` ~800 ms) to avoid hammering disk on every keystroke. `App.OnExit → Widgets.SaveStates()` does a final flush.
- **Host csproj has `<EnableDefaultItems>false</EnableDefaultItems>`** with explicit `<Compile>` globs (`Models\**`, `Services\**`, `Views\**`, plus the two root xaml.cs files). This is because `Shelf.Sdk\` is a subfolder and would otherwise be double-compiled into both the host and the SDK assemblies, producing CS0436 conflicts.
- **Clock, Timer, and Stopwatch use tabular numerals** — `Typography.NumeralAlignment="Tabular"` so digits stay fixed-width while counting. Without it Segoe UI gives proportional widths and the clock visibly jitters.
- **Theme brushes — DynamicResource only** in styles, templates, runtime-built widget content, and Window-level XAML attributes. `{StaticResource <Brush>}` won't react to `Theme.Apply` and breaks live-switching. In `Theme.xaml` styles, the rule is: brushes via `{DynamicResource}`, fonts/style refs via `{StaticResource}` (those don't change with theme).
- **Icon (`Resources/shelf.ico`)** is multi-size (16/24/32/48/64/128/256 PNG-encoded). Generated from `Resources/shelf.png` via `tools/make-ico.ps1` (uses `System.Drawing` from PowerShell — no ImageMagick required). Re-run that script if the PNG is updated. Backup of any previous icon is in `Resources/shelf.ico.bak` (gitignore candidate).
- **Email / Developer / Copyright** in About tab — read from `About_Email` / `About_Developer` / `About_Copyright` keys in `Strings.*.xaml`. Same value in both languages for Email/Developer/Copyright (proper-noun strings). Current contact: `shelf@bridges.net.ua`.
- **DPI:** `app.manifest` declares `PerMonitorV2`. AppBar coords and the virtual-desktop helper use physical pixels; everywhere else WPF DIPs are fine. Don't mix without an explicit `dpiX/dpiY` conversion. (Yes, MSBuild prints `WFAC010` warning about high DPI in app.manifest. It's a false-positive in this hybrid WPF+WinForms setup; leave the manifest alone — the warning is cosmetic.)
- **Smart App Control / WDAC** can block debug builds outright because the build is unsigned. On the current dev machine SAC was switched to Off and `D:\project\Shelf` was added to Microsoft Defender exclusions — both via Windows Security GUI (admin required). If you ever see the app fail to launch with `Could not load file or assembly '...Shelf.Sdk.dll'. An Application Control policy has blocked this file. (0x800711C7)` in Event Viewer / `Shelf.crash.log`, that's WDAC — not a bug in the code. Unblock-File and Defender exclusions don't help; only SAC Off (one-way to clean Windows install) or proper code signing fixes it. See `RELEASE.md` for the signing plan.
