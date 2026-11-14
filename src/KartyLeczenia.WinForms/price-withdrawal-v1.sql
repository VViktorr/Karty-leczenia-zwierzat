CREATE TABLE IF NOT EXISTS price_withdrawal_entries (
    id INTEGER PRIMARY KEY REFERENCES variants(id),
    compact_text TEXT NOT NULL
);

WITH source AS (
    SELECT id, rpl_id, trim(coalesce(edible_tissues, '')) AS tissues,
           trim(coalesce(milk, '')) AS milk, trim(coalesce(restrictions, '')) AS restrictions,
           trim(coalesce(withdrawal_short, '')) AS saved_short,
           lower(coalesce(species, '')) AS species
    FROM variants
), classified AS (
    SELECT *, (species LIKE 'kur%' OR species LIKE 'indyk%' OR species LIKE 'kaczk%'
               OR species LIKE 'drób%' OR species LIKE 'gęs%' OR species LIKE 'przepiór%') AS poultry
    FROM source
), parts AS (
    SELECT id, saved_short, poultry,
           CASE WHEN tissues IN ('', '-', '—', '–') OR lower(tissues) LIKE 'nie dotyczy%' THEN ''
                WHEN lower(tissues) IN ('zero dni', 'zero dni.') THEN 'tk. 0 dni'
                ELSE 'tk. ' || tissues END AS tissue_text,
           CASE
               WHEN poultry AND rpl_id = '18647'
                   THEN 'Jaja konsumpcyjne: zakaz u ptaków produkujących je lub odchowywanych w tym celu. Nie stosować przez 6 tyg. przed nieśnością.'
               WHEN poultry AND (lower(milk) LIKE '%jaj%' OR lower(milk) LIKE '%nieśno%')
                    AND lower(milk) NOT LIKE 'nie dotyczy%' THEN
                   CASE WHEN lower(milk) = '0 dni (jaja)' THEN 'Jaja: 0 dni'
                        WHEN lower(milk) = 'niedopuszczony (jaja)' THEN 'Jaja: niedopuszczony'
                        ELSE milk END
               WHEN poultry AND lower(milk) LIKE '%jaj%0 dni%' THEN 'Jaja: 0 dni'
               WHEN poultry THEN ''
               WHEN milk IN ('', '-', '—', '–') OR lower(milk) LIKE 'nie dotyczy%' THEN ''
               WHEN lower(milk) IN ('zero godzin', 'zero godzin.') THEN 'ml. 0 h'
               WHEN lower(milk) LIKE 'mleko:%' THEN milk
               ELSE 'ml. ' || milk
           END AS milk_or_eggs_text,
           CASE WHEN poultry AND coalesce(rpl_id, '') <> '18647'
                     AND (lower(restrictions) LIKE '%jaj%' OR lower(restrictions) LIKE '%nieśno%')
                THEN restrictions ELSE '' END AS egg_restrictions
    FROM classified
), formatted AS (
    SELECT id, CASE WHEN NOT poultry AND saved_short <> '' THEN saved_short
                    ELSE coalesce(nullif(rtrim(tissue_text || CASE WHEN tissue_text <> '' THEN ', ' ELSE '' END ||
                                      milk_or_eggs_text || CASE WHEN milk_or_eggs_text <> '' THEN ', ' ELSE '' END ||
                                      egg_restrictions, ', '), ''), 'Brak okresu karencji w danych katalogu')
               END AS compact_text
    FROM parts
)
UPDATE variants
SET withdrawal_short = formatted.compact_text
FROM formatted
WHERE variants.id = formatted.id;

INSERT INTO price_withdrawal_entries (id, compact_text)
SELECT id, withdrawal_short FROM variants WHERE true
ON CONFLICT(id) DO UPDATE SET compact_text = excluded.compact_text;

PRAGMA user_version = 3;
