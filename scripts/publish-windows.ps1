param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path -Parent $PSScriptRoot
$webRoot = Join-Path $projectRoot 'src/Ls.Inventory.Web'
$outputRoot = Join-Path $projectRoot ('.artifacts/windows-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
foreach ($command in @('node', 'npm.cmd', 'dotnet')) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) { throw "Required command missing: $command" }
}
# npm ci replaces native dependencies; do not run it while the local dev server holds them open.
if (Get-NetTCPConnection -LocalPort 5173 -State Listen -ErrorAction SilentlyContinue) {
    throw 'Port 5173 is in use. Stop the development server before publishing, or build in a separate checkout.'
}
Push-Location $webRoot
try {
    & npm.cmd ci
    if ($LASTEXITCODE -ne 0) { throw 'npm ci failed' }
    & npm.cmd test
    if ($LASTEXITCODE -ne 0) { throw 'Frontend tests failed' }
    & npm.cmd run build
    if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed' }
} finally { Pop-Location }
Push-Location $projectRoot
try {
    & dotnet publish 'src/Ls.Inventory.Api/Ls.Inventory.Api.csproj' -c Release --self-contained false -o $outputRoot
    if ($LASTEXITCODE -ne 0) { throw 'API publish failed' }
    $wwwroot = Join-Path $outputRoot 'wwwroot'
    New-Item -ItemType Directory -Path $wwwroot -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $webRoot 'dist') | Copy-Item -Destination $wwwroot -Recurse
    foreach ($required in @('web.config', 'Ls.Inventory.Api.dll', 'wwwroot/index.html')) {
        if (-not (Test-Path -LiteralPath (Join-Path $outputRoot $required))) { throw "Missing publish file: $required" }
    }
    if (@(Get-ChildItem -LiteralPath (Join-Path $outputRoot 'Templates') -Filter '*.xlsx').Count -ne 1) { throw 'Missing or ambiguous import template' }
    if (Test-Path -LiteralPath (Join-Path $outputRoot 'appsettings.Production.json')) { throw 'Do not package production secrets' }
    Write-Host "Publish directory: $outputRoot"
    Write-Host 'Copy the directory contents to the IIS site. Configure production secrets on the target computer only.'
} finally { Pop-Location }
