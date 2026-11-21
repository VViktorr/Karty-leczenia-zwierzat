USE [KartyLeczenia];
SET NOCOUNT ON;
SET XACT_ABORT ON;

-- 0: podgląd; 1: wykonanie scalenia.
DECLARE @Zastosuj bit = 0;

DECLARE @Pary table (
    Symbol nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    UsunId int PRIMARY KEY,
    ZachowajId int NOT NULL UNIQUE,
    GatunekOgolny nvarchar(256) NOT NULL,
    GatunekDokladny nvarchar(256) NOT NULL
);

INSERT @Pary VALUES
    (N'000003357', 1093, 1094, N'Bydło', N'Bydło (ciężarne jałówki i krowy)'),
    (N'000003358', 1095, 1096, N'Bydło', N'Bydło (ciężarne jałówki i krowy)'),
    (N'000003356', 1091, 1092, N'Bydło', N'Bydło (ciężarne jałówki i krowy)'),
    (N'000000000000000968', 197, 201, N'Bydło', N'Bydło (krowy)'),
    (N'000000000000000968', 198, 202, N'Bydło', N'Bydło (krowy)'),
    (N'000000000000000968', 199, 203, N'Bydło', N'Bydło (krowy)'),
    (N'000000000000000968', 200, 204, N'Bydło', N'Bydło (krowy)'),
    (N'000000000000000993', 219, 223, N'Bydło', N'Bydło (krowy)'),
    (N'000000000000000993', 220, 224, N'Bydło', N'Bydło (krowy)'),
    (N'000000000000000993', 221, 225, N'Bydło', N'Bydło (krowy)'),
    (N'000000000000000993', 222, 226, N'Bydło', N'Bydło (krowy)'),
    (N'000002584', 872, 873, N'Bydło', N'Bydło (krowy przeznaczone do rozrodu)'),
    (N'000002803', 919, 920, N'Bydło', N'Bydło (krowy przeznaczone do rozrodu)'),
    (N'000000000000000252', 71, 72, N'Bydło', N'Bydło (ciężarne jałówki i krowy)'),
    (N'00000000000000025(1)', 66, 67, N'Bydło', N'Bydło (ciężarne jałówki i krowy)'),
    (N'187', 2011, 2012, N'Bydło', N'Bydło (cielęta)'),
    (N'000000000000001005', 233, 234, N'Bydło', N'Bydło (krowy w laktacji)'),
    (N'000002574', 869, 870, N'Bydło', N'Bydło (krowy w laktacji)'),
    (N'04221', 1418, 1419, N'Bydło', N'Bydło (krowa w laktacji)'),
    (N'04272', 1446, 1447, N'Bydło', N'Bydło (krowa w laktacji)'),
    (N'0000003250', 659, 660, N'Bydło', N'Bydło (krowa w laktacji)'),
    (N'PKO484', 2082, 2083, N'Bydło', N'Bydło (dorosłe)'),
    (N'000000000000001343', 331, 332, N'Bydło', N'Bydło (dorosłe)'),
    (N'04983', 1648, 1649, N'Bydło', N'Bydło (krowy zasuszane)'),
    (N'0000003541', 728, 729, N'Bydło', N'Bydło (krowy zasuszane)'),
    (N'04428', 1526, 1527, N'Bydło', N'Bydło (krowy zasuszone)'),
    (N'05139', 1743, 1744, N'Bydło', N'Bydło (krowy zasuszone)'),
    (N'02268', 1255, 1256, N'Bydło', N'Bydło (krowy zasuszone)'),
    (N'05099', 1726, 1727, N'Bydło', N'Bydło (krowy zasuszone)'),
    (N'04263', 1444, 1445, N'Bydło', N'Bydło (krowy zasuszone)'),
    (N'04239', 1426, 1427, N'Bydło', N'Bydło (krowy w zasuszeniu)'),
    (N'04239(1)', 1428, 1429, N'Bydło', N'Bydło (krowy w zasuszeniu)'),
    (N'04239(2)', 1430, 1431, N'Bydło', N'Bydło (krowy w zasuszeniu)'),
    (N'000000000000001080', 254, 255, N'Bydło', N'Bydło (krowy i jałówki)'),
    (N'000002387', 814, 815, N'Bydło', N'Bydło (krowy i jałówki)'),
    (N'000000000000001081', 256, 257, N'Bydło', N'Bydło (krowy i jałówki)'),
    (N'00000000000000000(1)', 1, 2, N'Bydło', N'Bydło (krowa/jałówka)'),
    (N'000000000000000002', 6, 7, N'Bydło', N'Bydło (krowa/jałówka)'),
    (N'00000000000000034(2)', 80, 81, N'Bydło', N'Bydło (krowy w laktacji)'),
    (N'04604(1)', 1560, 1561, N'Bydło', N'Bydło (krowy i jałówki)'),
    (N'05308', 1911, 1912, N'Bydło', N'Bydło (krowy i jałówki)'),
    (N'04604', 1558, 1559, N'Bydło', N'Bydło (krowy i jałówki)'),
    (N'04999', 1661, 1662, N'Bydło', N'Bydło (krowy i cielęta)'),
    (N'02705', 1308, 1309, N'Bydło', N'Bydło (krowy i cielęta)'),
    (N'05100', 1728, 1729, N'Bydło', N'Bydło (krowy i cielęta)'),
    (N'04466', 1530, 1531, N'Bydło', N'Bydło (krowy i cielęta)'),
    (N'000003361', 1099, 1100, N'Bydło', N'Bydło (krowy i cielęta)'),
    (N'04206', 1410, 1411, N'Bydło', N'Bydło (krowy w laktacji)'),
    (N'04243', 1433, 1434, N'Bydło', N'Bydło (krowy w laktacji)'),
    (N'000003379', 1113, 1114, N'Bydło', N'Bydło (cielęta ≤330 kg)'),
    (N'000000000000000342', 85, 86, N'Bydło', N'Bydło (krowy w laktacji)'),
    (N'Z678', 2089, 2090, N'Bydło', N'Bydło (krowy w laktacji)'),
    (N'000000000000000786', 141, 142, N'Bydło', N'Bydło (krowy w laktacji)'),
    (N'05281', 1858, 2507, N'Świnie', N'Świnie'),
    (N'04177', 1402, 2505, N'Świnie', N'Świnie'),
    (N'05076', 1701, 2506, N'Świnie', N'Świnie');

