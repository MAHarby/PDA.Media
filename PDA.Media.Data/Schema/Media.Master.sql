USE [Media.Master]
GO
/****** Object:  Table [dbo].[Albums]    Script Date: 10/10/2026 14:20:16 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Albums](
    [Id] [int] IDENTITY(1,1) NOT NULL,
    [ArtistId] [int] NOT NULL,
    [Name] [varchar](200) NOT NULL,
    [Description] [varchar](500) NULL,
    [AlbumTypeId] [int] NOT NULL,
    [TrackCount] [int] NOT NULL,
    [Length] [int] NOT NULL,
    [Folder] [varchar](1000) NULL,
    [OriginalFilename] [varchar](500) NULL,
    [CoverArtFilename] [varchar](500) NULL,
    [Notes] [text] NULL,
    [IsDeleted] [bit] NOT NULL,
    [IsFavourite] [bit] NOT NULL,
    [MusicBrainzId] [varchar](100) NULL,
    [CreatedOn] [datetime] NOT NULL,
    [CreatedBy] [varchar](100) NOT NULL,
    [ModifiedOn] [datetime] NOT NULL,
    [ModifiedBy] [varchar](100) NULL,
    CONSTRAINT [PK_Albums] PRIMARY KEY NONCLUSTERED
(
[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
    ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
    GO
/****** Object:  Table [dbo].[AlbumTypes]    Script Date: 10/10/2026 14:20:16 ******/
    SET ANSI_NULLS ON
    GO
    SET QUOTED_IDENTIFIER ON
    GO
