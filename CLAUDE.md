# PDA.Media

Personal media tooling for batch-transcoding MKV movies and TV shows with FFmpeg (via FFMpegCore).
The main development target is the **Avalonia** app in `PDA.Media.Utils`. The developer works on Windows
(Rider / Visual Studio); media lives on a NAS share (`\\pda-hp-z620\...`).

## Solution layout (`PDA.Media.slnx`, all .NET 10)

| Project | Type | Notes |
| --- | --- | --- |
| `PDA.Media.Utils` | **Avalonia 12** desktop app (`WinExe`, `net10.0`) | Active app: "Media Utilities - Batch Encoder". Views are `.axaml`. |
| `PDA.Media.Desktop` | **WPF** app (`net10.0-windows`, `UseWPF`) | Older UI. Views are `.xaml`, not Avalonia. Don't mix WPF and Avalonia APIs. |
| `PDA.Media.BatchConverter` | Console app (`net10.0`) | Command-line batch encoder; input/output folders are hard-coded (see TODOs in `Program.cs`). |
| `PDA.Media.Data` | Class library (`net10.0`) | Placeholder (empty `Entities/`). |
| `PDA.Media.Tests` | MSTest 4 (`net10.0`) | References `PDA.Media.Utils`. Method-level parallelization is on (`MSTestSettings.cs`). |

## PDA.Media.Utils (Avalonia) architecture

- **MVVM with CommunityToolkit.Mvvm.** View models derive from `ViewModelBase` (an `ObservableObject`).
  Use `[ObservableProperty]` and `[RelayCommand]`. For new code, prefer the partial-property form
  (`[ObservableProperty] public partial string Foo { get; set; }`) as in `MainViewModel`.
  `EncoderProfilesViewModel` still uses the older field form.
- **View resolution:** `ViewLocator` maps `Xxx.ViewModels.FooViewModel` to `Xxx.Views.FooView` by name.
  Keep that naming pair when adding views.
- **Compiled bindings:** every view declares `x:DataType="vm:..."`. Keep it on new views and `DataTemplate`s.
- **Theme:** `FluentTheme` with `DensityStyle="Compact"` and the Inter font (`App.axaml`, `Program.cs`).
  Developer tools are enabled in Debug builds via `WithDeveloperTools()`.
- **Styles and icons** (`Styles/`): keep styling out of the views.
  - `IconResources.axaml` holds every icon as a `StreamGeometry` resource (`folder_open`, `trash_icon`, ...).
    It is merged into `Application.Resources` in `App.axaml`, so any view can use `{StaticResource name}`.
    Add new icons here rather than to a view's `Window.Resources`.
  - `CommonStyles.axaml` holds styles used by more than one view (`StackPanel.ButtonContent` / `PathIcon.ButtonIcon`
    for icon-and-text buttons, `Border.ProfileTag` / `TextBlock.ProfileTag` for category badges). It is included
    once in `Application.Styles` in `App.axaml`. Put a style here instead of copying it into a second view file;
    a view file can still override it (e.g. `Button.ListAction` shrinks its `ButtonIcon`), since closer styles win.
  - Each view has its own style file named after it: `MainWindowStyles.axaml`, `EncoderProfilesViewStyles.axaml`
    (profile manager window) and `EncoderProfileViewStyles.axaml` (its editor form). A view includes its file with
    `<StyleInclude Source="../Styles/<Name>Styles.axaml"/>` in `Window.Styles` / `UserControl.Styles`.
  - Styles are class-based (e.g. `Button.Toolbar`, `Border.EditorSection`, `Label.FieldLabel`) and grouped under
    `<!-- Group. -->` + `<!-- ==== -->` comment headers. Give controls a `Classes` value and put appearance setters
    in the style file; keep layout and behaviour (grid placement, definitions, bindings, commands) on the control.
  - Styles in a window's file also apply inside its user controls. Scope descendant selectors tightly (e.g.
    `^ StackPanel.ButtonContent > TextBlock`, not `^ TextBlock`), because a tooltip's content also counts as a
    descendant of its control.
- **Dependency injection:** `Microsoft.Extensions.DependencyInjection`. Registrations are in `ServiceConfiguration.cs`.
  The container is built in `Program.Main` and exposed as `App.Services`. `App` resolves `MainWindow`, which receives
  `MainViewModel` and an `EncoderProfilesViewModel` factory. When adding a service, view model or window, register
  it there. Keep the existing non-DI constructors (they default to `NullLogger`), because tests and the XAML
  previewer use them.
- **Logging:** Serilog behind `Microsoft.Extensions.Logging`. Inject `ILogger<T>` and use message templates
  (`_logger.LogInformation("Saved {Count} profiles", n)`); don't reference Serilog outside `Logging/` and `Program`.
  The minimum level is Information. `LoggingSetup` writes to the console and to a new timestamped file per run in
  `%APPDATA%/PDA.Media/Logs` (keeps the latest 30). `AuditLogSink` feeds the Audit Log panel on the main window
  (`MainViewModel.AuditLogEntries`), whose buttons clear the panel, open `LogViewerView`, and save a copy to
  Downloads through `LogFileService`.
- **Services** (`Services/`): singletons resolved from DI. They take an optional custom file path so tests can
  redirect storage.
  - `AppSettingsService` stores `UserSettings` as JSON in `%APPDATA%/PDA.Media/settings.json`.
  - `EncoderProfileService` stores `List<EncodeProfile>` in `%APPDATA%/PDA.Media/profiles.json` and
    provides the built-in defaults (`GetDefaultProfiles`).
- **Models** (`Models/`, namespace `PDA.Media.Utils.Models`): `EncodeProfile` holds the FFmpeg settings (codec, preset,
  CRF, pixel format, audio, streams, subtitles) and supports `Clone()`. `MediaNode` is a source tree node (selecting a
  folder cascades to its children); `DestinationItem` is a file queued in the destination list. Put new model
  classes here, not at the bottom of view model files.

## Commands

```bash
dotnet build PDA.Media.slnx
dotnet test PDA.Media.Tests/PDA.Media.Tests.csproj
dotnet test PDA.Media.Tests/PDA.Media.Tests.csproj --filter "FullyQualifiedName~EncodeProfileTests"
dotnet format PDA.Media.slnx --verify-no-changes     # style check
dotnet run --project PDA.Media.Utils                 # needs a desktop/display (Windows)
```

## Conventions

- Nullable reference types are enabled. Keep new code warning-free (the build currently has 0 warnings).
- In tests that touch services, pass a temp file path to the service constructor. Never write to the real
  `%APPDATA%` settings. For view model tests use `TempServices` (`PDA.Media.Tests/TempServices.cs`) and pass its
  services in; the parameterless `MainViewModel()` uses the real settings and profiles files.
- Add MSTest tests in `PDA.Media.Tests` for new model, service or view-model logic.
- Avalonia 12 is newer than much online material. Check https://docs.avaloniaui.net before using an API
  and don't assume Avalonia 11 or WPF behaviour.
- FFmpeg binaries aren't in the repo. `BatchConverter` expects them in `./bin`.

## Cloud sessions (Claude Code on the web)

`.claude/hooks/session-start.sh` installs the .NET 10 SDK into `~/.dotnet` and restores packages.
`EnableWindowsTargeting=true` is set so the WPF project restores and builds on Linux. The Avalonia app can be
started under `Xvfb` for screenshots (settings go to `~/.config/PDA.Media`), but check the final look on Windows.
