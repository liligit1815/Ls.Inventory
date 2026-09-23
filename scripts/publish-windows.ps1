param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.2.0',
    [switch]$UseInstalledDependencies
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path -Parent $PSScriptRoot
$webRoot = Join-Path $projectRoot 'src/Ls.Inventory.Web'
$buildRoot = Join-Path $projectRoot ('.artifacts/windows-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
$packageName = "LS.Inventory-$Version-win-x64"
$packageRoot = Join-Path $buildRoot $packageName
$outputRoot = Join-Path $packageRoot 'site'
foreach ($command in @('node', 'npm.cmd', 'dotnet', 'git')) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) { throw "Required command missing: $command" }
}
# npm ci replaces native dependencies; a running development server can hold them open.
if (-not $UseInstalledDependencies -and (Get-NetTCPConnection -LocalPort 5173 -State Listen -ErrorAction SilentlyContinue)) {
    throw 'Port 5173 is in use. Use a separate checkout or -UseInstalledDependencies with dependencies already installed from package-lock.json.'
}
Push-Location $webRoot
try {
    if (-not $UseInstalledDependencies) {
        & npm.cmd ci
        if ($LASTEXITCODE -ne 0) { throw 'npm ci failed' }
    }
    & npm.cmd test
    if ($LASTEXITCODE -ne 0) { throw 'Frontend tests failed' }
    & npm.cmd run build
    if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed' }
} finally { Pop-Location }
Push-Location $projectRoot
try {
    $commit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify source commit' }
    $dirty = [bool](& git status --porcelain)
    & dotnet publish 'src/Ls.Inventory.Api/Ls.Inventory.Api.csproj' -c Release -r win-x64 --self-contained true "-p:Version=$Version" -p:DebugType=None -p:DebugSymbols=false -o $outputRoot
    if ($LASTEXITCODE -ne 0) { throw 'API publish failed' }
    $wwwroot = Join-Path $outputRoot 'wwwroot'
    New-Item -ItemType Directory -Path $wwwroot -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $webRoot 'dist') | Copy-Item -Destination $wwwroot -Recurse
    foreach ($required in @('web.config', 'Ls.Inventory.Api.exe', 'Ls.Inventory.Api.dll', 'coreclr.dll', 'wwwroot/index.html')) {
        if (-not (Test-Path -LiteralPath (Join-Path $outputRoot $required))) { throw "Missing publish file: $required" }
    }
    if (@(Get-ChildItem -LiteralPath (Join-Path $outputRoot 'Templates') -Filter '*.xlsx').Count -ne 1) { throw 'Missing or ambiguous import template' }
    $forbidden = Get-ChildItem -LiteralPath $outputRoot -Recurse -File | Where-Object {
        $_.Name -match '(?i)(^appsettings\.(Production|Development|Local)\.json$|\.local\.json$|^\.env|\.(pfx|p12|key|dump|backup|log)$)'
    }
    if ($forbidden) { throw 'Publish output contains local configuration or data; refusing to package.' }
    New-Item -ItemType Directory -Path (Join-Path $packageRoot 'config'), (Join-Path $packageRoot 'tools') -Force | Out-Null
    Copy-Item -LiteralPath 'deploy/appsettings.Production.example.json' -Destination (Join-Path $packageRoot 'config')
    Copy-Item -LiteralPath 'deploy/Prepare-Update.ps1' -Destination (Join-Path $packageRoot 'tools')
    Copy-Item -LiteralPath 'deploy/README-Windows.md' -Destination (Join-Path $packageRoot 'README-Windows.md')
    Copy-Item -LiteralPath 'docs/Windows生产环境安装部署指南.md' -Destination $packageRoot
    [ordered]@{ version=$Version; runtime='win-x64'; selfContained=$true; sourceCommit=$commit; workingTreeDirty=$dirty; builtAtUtc=[DateTime]::UtcNow.ToString('o') } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $packageRoot 'release.json') -Encoding UTF8
    $manifest = @(Get-ChildItem -LiteralPath $outputRoot -Recurse -File | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path=$_.FullName.Substring($outputRoot.Length + 1).Replace('\','/'); sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
    ConvertTo-Json -InputObject $manifest -Depth 4 | Set-Content -LiteralPath (Join-Path $packageRoot 'site-manifest.json') -Encoding UTF8
    $zipPath = Join-Path $buildRoot ($packageName + '.zip')
    Compress-Archive -LiteralPath $packageRoot -DestinationPath $zipPath -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $packageName.zip" | Set-Content -LiteralPath ($zipPath + '.sha256') -Encoding ASCII
    Write-Host "Package: $zipPath"
    Write-Host "SHA256: $hash"
    Write-Host 'Includes runtime and frontend; requires IIS Hosting Bundle, HTTPS and PostgreSQL on the target.'
} finally { Pop-Location }
