-- Cleanup for candidates in the reviewed milk report.
-- Set @Apply to 1 only after reviewing the output, including conditions in milk.
USE [KartyLeczenia];
SET XACT_ABORT ON;
SET NOCOUNT ON;

DECLARE @Apply bit = 0;
DECLARE @Candidates table (variant_id int PRIMARY KEY);

BEGIN TRANSACTION;

INSERT @Candidates (variant_id)
SELECT v.id
FROM dbo.catalog_variants AS v WITH (UPDLOCK, HOLDLOCK)
LEFT JOIN dbo.catalog_variant_withdrawal AS w WITH (UPDLOCK, HOLDLOCK)
    ON w.variant_id = v.id AND w.commodity = 'milk'
WHERE v.id IN (
    50, 191, 195, 280, 281, 308, 376, 417, 418, 496, 498, 500,
    502, 528, 587, 608, 610, 617, 618, 817, 884, 890, 994, 1015,
    1038, 1039, 1089, 1116, 1145, 1146, 1154, 1164, 1166, 1179, 1181, 1195,
    1204, 1205, 1258, 1260, 1554, 1556, 1679, 1740, 1906, 2183, 2184, 2187,
    2188, 2191, 2192, 2195, 2196, 2301, 2449, 2533, 2539, 2542, 2576, 2584,
    2585, 2619, 2657, 2662, 2668, 2835, 2836, 2852, 2924, 2926, 2927, 2929
)
  AND NULLIF(LTRIM(RTRIM(v.edible_tissues)), N'') IS NOT NULL
  AND NULLIF(LTRIM(RTRIM(v.eggs)), N'') IS NOT NULL
  AND (NULLIF(LTRIM(RTRIM(v.milk)), N'') IS NOT NULL OR w.variant_id IS NOT NULL);

SELECT v.id, v.symbol, v.species, v.edible_tissues, v.eggs, v.milk, w.short_text AS short_milk
FROM @Candidates AS c
JOIN dbo.catalog_variants AS v ON v.id = c.variant_id
LEFT JOIN dbo.catalog_variant_withdrawal AS w
    ON w.variant_id = c.variant_id AND w.commodity = 'milk'
ORDER BY v.species, v.id;

IF @Apply = 1
BEGIN
    DELETE w
    FROM dbo.catalog_variant_withdrawal AS w
    JOIN @Candidates AS c ON c.variant_id = w.variant_id
    WHERE w.commodity = 'milk';
    DECLARE @ShortRemoved int = @@ROWCOUNT;

    UPDATE v SET milk = NULL
    FROM dbo.catalog_variants AS v
    JOIN @Candidates AS c ON c.variant_id = v.id
    WHERE v.milk IS NOT NULL;
    DECLARE @FullRemoved int = @@ROWCOUNT;

    COMMIT TRANSACTION;
    SELECT @FullRemoved AS [Wyczyszczone pełne wpisy], @ShortRemoved AS [Usunięte krótkie wpisy];
END
ELSE
    ROLLBACK TRANSACTION;
