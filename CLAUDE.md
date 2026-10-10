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
| `PDA.Media.Data` | Class library (`net10.0`) | EF Core 10 data layer over an existing SQL Server database (see below). Used by `PDA.Media.Utils`. |
| `PDA.Media.Tests` | MSTest 4 (`net10.0`) | References `PDA.Media.Utils` and `PDA.Media.Data`. Method-level parallelization is on (`MSTestSettings.cs`). |

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
  - `CommonStyles.axaml` holds styles used by more than one view: `Grid.WindowContent` (dialog window margin),
    `Button.DialogAction` (Close / Refresh footer buttons), `StackPanel.ButtonContent` / `PathIcon.ButtonIcon`
    (icon-and-text buttons) and `Border.ProfileTag` / `TextBlock.ProfileTag` (category badges). It is included
    once in `Application.Styles` in `App.axaml`. Put a style here instead of copying it into a second view file;
    a view file can still override it (e.g. `Button.ListAction` shrinks its `ButtonIcon`), since closer styles win.
  - Each view has its own style file named after it: `MainWindowStyles.axaml`, `EncoderProfilesViewStyles.axaml`
    (profile manager window), `EncoderProfileViewStyles.axaml` (its editor form) and `LogViewerViewStyles.axaml`. A view includes its file with
    `<StyleInclude Source="../Styles/<Name>Styles.axaml"/>` in `Window.Styles` / `UserControl.Styles`.
  - Styles are class-based (e.g. `Button.Toolbar`, `Border.EditorSection`, `Label.FieldLabel`) and grouped under
    `<!-- Group. -->` + `<!-- ==== -->` comment headers. Give controls a `Classes` value and put appearance setters
    in the style file; keep layout and behaviour (grid placement, definitions, bindings, commands) on the control.
  - For a primary (accent-coloured) button, add the Fluent theme's `accent` class (e.g. `Classes="PrimaryAction accent"`)
    rather than setting `Background`: the theme then handles hover, pressed and disabled colours.
  - Styles in a window's file also apply inside its user controls. Scope descendant selectors tightly (e.g.
    `^ StackPanel.ButtonContent > TextBlock`, not `^ TextBlock`), because a tooltip's content also counts as a
    descendant of its control.
- **Dependency injection:** `Microsoft.Extensions.DependencyInjection`. Registrations are in `ServiceConfiguration.cs`.
  The container is built in `Program.Main` and exposed as `App.Services`. `App` resolves `MainWindow`, which receives
  `MainViewModel` and an `EncoderProfilesViewModel` factory. When adding a service, view model or window, register
  it there. Keep the existing non-DI constructors (they default to `NullLogger`), because tests and the XAML
  previewer use them. The data layer is registered with `services.AddMediaData(...)` (connection string from
  `UserSettings`, CreatedBy/ModifiedBy = the Windows user); `MainViewModel` takes an optional `DatabaseStatusService`.
- **Logging:** Serilog behind `Microsoft.Extensions.Logging`. Inject `ILogger<T>` and use message templates
  (`_logger.LogInformation("Saved {Count} profiles", n)`); don't reference Serilog outside `Logging/` and `Program`.
  The minimum level is Information, except `Microsoft.EntityFrameworkCore` at Warning (`MinimumLevel.Override` in
  `LoggingSetup`; EF logs every SQL command and connection retry at Information). Filter categories there, not with
  `AddFilter` in `ServiceConfiguration`: `AddSerilog` adds a provider rule that overrides category filters. `LoggingSetup` writes to the console and to a new timestamped file per run in
  `%APPDATA%/PDA.Media/Logs` (keeps the latest 30). `AuditLogSink` feeds the Audit Log panel on the main window
  (`MainViewModel.AuditLogEntries`), whose buttons clear the panel, open `LogViewerView`, and save a copy to
  Downloads through `LogFileService`.
