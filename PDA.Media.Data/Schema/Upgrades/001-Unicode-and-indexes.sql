/*
    Upgrade 001: Unicode text columns, datetime2 dates, Albums clustered index and unique indexes.

    Brings a Media.Master database created from the original schema (varchar / text / datetime columns) up to
    Schema/Media.Master.sql. Runs as one transaction: if anything fails, nothing is changed.

    - Lookup and configuration tables (AlbumTypes, MovieTypes, TVShowTypes, MediaCategories, Settings) are changed
      in place and KEEP their rows.
    - Media cache tables (Artists, Albums, Tracks, Movies, TVShows, TVShowEpisodes) are DROPPED and recreated empty;
      re-scan the media folders afterwards. The artist with Id 0 (the default the Albums foreign key falls back to)
      is kept if there is one.

    Take a backup first. Run once, in SSMS, connected to the server that holds Media.Master.
*/
USE [Media.Master]
GO

SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;

-- Stop if this upgrade has already been run (Artists.Name is already nvarchar).
IF EXISTS (SELECT 1 FROM sys.columns WHERE [object_id] = OBJECT_ID(N'[dbo].[Artists]') AND [name] = N'Name'
           AND TYPE_NAME([system_type_id]) = N'nvarchar')
    THROW 50001, 'Upgrade 001 has already been applied to this database.', 1;

-- Keep the Id 0 artist, if any, to put back after Artists is recreated.
SELECT [Id], [Name], [Description], [Folder], [IsDeleted], [IsFavourite], [Notes], [MusicBrainzId],
       [CreatedOn], [CreatedBy], [ModifiedOn], [ModifiedBy]
INTO #KeepArtists
FROM [dbo].[Artists]
WHERE [Id] = 0;

-- Drop the media cache tables, children first (their foreign keys go with them).
DROP TABLE [dbo].[Tracks];
DROP TABLE [dbo].[Albums];
DROP TABLE [dbo].[TVShowEpisodes];
DROP TABLE [dbo].[TVShows];
DROP TABLE [dbo].[Movies];
DROP TABLE [dbo].[Artists];

-- Lookup and configuration tables: change the text columns in place.
ALTER TABLE [dbo].[AlbumTypes] ALTER COLUMN [Name] nvarchar(200) NOT NULL;
ALTER TABLE [dbo].[AlbumTypes] ALTER COLUMN [Description] nvarchar(500) NULL;

DROP INDEX [IX_MovieTypes_Name] ON [dbo].[MovieTypes];
ALTER TABLE [dbo].[MovieTypes] ALTER COLUMN [Name] nvarchar(200) NOT NULL;
ALTER TABLE [dbo].[MovieTypes] ALTER COLUMN [Description] nvarchar(500) NULL;
CREATE NONCLUSTERED INDEX [IX_MovieTypes_Name] ON [dbo].[MovieTypes] ([Name]);

ALTER TABLE [dbo].[TVShowTypes] ALTER COLUMN [Name] nvarchar(200) NOT NULL;
ALTER TABLE [dbo].[TVShowTypes] ALTER COLUMN [Description] nvarchar(500) NULL;

ALTER TABLE [dbo].[MediaCategories] ALTER COLUMN [Name] nvarchar(200) NOT NULL;
ALTER TABLE [dbo].[MediaCategories] ALTER COLUMN [Description] nvarchar(500) NULL;
ALTER TABLE [dbo].[MediaCategories] ALTER COLUMN [RootFolder] nvarchar(1000) NULL;
ALTER TABLE [dbo].[MediaCategories] ALTER COLUMN [Notes] nvarchar(max) NULL;

ALTER TABLE [dbo].[Settings] ALTER COLUMN [Value] nvarchar(500) NOT NULL;

