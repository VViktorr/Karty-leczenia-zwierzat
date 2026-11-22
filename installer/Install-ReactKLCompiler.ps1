$ErrorActionPreference = 'Stop'
$version = '7.1.1'
$distribution = 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_1/innosetup-7.1.1-x64.exe'
$setup = Join-Path $env:RUNNER_TEMP "innosetup-$version-x64.exe"
$destination = Join-Path $env:RUNNER_TEMP "ReactKL-Inno-$version"
Invoke-WebRequest -Uri $distribution -OutFile $setup
$process = Start-Process -FilePath $setup -ArgumentList @(
    '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=`"$destination`""
) -Wait -PassThru
$iscc = Join-Path $destination 'ISCC.exe'
if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $iscc)) {
    throw "Installation of Inno Setup $version failed (exit code $($process.ExitCode))."
}
"REACTKL_ISCC=$iscc" >> $env:GITHUB_ENV
