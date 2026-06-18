/*
    Popola la tabella dbo.Calendario per l'intervallo di anni indicato.
    Compatibile con SQL Server 2012.

    Lo script inserisce solo le date mancanti: eventuali righe gia' presenti
    non vengono modificate, cosi' restano valide correzioni manuali successive.
*/

SET NOCOUNT ON;
SET DATEFIRST 1; -- lunedi = 1, sabato = 6, domenica = 7

DECLARE @StartYear INT = YEAR(GETDATE());
DECLARE @EndYear INT = YEAR(GETDATE()) + 2;
DECLARE @UserId NVARCHAR(50) = 'SCRIPT';

;WITH Giorni AS
(
    SELECT CAST(CAST(@StartYear AS CHAR(4)) + '0101' AS DATE) AS Giorno
    UNION ALL
    SELECT DATEADD(DAY, 1, Giorno)
    FROM Giorni
    WHERE Giorno < DATEADD(DAY, -1, DATEADD(YEAR, 1, CAST(CAST(@EndYear AS CHAR(4)) + '0101' AS DATE)))
),
Pasqua AS
(
    SELECT
        Giorno,
        YEAR(Giorno) AS Anno,
        YEAR(Giorno) % 19 AS A,
        YEAR(Giorno) / 100 AS B,
        YEAR(Giorno) % 100 AS C
    FROM Giorni
),
PasquaCalcolo AS
(
    SELECT
        Giorno,
        Anno,
        A,
        B,
        C,
        B / 4 AS D,
        B % 4 AS E,
        (B + 8) / 25 AS F,
        C / 4 AS I,
        C % 4 AS K
    FROM Pasqua
),
PasquaCalcolo2 AS
(
    SELECT
        Giorno,
        Anno,
        A,
        B,
        C,
        D,
        E,
        I,
        K,
        (B - F + 1) / 3 AS G
    FROM PasquaCalcolo
),
PasquaCalcolo3 AS
(
    SELECT
        Giorno,
        Anno,
        A,
        E,
        I,
        K,
        (19 * A + B - D - G + 15) % 30 AS H
    FROM PasquaCalcolo2
),
PasquaCalcolo4 AS
(
    SELECT
        Giorno,
        Anno,
        H,
        (32 + 2 * E + 2 * I - H - K) % 7 AS L,
        A
    FROM PasquaCalcolo3
),
GiorniClassificati AS
(
    SELECT
        Giorno,
        Anno,
        CASE
            WHEN DATEPART(WEEKDAY, Giorno) IN (6, 7) THEN 'F'
            WHEN MONTH(Giorno) = 1 AND DAY(Giorno) IN (1, 6) THEN 'F'
            WHEN MONTH(Giorno) = 4 AND DAY(Giorno) = 25 THEN 'F'
            WHEN MONTH(Giorno) = 5 AND DAY(Giorno) = 1 THEN 'F'
            WHEN MONTH(Giorno) = 6 AND DAY(Giorno) IN (2, 29) THEN 'F'
            WHEN MONTH(Giorno) = 8 AND DAY(Giorno) = 15 THEN 'F'
            WHEN MONTH(Giorno) = 11 AND DAY(Giorno) = 2 THEN 'F'
            WHEN MONTH(Giorno) = 12 AND DAY(Giorno) IN (8, 25, 26) THEN 'F'
            WHEN Giorno = DATEADD(DAY, 1,
                DATEFROMPARTS(
                    Anno,
                    (H + L - 7 * ((A + 11 * H + 22 * L) / 451) + 114) / 31,
                    ((H + L - 7 * ((A + 11 * H + 22 * L) / 451) + 114) % 31) + 1
                )) THEN 'F'
            ELSE 'L'
        END AS CalendarioTipo
    FROM PasquaCalcolo4
)
INSERT INTO dbo.Calendario
(
    CalendarioStato,
    CalendarioAnno,
    CalendarioMese,
    CalendarioGiorno,
    CalendarioTipo,
    CalendarioOreLavorative,
    CalendarioData,
    CalendarioDUM,
    CalendarioUDUM
)
SELECT
    '',
    YEAR(Giorno),
    MONTH(Giorno),
    DAY(Giorno),
    CalendarioTipo,
    CASE WHEN CalendarioTipo = 'L' THEN 8 ELSE 0 END,
    Giorno,
    GETDATE(),
    @UserId
FROM GiorniClassificati gc
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.Calendario c
    WHERE c.CalendarioData = gc.Giorno
)
OPTION (MAXRECURSION 0);

SELECT
    @StartYear AS StartYear,
    @EndYear AS EndYear,
    COUNT(*) AS RigheCalendarioPresenti
FROM dbo.Calendario
WHERE CalendarioData >= CAST(CAST(@StartYear AS CHAR(4)) + '0101' AS DATE)
  AND CalendarioData < DATEADD(YEAR, 1, CAST(CAST(@EndYear AS CHAR(4)) + '0101' AS DATE));
