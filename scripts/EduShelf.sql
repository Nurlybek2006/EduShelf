IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetRoles] (
        [Id] nvarchar(450) NOT NULL,
        [Name] nvarchar(256) NULL,
        [NormalizedName] nvarchar(256) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUsers] (
        [Id] nvarchar(450) NOT NULL,
        [FullName] nvarchar(120) NOT NULL,
        [UserName] nvarchar(256) NULL,
        [NormalizedUserName] nvarchar(256) NULL,
        [Email] nvarchar(256) NULL,
        [NormalizedEmail] nvarchar(256) NULL,
        [EmailConfirmed] bit NOT NULL,
        [PasswordHash] nvarchar(max) NULL,
        [SecurityStamp] nvarchar(max) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        [PhoneNumber] nvarchar(max) NULL,
        [PhoneNumberConfirmed] bit NOT NULL,
        [TwoFactorEnabled] bit NOT NULL,
        [LockoutEnd] datetimeoffset NULL,
        [LockoutEnabled] bit NOT NULL,
        [AccessFailedCount] int NOT NULL,
        CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE TABLE [ResourceTypes] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(80) NOT NULL,
        CONSTRAINT [PK_ResourceTypes] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE TABLE [Subjects] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(100) NOT NULL,
        [Description] nvarchar(500) NULL,
        CONSTRAINT [PK_Subjects] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE TABLE [Tags] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(60) NOT NULL,
        CONSTRAINT [PK_Tags] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetRoleClaims] (
        [Id] int NOT NULL IDENTITY,
        [RoleId] nvarchar(450) NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserClaims] (
        [Id] int NOT NULL IDENTITY,
        [UserId] nvarchar(450) NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserLogins] (
        [LoginProvider] nvarchar(450) NOT NULL,
        [ProviderKey] nvarchar(450) NOT NULL,
        [ProviderDisplayName] nvarchar(max) NULL,
        [UserId] nvarchar(450) NOT NULL,
        CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
        CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserRoles] (
        [UserId] nvarchar(450) NOT NULL,
        [RoleId] nvarchar(450) NOT NULL,
        CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserTokens] (
        [UserId] nvarchar(450) NOT NULL,
        [LoginProvider] nvarchar(450) NOT NULL,
        [Name] nvarchar(450) NOT NULL,
        [Value] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
        CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE TABLE [Topics] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(120) NOT NULL,
        [SubjectId] int NOT NULL,
        CONSTRAINT [PK_Topics] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Topics_Id_SubjectId] UNIQUE ([Id], [SubjectId]),
        CONSTRAINT [FK_Topics_Subjects_SubjectId] FOREIGN KEY ([SubjectId]) REFERENCES [Subjects] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE TABLE [Resources] (
        [Id] int NOT NULL IDENTITY,
        [Title] nvarchar(200) NOT NULL,
        [Description] nvarchar(4000) NOT NULL,
        [SubjectId] int NOT NULL,
        [TopicId] int NULL,
        [ResourceTypeId] int NOT NULL,
        [OwnerId] nvarchar(450) NOT NULL,
        [OriginalFileName] nvarchar(255) NOT NULL,
        [StoredFileName] nvarchar(80) NOT NULL,
        [ContentType] nvarchar(100) NOT NULL,
        [FileSize] bigint NOT NULL,
        [Extension] nvarchar(12) NOT NULL,
        [FileHash] nvarchar(64) NOT NULL,
        [ExtractedText] nvarchar(max) NULL,
        [AccessLevel] nvarchar(20) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Resources] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Resources_AccessLevel] CHECK ([AccessLevel] IN ('Public','Authenticated')),
        CONSTRAINT [CK_Resources_FileSize] CHECK ([FileSize] > 0),
        CONSTRAINT [CK_Resources_Status] CHECK ([Status] IN ('Draft','Published','Archived')),
        CONSTRAINT [FK_Resources_AspNetUsers_OwnerId] FOREIGN KEY ([OwnerId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Resources_ResourceTypes_ResourceTypeId] FOREIGN KEY ([ResourceTypeId]) REFERENCES [ResourceTypes] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Resources_Subjects_SubjectId] FOREIGN KEY ([SubjectId]) REFERENCES [Subjects] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Resources_Topics_TopicId_SubjectId] FOREIGN KEY ([TopicId], [SubjectId]) REFERENCES [Topics] ([Id], [SubjectId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE TABLE [Favorites] (
        [UserId] nvarchar(450) NOT NULL,
        [ResourceId] int NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Favorites] PRIMARY KEY ([UserId], [ResourceId]),
        CONSTRAINT [FK_Favorites_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Favorites_Resources_ResourceId] FOREIGN KEY ([ResourceId]) REFERENCES [Resources] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE TABLE [ResourceAnalyses] (
        [ResourceId] int NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [DetectedTopic] nvarchar(200) NULL,
        [Summary] nvarchar(4000) NULL,
        [Difficulty] nvarchar(50) NULL,
        [Keywords] nvarchar(2000) NULL,
        [Provider] nvarchar(100) NULL,
        [Model] nvarchar(100) NULL,
        [AnalyzedAtUtc] datetime2 NULL,
        [ErrorCode] nvarchar(100) NULL,
        [AnalyzedFileHash] nvarchar(64) NULL,
        CONSTRAINT [PK_ResourceAnalyses] PRIMARY KEY ([ResourceId]),
        CONSTRAINT [FK_ResourceAnalyses_Resources_ResourceId] FOREIGN KEY ([ResourceId]) REFERENCES [Resources] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE TABLE [ResourceDownloads] (
        [Id] bigint NOT NULL IDENTITY,
        [ResourceId] int NOT NULL,
        [UserId] nvarchar(450) NULL,
        [OccurredAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_ResourceDownloads] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ResourceDownloads_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ResourceDownloads_Resources_ResourceId] FOREIGN KEY ([ResourceId]) REFERENCES [Resources] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE TABLE [ResourceTags] (
        [ResourceId] int NOT NULL,
        [TagId] int NOT NULL,
        CONSTRAINT [PK_ResourceTags] PRIMARY KEY ([ResourceId], [TagId]),
        CONSTRAINT [FK_ResourceTags_Resources_ResourceId] FOREIGN KEY ([ResourceId]) REFERENCES [Resources] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ResourceTags_Tags_TagId] FOREIGN KEY ([TagId]) REFERENCES [Tags] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE TABLE [ResourceViews] (
        [Id] bigint NOT NULL IDENTITY,
        [ResourceId] int NOT NULL,
        [UserId] nvarchar(450) NULL,
        [OccurredAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_ResourceViews] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ResourceViews_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ResourceViews_Resources_ResourceId] FOREIGN KEY ([ResourceId]) REFERENCES [Resources] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Favorites_ResourceId] ON [Favorites] ([ResourceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ResourceDownloads_ResourceId] ON [ResourceDownloads] ([ResourceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ResourceDownloads_UserId_OccurredAtUtc] ON [ResourceDownloads] ([UserId], [OccurredAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Resources_FileHash] ON [Resources] ([FileHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Resources_OwnerId_Status] ON [Resources] ([OwnerId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Resources_ResourceTypeId] ON [Resources] ([ResourceTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Resources_Status_AccessLevel_CreatedAtUtc] ON [Resources] ([Status], [AccessLevel], [CreatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Resources_StoredFileName] ON [Resources] ([StoredFileName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Resources_SubjectId] ON [Resources] ([SubjectId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Resources_TopicId_SubjectId] ON [Resources] ([TopicId], [SubjectId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ResourceTags_TagId] ON [ResourceTags] ([TagId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ResourceTypes_Name] ON [ResourceTypes] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ResourceViews_ResourceId] ON [ResourceViews] ([ResourceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ResourceViews_UserId_OccurredAtUtc] ON [ResourceViews] ([UserId], [OccurredAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Subjects_Name] ON [Subjects] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Tags_Name] ON [Tags] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Topics_SubjectId_Name] ON [Topics] ([SubjectId], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020103236_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261020103236_InitialCreate', N'10.0.13');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020110540_PhaseTwoProcessingSearch'
)
BEGIN
    ALTER TABLE [ResourceAnalyses] ADD [AnalyzedCharacters] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020110540_PhaseTwoProcessingSearch'
)
BEGIN
    ALTER TABLE [ResourceAnalyses] ADD [ErrorMessage] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020110540_PhaseTwoProcessingSearch'
)
BEGIN
    ALTER TABLE [ResourceAnalyses] ADD [IsPartial] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020110540_PhaseTwoProcessingSearch'
)
BEGIN
    ALTER TABLE [ResourceAnalyses] ADD [ProcessedPages] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020110540_PhaseTwoProcessingSearch'
)
BEGIN
    ALTER TABLE [ResourceAnalyses] ADD [SourceCharacters] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020110540_PhaseTwoProcessingSearch'
)
BEGIN
    ALTER TABLE [ResourceAnalyses] ADD [SuggestedSubjectId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020110540_PhaseTwoProcessingSearch'
)
BEGIN
    ALTER TABLE [ResourceAnalyses] ADD [SuggestedTopicId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020110540_PhaseTwoProcessingSearch'
)
BEGIN
    ALTER TABLE [ResourceAnalyses] ADD [TotalPages] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020110540_PhaseTwoProcessingSearch'
)
BEGIN
    CREATE TABLE [PageBookmarks] (
        [UserId] nvarchar(450) NOT NULL,
        [ResourceId] int NOT NULL,
        [PageNumber] int NOT NULL,
        [FileHash] nvarchar(64) NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_PageBookmarks] PRIMARY KEY ([UserId], [ResourceId]),
        CONSTRAINT [CK_PageBookmarks_PageNumber] CHECK ([PageNumber] > 0),
        CONSTRAINT [FK_PageBookmarks_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PageBookmarks_Resources_ResourceId] FOREIGN KEY ([ResourceId]) REFERENCES [Resources] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020110540_PhaseTwoProcessingSearch'
)
BEGIN
    CREATE TABLE [ProcessingJobs] (
        [ResourceId] int NOT NULL,
        [FileHash] nvarchar(64) NOT NULL,
        [AnalyzeRequested] bit NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [Attempts] int NOT NULL,
        [LeaseToken] uniqueidentifier NULL,
        [LeaseExpiresAtUtc] datetime2 NULL,
        [NotBeforeUtc] datetime2 NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [LastErrorCode] nvarchar(100) NULL,
        CONSTRAINT [PK_ProcessingJobs] PRIMARY KEY ([ResourceId]),
        CONSTRAINT [FK_ProcessingJobs_Resources_ResourceId] FOREIGN KEY ([ResourceId]) REFERENCES [Resources] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020110540_PhaseTwoProcessingSearch'
)
BEGIN
    CREATE TABLE [ResourceEmbeddings] (
        [ResourceId] int NOT NULL,
        [Provider] nvarchar(100) NOT NULL,
        [Model] nvarchar(100) NOT NULL,
        [Dimensions] int NOT NULL,
        [FileHash] nvarchar(64) NOT NULL,
        [VectorJson] nvarchar(max) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CoveredCharacters] int NOT NULL,
        [IsPartial] bit NOT NULL,
        CONSTRAINT [PK_ResourceEmbeddings] PRIMARY KEY ([ResourceId]),
        CONSTRAINT [FK_ResourceEmbeddings_Resources_ResourceId] FOREIGN KEY ([ResourceId]) REFERENCES [Resources] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020110540_PhaseTwoProcessingSearch'
)
BEGIN
    CREATE TABLE [ResourceSearchDocuments] (
        [ResourceId] int NOT NULL,
        [SearchText] nvarchar(max) NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_ResourceSearchDocuments] PRIMARY KEY ([ResourceId]),
        CONSTRAINT [FK_ResourceSearchDocuments_Resources_ResourceId] FOREIGN KEY ([ResourceId]) REFERENCES [Resources] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020110540_PhaseTwoProcessingSearch'
)
BEGIN
    CREATE TABLE [ResourceTextExtractions] (
        [ResourceId] int NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [ExtractedFileHash] nvarchar(64) NULL,
        [TotalPages] int NOT NULL,
        [ProcessedPages] int NOT NULL,
        [CharacterCount] int NOT NULL,
        [IsTruncated] bit NOT NULL,
        [ErrorCode] nvarchar(100) NULL,
        [ErrorMessage] nvarchar(500) NULL,
        [ExtractedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_ResourceTextExtractions] PRIMARY KEY ([ResourceId]),
        CONSTRAINT [FK_ResourceTextExtractions_Resources_ResourceId] FOREIGN KEY ([ResourceId]) REFERENCES [Resources] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020110540_PhaseTwoProcessingSearch'
)
BEGIN
    CREATE TABLE [ResourceTextPages] (
        [ResourceId] int NOT NULL,
        [PageNumber] int NOT NULL,
        [Text] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_ResourceTextPages] PRIMARY KEY ([ResourceId], [PageNumber]),
        CONSTRAINT [FK_ResourceTextPages_ResourceTextExtractions_ResourceId] FOREIGN KEY ([ResourceId]) REFERENCES [ResourceTextExtractions] ([ResourceId]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020110540_PhaseTwoProcessingSearch'
)
BEGIN
    CREATE INDEX [IX_ResourceAnalyses_SuggestedSubjectId] ON [ResourceAnalyses] ([SuggestedSubjectId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020110540_PhaseTwoProcessingSearch'
)
BEGIN
    CREATE INDEX [IX_ResourceAnalyses_SuggestedTopicId] ON [ResourceAnalyses] ([SuggestedTopicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020110540_PhaseTwoProcessingSearch'
)
BEGIN
    CREATE INDEX [IX_PageBookmarks_ResourceId] ON [PageBookmarks] ([ResourceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020110540_PhaseTwoProcessingSearch'
)
BEGIN
    CREATE INDEX [IX_ProcessingJobs_Status_NotBeforeUtc] ON [ProcessingJobs] ([Status], [NotBeforeUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020110540_PhaseTwoProcessingSearch'
)
BEGIN
    CREATE INDEX [IX_ResourceEmbeddings_Model_Dimensions] ON [ResourceEmbeddings] ([Model], [Dimensions]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020110540_PhaseTwoProcessingSearch'
)
BEGIN
    ALTER TABLE [ResourceAnalyses] ADD CONSTRAINT [FK_ResourceAnalyses_Subjects_SuggestedSubjectId] FOREIGN KEY ([SuggestedSubjectId]) REFERENCES [Subjects] ([Id]) ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020110540_PhaseTwoProcessingSearch'
)
BEGIN
    ALTER TABLE [ResourceAnalyses] ADD CONSTRAINT [FK_ResourceAnalyses_Topics_SuggestedTopicId] FOREIGN KEY ([SuggestedTopicId]) REFERENCES [Topics] ([Id]) ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020110540_PhaseTwoProcessingSearch'
)
BEGIN
    INSERT INTO [ResourceSearchDocuments] ([ResourceId],[SearchText],[UpdatedAtUtc])
    SELECT r.[Id], CONCAT(r.[Title],NCHAR(10),r.[Description],NCHAR(10),
        (SELECT STRING_AGG(CONVERT(nvarchar(max),t.[Name]),NCHAR(10)) FROM [ResourceTags] rt JOIN [Tags] t ON rt.[TagId]=t.[Id] WHERE rt.[ResourceId]=r.[Id])), SYSUTCDATETIME()
    FROM [Resources] r WHERE NOT EXISTS (SELECT 1 FROM [ResourceSearchDocuments] s WHERE s.[ResourceId]=r.[Id]);
    INSERT INTO [ResourceTextExtractions] ([ResourceId],[Status],[TotalPages],[ProcessedPages],[CharacterCount],[IsTruncated])
    SELECT r.[Id],N'Pending',0,0,0,0 FROM [Resources] r
    WHERE NOT EXISTS (SELECT 1 FROM [ResourceTextExtractions] e WHERE e.[ResourceId]=r.[Id]);
    INSERT INTO [ProcessingJobs] ([ResourceId],[FileHash],[AnalyzeRequested],[Status],[Attempts],[NotBeforeUtc],[CreatedAtUtc],[UpdatedAtUtc])
    SELECT r.[Id],r.[FileHash],0,N'Pending',0,SYSUTCDATETIME(),SYSUTCDATETIME(),SYSUTCDATETIME()
    FROM [Resources] r WHERE NOT EXISTS (SELECT 1 FROM [ProcessingJobs] j WHERE j.[ResourceId]=r.[Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020110540_PhaseTwoProcessingSearch'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261020110540_PhaseTwoProcessingSearch', N'10.0.13');
END;

COMMIT;
GO