-- Recreate the media cache tables (same definitions as Media.Master.sql).
CREATE TABLE [dbo].[Artists] (
    [Id]                int             IDENTITY(0,1) NOT NULL,
    [Name]              nvarchar(200)   NOT NULL,
    [Description]       nvarchar(500)   NULL,
    [Folder]            nvarchar(1000)  NULL,
    [IsDeleted]         bit             NOT NULL CONSTRAINT [DF_Artists_IsDeleted] DEFAULT (0),
    [IsFavourite]       bit             NOT NULL CONSTRAINT [DF_Artists_IsFavourite] DEFAULT (0),
    [Notes]             nvarchar(max)   NULL,
    [MusicBrainzId]     varchar(100)    NULL,
    [CreatedOn]         datetime2       NOT NULL CONSTRAINT [DF_Artists_CreatedOn] DEFAULT (SYSDATETIME()),
    [CreatedBy]         varchar(100)    NOT NULL CONSTRAINT [DF_Artists_CreatedBy] DEFAULT ('API'),
    [ModifiedOn]        datetime2       NOT NULL CONSTRAINT [DF_Artists_ModifiedOn] DEFAULT (SYSDATETIME()),
    [ModifiedBy]        varchar(100)    NULL,
    CONSTRAINT [PK_Artists] PRIMARY KEY NONCLUSTERED ([Id])
);

CREATE TABLE [dbo].[Albums] (
    [Id]                int             IDENTITY(1,1) NOT NULL,
    [ArtistId]          int             NOT NULL CONSTRAINT [DF_Albums_ArtistId] DEFAULT (0),
    [Name]              nvarchar(200)   NOT NULL,
    [Description]       nvarchar(500)   NULL,
    [AlbumTypeId]       int             NOT NULL CONSTRAINT [DF_Albums_AlbumType] DEFAULT (1),
    [TrackCount]        int             NOT NULL CONSTRAINT [DF_Albums_TrackCount] DEFAULT (1),
    [Length]            int             NOT NULL CONSTRAINT [DF_Albums_Length] DEFAULT (0),
    [Folder]            nvarchar(1000)  NULL,
    [OriginalFilename]  nvarchar(500)   NULL,
    [CoverArtFilename]  nvarchar(500)   NULL,
    [Notes]             nvarchar(max)   NULL,
    [IsDeleted]         bit             NOT NULL CONSTRAINT [DF_Albums_IsDeleted] DEFAULT (0),
    [IsFavourite]       bit             NOT NULL CONSTRAINT [DF_Albums_IsFavourite] DEFAULT (0),
    [MusicBrainzId]     varchar(100)    NULL,
    [CreatedOn]         datetime2       NOT NULL CONSTRAINT [DF_Albums_CreatedOn] DEFAULT (SYSDATETIME()),
    [CreatedBy]         varchar(100)    NOT NULL CONSTRAINT [DF_Albums_CreatedBy] DEFAULT ('API'),
    [ModifiedOn]        datetime2       NOT NULL CONSTRAINT [DF_Albums_ModifiedOn] DEFAULT (SYSDATETIME()),
    [ModifiedBy]        varchar(100)    NULL,
    CONSTRAINT [PK_Albums] PRIMARY KEY NONCLUSTERED ([Id])
);

CREATE TABLE [dbo].[Tracks] (
    [Id]                int             IDENTITY(1,1) NOT NULL,
    [AlbumId]           int             NOT NULL,
    [ArtistId]          int             NOT NULL,
    [Name]              nvarchar(200)   NOT NULL,
    [Description]       nvarchar(500)   NULL,
    [TrackNo]           int             NOT NULL CONSTRAINT [DF_Tracks_TrackNo] DEFAULT (0),
    [Length]            int             NOT NULL CONSTRAINT [DF_Tracks_Length] DEFAULT (0),
    [Folder]            nvarchar(1000)  NULL,
    [OriginalFilename]  nvarchar(500)   NULL,
    [CoverArtFilename]  nvarchar(500)   NULL,
    [Notes]             nvarchar(max)   NULL,
    [IsDeleted]         bit             NOT NULL CONSTRAINT [DF_Tracks_IsDeleted] DEFAULT (0),
    [IsFavourite]       bit             NOT NULL CONSTRAINT [DF_Tracks_IsFavourite] DEFAULT (0),
    [MusicBrainzId]     varchar(100)    NULL,
    [CreatedOn]         datetime2       NOT NULL CONSTRAINT [DF_Tracks_CreatedOn] DEFAULT (SYSDATETIME()),
    [CreatedBy]         varchar(100)    NOT NULL CONSTRAINT [DF_Tracks_CreatedBy] DEFAULT ('API'),
    [ModifiedOn]        datetime2       NOT NULL CONSTRAINT [DF_Tracks_ModifiedOn] DEFAULT (SYSDATETIME()),
    [ModifiedBy]        varchar(100)    NULL,
    CONSTRAINT [PK_Tracks] PRIMARY KEY NONCLUSTERED ([Id])
);

