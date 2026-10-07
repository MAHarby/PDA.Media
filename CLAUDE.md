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
- **Models:** `EncodeProfile` holds the FFmpeg settings (codec, preset, CRF, pixel format, audio, streams, subtitles)
  and supports `Clone()`.

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
  `%APPDATA%` settings.
- Add MSTest tests in `PDA.Media.Tests` for new model, service or view-model logic.
- Avalonia 12 is newer than much online material. Check https://docs.avaloniaui.net before using an API
  and don't assume Avalonia 11 or WPF behaviour.
- FFmpeg binaries aren't in the repo. `BatchConverter` expects them in `./bin`.

## Cloud sessions (Claude Code on the web)

`.claude/hooks/session-start.sh` installs the .NET 10 SDK into `~/.dotnet` and restores packages.
`EnableWindowsTargeting=true` is set so the WPF project restores and builds on Linux. The UI can't be run
or viewed there, so verify UI changes on Windows.
