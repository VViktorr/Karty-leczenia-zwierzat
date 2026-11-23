# Regression checks for installer metadata parsing. No SQL connection or installer is needed.
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot '..\..\scripts\Publish-ReactKLUpdate.ps1'
$tokens = $null; $errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    (Resolve-Path $source).Path, [ref]$tokens, [ref]$errors)
if ($errors.Count -gt 0) { throw "Cannot parse publisher: $($errors | Out-String)" }
$required = @('ParseRelease', 'CompareRelease', 'GetInstallerReleaseLabel')
$definitions = $ast.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst]
}, $false)
foreach ($name in $required) {
    $match = @($definitions | Where-Object { $_.Name -eq $name })
    if ($match.Count -ne 1) { throw "Expected exactly one $name definition" }
    . ([scriptblock]::Create($match[0].Extent.Text))
}
function Assert([bool]$test, [string]$message) {
    if (-not $test) { throw "Release metadata regression: $message" }
}
foreach ($version in @('4.0.5', '4.0.5-rc.1', '4.0.5-rc.12')) {
    $label = GetInstallerReleaseLabel "Karty Leczenia $version                                   " "0.0.0"
    Assert ($label -ceq $version) "Trailing spaces were not removed: $version"
    $parsed = ParseRelease $label
    Assert ((CompareRelease $parsed (ParseRelease $version)) -eq 0) "Cannot parse $version"
}
$backupLabel = GetInstallerReleaseLabel 'Other description   ' '4.0.5-rc.2    '
Assert ($backupLabel -ceq '4.0.5-rc.2') 'ProductVersion fallback must be trimmed'
Assert ((CompareRelease (ParseRelease '4.0.5-rc.2') (ParseRelease '4.0.5-rc.10')) -lt 0) 'RC numerical order'
Assert ((CompareRelease (ParseRelease '4.0.5-rc.10') (ParseRelease '4.0.5')) -lt 0) 'Stable supersedes RC'
foreach ($bad in @('4.0.5   ', '4.0.5-rc.0', '4.0.5-rc.01', '4.0.5-preview.1')) {
    $rejected = $false
    try { $null = ParseRelease $bad } catch { $rejected = $true }
    Assert $rejected "Untrusted version must be rejected: [$bad]"
}
# Ensure the production publish path really uses the tested metadata helper.
Assert ($ast.Extent.Text.Contains('GetInstallerReleaseLabel ([string]$meta.FileDescription) ([string]$meta.ProductVersion)')) 'Publisher must call tested helper'
Assert ($ast.Extent.Text.Contains('[Security.Cryptography.SHA256]::Create()')) 'SHA-256 protection must remain'
Assert ($ast.Extent.Text.Contains('W SQL jest już wersja')) 'Same-version/hash protection must remain'
Write-Host 'Installer release metadata regression checks passed (stable, RC, padded FileDescription, strict input).'