CREATE TABLE [dbo].[Movies] (
    [Id]                int             IDENTITY(1,1) NOT NULL,
    [Name]              nvarchar(200)   NOT NULL,
    [Description]       nvarchar(500)   NULL,
    [MovieTypeId]       int             NOT NULL CONSTRAINT [DF_Movies_MovieTypeId] DEFAULT (0),
    [Notes]             nvarchar(max)   NULL,
    [IsDeleted]         bit             NOT NULL CONSTRAINT [DF_Movies_IsDeleted] DEFAULT (0),
    [IsFavourite]       bit             NOT NULL CONSTRAINT [DF_Movies_IsFavourite] DEFAULT (0),
    [TMDB_Id]           varchar(100)    NULL,
    [CreatedOn]         datetime2       NOT NULL CONSTRAINT [DF_Movies_CreatedOn] DEFAULT (SYSDATETIME()),
    [CreatedBy]         varchar(100)    NOT NULL CONSTRAINT [DF_Movies_CreatedBy] DEFAULT ('API'),
    [UpdatedOn]         datetime2       NOT NULL CONSTRAINT [DF_Movies_UpdatedOn] DEFAULT (SYSDATETIME()),
    [UpdatedBy]         varchar(100)    NULL,
    [Folder]            nvarchar(1000)  NULL,
    [CoverArtFilename]  nvarchar(500)   NULL,
    [OriginalFilename]  nvarchar(500)   NULL,
    CONSTRAINT [PK_Movies] PRIMARY KEY NONCLUSTERED ([Id])
);

CREATE TABLE [dbo].[TVShows] (
    [Id]                int             IDENTITY(1,1) NOT NULL,
    [Name]              nvarchar(200)   NOT NULL,
    [Description]       nvarchar(500)   NULL,
    [TVShowTypeId]      int             NOT NULL CONSTRAINT [DF_TVShows_TVShowTypeId] DEFAULT (0),
    [Notes]             nvarchar(max)   NULL,
    [ReleaseYear]       int             NOT NULL CONSTRAINT [DF_TVShows_ReleaseYear] DEFAULT (0),
    [TMDB_Id]           varchar(100)    NULL,
    [CreatedOn]         datetime2       NOT NULL CONSTRAINT [DF_TVShows_CreatedOn] DEFAULT (SYSDATETIME()),
    [CreatedBy]         varchar(100)    NOT NULL CONSTRAINT [DF_TVShows_CreatedBy] DEFAULT ('API'),
    [UpdatedOn]         datetime2       NOT NULL CONSTRAINT [DF_TVShows_UpdatedOn] DEFAULT (SYSDATETIME()),
    [UpdatedBy]         varchar(100)    NULL,
    [IsDeleted]         bit             NOT NULL CONSTRAINT [DF_TVShows_IsDeleted] DEFAULT (0),
    [IsFavourite]       bit             NOT NULL CONSTRAINT [DF_TVShows_IsFavourite] DEFAULT (0),
    [Folder]            nvarchar(1000)  NULL,
    [CoverArtFilename]  nvarchar(500)   NULL,
    CONSTRAINT [PK_TVShows] PRIMARY KEY CLUSTERED ([Id])
);

