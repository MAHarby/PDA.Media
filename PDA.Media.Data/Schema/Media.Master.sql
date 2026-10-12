/*
    Media.Master: full schema.

    Creates every table in an EMPTY database. To upgrade an existing database, run the scripts in Upgrades/ instead.
    This file is the source of truth for the entity maps (Entities/EntityMaps): SchemaTests checks that the maps
    describe exactly these tables, columns, keys, indexes and foreign keys.

    Text that comes from the media folders (names, descriptions, folders, file names, notes) is nvarchar, so any
    Unicode file name survives; values the code controls (ids, audit users, MediaClass) are varchar.
*/
USE [Media.Master]
GO

-- Lookup and configuration tables.
-- ==================================================================================================

CREATE TABLE [dbo].[AlbumTypes] (
    [Id]                int             IDENTITY(1,1) NOT NULL,
    [Name]              nvarchar(200)   NOT NULL,
    [Description]       nvarchar(500)   NULL,
    CONSTRAINT [PK_AlbumTypes] PRIMARY KEY CLUSTERED ([Id])
);
GO

CREATE TABLE [dbo].[MovieTypes] (
    [Id]                int             IDENTITY(0,1) NOT NULL,
    [Name]              nvarchar(200)   NOT NULL,
    [Description]       nvarchar(500)   NULL,
    CONSTRAINT [PK_MovieTypes] PRIMARY KEY CLUSTERED ([Id])
);
GO

CREATE TABLE [dbo].[TVShowTypes] (
    [Id]                int             IDENTITY(0,1) NOT NULL,
    [Name]              nvarchar(200)   NOT NULL,
    [Description]       nvarchar(500)   NULL,
    CONSTRAINT [PK_TVShowTypes] PRIMARY KEY CLUSTERED ([Id])
);
GO

CREATE TABLE [dbo].[MediaCategories] (
    [Id]                int             IDENTITY(0,1) NOT NULL,
    [Name]              nvarchar(200)   NOT NULL,
    [Description]       nvarchar(500)   NULL,
    [RootFolder]        nvarchar(1000)  NULL,
    [MediaClass]        varchar(50)     NOT NULL CONSTRAINT [DF_MediaCategories_MediaClass] DEFAULT ('Unknown'),
    [AlbumTypeId]       int             NOT NULL CONSTRAINT [DF_MediaCategories_AlbumTypeId] DEFAULT (5),
    [Notes]             nvarchar(max)   NULL,
    [IsActive]          bit             NOT NULL CONSTRAINT [DF_MediaCategories_IsActive] DEFAULT (1),
    [IsDeleted]         bit             NOT NULL CONSTRAINT [DF_MediaCategories_IsDeleted] DEFAULT (0),
    CONSTRAINT [PK_MediaCategories] PRIMARY KEY CLUSTERED ([Id])
);
GO

CREATE TABLE [dbo].[Settings] (
    [Key]               varchar(100)    NOT NULL,
    [Value]             nvarchar(500)   NOT NULL,
    CONSTRAINT [PK_Settings] PRIMARY KEY CLUSTERED ([Key])
);
GO

-- Media cache tables (rebuilt by re-scanning the media folders).
-- ==================================================================================================

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
GO

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
GO

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
GO

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
GO

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
GO

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
GO

-- Indexes.
-- ==================================================================================================

CREATE NONCLUSTERED INDEX [IX_MovieTypes_Name] ON [dbo].[MovieTypes] ([Name]);
CREATE NONCLUSTERED INDEX [IX_MediaCategories_AlbumTypeId] ON [dbo].[MediaCategories] ([AlbumTypeId]);

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
GO

-- Foreign keys.
-- ON DELETE SET DEFAULT moves rows to the default parent (Id 0, or AlbumType 1) when a parent row is deleted.
-- ==================================================================================================

ALTER TABLE [dbo].[Albums] ADD CONSTRAINT [FK_Albums_AlbumTypes] FOREIGN KEY ([AlbumTypeId]) REFERENCES [dbo].[AlbumTypes] ([Id]) ON DELETE SET DEFAULT;
ALTER TABLE [dbo].[Albums] ADD CONSTRAINT [FK_Albums_Artists] FOREIGN KEY ([ArtistId]) REFERENCES [dbo].[Artists] ([Id]) ON UPDATE CASCADE ON DELETE SET DEFAULT;
ALTER TABLE [dbo].[Tracks] ADD CONSTRAINT [FK_Tracks_Albums] FOREIGN KEY ([AlbumId]) REFERENCES [dbo].[Albums] ([Id]) ON DELETE CASCADE;
ALTER TABLE [dbo].[Movies] ADD CONSTRAINT [FK_Movies_MovieTypes] FOREIGN KEY ([MovieTypeId]) REFERENCES [dbo].[MovieTypes] ([Id]) ON UPDATE CASCADE ON DELETE SET DEFAULT;
ALTER TABLE [dbo].[TVShows] ADD CONSTRAINT [FK_TVShows_TVShowTypes] FOREIGN KEY ([TVShowTypeId]) REFERENCES [dbo].[TVShowTypes] ([Id]) ON DELETE SET DEFAULT;
ALTER TABLE [dbo].[TVShowEpisodes] ADD CONSTRAINT [FK_TVShowEpisodes_TVShows] FOREIGN KEY ([TVShowId]) REFERENCES [dbo].[TVShows] ([Id]) ON UPDATE CASCADE;

-- Stored procedures.
-- (The heading is in the batch above: comments in a procedure's own batch become part of its stored definition.)
-- ==================================================================================================
GO

-- Empties the media cache tables (everything a re-scan of the media folders rebuilds), children first, in one transaction.
-- Soft-deleted rows go too. Artist 0 (the albums' fallback) and the lookup and configuration tables are kept. Ids are not restarted.
-- Returns one row with the number of rows deleted from each table.
CREATE PROCEDURE [dbo].[ClearMediaCache]
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Tracks int, @Albums int, @TVShowEpisodes int, @TVShows int, @Movies int, @Artists int;

    BEGIN TRANSACTION;

    DELETE FROM [dbo].[Tracks];
    SET @Tracks = @@ROWCOUNT;
    DELETE FROM [dbo].[Albums];
    SET @Albums = @@ROWCOUNT;
    DELETE FROM [dbo].[TVShowEpisodes];
    SET @TVShowEpisodes = @@ROWCOUNT;
    DELETE FROM [dbo].[TVShows];
    SET @TVShows = @@ROWCOUNT;
    DELETE FROM [dbo].[Movies];
    SET @Movies = @@ROWCOUNT;
    DELETE FROM [dbo].[Artists] WHERE [Id] <> 0;
    SET @Artists = @@ROWCOUNT;

    COMMIT TRANSACTION;

    SELECT @Tracks AS [Tracks], @Albums AS [Albums], @TVShowEpisodes AS [TVShowEpisodes], @TVShows AS [TVShows], @Movies AS [Movies], @Artists AS [Artists];
END
GO
