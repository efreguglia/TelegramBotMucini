IF OBJECT_ID('dbo.TelegramBotMessageDeletes', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TelegramBotMessageDeletes
    (
        TelegramBotMessageDeleteId INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_TelegramBotMessageDeletes PRIMARY KEY,
        ChatId NVARCHAR(50) NOT NULL,
        MessageId NVARCHAR(50) NOT NULL,
        DeleteAfter DATETIME NOT NULL,
        DeletedAt DATETIME NULL,
        LastAttemptAt DATETIME NULL,
        DeleteFailedCount INT NOT NULL
            CONSTRAINT DF_TelegramBotMessageDeletes_DeleteFailedCount DEFAULT (0),
        LastError NVARCHAR(1000) NULL,
        CreatedAt DATETIME NOT NULL
            CONSTRAINT DF_TelegramBotMessageDeletes_CreatedAt DEFAULT (GETDATE())
    );

    CREATE INDEX IX_TelegramBotMessageDeletes_Due
        ON dbo.TelegramBotMessageDeletes(DeletedAt, DeleteAfter, DeleteFailedCount);
END;
