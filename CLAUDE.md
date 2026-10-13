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
| `PDA.Media.Scanner` | Class library (`net10.0`) | Scans the media folders into the database (`MediaScanner`). Uses `PDA.Media.Data`; used by `PDA.Media.Utils`. |
| `PDA.Media.Tests` | MSTest 4 (`net10.0`) | References `PDA.Media.Utils`, `PDA.Media.Data` and `PDA.Media.Scanner`. Method-level parallelization is on (`MSTestSettings.cs`). |

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
    (icon-and-text buttons), `Border.ProfileTag` / `TextBlock.ProfileTag` (category badges), `ListBox.AuditLog` / `TextBlock.AuditLogEntry`
    (the console-look log lists of the main window and the media scanner), and the window chrome shared by the profile manager and Media Library
    Services: `Border.HeaderPanel` (heading: `HeaderTitle`, `HeaderSubtitle`, `HeaderActions`), `Border.ContentPanel` (rounded panel) and `Grid.FooterBar`. It is included
    once in `Application.Styles` in `App.axaml`. Put a style here instead of copying it into a second view file;
    a view file can still override it (e.g. `Button.ListAction` shrinks its `ButtonIcon`), since closer styles win.
  - Each view has its own style file named after it: `MainWindowStyles.axaml`, `EncoderProfilesViewStyles.axaml`
    (profile manager window), `EncoderProfileViewStyles.axaml` (its editor form), `LogViewerViewStyles.axaml`, `MediaLibraryViewStyles.axaml` (the window,
    its tabs, and the section / confirmation styles both tabs use), `MediaSearchViewStyles.axaml` and `MediaScannerViewStyles.axaml` (its two tabs). A view includes its file with
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
  `UserSettings`, CreatedBy/ModifiedBy = the Windows user) and the scanner with `services.AddMediaScanner()`;
  `MainViewModel` takes an optional `DatabaseStatusService` and `MediaScanner`. The toolbar's Media Library button (shown when there is a scanner,
  enabled once the database indicator is green: `CanOpenMediaScanner`) opens one `MediaLibraryView` window (from a `MediaLibraryViewModel` factory; a
  second click brings it to the front), and puts the summary of each scan in the main status bar.
- **Media Library Services** (`MediaLibraryView` / `MediaLibraryViewModel`): a window styled like the profile manager (heading, rounded panels, footer)
  with two tabs, each a `UserControl` with its own view model: **Search** (shown first) and **Scanner**. Closing asks first, on the tab concerned, when a
  scan is running or there are unsaved edits (`CanClose`; `CloseRequested` once answered). It loads both tabs' categories when it opens.
- **Search tab** (`MediaSearchView` / `MediaSearchViewModel`): search box (`*` wildcard; Enter searches) over Name, Description and Notes; tick boxes for
  the scannable categories (active ones ticked); Favourites only; Show deleted. Results are a tree of `MediaTreeNode`: Music → Artist → Album → Track,
  Movies → source folder → Movie, TV shows → Show → Season (with missing episode numbers) → Episode. Branches load when opened
  (`LoadChildrenAsync`, a "Loading..." child until then); after a search the way down to the matches is opened (up to 50 paths), and matches are bold.
  The details panel shows cover art (decoded off the UI thread), the read-only fields (`DetailRow`), the file's size / date from disk, Open folder (selects
  the file in Explorer) and Copy path, and edits the hand-editable fields through `MediaItemEditor` (Save Changes / Discard; selecting another row with
  unsaved changes asks first). Names, folders, files, numbers and years are read-only there: the scan owns them or matches rows by them, so they are
  changed with **Rename...** instead (`MediaRenameViewModel`, a panel over the tab: new name, Tidy name, the folder / files options for movies and shows,
  a preview of every disk step and database change that refreshes as you type, Apply; it needs no unsaved edits). **Tidy names...** (`TidyNamesViewModel`)
  works on the selected branch: every item under it (up to 1000) whose `MediaNameTidy` suggestion differs, each checked first (problems unticked),
  editable `TidyRow`s, then Apply renames the ticked rows one at a time, deepest first, each re-planned and with its own rollback; closing after any
  renames runs the search again.
