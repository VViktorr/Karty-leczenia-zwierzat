$ErrorActionPreference = 'Stop'
$app = Start-Process -FilePath "$pwd/publish/KartyLeczenia.exe" -PassThru
try {
    $found = $false
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Seconds 1
        $app.Refresh()
        if ($app.HasExited) { throw "Application exited during startup: $($app.ExitCode)" }
        if ($app.MainWindowHandle -ne 0 -and $app.MainWindowTitle -like '*Karty leczenia*') { $found = $true; break }
    }
    if (-not $found) { throw 'MAUI did not create its native Windows window.' }
    Start-Sleep -Seconds 3
    $app.Refresh()
    if ($app.HasExited) { throw 'MAUI window closed unexpectedly.' }
    Write-Output "Native MAUI window opened: $($app.MainWindowTitle)"
}
finally { if (-not $app.HasExited) { Stop-Process -Id $app.Id } }
