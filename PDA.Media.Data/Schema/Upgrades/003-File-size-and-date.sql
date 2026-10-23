/*
    Upgrade 003: adds FileSize (bigint) and FileDate (datetime2) to Tracks, Movies and TVShowEpisodes.

    The scan fills them in from the media files (size in bytes and last-modified time), for the Media Library's Analysis tab.
    Existing rows keep their data; the new columns are NULL until the next scan of each category.

    Run once, in SSMS, connected to the server that holds Media.Master. Runs as one transaction: if anything fails, nothing is changed.
*/
USE [Media.Master]
GO

SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;

-- Stop if this upgrade has already been run (Movies.FileSize exists).
IF COL_LENGTH(N'[dbo].[Movies]', N'FileSize') IS NOT NULL
    THROW 50003, 'Upgrade 003 has already been applied to this database.', 1;

ALTER TABLE [dbo].[Tracks] ADD [FileSize] bigint NULL, [FileDate] datetime2 NULL;
ALTER TABLE [dbo].[Movies] ADD [FileSize] bigint NULL, [FileDate] datetime2 NULL;
ALTER TABLE [dbo].[TVShowEpisodes] ADD [FileSize] bigint NULL, [FileDate] datetime2 NULL;

COMMIT TRANSACTION;
PRINT 'Upgrade 003 applied. Scan the media categories to fill in the file sizes and dates.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
GO