CREATE TABLE [dbo].[TVShowEpisodes] (
    [Id]                int             IDENTITY(1,1) NOT NULL,
    [TVShowId]          int             NOT NULL,
    [SeasonNo]          int             NOT NULL CONSTRAINT [DF_TVShowEpisode_SeasonNo] DEFAULT (0),
    [EpisodeNo]         int             NOT NULL CONSTRAINT [DF_TVShowEpisode_EpisodeNo] DEFAULT (0),
    [Name]              nvarchar(200)   NOT NULL,
    [Description]       nvarchar(500)   NULL,
    [Notes]             nvarchar(max)   NULL,
    [TMDB_Id]           varchar(100)    NULL,
    [ReleaseDate]       datetime2       NULL,
    [IsDeleted]         bit             NOT NULL CONSTRAINT [DF_TVShowEpisode_IsDeleted] DEFAULT (0),
    [IsFavourite]       bit             NOT NULL CONSTRAINT [DF_TVShowEpisode_IsFavourite] DEFAULT (0),
    [CreatedOn]         datetime2       NOT NULL CONSTRAINT [DF_TVShowEpisode_CreatedOn] DEFAULT (SYSDATETIME()),
    [CreatedBy]         varchar(100)    NOT NULL CONSTRAINT [DF_TVShowEpisode_CreatedBy] DEFAULT ('API'),
    [UpdatedOn]         datetime2       NOT NULL CONSTRAINT [DF_TVShowEpisode_UpdatedOn] DEFAULT (SYSDATETIME()),
    [UpdatedBy]         varchar(100)    NULL,
    [Folder]            nvarchar(1000)  NULL,
    [OriginalFilename]  nvarchar(500)   NULL,
    [CoverArtFilename]  nvarchar(500)   NULL,
    CONSTRAINT [PK_TVShowEpisodes] PRIMARY KEY NONCLUSTERED ([Id])
);

CREATE CLUSTERED INDEX [IX_Artists_Name] ON [dbo].[Artists] ([Name]);
CREATE NONCLUSTERED INDEX [IX_Artists_IsDeleted] ON [dbo].[Artists] ([IsDeleted]);
CREATE NONCLUSTERED INDEX [IX_Artists_IsFavourite] ON [dbo].[Artists] ([IsFavourite]);
CREATE CLUSTERED INDEX [IX_Albums_Clustered] ON [dbo].[Albums] ([ArtistId], [Id]);
CREATE NONCLUSTERED INDEX [IX_Albums_AlbumTypeId] ON [dbo].[Albums] ([AlbumTypeId]);
CREATE NONCLUSTERED INDEX [IX_Albums_Name] ON [dbo].[Albums] ([Name]);
CREATE NONCLUSTERED INDEX [IX_Albums_IsDeleted] ON [dbo].[Albums] ([IsDeleted]);
CREATE NONCLUSTERED INDEX [IX_Albums_IsFavourite] ON [dbo].[Albums] ([IsFavourite]);
-- One live album per artist and name; a soft-deleted copy doesn't block a new one.
CREATE UNIQUE NONCLUSTERED INDEX [UX_Albums_ArtistId_Name] ON [dbo].[Albums] ([ArtistId], [Name]) WHERE [IsDeleted] = 0;
CREATE CLUSTERED INDEX [IX_Tracks_Clustered] ON [dbo].[Tracks] ([AlbumId], [Id]);
CREATE NONCLUSTERED INDEX [IX_Tracks_AlbumId] ON [dbo].[Tracks] ([AlbumId]);
CREATE NONCLUSTERED INDEX [IX_Tracks_ArtistId] ON [dbo].[Tracks] ([ArtistId]);
CREATE NONCLUSTERED INDEX [IX_Tracks_Name] ON [dbo].[Tracks] ([Name]);
CREATE NONCLUSTERED INDEX [IX_Tracks_IsDeleted] ON [dbo].[Tracks] ([IsDeleted]);
CREATE NONCLUSTERED INDEX [IX_Tracks_IsFavourite] ON [dbo].[Tracks] ([IsFavourite]);
CREATE CLUSTERED INDEX [IX_Movies_Name_Clustered] ON [dbo].[Movies] ([Name]);
CREATE NONCLUSTERED INDEX [IX_Movies_MovieTypeId] ON [dbo].[Movies] ([MovieTypeId]);
CREATE NONCLUSTERED INDEX [IX_Movies_IsDeleted] ON [dbo].[Movies] ([IsDeleted]);
CREATE NONCLUSTERED INDEX [IX_TVShows_Name] ON [dbo].[TVShows] ([Name]);
CREATE NONCLUSTERED INDEX [IX_TVShows_TVShowTypeId] ON [dbo].[TVShows] ([TVShowTypeId]);
CREATE NONCLUSTERED INDEX [IX_TVShows_IsDeleted] ON [dbo].[TVShows] ([IsDeleted]);
CREATE CLUSTERED INDEX [IX_TVShowEpisodes_Clustered] ON [dbo].[TVShowEpisodes] ([TVShowId], [Id]);
CREATE NONCLUSTERED INDEX [IX_TVShowEpisodes_TVShowId] ON [dbo].[TVShowEpisodes] ([TVShowId]);
CREATE NONCLUSTERED INDEX [IX_TVShowEpisodes_Name] ON [dbo].[TVShowEpisodes] ([Name]);
CREATE NONCLUSTERED INDEX [IX_TVShowEpisodes_IsDeleted] ON [dbo].[TVShowEpisodes] ([IsDeleted]);
-- One live episode per show, season and episode number. Episodes whose number couldn't be read (0) are exempt.
CREATE UNIQUE NONCLUSTERED INDEX [UX_TVShowEpisodes_Episode] ON [dbo].[TVShowEpisodes] ([TVShowId], [SeasonNo], [EpisodeNo]) WHERE [IsDeleted] = 0 AND [EpisodeNo] > 0;

