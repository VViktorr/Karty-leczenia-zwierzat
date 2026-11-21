-- Run in the existing archive database as a schema administrator.
USE [KartyLeczenia];
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.catalog_variants', N'U') IS NULL
    THROW 50000, N'Brak tabeli dbo.catalog_variants w bazie KartyLeczenia.', 1;

IF COL_LENGTH(N'dbo.catalog_variants', N'eggs') IS NULL
    ALTER TABLE dbo.catalog_variants ADD eggs nvarchar(max) NULL;

-- RPL 23482, section 4.11. Transfer only the verified value already present
-- in the notes of the pictured Subiekt variant. Keep all existing notes.
EXEC sys.sp_executesql N'
UPDATE v SET eggs = N''0 dni''
FROM dbo.catalog_variants v
JOIN dbo.catalog_products p ON p.symbol = v.symbol
WHERE v.symbol = N''05026(1)''
  AND (p.rpl_id = N''23482'' OR v.rpl_id = N''23482'')
  AND LTRIM(RTRIM(v.species)) = N''Kury''
  AND v.eggs IS NULL
  AND v.restrictions LIKE N''Jaja: 0 dni.%'';

-- Candidates for review against ChPL. This query does not set any values.
SELECT v.id, v.symbol, p.name, v.species, v.route, v.restrictions,
       v.withdrawal_short, w.compact_text, v.pdf_path
FROM dbo.catalog_variants v
JOIN dbo.catalog_products p ON p.symbol = v.symbol
LEFT JOIN dbo.catalog_price_withdrawal w ON w.id = v.id
WHERE NULLIF(LTRIM(RTRIM(v.eggs)), N'''') IS NULL
  AND (LOWER(v.species) LIKE N''kur%'' OR LOWER(v.species) LIKE N''%drób%''
    OR LOWER(v.species) LIKE N''indyk%'' OR LOWER(v.species) LIKE N''kaczk%''
    OR LOWER(v.species) LIKE N''gęs%'' OR LOWER(v.species) LIKE N''przepiór%''
    OR LOWER(v.restrictions) LIKE N''%jaj%'' OR LOWER(v.withdrawal_short) LIKE N''%jaj%''
    OR LOWER(w.compact_text) LIKE N''%jaj%'')
ORDER BY p.name, v.id;';
COMMIT TRANSACTION;
