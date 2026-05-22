IF COL_LENGTH('dbo.Device', 'DeviceTelegramReminderRapportiniSN') IS NULL
BEGIN
    ALTER TABLE dbo.Device
        ADD DeviceTelegramReminderRapportiniSN CHAR(1) NULL;
END;