BEGIN TRY
    BEGIN TRANSACTION;

    SELECT
        x.Symbol, p.name AS Produkt, x.UsunId, x.ZachowajId,
        x.GatunekOgolny, x.GatunekDokladny,
        CASE
            WHEN a.id IS NULL AND b.id IS NOT NULL THEN N'Brak rekordu ogólnego'
            WHEN a.id IS NULL OR b.id IS NULL THEN N'Brak wymaganej pary'
            WHEN a.species COLLATE Latin1_General_100_BIN2 = x.GatunekOgolny COLLATE Latin1_General_100_BIN2
             AND b.species COLLATE Latin1_General_100_BIN2 = x.GatunekDokladny COLLATE Latin1_General_100_BIN2
             AND (
                 (x.GatunekOgolny <> x.GatunekDokladny
                  AND LEFT(a.card_text, LEN(a.species) + 3) COLLATE Latin1_General_100_BIN2
                       = (a.species + N' · ') COLLATE Latin1_General_100_BIN2
                  AND b.card_text COLLATE Latin1_General_100_BIN2 =
                       STUFF(a.card_text, 1, LEN(a.species), b.species) COLLATE Latin1_General_100_BIN2
                  AND DATALENGTH(b.card_text) = DATALENGTH(STUFF(a.card_text, 1, LEN(a.species), b.species)))
                 OR (x.GatunekOgolny = x.GatunekDokladny
                     AND a.verified_at = N'2026-11-10' AND b.verified_at = N'2026-11-11')
             )
             AND NOT EXISTS (
                SELECT [key], [type], [value] COLLATE Latin1_General_100_BIN2, DATALENGTH([value])
                FROM OPENJSON(j.Dane1) WHERE [key] NOT IN (N'id', N'species')
                  AND NOT (x.GatunekOgolny <> x.GatunekDokladny AND [key] = N'card_text')
                  AND NOT (x.GatunekOgolny = x.GatunekDokladny AND [key] = N'verified_at')
                EXCEPT
                SELECT [key], [type], [value] COLLATE Latin1_General_100_BIN2, DATALENGTH([value])
                FROM OPENJSON(j.Dane2) WHERE [key] NOT IN (N'id', N'species')
                  AND NOT (x.GatunekOgolny <> x.GatunekDokladny AND [key] = N'card_text')
                  AND NOT (x.GatunekOgolny = x.GatunekDokladny AND [key] = N'verified_at')
             )
             AND NOT EXISTS (
                SELECT [key], [type], [value] COLLATE Latin1_General_100_BIN2, DATALENGTH([value])
                FROM OPENJSON(j.Dane2) WHERE [key] NOT IN (N'id', N'species')
                  AND NOT (x.GatunekOgolny <> x.GatunekDokladny AND [key] = N'card_text')
                  AND NOT (x.GatunekOgolny = x.GatunekDokladny AND [key] = N'verified_at')
                EXCEPT
                SELECT [key], [type], [value] COLLATE Latin1_General_100_BIN2, DATALENGTH([value])
                FROM OPENJSON(j.Dane1) WHERE [key] NOT IN (N'id', N'species')
                  AND NOT (x.GatunekOgolny <> x.GatunekDokladny AND [key] = N'card_text')
                  AND NOT (x.GatunekOgolny = x.GatunekDokladny AND [key] = N'verified_at')
             )
             THEN N'Gotowe do scalenia'
            ELSE N'Dane się różnią — pominięto'
        END AS Stan
    INTO #ScalenieGatunkow
    FROM @Pary x
    LEFT JOIN dbo.catalog_products p ON p.symbol = x.Symbol
    LEFT JOIN dbo.catalog_variants a WITH (UPDLOCK, HOLDLOCK)
        ON a.id = x.UsunId AND a.symbol = x.Symbol
    LEFT JOIN dbo.catalog_variants b WITH (UPDLOCK, HOLDLOCK)
        ON b.id = x.ZachowajId AND b.symbol = x.Symbol
    LEFT JOIN dbo.catalog_price_withdrawal wa WITH (UPDLOCK, HOLDLOCK) ON wa.id = a.id
    LEFT JOIN dbo.catalog_price_withdrawal wb WITH (UPDLOCK, HOLDLOCK) ON wb.id = b.id
    CROSS APPLY (SELECT
        (SELECT a.*, wa.compact_text AS price_text FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES),
        (SELECT b.*, wb.compact_text AS price_text FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES)
    ) j(Dane1, Dane2);

    SELECT * FROM #ScalenieGatunkow ORDER BY Produkt, UsunId;

    DECLARE @Usuniete int = 0;
    IF @Zastosuj = 1
    BEGIN
        DELETE v
        FROM dbo.catalog_variants v
        JOIN #ScalenieGatunkow s ON s.UsunId = v.id AND s.Symbol = v.symbol
        WHERE s.Stan = N'Gotowe do scalenia';
        SET @Usuniete = @@ROWCOUNT;

        UPDATE p SET name = p.name
        FROM dbo.catalog_products p
        WHERE EXISTS (SELECT 1 FROM #ScalenieGatunkow s
            WHERE s.Symbol = p.symbol AND s.Stan = N'Gotowe do scalenia');
    END;

    SELECT @Usuniete AS UsunieteRekordy, @Zastosuj AS Zastosowano;
    DROP TABLE #ScalenieGatunkow;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
