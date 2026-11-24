-- Run in SSMS against KartyLeczenia. Set @Apply to 1 after reviewing the preview.
USE [KartyLeczenia];
SET XACT_ABORT ON;
SET NOCOUNT ON;

DECLARE @Apply bit = 0;
DECLARE @Candidates table (
    variant_id int PRIMARY KEY,
    species nvarchar(max),
    milk nvarchar(max),
    eggs nvarchar(max),
    target_eggs nvarchar(100),
    can_move bit
);

BEGIN TRANSACTION;

INSERT @Candidates (variant_id, species, milk, eggs, target_eggs, can_move)
SELECT v.id, v.species, v.milk, v.eggs, m.target_eggs,
       CASE WHEN (
           NULLIF(LTRIM(RTRIM(v.eggs)), N'') IS NULL
           OR LTRIM(RTRIM(v.eggs)) COLLATE Latin1_General_100_CI_AS = m.target_eggs
           OR (m.target_eggs = N'Niedopuszczony' AND LTRIM(RTRIM(v.eggs))
               COLLATE Latin1_General_100_CI_AS IN (N'Niedopuszczone', N'Jaja: niedopuszczony'))
       ) AND NOT EXISTS (
           SELECT 1 FROM dbo.catalog_variant_withdrawal AS w WITH (UPDLOCK, HOLDLOCK)
           WHERE w.variant_id = v.id AND w.commodity = 'milk'
       ) THEN 1 ELSE 0 END
FROM dbo.catalog_variants AS v WITH (UPDLOCK, HOLDLOCK)
JOIN (VALUES
    (N'Indyk', N'Niedopuszczony (jaja)', N'Niedopuszczony'),
    (N'Indyki', N'Jaja: niedopuszczony', N'Niedopuszczony'),
    (N'Kury (z wyjątkiem niosek produkujących jaja do spożycia)', N'Jaja: niedopuszczony', N'Niedopuszczony'),
    (N'Kaczki', N'Jaja: niedopuszczony', N'Niedopuszczony'),
    (N'Indyki', N'Niedopuszczony u niosek', N'Niedopuszczony u niosek')
) AS m(species, milk, target_eggs)
ON LTRIM(RTRIM(v.species)) COLLATE Latin1_General_100_CI_AS = m.species
AND LTRIM(RTRIM(v.milk)) COLLATE Latin1_General_100_CI_AS = m.milk;

SELECT variant_id, species, milk, eggs,
       COALESCE(NULLIF(LTRIM(RTRIM(eggs)), N''), target_eggs) AS proposed_eggs,
       CASE WHEN can_move = 1 THEN N'Gotowe' ELSE N'Konflikt w jajach lub istniejący skrót mleka' END AS result
FROM @Candidates
ORDER BY can_move DESC, species, variant_id;

IF @Apply = 1
BEGIN
    UPDATE v SET milk = NULL,
                 eggs = CASE WHEN NULLIF(LTRIM(RTRIM(v.eggs)), N'') IS NULL
                             THEN c.target_eggs ELSE v.eggs END
    FROM dbo.catalog_variants AS v
    JOIN @Candidates AS c ON c.variant_id = v.id
    WHERE c.can_move = 1;

    SELECT @@ROWCOUNT AS [Przeniesione wpisy];
    COMMIT TRANSACTION;
END
ELSE
    ROLLBACK TRANSACTION;