- **Services** (`Services/`): singletons resolved from DI. They take an optional custom file path so tests can
  redirect storage.
  - `AppSettingsService` stores `UserSettings` as JSON in `%APPDATA%/PDA.Media/settings.json`, including
    `DatabaseServer` / `DatabaseName` (default `PDA-Main` / `Media.Master`, read at startup; restart after changing).
    `MainViewModel` keeps the loaded `UserSettings` and updates only its own fields when saving, so other settings
    survive; do the same in any other view model that saves settings.
  - `EncoderProfileService` stores `List<EncodeProfile>` in `%APPDATA%/PDA.Media/profiles.json` and
    provides the built-in defaults (`GetDefaultProfiles`).
  - `FFmpegService` finds `ffmpeg`/`ffprobe` in the app's `bin` folder (`<exe folder>/bin`), then on the PATH, and
    configures FFMpegCore. When they're missing, the toolbar's Download button uses `FFMpegCore.Extensions.Downloader`
    (ffbinaries.com, FFmpeg 6.1) to put them in that `bin` folder.
  - `MediaEncodingService` encodes one file with a profile. A file under `MinimumSourceFileSize` (too small or already
    encoded) isn't encoded: it is copied unchanged, keeping its own extension, when it isn't at the destination yet
    (status `Copied`), and skipped when it is. It never modifies the source, writes `<output>.partial` and only replaces
    (overwrites) the real output on success.
  - `PlexNaming` (static, pure) turns a source path into a Plex output path: `Show (Year)/Season 01/Show (Year) - s01e02 -
    Title.mkv` or `Movie (Year)/Movie (Year).mkv`, cutting release tags (Bluray, 1080p, x265, ...). Covered by
    `PlexNamingTests`; add a DataRow there for any new naming case.
- **Models** (`Models/`, namespace `PDA.Media.Utils.Models`): `EncodeProfile` holds the FFmpeg settings (codec, preset,
  CRF, pixel format, audio, streams, subtitles) and supports `Clone()`. Its `GeneratedInputArguments` (e.g. `-hwaccel`)
  go before the input and `GeneratedOutputArguments` after it; `GeneratedFFMpegArguments` is the combined preview.
  `CopySubtitles` (shown as "Include Subtitles") either copies subtitle tracks (`-c:s copy`) or leaves them out (`-sn`);
  never let FFmpeg convert them, because image-based Blu-ray (PGS) subtitles can't be converted and the encode fails. `MediaNode` is a source tree node (selecting a
  folder cascades to its children; folders total their `FileCount` and `Size`, read from the folder listing so no
  extra network calls are made); `FileSize.Format` gives the "12.5 GB" / "850 MB" text used in both lists; `DestinationItem` is a file queued in the destination list, with its Plex output path and encoding status. Put new model
  classes here, not at the bottom of view model files.

## PDA.Media.Data (EF Core)

- **Database-first:** the SQL Server database (`Media.Master`, collation `SQL_Latin1_General_CP1_CI_AS`) is the source
  of truth, described by `Schema/Media.Master.sql` (full create script). There are no EF migrations; never call
  `EnsureCreated`/`Migrate`. The entity maps (`Entities/EntityMaps/*EntityMap.cs`, `IEntityTypeConfiguration<T>`, picked
  up by `ApplyConfigurationsFromAssembly`) must describe that script exactly: `SchemaTests` compares columns, types,
  identity seeds, keys, indexes (clustered / unique / filter), foreign keys and default constraint names.
- **Schema changes:** edit `Media.Master.sql`, add a numbered upgrade script in `Schema/Upgrades/` (one transaction,
  refuses to run twice; the developer runs it in SSMS), and change the maps to match. The media cache tables (Artists,
  Albums, Tracks, Movies, TVShows, TVShowEpisodes) can be emptied and re-scanned; the lookup and configuration tables
  (AlbumTypes, MovieTypes, TVShowTypes, MediaCategories, Settings) and the Id 0 rows must be kept.
- **Text columns:** anything that comes from the media folders (names, descriptions, folders, file names, notes) is
  `nvarchar` (`Notes` is `nvarchar(max)`), because `varchar` with this collation turns non-Western characters into `?`.
  Values the code controls (audit users, `MediaClass`, external ids, setting keys) are `varchar` (`.IsUnicode(false)`).
  Dates are `datetime2`.
- **Foreign keys:** several use `ON DELETE SET DEFAULT` (Id 0 / AlbumType 1), which EF can't express; map them as
  `DeleteBehavior.ClientNoAction` so the database applies it.
- **Connecting:** `DataConnection` builds the Windows-authentication connection string (no password; 5 s connect
  timeout) and the EF options (SQL Server with `EnableRetryOnFailure`). Both `AddMediaData` (the app) and
  `DataContextFactory(connectionString)` (tests, tools) use it; `DataContext` itself has no connection string. Because
  of the retries, a transaction started in code must run inside `context.Database.CreateExecutionStrategy().Execute(...)`.
  `DatabaseStatusService.CanConnectAsync` is the background check behind the status-bar indicator (green / amber /
  red; click to check again). The app must work without the database: never connect on the UI thread or at startup.
- **No logging setup in the library:** don't reference Serilog here; the app passes its logging in through the options.
- **Soft delete:** entities with `IsDeleted` have a named query filter (`DataContext.SoftDeleteFilter`), so queries
  skip deleted rows. Use `IgnoreQueryFilters([DataContext.SoftDeleteFilter])` to include them.
