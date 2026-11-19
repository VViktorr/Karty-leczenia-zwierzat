[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Server,
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [switch]$Preview
)

# Idempotent, non-destructive catalogue addition. Run on the SQL server under
# a Windows account with write access to the KartyLeczenia database.
$ErrorActionPreference = 'Stop'
$symbol = '000002691'
$pdfRelative = 'data/ChPL/RPL/Charakterystyka-40841-2025-10-23-6235_W-2025-12-03.pdf'
$pdfFile = Join-Path $RepositoryRoot ($pdfRelative -replace '/', [IO.Path]::DirectorySeparatorChar)
if (-not (Test-Path -LiteralPath $pdfFile -PathType Leaf)) {
    throw "Nie znaleziono ChPL z repozytorium: $pdfFile"
}
$pdf = [IO.File]::ReadAllBytes($pdfFile)
if ($pdf.Length -lt 5 -or [Text.Encoding]::ASCII.GetString($pdf, 0, 5) -ne '%PDF-') {
    throw "Plik nie jest poprawnym PDF: $pdfFile"
}
$shaAlgorithm = [Security.Cryptography.SHA256]::Create()
try { $hash = $shaAlgorithm.ComputeHash($pdf) }
finally { $shaAlgorithm.Dispose() }

$builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
$builder['Data Source'] = $Server
$builder['Initial Catalog'] = 'KartyLeczenia'
$builder['Integrated Security'] = $true
$builder['Encrypt'] = $true
$builder['TrustServerCertificate'] = $true
$builder['Connect Timeout'] = 10
$db = New-Object System.Data.SqlClient.SqlConnection $builder.ConnectionString
try {
    $db.Open()
    $transaction = $db.BeginTransaction([System.Data.IsolationLevel]::Serializable)
    try {
        $command = $db.CreateCommand()
        $command.Transaction = $transaction
        $command.CommandTimeout = 60
        $command.CommandText = @'
IF OBJECT_ID(N'dbo.catalog_products', N'U') IS NULL OR
   OBJECT_ID(N'dbo.catalog_variants', N'U') IS NULL OR
   OBJECT_ID(N'dbo.catalog_documents', N'U') IS NULL OR
   OBJECT_ID(N'dbo.catalog_price_withdrawal', N'U') IS NULL
    THROW 51001, N'Brak tabel katalogu leków. Najpierw przygotuj archiwum SQL.', 1;

IF OBJECT_ID(N'dbo.catalog_import', N'U') IS NULL
    THROW 51003, N'Nie zaimportowano jeszcze katalogu SQL.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.catalog_import WHERE id = 1)
    THROW 51003, N'Najpierw zaimportuj katalog instalatorem serwera.', 1;

DECLARE @symbol nvarchar(128) = @symbolParameter;
DECLARE @path nvarchar(450) = @pathParameter;

-- Never change a PDF silently when an existing row uses the same path.
IF EXISTS (SELECT 1 FROM dbo.catalog_documents WITH (UPDLOCK, HOLDLOCK) WHERE path = @path)
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.catalog_documents WHERE path = @path
               AND HASHBYTES('SHA2_256', pdf) <> @sha)
        THROW 51002, N'PDF pod istniejącą ścieżką ma inną zawartość.', 1;
END
ELSE
    INSERT dbo.catalog_documents(path, pdf) VALUES (@path, @pdf);

-- Keep any operator-edited existing entry intact. Subiekt name/stock are
-- external and are NOT modified by this catalogue registration.
IF NOT EXISTS (SELECT 1 FROM dbo.catalog_products WITH (UPDLOCK, HOLDLOCK) WHERE symbol = @symbol)
    INSERT dbo.catalog_products(symbol, name, description, rpl_id, status)
    VALUES (
        @symbol,
        N'Vigophos 100 mg/ml + 0,05 mg/ml, roztwór do wstrzykiwań, 100 ml',
        N'Butafosfan 100 mg/ml + cyjanokobalamina 0,05 mg/ml. Uwaga: nazwa w Subiekcie zawiera 0,5 mg/ml; zweryfikować nazwę w Subiekcie. Według ChPL RPL 40841; pozwolenie 2800/18.',
        N'2800/18',
        N'Zweryfikowano wg ChPL');

DECLARE @v TABLE (
    species nvarchar(80), route nvarchar(120), dose nvarchar(max),
    indication nvarchar(max), tissues nvarchar(max), milk nvarchar(max),
    restrictions nvarchar(max), compact nvarchar(max)
);

INSERT @v (species,route,dose,indication,tissues,milk,restrictions,compact)
VALUES
(N'Bydło', N'dożylna (i.v.)', N'5–10 ml/100 kg m.c.; przy wspomaganiu leczenia wtórnej ketozy przez 3 kolejne dni; profilaktycznie przez 3 dni w ciągu 10 dni przed wycieleniem',
 N'Leczenie wspomagające i zapobieganie hipofosfatemii i/lub niedoborom witaminy B12; wspomaganie przeżuwania po leczeniu przemieszczenia trawieńca z wtórną ketozą; uzupełnienie terapii Ca/Mg porażenia poporodowego; zapobieganie ketozie przed wycieleniem',
 N'0 dni', N'0 godzin', N'Podawać dożylnie bardzo powoli; ryzyko wstrząsu przy zbyt szybkim podaniu.', N'tk. 0 dni, ml. 0 h'),
(N'Konie', N'dożylna (i.v.)', N'5–10 ml/100 kg m.c.; w razie potrzeby powtarzać',
 N'Leczenie wspomagające i zapobieganie hipofosfatemii i/lub niedoborom witaminy B12; wspomaganie przy przeciążeniu mięśniowym',
 N'0 dni', N'0 godzin', N'Podawać dożylnie bardzo powoli; bezpieczeństwo podczas ciąży/laktacji u klaczy nieustalone.', N'tk. 0 dni, ml. 0 h'),
(N'Psy', N'dożylna (i.v.)', N'0,1–0,15 ml/kg m.c.; w razie potrzeby powtarzać',
 N'Leczenie wspomagające i zapobieganie hipofosfatemii i/lub niedoborom witaminy B12',
 N'', N'', N'Podawać dożylnie bardzo powoli; w przewlekłej niewydolności nerek po ocenie korzyści/ryzyka.', N''),
(N'Psy', N'domięśniowa (i.m.)', N'0,1–0,15 ml/kg m.c.; w razie potrzeby powtarzać',
 N'Leczenie wspomagające i zapobieganie hipofosfatemii i/lub niedoborom witaminy B12',
 N'', N'', N'W przewlekłej niewydolności nerek po ocenie korzyści/ryzyka.', N''),
(N'Psy', N'podskórna (s.c.)', N'0,1–0,15 ml/kg m.c.; w razie potrzeby powtarzać',
 N'Leczenie wspomagające i zapobieganie hipofosfatemii i/lub niedoborom witaminy B12',
 N'', N'', N'W przewlekłej niewydolności nerek po ocenie korzyści/ryzyka.', N'');

-- Only add missing species/route pairs: no overwriting or deleting variants.
DECLARE @species nvarchar(80), @route nvarchar(120), @dose nvarchar(max),
        @indication nvarchar(max), @tissues nvarchar(max), @milk nvarchar(max),
        @restrictions nvarchar(max), @compact nvarchar(max), @id int;

DECLARE variants_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT species,route,dose,indication,tissues,milk,restrictions,compact FROM @v;

OPEN variants_cursor;
FETCH NEXT FROM variants_cursor INTO @species,@route,@dose,@indication,@tissues,@milk,@restrictions,@compact;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.catalog_variants WITH (UPDLOCK, HOLDLOCK)
                   WHERE symbol=@symbol AND species=@species AND route=@route)
    BEGIN
        INSERT dbo.catalog_variants
            (symbol,name,rpl_id,species,indication,route,dose,edible_tissues,milk,restrictions,
             source,verified_at,card_text,pdf_path,withdrawal_short)
        VALUES
            (@symbol,N'Vigophos 100 mg/ml + 0,05 mg/ml, 100 ml',N'2800/18',
             @species,@indication,@route,@dose,@tissues,@milk,@restrictions,
             N'RPL 40841; ChPL aktualizacja 09/2025',N'2026-11-19',
             CONCAT(@species,N' · ',@indication,N' · ',@route,N' · ',@dose),
             @path,@compact);
        SET @id = CONVERT(int, SCOPE_IDENTITY());
        INSERT dbo.catalog_price_withdrawal(id,compact_text) VALUES (@id,@compact);
    END
    FETCH NEXT FROM variants_cursor INTO @species,@route,@dose,@indication,@tissues,@milk,@restrictions,@compact;