CREATE TABLE [dbo].[AlbumTypes](
    [Id] [int] IDENTITY(1,1) NOT NULL,
    [Name] [varchar](200) NOT NULL,
    [Description] [varchar](500) NULL,
    CONSTRAINT [PK_AlbumTypes] PRIMARY KEY CLUSTERED
(
[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
    ) ON [PRIMARY]
    GO
/****** Object:  Table [dbo].[Artists]    Script Date: 10/10/2026 14:20:16 ******/
    SET ANSI_NULLS ON
    GO
    SET QUOTED_IDENTIFIER ON
    GO
CREATE TABLE [dbo].[Artists](
    [Id] [int] IDENTITY(0,1) NOT NULL,
    [Name] [varchar](200) NOT NULL,
    [Description] [varchar](500) NULL,
    [Folder] [varchar](1000) NULL,
    [IsDeleted] [bit] NOT NULL,
    [IsFavourite] [bit] NOT NULL,
    [Notes] [text] NULL,
    [MusicBrainzId] [varchar](100) NULL,
    [CreatedOn] [datetime] NOT NULL,
    [CreatedBy] [varchar](100) NOT NULL,
    [ModifiedOn] [datetime] NOT NULL,
    [ModifiedBy] [varchar](100) NULL,
    CONSTRAINT [PK_Artists] PRIMARY KEY NONCLUSTERED
(
[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
    ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
    GO
    SET ANSI_PADDING ON
    GO
/****** Object:  Index [IX_Artists_Name]    Script Date: 10/10/2026 14:20:16 ******/
CREATE CLUSTERED INDEX [IX_Artists_Name] ON [dbo].[Artists]
(
	[Name] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[MediaCategories]    Script Date: 10/10/2026 14:20:16 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[MediaCategories](
    [Id] [int] IDENTITY(0,1) NOT NULL,
    [Name] [varchar](200) NOT NULL,
    [Description] [varchar](500) NULL,
    [RootFolder] [varchar](1000) NULL,
    [MediaClass] [varchar](50) NOT NULL,
    [AlbumTypeId] [int] NOT NULL,
    [Notes] [text] NULL,
    [IsActive] [bit] NOT NULL,
    [IsDeleted] [bit] NOT NULL,
    CONSTRAINT [PK_MediaCategories] PRIMARY KEY CLUSTERED
(
[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
    ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
    GO
/****** Object:  Table [dbo].[Movies]    Script Date: 10/10/2026 14:20:16 ******/
    SET ANSI_NULLS ON
    GO
    SET QUOTED_IDENTIFIER ON
    GO
CREATE TABLE [dbo].[Movies](
    [Id] [int] IDENTITY(1,1) NOT NULL,
    [Name] [varchar](200) NOT NULL,
    [Description] [varchar](500) NULL,
    [MovieTypeId] [int] NOT NULL,
    [Notes] [text] NULL,
    [IsDeleted] [bit] NOT NULL,
    [IsFavourite] [bit] NOT NULL,
    [TMDB_Id] [varchar](100) NULL,
    [CreatedOn] [datetime] NOT NULL,
    [CreatedBy] [varchar](100) NOT NULL,
    [UpdatedOn] [datetime] NOT NULL,
    [UpdatedBy] [varchar](100) NULL,
    [Folder] [varchar](1000) NULL,
    [CoverArtFilename] [varchar](500) NULL,
    [OriginalFilename] [varchar](500) NULL,
    CONSTRAINT [PK_Movies] PRIMARY KEY NONCLUSTERED
(
[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
    ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
    GO
    SET ANSI_PADDING ON
    GO
/****** Object:  Index [IX_Movies_Name_Clustered]    Script Date: 10/10/2026 14:20:16 ******/
CREATE CLUSTERED INDEX [IX_Movies_Name_Clustered] ON [dbo].[Movies]
(
	[Name] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[MovieTypes]    Script Date: 10/10/2026 14:20:16 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[MovieTypes](
    [Id] [int] IDENTITY(0,1) NOT NULL,
    [Name] [varchar](200) NOT NULL,
    [Description] [varchar](500) NULL,
    CONSTRAINT [PK_MovieTypes] PRIMARY KEY CLUSTERED
(
[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
    ) ON [PRIMARY]
    GO
/****** Object:  Table [dbo].[Settings]    Script Date: 10/10/2026 14:20:16 ******/
    SET ANSI_NULLS ON
    GO
    SET QUOTED_IDENTIFIER ON
    GO
CREATE TABLE [dbo].[Settings](
    [Key] [varchar](100) NOT NULL,
    [Value] [varchar](500) NOT NULL,
    CONSTRAINT [PK_Settings] PRIMARY KEY CLUSTERED
(
[Key] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
    ) ON [PRIMARY]
    GO
/****** Object:  Table [dbo].[Tracks]    Script Date: 10/10/2026 14:20:16 ******/
    SET ANSI_NULLS ON
    GO
    SET QUOTED_IDENTIFIER ON
    GO
CREATE TABLE [dbo].[Tracks](
    [Id] [int] IDENTITY(1,1) NOT NULL,
    [AlbumId] [int] NOT NULL,
    [ArtistId] [int] NOT NULL,
    [Name] [varchar](200) NOT NULL,
    [Description] [varchar](500) NULL,
    [TrackNo] [int] NOT NULL,
    [Length] [int] NOT NULL,
    [Folder] [varchar](1000) NULL,
    [OriginalFilename] [varchar](500) NULL,
    [CoverArtFilename] [varchar](500) NULL,
    [Notes] [text] NULL,
    [IsDeleted] [bit] NOT NULL,
    [IsFavourite] [bit] NOT NULL,
    [MusicBrainzId] [varchar](100) NULL,
    [CreatedOn] [datetime] NOT NULL,
    [CreatedBy] [varchar](100) NOT NULL,
    [ModifiedOn] [datetime] NOT NULL,
    [ModifiedBy] [varchar](100) NULL,
    CONSTRAINT [PK_Tracks] PRIMARY KEY NONCLUSTERED
(
[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
    ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
    GO
/****** Object:  Index [IX_Tracks_Clustered]    Script Date: 10/10/2026 14:20:16 ******/
CREATE CLUSTERED INDEX [IX_Tracks_Clustered] ON [dbo].[Tracks]
(
	[AlbumId] ASC,
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[TVShowEpisodes]    Script Date: 10/10/2026 14:20:16 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[TVShowEpisodes](
    [Id] [int] IDENTITY(1,1) NOT NULL,
    [TVShowId] [int] NOT NULL,
    [SeasonNo] [int] NOT NULL,
    [EpisodeNo] [int] NOT NULL,
    [Name] [varchar](200) NOT NULL,
    [Description] [varchar](500) NULL,
    [Notes] [text] NULL,
    [TMDB_Id] [varchar](100) NULL,
    [ReleaseDate] [datetime] NULL,
    [IsDeleted] [bit] NOT NULL,
    [IsFavourite] [bit] NOT NULL,
    [CreatedOn] [datetime] NOT NULL,
    [CreatedBy] [varchar](100) NOT NULL,
    [UpdatedOn] [datetime] NOT NULL,
    [UpdatedBy] [varchar](100) NULL,
    [Folder] [varchar](1000) NULL,
    [OriginalFilename] [varchar](500) NULL,
    [CoverArtFilename] [varchar](500) NULL,
    CONSTRAINT [PK_TVShowEpisodes] PRIMARY KEY NONCLUSTERED
(
[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
    ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
    GO
/****** Object:  Index [IX_TVShowEpisodes_Clustered]    Script Date: 10/10/2026 14:20:16 ******/
CREATE CLUSTERED INDEX [IX_TVShowEpisodes_Clustered] ON [dbo].[TVShowEpisodes]
(
	[TVShowId] ASC,
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[TVShows]    Script Date: 10/10/2026 14:20:16 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[TVShows](
    [Id] [int] IDENTITY(1,1) NOT NULL,
    [Name] [varchar](200) NOT NULL,
    [Description] [varchar](500) NULL,
    [TVShowTypeId] [int] NOT NULL,
    [Notes] [text] NULL,
    [ReleaseYear] [int] NULL,
    [TMDB_Id] [varchar](100) NULL,
    [CreatedOn] [datetime] NOT NULL,
    [CreatedBy] [varchar](100) NOT NULL,
    [UpdatedOn] [datetime] NOT NULL,
    [UpdatedBy] [varchar](100) NULL,
    [IsDeleted] [bit] NOT NULL,
    [IsFavourite] [bit] NOT NULL,
    [Folder] [varchar](1000) NULL,
    [CoverArtFilename] [varchar](500) NULL,
    CONSTRAINT [PK_TVShows] PRIMARY KEY CLUSTERED
(
[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
    ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
    GO
/****** Object:  Table [dbo].[TVShowTypes]    Script Date: 10/10/2026 14:20:16 ******/
    SET ANSI_NULLS ON
    GO
    SET QUOTED_IDENTIFIER ON
    GO
CREATE TABLE [dbo].[TVShowTypes](
    [Id] [int] IDENTITY(0,1) NOT NULL,
    [Name] [varchar](200) NOT NULL,
    [Description] [varchar](500) NULL,
    CONSTRAINT [PK_TVShowTypes] PRIMARY KEY CLUSTERED
(
[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
    ) ON [PRIMARY]
    GO
/****** Object:  Index [IX_Albums_AlbumTypeId]    Script Date: 10/10/2026 14:20:16 ******/
CREATE NONCLUSTERED INDEX [IX_Albums_AlbumTypeId] ON [dbo].[Albums]
(
	[AlbumTypeId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
/****** Object:  Index [IX_Albums_ArtistId]    Script Date: 10/10/2026 14:20:16 ******/
CREATE NONCLUSTERED INDEX [IX_Albums_ArtistId] ON [dbo].[Albums]
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
/****** Object:  Index [IX_Albums_Clustered]    Script Date: 10/10/2026 14:20:16 ******/
CREATE NONCLUSTERED INDEX [IX_Albums_Clustered] ON [dbo].[Albums]
(
	[ArtistId] ASC,
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
/****** Object:  Index [IX_Albums_IsDeleted]    Script Date: 10/10/2026 14:20:16 ******/
CREATE NONCLUSTERED INDEX [IX_Albums_IsDeleted] ON [dbo].[Albums]
(
	[IsDeleted] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
/****** Object:  Index [IX_Albums_IsFavourite]    Script Date: 10/10/2026 14:20:16 ******/
CREATE NONCLUSTERED INDEX [IX_Albums_IsFavourite] ON [dbo].[Albums]
(
	[IsFavourite] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [IX_Albums_Name]    Script Date: 10/10/2026 14:20:16 ******/
CREATE NONCLUSTERED INDEX [IX_Albums_Name] ON [dbo].[Albums]
(
	[Name] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
/****** Object:  Index [IX_Artists_IsDeleted]    Script Date: 10/10/2026 14:20:16 ******/
CREATE NONCLUSTERED INDEX [IX_Artists_IsDeleted] ON [dbo].[Artists]
(
	[IsDeleted] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
/****** Object:  Index [IX_Artists_IsFavourite]    Script Date: 10/10/2026 14:20:16 ******/
CREATE NONCLUSTERED INDEX [IX_Artists_IsFavourite] ON [dbo].[Artists]
(
	[IsFavourite] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
/****** Object:  Index [IX_MediaCategories_AlbumTypeId]    Script Date: 10/10/2026 14:20:16 ******/
CREATE NONCLUSTERED INDEX [IX_MediaCategories_AlbumTypeId] ON [dbo].[MediaCategories]
(
	[AlbumTypeId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
/****** Object:  Index [IX_Movies_IsDeleted]    Script Date: 10/10/2026 14:20:16 ******/
CREATE NONCLUSTERED INDEX [IX_Movies_IsDeleted] ON [dbo].[Movies]
(
	[IsDeleted] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
/****** Object:  Index [IX_Movies_MovieTypeId]    Script Date: 10/10/2026 14:20:16 ******/
CREATE NONCLUSTERED INDEX [IX_Movies_MovieTypeId] ON [dbo].[Movies]
(
	[MovieTypeId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [IX_MovieTypes_Name]    Script Date: 10/10/2026 14:20:16 ******/
CREATE NONCLUSTERED INDEX [IX_MovieTypes_Name] ON [dbo].[MovieTypes]
(
	[Name] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
/****** Object:  Index [IX_Tracks_AlbumId]    Script Date: 10/10/2026 14:20:16 ******/
CREATE NONCLUSTERED INDEX [IX_Tracks_AlbumId] ON [dbo].[Tracks]
(
	[AlbumId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
/****** Object:  Index [IX_Tracks_ArtistId]    Script Date: 10/10/2026 14:20:16 ******/
CREATE NONCLUSTERED INDEX [IX_Tracks_ArtistId] ON [dbo].[Tracks]
(
	[ArtistId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
/****** Object:  Index [IX_Tracks_IsDeleted]    Script Date: 10/10/2026 14:20:16 ******/
CREATE NONCLUSTERED INDEX [IX_Tracks_IsDeleted] ON [dbo].[Tracks]
(
	[IsDeleted] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
/****** Object:  Index [IX_Tracks_IsFavourite]    Script Date: 10/10/2026 14:20:16 ******/
CREATE NONCLUSTERED INDEX [IX_Tracks_IsFavourite] ON [dbo].[Tracks]
(
	[IsFavourite] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [IX_Tracks_Name]    Script Date: 10/10/2026 14:20:16 ******/
CREATE NONCLUSTERED INDEX [IX_Tracks_Name] ON [dbo].[Tracks]
(
	[Name] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
/****** Object:  Index [IX_TVShowEpisodes_IsDeleted]    Script Date: 10/10/2026 14:20:16 ******/
CREATE NONCLUSTERED INDEX [IX_TVShowEpisodes_IsDeleted] ON [dbo].[TVShowEpisodes]
(
	[IsDeleted] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [IX_TVShowEpisodes_Name]    Script Date: 10/10/2026 14:20:16 ******/
CREATE NONCLUSTERED INDEX [IX_TVShowEpisodes_Name] ON [dbo].[TVShowEpisodes]
(
	[Name] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
/****** Object:  Index [IX_TVShowEpisodes_TVShowId]    Script Date: 10/10/2026 14:20:16 ******/
CREATE NONCLUSTERED INDEX [IX_TVShowEpisodes_TVShowId] ON [dbo].[TVShowEpisodes]
(
	[TVShowId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
/****** Object:  Index [IX_TVShows_IsDeleted]    Script Date: 10/10/2026 14:20:16 ******/
CREATE NONCLUSTERED INDEX [IX_TVShows_IsDeleted] ON [dbo].[TVShows]
(
	[IsDeleted] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [IX_TVShows_Name]    Script Date: 10/10/2026 14:20:16 ******/
CREATE NONCLUSTERED INDEX [IX_TVShows_Name] ON [dbo].[TVShows]
(
	[Name] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
/****** Object:  Index [IX_TVShows_TVShowTypeId]    Script Date: 10/10/2026 14:20:16 ******/
CREATE NONCLUSTERED INDEX [IX_TVShows_TVShowTypeId] ON [dbo].[TVShows]
(
	[TVShowTypeId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
ALTER TABLE [dbo].[Albums] ADD  CONSTRAINT [DF_Albums_ArtistId]  DEFAULT ((0)) FOR [ArtistId]
    GO
ALTER TABLE [dbo].[Albums] ADD  CONSTRAINT [DF_Albums_AlbumType]  DEFAULT ((1)) FOR [AlbumTypeId]
    GO
ALTER TABLE [dbo].[Albums] ADD  CONSTRAINT [DF_Albums_TrackCount]  DEFAULT ((1)) FOR [TrackCount]
    GO
ALTER TABLE [dbo].[Albums] ADD  CONSTRAINT [DF_Albums_Length]  DEFAULT ((0)) FOR [Length]
    GO
ALTER TABLE [dbo].[Albums] ADD  CONSTRAINT [DF_Albums_IsDeleted]  DEFAULT ((0)) FOR [IsDeleted]
    GO
ALTER TABLE [dbo].[Albums] ADD  CONSTRAINT [DF_Albums_IsFavourite]  DEFAULT ((0)) FOR [IsFavourite]
    GO
ALTER TABLE [dbo].[Albums] ADD  CONSTRAINT [DF_Albums_CreatedOn]  DEFAULT (getdate()) FOR [CreatedOn]
    GO
ALTER TABLE [dbo].[Albums] ADD  CONSTRAINT [DF_Albums_CreatedBy]  DEFAULT ('API') FOR [CreatedBy]
    GO
ALTER TABLE [dbo].[Albums] ADD  CONSTRAINT [DF_Albums_ModifiedOn]  DEFAULT (getdate()) FOR [ModifiedOn]
    GO
ALTER TABLE [dbo].[Artists] ADD  CONSTRAINT [DF_Artists_IsDeleted]  DEFAULT ((0)) FOR [IsDeleted]
    GO
ALTER TABLE [dbo].[Artists] ADD  CONSTRAINT [DF_Artists_IsFavourite]  DEFAULT ((0)) FOR [IsFavourite]
    GO
ALTER TABLE [dbo].[Artists] ADD  CONSTRAINT [DF_Artists_CreatedOn]  DEFAULT (getdate()) FOR [CreatedOn]
    GO
ALTER TABLE [dbo].[Artists] ADD  CONSTRAINT [DF_Artists_CreatedBy]  DEFAULT ('API') FOR [CreatedBy]
    GO
ALTER TABLE [dbo].[Artists] ADD  CONSTRAINT [DF_Artists_ModifiedOn]  DEFAULT (getdate()) FOR [ModifiedOn]
    GO
ALTER TABLE [dbo].[MediaCategories] ADD  CONSTRAINT [DF_MediaCategories_MediaClass]  DEFAULT ('Unknown') FOR [MediaClass]
    GO
ALTER TABLE [dbo].[MediaCategories] ADD  CONSTRAINT [DF_MediaCategories_AlbumTypeId]  DEFAULT ((5)) FOR [AlbumTypeId]
    GO
ALTER TABLE [dbo].[MediaCategories] ADD  CONSTRAINT [DF_MediaCategories_IsActive]  DEFAULT ((1)) FOR [IsActive]
    GO
ALTER TABLE [dbo].[MediaCategories] ADD  CONSTRAINT [DF_MediaCategories_IsDeleted]  DEFAULT ((0)) FOR [IsDeleted]
    GO
ALTER TABLE [dbo].[Movies] ADD  CONSTRAINT [DF_Movies_MovieTypeId]  DEFAULT ((0)) FOR [MovieTypeId]
    GO
ALTER TABLE [dbo].[Movies] ADD  CONSTRAINT [DF_Movies_IsDeleted]  DEFAULT ((0)) FOR [IsDeleted]
    GO
ALTER TABLE [dbo].[Movies] ADD  CONSTRAINT [DF_Movies_IsFavourite]  DEFAULT ((0)) FOR [IsFavourite]
    GO
ALTER TABLE [dbo].[Movies] ADD  CONSTRAINT [DF_Movies_CreatedOn]  DEFAULT (getdate()) FOR [CreatedOn]
    GO
ALTER TABLE [dbo].[Movies] ADD  CONSTRAINT [DF_Movies_CreatedBy]  DEFAULT ('API') FOR [CreatedBy]
    GO
ALTER TABLE [dbo].[Movies] ADD  CONSTRAINT [DF_Movies_UpdatedOn]  DEFAULT (getdate()) FOR [UpdatedOn]
    GO
ALTER TABLE [dbo].[Tracks] ADD  CONSTRAINT [DF_Tracks_TrackNo]  DEFAULT ((0)) FOR [TrackNo]
    GO
ALTER TABLE [dbo].[Tracks] ADD  CONSTRAINT [DF_Tracks_Length]  DEFAULT ((0)) FOR [Length]
    GO
ALTER TABLE [dbo].[Tracks] ADD  CONSTRAINT [DF_Tracks_IsDeleted]  DEFAULT ((0)) FOR [IsDeleted]
    GO
ALTER TABLE [dbo].[Tracks] ADD  CONSTRAINT [DF_Tracks_IsFavourite]  DEFAULT ((0)) FOR [IsFavourite]
    GO
ALTER TABLE [dbo].[Tracks] ADD  CONSTRAINT [DF_Tracks_CreatedOn]  DEFAULT (getdate()) FOR [CreatedOn]
    GO
ALTER TABLE [dbo].[Tracks] ADD  CONSTRAINT [DF_Tracks_CreatedBy]  DEFAULT ('API') FOR [CreatedBy]
    GO
ALTER TABLE [dbo].[Tracks] ADD  CONSTRAINT [DF_Tracks_ModifiedOn]  DEFAULT (getdate()) FOR [ModifiedOn]
    GO
ALTER TABLE [dbo].[TVShowEpisodes] ADD  CONSTRAINT [DF_TVShowEpisode_SeasonNo]  DEFAULT ((0)) FOR [SeasonNo]
    GO
ALTER TABLE [dbo].[TVShowEpisodes] ADD  CONSTRAINT [DF_TVShowEpisode_EpisodeNo]  DEFAULT ((0)) FOR [EpisodeNo]
    GO
ALTER TABLE [dbo].[TVShowEpisodes] ADD  CONSTRAINT [DF_TVShowEpisode_IsDeleted]  DEFAULT ((0)) FOR [IsDeleted]
    GO
ALTER TABLE [dbo].[TVShowEpisodes] ADD  CONSTRAINT [DF_TVShowEpisode_IsFavourite]  DEFAULT ((0)) FOR [IsFavourite]
    GO
ALTER TABLE [dbo].[TVShowEpisodes] ADD  CONSTRAINT [DF_TVShowEpisode_CreatedOn]  DEFAULT (getdate()) FOR [CreatedOn]
    GO
ALTER TABLE [dbo].[TVShowEpisodes] ADD  CONSTRAINT [DF_TVShowEpisode_CreatedBy]  DEFAULT ('API') FOR [CreatedBy]
    GO
ALTER TABLE [dbo].[TVShowEpisodes] ADD  CONSTRAINT [DF_TVShowEpisode_UpdatedOn]  DEFAULT (getdate()) FOR [UpdatedOn]
    GO
ALTER TABLE [dbo].[TVShows] ADD  CONSTRAINT [DF_TVShows_TVShowTypeId]  DEFAULT ((0)) FOR [TVShowTypeId]
    GO
ALTER TABLE [dbo].[TVShows] ADD  CONSTRAINT [DF_TVShows_ReleaseYear]  DEFAULT ((0)) FOR [ReleaseYear]
    GO
ALTER TABLE [dbo].[TVShows] ADD  CONSTRAINT [DF_TVShows_CreatedOn]  DEFAULT (getdate()) FOR [CreatedOn]
    GO
ALTER TABLE [dbo].[TVShows] ADD  CONSTRAINT [DF_TVShows_CreatedBy]  DEFAULT ('API') FOR [CreatedBy]
    GO
ALTER TABLE [dbo].[TVShows] ADD  CONSTRAINT [DF_TVShows_UpdatedOn]  DEFAULT (getdate()) FOR [UpdatedOn]
    GO
ALTER TABLE [dbo].[TVShows] ADD  CONSTRAINT [DF_TVShows_IsDeleted]  DEFAULT ((0)) FOR [IsDeleted]
    GO
ALTER TABLE [dbo].[TVShows] ADD  CONSTRAINT [DF_TVShows_IsFavourite]  DEFAULT ((0)) FOR [IsFavourite]
    GO
ALTER TABLE [dbo].[Albums]  WITH CHECK ADD  CONSTRAINT [FK_Albums_AlbumTypes] FOREIGN KEY([AlbumTypeId])
    REFERENCES [dbo].[AlbumTypes] ([Id])
    ON DELETE SET DEFAULT
GO
ALTER TABLE [dbo].[Albums] CHECK CONSTRAINT [FK_Albums_AlbumTypes]
    GO
ALTER TABLE [dbo].[Albums]  WITH CHECK ADD  CONSTRAINT [FK_Albums_Artists] FOREIGN KEY([ArtistId])
    REFERENCES [dbo].[Artists] ([Id])
    ON UPDATE CASCADE
       ON DELETE SET DEFAULT
GO
ALTER TABLE [dbo].[Albums] CHECK CONSTRAINT [FK_Albums_Artists]
    GO
ALTER TABLE [dbo].[Movies]  WITH CHECK ADD  CONSTRAINT [FK_Movies_MovieTypes] FOREIGN KEY([MovieTypeId])
    REFERENCES [dbo].[MovieTypes] ([Id])
    ON UPDATE CASCADE
       ON DELETE SET DEFAULT
GO
ALTER TABLE [dbo].[Movies] CHECK CONSTRAINT [FK_Movies_MovieTypes]
    GO
ALTER TABLE [dbo].[Tracks]  WITH CHECK ADD  CONSTRAINT [FK_Tracks_Albums] FOREIGN KEY([AlbumId])
    REFERENCES [dbo].[Albums] ([Id])
    ON DELETE CASCADE
GO
ALTER TABLE [dbo].[Tracks] CHECK CONSTRAINT [FK_Tracks_Albums]
    GO
ALTER TABLE [dbo].[TVShowEpisodes]  WITH CHECK ADD  CONSTRAINT [FK_TVShowEpisodes_TVShows] FOREIGN KEY([TVShowId])
    REFERENCES [dbo].[TVShows] ([Id])
    ON UPDATE CASCADE
           GO
ALTER TABLE [dbo].[TVShowEpisodes] CHECK CONSTRAINT [FK_TVShowEpisodes_TVShows]
    GO
ALTER TABLE [dbo].[TVShows]  WITH CHECK ADD  CONSTRAINT [FK_TVShows_TVShowTypes] FOREIGN KEY([TVShowTypeId])
    REFERENCES [dbo].[TVShowTypes] ([Id])
    ON DELETE SET DEFAULT
GO
ALTER TABLE [dbo].[TVShows] CHECK CONSTRAINT [FK_TVShows_TVShowTypes]
    GO
