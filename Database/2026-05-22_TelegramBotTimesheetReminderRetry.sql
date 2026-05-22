IF COL_LENGTH('dbo.TelegramBotTimesheetReminders', 'SendAttemptCount') IS NULL
BEGIN
    ALTER TABLE dbo.TelegramBotTimesheetReminders
        ADD SendAttemptCount INT NOT NULL
            CONSTRAINT DF_TelegramBotTimesheetReminders_SendAttemptCount DEFAULT (0);
END;

IF COL_LENGTH('dbo.TelegramBotTimesheetReminders', 'SendFailedAt') IS NULL
BEGIN
    ALTER TABLE dbo.TelegramBotTimesheetReminders
        ADD SendFailedAt DATETIME NULL;
END;

IF COL_LENGTH('dbo.TelegramBotTimesheetReminders', 'LastSendError') IS NULL
BEGIN
    ALTER TABLE dbo.TelegramBotTimesheetReminders
        ADD LastSendError NVARCHAR(1000) NULL;
END;