ALTER TABLE [dbo].[Albums] ADD CONSTRAINT [FK_Albums_AlbumTypes] FOREIGN KEY ([AlbumTypeId]) REFERENCES [dbo].[AlbumTypes] ([Id]) ON DELETE SET DEFAULT;
ALTER TABLE [dbo].[Albums] ADD CONSTRAINT [FK_Albums_Artists] FOREIGN KEY ([ArtistId]) REFERENCES [dbo].[Artists] ([Id]) ON UPDATE CASCADE ON DELETE SET DEFAULT;
ALTER TABLE [dbo].[Tracks] ADD CONSTRAINT [FK_Tracks_Albums] FOREIGN KEY ([AlbumId]) REFERENCES [dbo].[Albums] ([Id]) ON DELETE CASCADE;
ALTER TABLE [dbo].[Movies] ADD CONSTRAINT [FK_Movies_MovieTypes] FOREIGN KEY ([MovieTypeId]) REFERENCES [dbo].[MovieTypes] ([Id]) ON UPDATE CASCADE ON DELETE SET DEFAULT;
ALTER TABLE [dbo].[TVShows] ADD CONSTRAINT [FK_TVShows_TVShowTypes] FOREIGN KEY ([TVShowTypeId]) REFERENCES [dbo].[TVShowTypes] ([Id]) ON DELETE SET DEFAULT;
ALTER TABLE [dbo].[TVShowEpisodes] ADD CONSTRAINT [FK_TVShowEpisodes_TVShows] FOREIGN KEY ([TVShowId]) REFERENCES [dbo].[TVShows] ([Id]) ON UPDATE CASCADE;

-- Put the Id 0 artist back.
SET IDENTITY_INSERT [dbo].[Artists] ON;
INSERT INTO [dbo].[Artists] ([Id], [Name], [Description], [Folder], [IsDeleted], [IsFavourite], [Notes], [MusicBrainzId],
                             [CreatedOn], [CreatedBy], [ModifiedOn], [ModifiedBy])
SELECT [Id], [Name], [Description], [Folder], [IsDeleted], [IsFavourite], [Notes], [MusicBrainzId],
       [CreatedOn], [CreatedBy], [ModifiedOn], [ModifiedBy]
FROM #KeepArtists;
SET IDENTITY_INSERT [dbo].[Artists] OFF;

DROP TABLE #KeepArtists;

COMMIT TRANSACTION;
PRINT 'Upgrade 001 complete. Re-scan the media folders to refill the cache tables.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT 'Upgrade 001 failed; nothing was changed.';
    THROW;
END CATCH
GO
