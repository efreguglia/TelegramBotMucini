IF OBJECT_ID('dbo.TelegramBotTimesheetReminders', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TelegramBotTimesheetReminders
    (
        TelegramBotTimesheetReminderId INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_TelegramBotTimesheetReminders PRIMARY KEY,
        ChatId NVARCHAR(50) NOT NULL,
        SoggettiCodice INT NULL,
        Azienda NVARCHAR(50) NOT NULL,
        ReminderDate DATE NOT NULL,
        ReminderSlot NVARCHAR(4) NOT NULL,
        OreInserite DECIMAL(5,2) NOT NULL,
        TelegramMessageId NVARCHAR(50) NULL,
        SentAt DATETIME NULL,
        SendAttemptCount INT NOT NULL
            CONSTRAINT DF_TelegramBotTimesheetReminders_SendAttemptCount DEFAULT (0),
        SendFailedAt DATETIME NULL,
        LastSendError NVARCHAR(1000) NULL,
        CreatedAt DATETIME NOT NULL
            CONSTRAINT DF_TelegramBotTimesheetReminders_CreatedAt DEFAULT (GETDATE())
    );

    CREATE UNIQUE INDEX UX_TelegramBotTimesheetReminders_ChatDateSlot
        ON dbo.TelegramBotTimesheetReminders(ChatId, ReminderDate, ReminderSlot);
END;

IF OBJECT_ID('dbo.TelegramBotTimesheetDrafts', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TelegramBotTimesheetDrafts
    (
        TelegramBotTimesheetDraftId INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_TelegramBotTimesheetDrafts PRIMARY KEY,
        ChatId NVARCHAR(50) NOT NULL,
        SoggettiCodice INT NOT NULL,
        Azienda NVARCHAR(50) NOT NULL,
        Step NVARCHAR(30) NOT NULL,
        RapportiData DATE NOT NULL,
        ClientiCodice INT NULL,
        ClientiSediCodice INT NULL,
        Ore INT NULL,
        Testo NVARCHAR(MAX) NULL,
        CreatedAt DATETIME NOT NULL
            CONSTRAINT DF_TelegramBotTimesheetDrafts_CreatedAt DEFAULT (GETDATE()),
        UpdatedAt DATETIME NOT NULL
            CONSTRAINT DF_TelegramBotTimesheetDrafts_UpdatedAt DEFAULT (GETDATE())
    );

    CREATE INDEX IX_TelegramBotTimesheetDrafts_ChatStep
        ON dbo.TelegramBotTimesheetDrafts(ChatId, Step, UpdatedAt);
END;
