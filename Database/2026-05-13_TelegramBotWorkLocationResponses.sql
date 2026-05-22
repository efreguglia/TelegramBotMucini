IF OBJECT_ID('dbo.TelegramBotWorkLocationResponses', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TelegramBotWorkLocationResponses
    (
        TelegramBotWorkLocationResponseId INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_TelegramBotWorkLocationResponses PRIMARY KEY,
        ChatId NVARCHAR(50) NOT NULL,
        SoggettiCodice INT NULL,
        Azienda NVARCHAR(50) NOT NULL,
        ReminderDate DATE NOT NULL,
        ReminderSlot NVARCHAR(2) NOT NULL,
        TelegramMessageId NVARCHAR(50) NULL,
        ResponseType NVARCHAR(30) NOT NULL,
        ClientiSediCodice INT NULL,
        ClientiSediDescrizione NVARCHAR(255) NULL,
        FreeText NVARCHAR(1000) NULL,
        SentAt DATETIME NULL,
        RespondedAt DATETIME NULL,
        CreatedAt DATETIME NOT NULL
            CONSTRAINT DF_TelegramBotWorkLocationResponses_CreatedAt DEFAULT (GETDATE())
    );

    CREATE UNIQUE INDEX UX_TelegramBotWorkLocationResponses_ChatDateSlot
        ON dbo.TelegramBotWorkLocationResponses(ChatId, ReminderDate, ReminderSlot);

    CREATE INDEX IX_TelegramBotWorkLocationResponses_ResponseType
        ON dbo.TelegramBotWorkLocationResponses(ResponseType, ChatId, CreatedAt);
END;
