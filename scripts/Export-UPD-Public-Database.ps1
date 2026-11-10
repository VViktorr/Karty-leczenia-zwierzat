param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\upd-export'),
    [ValidateRange(1, 1000)]
    [int]$PageSize = 250,
    [ValidateRange(0, 30)]
    [int]$DelayMilliseconds = 400
)

$ErrorActionPreference = 'Stop'

# Public UPD API: https://prod-upd-openapi-app.azurewebsites.net/?urls.primaryName=PUBLIC+API
$productSearchUrl = 'https://spor.azure-api.net/upd/public/v4/MedicinalProductDefinition'

function Get-FormEncodedValue([string]$Value) {
    return [Uri]::EscapeDataString($Value)
}

function Invoke-UpdGet([string]$Uri, [string]$AccessToken) {
    $attempt = 0
    while ($true) {
        $attempt++
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Method Get -Uri $Uri -Headers @{
                Authorization = "Bearer $AccessToken"
                Accept        = 'application/fhir+json'
            }
            return ($response.Content | ConvertFrom-Json)
        }
        catch {
            $statusCode = 0
            $retryAfter = 2
            try {
                $statusCode = [int]$_.Exception.Response.StatusCode
                $header = $_.Exception.Response.Headers['Retry-After']
                if ($header -and [int]::TryParse([string]$header, [ref]$retryAfter)) { }
            } catch { }

            if (($statusCode -eq 429 -or $statusCode -ge 500) -and $attempt -lt 6) {
                $waitSeconds = [Math]::Min([Math]::Max($retryAfter, [Math]::Pow(2, $attempt)), 60)
                Write-Warning "UPD zwrócił HTTP $statusCode. Ponawiam za $waitSeconds s (próba $attempt/5)."
                Start-Sleep -Seconds $waitSeconds
                continue
            }

            throw "Nie udało się pobrać danych z UPD (HTTP $statusCode). Szczegóły: $($_.Exception.Message)"
        }
    }
}

function Get-NextPageUrl($Bundle, [string]$CurrentUrl) {
    if (-not $Bundle.link) { return $null }
    $nextLink = $Bundle.link | Where-Object { $_.relation -eq 'next' } | Select-Object -First 1
    if (-not $nextLink -or -not $nextLink.url) { return $null }
    return ([Uri]::new([Uri]$CurrentUrl, [string]$nextLink.url)).AbsoluteUri
}

$tokenUrl = (Read-Host 'OAuth2 Token Endpoint z e-maila EMA').Trim()
$clientId = (Read-Host 'Client ID').Trim()
$scope = (Read-Host 'Scope z e-maila EMA').Trim()
$secureSecret = Read-Host 'Client Secret (wprowadzany bez zapisu do pliku)' -AsSecureString

if ([string]::IsNullOrWhiteSpace($tokenUrl) -or
    [string]::IsNullOrWhiteSpace($clientId) -or
    [string]::IsNullOrWhiteSpace($scope) -or
    $secureSecret.Length -eq 0) {
    throw 'Brakuje jednego z wymaganych danych OAuth2.'
}

$secretPointer = [IntPtr]::Zero
$clientSecret = $null
$accessToken = $null
$writer = $null

try {
    $secretPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureSecret)
    $clientSecret = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($secretPointer)

    $tokenForm = 'grant_type=client_credentials' +
        '&client_id=' + (Get-FormEncodedValue $clientId) +
        '&client_secret=' + (Get-FormEncodedValue $clientSecret) +
        '&scope=' + (Get-FormEncodedValue $scope)

    $tokenResponse = Invoke-RestMethod -Method Post -Uri $tokenUrl `
        -ContentType 'application/x-www-form-urlencoded' -Body $tokenForm
    $accessToken = [string]$tokenResponse.access_token
    if ([string]::IsNullOrWhiteSpace($accessToken)) {
        throw 'EMA nie zwróciła tokenu dostępu. Sprawdź Token Endpoint, Scope i dane aplikacji.'
    }

    $resolvedOutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
    New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null
    $jsonlPath = Join-Path $resolvedOutputDirectory 'upd-products.jsonl'
    $manifestPath = Join-Path $resolvedOutputDirectory 'manifest.json'
    $zipPath = $resolvedOutputDirectory.TrimEnd('\','/') + '.zip'

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    $writer = New-Object System.IO.StreamWriter($jsonlPath, $false, $utf8NoBom)

    $nextUrl = $productSearchUrl + '?_count=' + $PageSize
    $seenUrls = New-Object 'System.Collections.Generic.HashSet[string]'
    $seenIds = New-Object 'System.Collections.Generic.HashSet[string]'
    $page = 0
    $reportedTotal = $null

    while ($nextUrl) {
        if (-not $seenUrls.Add($nextUrl)) { throw 'API zwróciło zapętlony link do kolejnej strony.' }
        $page++
        Write-Progress -Activity 'Pobieranie katalogu UPD' -Status "Strona $page, pobrano $($seenIds.Count) produktów"

        $bundle = Invoke-UpdGet -Uri $nextUrl -AccessToken $accessToken
        if ($bundle.resourceType -ne 'Bundle') {
            throw "Oczekiwano odpowiedzi FHIR Bundle, otrzymano: $($bundle.resourceType)."
        }
        if ($null -ne $bundle.total) { $reportedTotal = [long]$bundle.total }

        foreach ($entry in @($bundle.entry)) {
            if (-not $entry.resource) { continue }
            $resource = $entry.resource
            if ($resource.resourceType -ne 'MedicinalProductDefinition') { continue }
            $id = [string]$resource.id
            if ($id -and -not $seenIds.Add($id)) { continue }
            $writer.WriteLine(($resource | ConvertTo-Json -Depth 100 -Compress))
        }
        $writer.Flush()

        $nextUrl = Get-NextPageUrl -Bundle $bundle -CurrentUrl $nextUrl
        if ($nextUrl -and $DelayMilliseconds -gt 0) { Start-Sleep -Milliseconds $DelayMilliseconds }
    }

    $writer.Dispose()
    $writer = $null
    Write-Progress -Activity 'Pobieranie katalogu UPD' -Completed

    $complete = ($null -eq $reportedTotal -or $seenIds.Count -ge $reportedTotal)
    $manifest = [ordered]@{
        source = 'EMA Union Product Database (UPD) Public API'
        apiVersion = 'v4'
        exportDate = (Get-Date).ToString('yyyy-MM-ddTHH:mm:ssK')
        pagesDownloaded = $page
        uniqueProductsDownloaded = $seenIds.Count
        totalReportedByApi = $reportedTotal
        complete = $complete
        rawDataFile = 'upd-products.jsonl'
        note = 'Jeden rekord JSON FHIR na linię. API publiczne nie dołącza treści dokumentów ChPL/PAR; są dostępne w portalu UPD.'
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $manifestPath -Encoding UTF8

    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $resolvedOutputDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal

    if (-not $complete) {
        throw "Eksport może być niepełny: API zgłosiło $reportedTotal produktów, pobrano $($seenIds.Count). Sprawdź manifest i log błędu paginacji."
    }

    Write-Host "Gotowe. Produkty: $($seenIds.Count), strony: $page"
    Write-Host "Plik ZIP: $zipPath"
}
finally {
    if ($writer) { $writer.Dispose() }
    if ($secretPointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($secretPointer) }
    $clientSecret = $null
    $accessToken = $null
    $tokenForm = $null
    $tokenResponse = $null
    if ($secureSecret) { $secureSecret.Dispose() }
}
