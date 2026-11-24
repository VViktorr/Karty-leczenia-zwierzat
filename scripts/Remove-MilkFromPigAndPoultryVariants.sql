-- Preview with @Apply = 0. Set to 1 only after checking the rows.
USE [KartyLeczenia];
SET XACT_ABORT ON;
SET NOCOUNT ON;
DECLARE @Apply bit = 0;

DECLARE @Targets table (variant_id int PRIMARY KEY, mixed bit NOT NULL);

BEGIN TRANSACTION;

INSERT @Targets (variant_id, mixed)
SELECT v.id,
       CASE WHEN EXISTS (
           SELECT 1 FROM STRING_SPLIT(n.species, N' ') s
           WHERE s.value LIKE N'byd%' OR s.value LIKE N'krow%'
              OR s.value LIKE N'ciel%' OR s.value LIKE N'jałów%'
              OR s.value LIKE N'owc%' OR s.value LIKE N'owiec%' OR s.value LIKE N'jagni%'
              OR s.value LIKE N'koz%' OR s.value LIKE N'kóz%'
              OR s.value LIKE N'koń%' OR s.value LIKE N'koni%' OR s.value LIKE N'klacz%'
              OR s.value LIKE N'owcz%' OR s.value LIKE N'baw%' OR s.value LIKE N'owcom%'
       ) THEN 1 ELSE 0 END
FROM dbo.catalog_variants v WITH (UPDLOCK, HOLDLOCK)
LEFT JOIN dbo.catalog_variant_withdrawal w WITH (UPDLOCK, HOLDLOCK)
    ON w.variant_id = v.id AND w.commodity = 'milk'
CROSS APPLY (VALUES (TRANSLATE(LOWER(v.species), N'(),;/–—', N'       '))) n(species)
WHERE (NULLIF(LTRIM(RTRIM(v.milk)), N'') IS NOT NULL OR w.variant_id IS NOT NULL)
  AND EXISTS (
      SELECT 1 FROM STRING_SPLIT(n.species, N' ') s
      WHERE s.value IN (N'kura',N'kury',N'kur',N'kurczę',N'kurczęta',N'kurcząt',
          N'brojler',N'brojlery',N'brojlerów',N'nioska',N'nioski',N'niosek',
          N'świnia',N'świnie',N'świń',N'prosię',N'prosięta',N'prosiąt',
          N'locha',N'lochy',N'loch',N'tucznik',N'tuczniki',N'warchlaki',
          N'indyk',N'indyki',N'indyków',N'indyczka',N'indyczki')
  );

SELECT v.id, v.symbol, v.species, v.milk, w.short_text AS short_milk, v.eggs,
       CASE WHEN t.mixed = 1 THEN N'Pomijany: wariant mieszany'
            ELSE N'Do usunięcia' END AS result
FROM @Targets t
JOIN dbo.catalog_variants v ON v.id = t.variant_id
LEFT JOIN dbo.catalog_variant_withdrawal w
    ON w.variant_id = v.id AND w.commodity = 'milk'
ORDER BY t.mixed, v.species, v.id;

IF @Apply = 1
BEGIN
    DELETE w FROM dbo.catalog_variant_withdrawal w
    JOIN @Targets t ON t.variant_id = w.variant_id
    WHERE w.commodity = 'milk' AND t.mixed = 0;
    DECLARE @ShortRemoved int = @@ROWCOUNT;

    UPDATE v SET milk = NULL
    FROM dbo.catalog_variants v
    JOIN @Targets t ON t.variant_id = v.id
    WHERE t.mixed = 0 AND v.milk IS NOT NULL;
    DECLARE @FullRemoved int = @@ROWCOUNT;

    COMMIT TRANSACTION;
    SELECT @FullRemoved AS [Wyczyszczone pełne], @ShortRemoved AS [Usunięte krótkie];
END
ELSE
    ROLLBACK TRANSACTION;
