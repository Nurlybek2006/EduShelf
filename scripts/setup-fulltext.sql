-- Optional SQL Server Full-Text setup. Execute in the EduShelf database, outside any transaction.
-- Core EF migrations intentionally do not depend on the Full-Text engine.
IF @@TRANCOUNT > 0
    THROW 51001, 'Full-Text setup must run outside a user transaction.', 1;
IF FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') <> 1
BEGIN
    PRINT N'Full-Text Search is not installed. EduShelf will use the configured SQL fallback.';
    RETURN;
END;
IF NOT EXISTS (SELECT 1 FROM sys.fulltext_languages WHERE lcid = 0)
BEGIN
    PRINT N'Neutral word breaker (LCID 0) is unavailable. Use SQL fallback or install the required language resource.';
    RETURN;
END;
IF OBJECT_ID(N'dbo.ResourceSearchDocuments', N'U') IS NULL
    THROW 51002, 'Apply the EduShelf EF migrations first.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = N'EduShelfSearch')
    EXEC(N'CREATE FULLTEXT CATALOG [EduShelfSearch] WITH ACCENT_SENSITIVITY = OFF');
IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID(N'dbo.ResourceSearchDocuments'))
    EXEC(N'CREATE FULLTEXT INDEX ON dbo.ResourceSearchDocuments (SearchText LANGUAGE 0) KEY INDEX [PK_ResourceSearchDocuments] ON [EduShelfSearch] WITH CHANGE_TRACKING AUTO, STOPLIST = OFF');
PRINT N'EduShelf Full-Text index is configured. Initial population runs asynchronously.';
