CREATE TEMP VIEW price_withdrawal_entries AS
SELECT id,
       COALESCE(NULLIF(trim(withdrawal_short), ''),
           rtrim(
               CASE WHEN trim(coalesce(edible_tissues, '')) = '' THEN ''
                    ELSE 'tk. ' || trim(edible_tissues) || ', ' END ||
               CASE WHEN trim(coalesce(milk, '')) = ''
                      OR lower(trim(milk)) = 'nie dotyczy'
                      OR trim(milk) LIKE 'Nie dotyczy — ChPL ustanawia dla świń karencję tkanek jadalnych%'
                      OR trim(milk) LIKE 'Nie dotyczy (docelowy gatunek:%'
                    THEN ''
                    ELSE 'ml. ' || CASE WHEN lower(trim(milk)) = 'niedopuszczony'
                                        THEN 'niedopuszczone' ELSE trim(milk) END END,
               ', ')
       ) AS compact_text
FROM variants;