END
CLOSE variants_cursor;
DEALLOCATE variants_cursor;

SELECT p.symbol, p.name,
       (SELECT COUNT(*) FROM dbo.catalog_variants WHERE symbol=p.symbol) AS variant_count,
       (SELECT COUNT(*) FROM dbo.catalog_variants WHERE symbol=p.symbol AND pdf_path=@path) AS pdf_links
FROM dbo.catalog_products p WHERE p.symbol=@symbol;
'@
        $null = $command.Parameters.Add('@symbolParameter', [Data.SqlDbType]::NVarChar, 128)
        $command.Parameters['@symbolParameter'].Value = $symbol
        $null = $command.Parameters.Add('@pathParameter', [Data.SqlDbType]::NVarChar, 450)
        $command.Parameters['@pathParameter'].Value = $pdfRelative
        $null = $command.Parameters.Add('@sha', [Data.SqlDbType]::Binary, 32)
        $command.Parameters['@sha'].Value = $hash
        $null = $command.Parameters.Add('@pdf', [Data.SqlDbType]::VarBinary, -1)
        $command.Parameters['@pdf'].Value = $pdf

        $reader = $command.ExecuteReader()
        try {
            while ($reader.Read()) {
                Write-Host ("Symbol: {0}; produkt: {1}; warianty: {2}; powiazane PDF: {3}" -f
                    $reader.GetString(0), $reader.GetString(1), $reader.GetInt32(2), $reader.GetInt32(3))
            }
        }
        finally { $reader.Close() }
        if ($Preview) {
            $transaction.Rollback()
            Write-Host 'Podglad zakonczony. Zmiany zostaly wycofane.'
        } else {
            $transaction.Commit()
            Write-Host 'Zapisano / zweryfikowano wpis Vigophos w bazie SQL.'
        }
    }
    catch {
        try { $transaction.Rollback() } catch {}
        throw
    }
    finally { $transaction.Dispose() }
}
finally { $db.Dispose() }