- **Scanner tab** (`MediaScannerView` / `MediaScannerViewModel`): on the left, the media categories (ticked: the ones ticked last time, else the
  active ones; ones that can't be scanned are disabled; each shows its last scan), the options (dry run, hold back deletions over a % of a category's
  rows, clear all media data first: only with every active category ticked, never in a dry run, asked first and not kept) and this run's results per
  category; on the right, the activity log (the scanner's messages and every changed row, kept until cleared, saved to Downloads as text); at the bottom,
  the progress bar (indeterminate while folders are read, then items saved / total), elapsed time, Start / Cancel / Close. Questions (clear all data,
  closing during a scan) are shown over the tab (`ConfirmationPrompt` in `Confirmation`, `ConfirmCommand` / `DismissConfirmationCommand`; the Search tab
  does the same); closing during a scan cancels it and closes once it has stopped (`CanClose`, `CloseRequested`). Progress comes through `UiProgress<T>` (posts to the UI thread; runs at once in tests).
  The ticked categories and options are saved in `UserSettings` (`MediaScan*`).
- **Logging:** Serilog behind `Microsoft.Extensions.Logging`. Inject `ILogger<T>` and use message templates
  (`_logger.LogInformation("Saved {Count} profiles", n)`); don't reference Serilog outside `Logging/` and `Program`.
  The minimum level is Information, except `Microsoft.EntityFrameworkCore` at Warning (`MinimumLevel.Override` in
  `LoggingSetup`; EF logs every SQL command and connection retry at Information). Filter categories there, not with
  `AddFilter` in `ServiceConfiguration`: `AddSerilog` adds a provider rule that overrides category filters. `LoggingSetup` writes to the console and to a new timestamped file per run in
  `%APPDATA%/PDA.Media/Logs` (keeps the latest 30). `AuditLogSink` feeds the Audit Log panel on the main window
  (`MainViewModel.AuditLogEntries`; it keeps every entry until cleared, the ListBox virtualizes), whose buttons clear the panel, open `LogViewerView`, and save a copy to
  Downloads through `LogFileService`.
- **Services** (`Services/`): singletons resolved from DI. They take an optional custom file path so tests can
  redirect storage.
  - `AppSettingsService` stores `UserSettings` as JSON in `%APPDATA%/PDA.Media/settings.json`, including
    `DatabaseServer` / `DatabaseName` (default `PDA-Main` / `Media.Master`, read at startup; restart after changing).
    A view model that saves settings loads them again, changes only its own fields and saves (as `MainViewModel` and
    `MediaScannerViewModel` do), so settings saved by another window survive.
  - `EncoderProfileService` stores `List<EncodeProfile>` in `%APPDATA%/PDA.Media/profiles.json` and
    provides the built-in defaults (`GetDefaultProfiles`).
  - `FFmpegService` finds `ffmpeg`/`ffprobe` in the app's `bin` folder (`<exe folder>/bin`), then on the PATH, and
    configures FFMpegCore. When they're missing, the toolbar's Download button uses `FFMpegCore.Extensions.Downloader`
    (ffbinaries.com, FFmpeg 6.1) to put them in that `bin` folder.
  - `MediaEncodingService` encodes one file with a profile. A file under `MinimumSourceFileSize` (too small or already
    encoded) isn't encoded: it is copied unchanged, keeping its own extension, when it isn't at the destination yet
    (status `Copied`), and skipped when it is. It never modifies the source, writes `<output>.partial` and only replaces
    (overwrites) the real output on success.
  - `MediaNameTidy` (static, pure) suggests tidy names: `PlexNaming.CleanMovieName` for movies and shows (`Title (Year)`; dots kept when the name
    already has spaces), `CleanEpisodeTitle` for episodes, and for music only technical tags (`[FLAC]`, `(Remastered 2011)`, ...) removed; always
    `PlexNaming.SafeName`. Covered by `MediaNameTidyTests`.
  - `PlexNaming` (static, pure) turns a source path into a Plex output path: `Show (Year)/Season 01/Show (Year) - s01e02 -
    Title.mkv` or `Movie (Year)/Movie (Year).mkv`, cutting release tags (Bluray, 1080p, x265, ...). Covered by
    `PlexNamingTests`; add a DataRow there for any new naming case.
- **Models** (`Models/`, namespace `PDA.Media.Utils.Models`): `EncodeProfile` holds the FFmpeg settings (codec, preset,
  CRF, pixel format, audio, streams, subtitles) and supports `Clone()`. Its `GeneratedInputArguments` (e.g. `-hwaccel`)
  go before the input and `GeneratedOutputArguments` after it; `GeneratedFFMpegArguments` is the combined preview.
  `CopySubtitles` (shown as "Include Subtitles") either copies subtitle tracks (`-c:s copy`) or leaves them out (`-sn`);
  never let FFmpeg convert them, because image-based Blu-ray (PGS) subtitles can't be converted and the encode fails. `MediaNode` is a source tree node (selecting a
  folder cascades to its children; folders total their `FileCount` and `Size`, read from the folder listing so no
  extra network calls are made); `FileSize.Format` gives the "12.5 GB" / "850 MB" text used in both lists; `MediaEntityInfo` reads the fields media
  rows share without an interface (favourite, folder, file, cover art); `DestinationItem` is a file queued in the destination list, with its Plex output path and encoding status. Put new model
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
- **Stored procedures** live in `Media.Master.sql` too (after the tables), each in its own batch: comments in that batch
  become part of the stored definition, so keep section headings in the batch before it. An upgrade that adds one
  checks first and uses `RAISERROR` + `SET NOEXEC ON` to skip the rest of the script if it has already run (a
  `CREATE PROCEDURE` can't sit inside a transaction block). `dbo.ClearMediaCache` empties the media cache tables in one
  transaction, keeps Artist 0 and the lookup / configuration tables, doesn't restart ids, and returns one row of counts.
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
    `AsNoTracking()`. `AddOrRestoreRecord(Async)` restores a soft-deleted match; `FindSameRecord(Async)` finds the live or soft-deleted match without
    changing anything. Row changes are logged at Debug (the scanner reports them itself).
  - A service derives from it and supplies `IsSameAs` (its natural key), optionally `DefaultOrder` and `IsProtected`
    (rows that can't be deleted: Artist 0, AlbumType 1, MovieType 0, TVShowType 0, which foreign keys fall back to), and
    its own queries as one-liners over the `List` / `First` helpers (and their async versions).
  - Services: `AlbumService` (artist + name), `ArtistService` (name), `TrackService` (album + track
    number + name), `MovieService` (name), `TVShowService` (name + release year), `TVShowEpisodeService` (show + season +
    episode, or name when the episode number is 0), `MediaCategoryService` (name), the lookup services
    `AlbumTypeService` / `MovieTypeService` / `TVShowTypeService` (name), `SettingService` (key / value, not a
    `DataService`) and `MediaCacheService` (`ClearMediaCache`, which runs `dbo.ClearMediaCache` through
    `Database.SqlQuery<MediaCacheCleared>`; for starting again, since it also loses favourites, notes and external ids).
  - `MediaSearchService` (not a `DataService`): `Search(Async)(MediaSearchCriteria)` finds the artists, movies and TV shows whose Name, Description or
    Notes match (or have an album, track or episode that does) under the given category roots (rows belong to a category by folder, so an artist with
    albums in several music categories is found in each), with favourites-only and include-deleted options and a `MaxResults` cap (`IsTruncated`). It
    returns the top-level rows plus the matched ids and the path albums, so the tree can open down to each match; `GetArtistAlbums` /
    `GetAlbumTracks` / `GetShowEpisodes` (and async) load a branch. `SaveEditableFields(Async)` saves only `CopyEditableFields`: Description, Notes,
    IsFavourite, plus MusicBrainzId, the type, Length and ReleaseDate where the row has them (also on a soft-deleted row). The sync methods run the async
    core with `isAsync: false`.
  - `MediaPathService.ApplyRename(Async)(MediaRenameChanges)`: the database side of a rename, in one SaveChanges: the row's new Name (and a show's
    year), and the Folder / OriginalFilename / CoverArtFilename of every row (any media table, deleted rows too) under the renamed folders or with a
    renamed file. `FolderQueries` holds the shared "folder or under it" query and path move.
- **Tests:** `DataContextTests` use `SqlCaptureContext`, which captures the SQL a save would run without a database;
  `SchemaTests` read `Media.Master.sql` (copied to the test output folder). Service tests (`AlbumServiceTests`, which also cover
  `DataService` in depth, and `DataServiceTests`) run against a real SQL Server: `SqlServerTestDatabase` creates a throwaway database from `Media.Master.sql` per test and
  drops it after; its `ExecuteScript` runs an upgrade script the way SSMS does (one connection, carrying on after
  errors). Set `PDA_MEDIA_TEST_SQL` to a server connection string (e.g.
  `Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true`); without it they report
  Inconclusive. Never point it at the real Media.Master server. In cloud sessions, SQL Server 2022 runs in Docker
  (`dockerd &`, then `docker run ... mcr.microsoft.com/mssql/server:2022-latest` with `MSSQL_COLLATION=SQL_Latin1_General_CP1_CI_AS`).

## PDA.Media.Scanner

- `MediaScanner` gets the data services it needs (and `SettingService`, for the last scans) and an `ILogger<MediaScanner>` through its constructor (logger optional, defaulting to `NullLogger`).
  Register it with `AddMediaScanner()` (`ScannerServiceCollectionExtensions`) after `AddMediaData(...)`; when it needs another service, add a constructor
  parameter (every data service is already registered).
- `ScanMediaCategories(Async)(categoryIds, MediaScanOptions, IProgress<MediaScanProgress>)` scans the given categories (active or not) and returns a
  `MediaScanResult` (rows added, restored, updated, unchanged, deleted, and a `MediaCategoryScanResult` per category: Scanned, DeletionsHeld, Skipped or
  Failed). `ScanAllMediaCategories(Async)(clearData)` scans the active ones with no deletion limit. Only one scan runs at a time (`IsScanning`; a second
  throws). It runs in two phases: every category's folders are read, then each is saved; progress reports carry the phase, folders read, items saved /
  total, each changed row (`MediaScanChange`) and each message (also logged; row changes are not logged: the data services log rows at Debug).
  Options: `DryRun` (compares through `FindSameRecord(Async)` and saves nothing), `MaxDeletePercent` (more deletions than this % of the rows that were
  under the root before the scan are held back and reported; the rest is saved) and `ClearData` (only with every active, scannable category selected,
  else InvalidOperationException; not done when a category couldn't be read). Each category's result is stored as its last scan (JSON in Settings,
  `MediaScanner.LastScan.<id>`, not for a dry run); `GetCategories(Async)` lists the categories with `CanScan` / `CannotScanReason` and `LastScan`. `MediaKinds.FromMediaClass` picks the layout from `MediaClass` (Music; Movie/Movies/Film; TV/TV Show/Series; anything else is
  skipped): music `Artist/Album/NN - Title.ext` (album type from the category), movies `Source/…` (each folder in the root is a source such as `Compressed Blu-Rays`: a video file directly in it is a movie named after the file; below it, a folder holding a video file is a movie named after the folder, using its largest video file and ignoring extras, and a folder without one is searched further; a duplicate name is logged and the first kept),
  TV `Show (Year)/Season NN/… sNNeNN - Title.ext`. Name parsing is in `MediaFileNames` (pure; `MediaFileNamesTests`).
- `MediaRenameService` renames an item on disk and in the database together, so the next scan finds the same row: `PlanAsync(MediaRenameRequest)`
  reads the row again and works out every step (`MediaRenamePlan`: steps, database changes, problems, warnings) without changing anything;
  `ApplyAsync` re-checks the disk, renames files first and then folders (a case-only change goes through a temporary name), then calls
  `MediaPathService`, and puts the disk back if a step or the database update fails (`MediaRenameResult` lists anything that couldn't be put back).
  Movie: its folder and/or the files named like it (a loose movie by its files); TV show: its folder and/or the show-name part of its numbered episode
  files; episode / track: the title part of its file and of files beside it with the same name start; album: its folder; artist: its folder in every
  music category. Checks: names Windows allows, nothing already at the target, no other live row with the name (the natural key), and that the scan
  will read the new file name back as the new name. Movies and TV get a Plex warning. It holds the scanner's busy flag (`MediaScanner.TryReserve`), so a
  rename and a scan never overlap. Tests: `MediaRenameServiceTests` (rename, rescan finds nothing to change), `MediaRenameViewModelTests`.
- An artist can be in more than one music category but has one row: its folder only moves to the category being scanned when the stored folder isn't
  another music category's copy that is still there, and an artist is only soft-deleted when it has no albums left anywhere.
- It is an incremental sync: each item goes through `AddOrRestoreRecordAsync` (restores a soft-deleted match with its id, favourites and notes), only the
  fields the scan owns (folder, file name, cover art, track count, episode title) are updated, and rows under the root (`GetAllRecordsUnderFolder`) that
  weren't seen are soft-deleted. Each category's folders are read completely before anything is written; a root that can't be reached, an unreadable
  folder or a database error skips that category without deleting anything (reporting the innermost exception's message). Hidden / system files and folders starting with `.`, `@` or `#` (Synology
  `@eaDir`, `#recycle`) are ignored. Clearing the cache goes through `MediaCacheService`, after reading and before saving.
- Tests: `MediaScannerTests` build real folder trees in a temp folder and scan them into `SqlServerTestDatabase` (`ListProgress` keeps the progress reports
  in order); `MediaScannerViewModelTests` and `MediaSearchViewModelTests` do the same through the Media Library tabs' view models;
  `MediaSearchServiceTests` cover the search, branches and editable fields.

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
