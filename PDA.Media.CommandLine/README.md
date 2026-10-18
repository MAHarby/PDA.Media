# pda-media

A command-line search of the PDA.Media library (the `Media.Master` database the PDA.Media app scans into), for Windows and Linux.
It uses the same search as Media Library Services: Name, Description and Notes, with `*` as a wildcard.

```text
$ pda-media search queen
Music (2 artists)
├── ABBA
│   └── Gold (Compilation · 2 tracks)
│       └── 01 Dancing Queen
└── Queen
    ├── A Night at the Opera ★ (Album · 12 tracks)
    └── Live Killers (Live · 22 tracks)
Found 2 artists.
```

## Commands

```text
pda-media search <text> [options]
  -c, --category <names>   Media categories, e.g. Music,Movies or -c Music -c Movies (default: All)
  -t, --type <names>       Album / movie / TV show types, e.g. Single,Live (default: All).
                           Naming types leaves out the kinds of media with none named: -t Single shows only music.
  -f, --favourites         Only favourites (the text can then be left out)
      --deleted            Include rows a scan has soft-deleted (struck through)
      --max <n>            The most results of each kind (default 500)
  -e, --expand             Show everything below the results (albums and tracks, seasons and episodes)
  -p, --paths              Show folders and files, mapped through the config file's path mappings

pda-media config [--init] [--test]
  (no option)              Show the config file in use and its settings (never the password)
  --init                   Create the config file, ready to fill in
  --test                   Try to connect to the database

Every command: --config <file> uses another config file; --help lists the options.
```

`--category:Music` and `--category=Music` work too. On Linux, quote a search with `*` in it (`pda-media search 'abba*'`), or the shell
expands it first.

The tree opens down to each match, and matches are bold:
- A matching track shows its artist and album.
- A matching artist lists its albums.
- A matching show lists its seasons, with any missing episode numbers.

Exit codes: **0** found, **1** nothing found, **2** an error (unknown category or type, no database, a bad config file).

## Building and installing

pda-media is published as **one self-contained file** per OS, so no .NET install is needed on the computer that runs it. From the solution folder:

```bash
dotnet publish PDA.Media.CommandLine -p:PublishProfile=win-x64     # PDA.Media.CommandLine/bin/publish/win-x64/pda-media.exe
dotnet publish PDA.Media.CommandLine -p:PublishProfile=linux-x64   # PDA.Media.CommandLine/bin/publish/linux-x64/pda-media
```

Either can be built on Windows or Linux. To run it from anywhere, put the file in a folder on your PATH.

**Windows** (PowerShell, once):

```powershell
$dir = "$env:LOCALAPPDATA\Programs\pda-media"
New-Item -ItemType Directory -Force $dir | Out-Null
Copy-Item PDA.Media.CommandLine\bin\publish\win-x64\pda-media.exe $dir
[Environment]::SetEnvironmentVariable("Path", [Environment]::GetEnvironmentVariable("Path", "User") + ";$dir", "User")   # then open a new terminal
```

**Linux**:

```bash
install -m 755 PDA.Media.CommandLine/bin/publish/linux-x64/pda-media ~/.local/bin/    # ~/.local/bin is on the PATH in most distributions
# or for every user: sudo install -m 755 PDA.Media.CommandLine/bin/publish/linux-x64/pda-media /usr/local/bin/
```

The Linux build needs the ICU library (`libicu`), which most distributions already have.

## The config file

| OS | Where |
| --- | --- |
| Linux | `~/.config/pda-media/config.json` (or `$XDG_CONFIG_HOME/pda-media/config.json`) |
| Windows | `%APPDATA%\PDA.Media\pda-media.json` (next to the PDA.Media app's `settings.json`) |

`--config <file>` or the `PDA_MEDIA_CONFIG` environment variable use another file. Without a config file, pda-media connects to
**PDA-Main / Media.Master with Windows authentication** and shows paths as stored, which is right on your Windows PC.

`pda-media config --init` creates the file. On Linux it is readable by you only (`chmod 600`), because it holds a password.

```jsonc
{
  "Server": "PDA-Main",               // SQL Server (add ",1433" or "\\INSTANCE" if needed)
  "Database": "Media.Master",
  "UserId": "pda_media",              // a SQL Server login; leave out (null) for Windows authentication
  "Password": "change-me",
  "ConnectionString": null,           // a full connection string instead of the four above, if you prefer
  "PathMappings": [                   // how the database's Windows paths appear on this computer
    { "From": "\\\\pda-hp-z620\\data", "To": "/mnt/nas/data" }
  ]
}
```

### A SQL Server login for Linux

Windows authentication doesn't normally work from Linux, so pda-media logs in with a SQL Server login there. Searching only reads, so the
login only needs to read:

1. **Allow SQL Server logins.** In SSMS, open the server's Properties → Security and choose "SQL Server and Windows Authentication mode",
   then restart the SQL Server service.
2. **Create a read-only login**, as an administrator:

   ```sql
   CREATE LOGIN pda_media WITH PASSWORD = 'a-long-password';
   USE [Media.Master];
   CREATE USER pda_media FOR LOGIN pda_media;
   ALTER ROLE db_datareader ADD MEMBER pda_media;
   ```

3. Put the login in the config file, then run `pda-media config --test`.

### Path mappings

The database stores the paths the scanner saw on Windows, e.g. `\\pda-hp-z620\data\Movies\Heat (1995)\Heat (1995).mkv`. Each mapping
replaces the start of a path, ignoring case and the kind of slash; the longest match wins. When the result is a Linux path, the rest of the
backslashes become slashes:

```text
\\pda-hp-z620\data\Movies\Heat (1995)\Heat (1995).mkv   →   /mnt/nas/data/Movies/Heat (1995)/Heat (1995).mkv
```

A path that no mapping matches is shown as stored.
