/*
    Upgrade 002: adds the dbo.ClearMediaCache stored procedure (see Schema/Media.Master.sql).

    Run once, in SSMS, connected to the server that holds Media.Master. Changes no data.
    If the procedure already exists, it stops with an error and changes nothing.
*/
USE [Media.Master]
GO

-- Stop if this upgrade has already been run. SET NOEXEC ON skips every later batch in this script.
IF OBJECT_ID(N'[dbo].[ClearMediaCache]', N'P') IS NOT NULL
BEGIN
    RAISERROR('Upgrade 002 has already been applied to this database.', 16, 1);
    SET NOEXEC ON;
END
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

PRINT 'Upgrade 002 complete.';
GO

SET NOEXEC OFF;
GO