- **Audit fields:** entities implementing `IAuditable` get `CreatedOn`/`CreatedBy`/`ModifiedOn`/`ModifiedBy` set by
  `DataContext` on save (local time, `AuditUser`, default "API"). Don't set them in services. Movie, TVShow and
  TVShowEpisode store `ModifiedOn`/`ModifiedBy` in their `UpdatedOn`/`UpdatedBy` columns.
- **Database defaults:** only map a default EF needs to know about. A non-string column whose database default differs
  from the C# default (e.g. `TrackCount` 1) needs `.ValueGeneratedNever()` and a matching C# initializer, otherwise EF
  leaves 0 out of the INSERT and the database default wins. Defaults of 0 / false needn't be mapped.
- **Services** (`Services/`): take an `IDbContextFactory<DataContext>` (`DataContextFactory` without DI) and create a
  short-lived context per method (`using var context = ...`), never one held for the service's lifetime. Each operation
  has a sync and an async method (keep both; async ones take a `CancellationToken`). All are singletons registered by
  `AddMediaData`; add a new service there.
  - `DataService<TEntity>` (entities implementing `IEntity`: `Id` + `Name`) holds the shared operations: get by id, list,
    search by name (`*` wildcard, turned into an escaped SQL `LIKE` pattern), add (returns the existing record when
    `IsSameAs` matches, also after a unique-index clash), update (loads the row and copies every column onto it,
    keeping the key and Created fields) and delete (soft for `ISoftDeletable`, otherwise the row is removed). Reads use
    `AsNoTracking()`.
  - A service derives from it and supplies `IsSameAs` (its natural key), optionally `DefaultOrder` and `IsProtected`
    (rows that can't be deleted: Artist 0, AlbumType 1, MovieType 0, TVShowType 0, which foreign keys fall back to), and
    its own queries as one-liners over the `List` / `First` helpers (and their async versions).
  - Services: `AlbumService` (artist + name; also `TruncateTable`), `ArtistService` (name), `TrackService` (album + track
    number + name), `MovieService` (name), `TVShowService` (name + release year), `TVShowEpisodeService` (show + season +
    episode, or name when the episode number is 0), `MediaCategoryService` (name), the lookup services
    `AlbumTypeService` / `MovieTypeService` / `TVShowTypeService` (name), and `SettingService` (key / value, not a
    `DataService`).
- **Tests:** `DataContextTests` use `SqlCaptureContext`, which captures the SQL a save would run without a database;
  `SchemaTests` read `Media.Master.sql` (copied to the test output folder). Service tests (`AlbumServiceTests`, which also cover
  `DataService` in depth, and `DataServiceTests`) run against a real SQL Server: `SqlServerTestDatabase` creates a throwaway database from `Media.Master.sql` per test and
  drops it after. Set `PDA_MEDIA_TEST_SQL` to a server connection string (e.g.
  `Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true`); without it they report
  Inconclusive. Never point it at the real Media.Master server. In cloud sessions, SQL Server 2022 runs in Docker
  (`dockerd &`, then `docker run ... mcr.microsoft.com/mssql/server:2022-latest` with `MSSQL_COLLATION=SQL_Latin1_General_CP1_CI_AS`).

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
- FFmpeg binaries aren't in the repo (`bin/` is git-ignored). `BatchConverter` expects them in `./bin`; the Avalonia app
  uses `FFmpegService` (above). Encoding tests that need FFmpeg report Inconclusive when it isn't on the PATH.
- Encoding runs on the UI thread's async context; progress comes through `Progress<T>` created on the UI thread.
  Progress reports are queued, so ignore ones that arrive after a file has finished.
- Encoding progress is encoded video frames / expected frames (mkvmerge's `NUMBER_OF_FRAMES` tag, else frame rate x
  duration), parsed from FFmpeg's `frame=` status lines. Don't use FFmpeg's `time=`: with `-map 0`, copied audio and
  subtitle streams run far ahead of the video being encoded, so `time=` jumps to near the end early (seen with
  FFmpeg 9 on a Blu-ray rip: 93% reported when the video was 30% done).

## Cloud sessions (Claude Code on the web)

`.claude/hooks/session-start.sh` installs the .NET 10 SDK into `~/.dotnet` and restores packages.
`EnableWindowsTargeting=true` is set so the WPF project restores and builds on Linux. The Avalonia app can be
started under `Xvfb` for screenshots (settings go to `~/.config/PDA.Media`), but check the final look on Windows.
FFmpeg can be installed with `apt-get install ffmpeg` for encoding tests; ffbinaries.com (the downloader's source) is
blocked by this environment's network policy.
