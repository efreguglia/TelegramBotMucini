IF OBJECT_ID('dbo.TelegramBotRecentClientsCache', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TelegramBotRecentClientsCache
    (
        TelegramBotRecentClientsCacheId INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_TelegramBotRecentClientsCache PRIMARY KEY,
        Azienda NVARCHAR(50) NOT NULL,
        SoggettiCodice INT NOT NULL,
        ClientiCodice INT NOT NULL,
        ClientiRagioneSociale NVARCHAR(255) NOT NULL,
        DataUltimoIntervento DATE NOT NULL,
        UltimoRapportiCodice INT NOT NULL,
        UpdatedAt DATETIME NOT NULL
            CONSTRAINT DF_TelegramBotRecentClientsCache_UpdatedAt DEFAULT (GETDATE())
    );

    CREATE UNIQUE INDEX UX_TelegramBotRecentClientsCache_AziendaSoggettoCliente
        ON dbo.TelegramBotRecentClientsCache(Azienda, SoggettiCodice, ClientiCodice);

    CREATE INDEX IX_TelegramBotRecentClientsCache_SoggettoAziendaData
        ON dbo.TelegramBotRecentClientsCache(SoggettiCodice, Azienda, DataUltimoIntervento DESC, UltimoRapportiCodice DESC);
END;
