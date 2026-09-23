# Stages a new release without modifying the existing site, IIS, or database.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ExistingSite,
    [Parameter(Mandatory=$true)][string]$NewSite
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
function Get-FullDirectory([string]$Path) {
    if (-not [IO.Path]::IsPathRooted($Path)) { throw 'Use an absolute directory path.' }
    return [IO.Path]::GetFullPath($Path).TrimEnd('\','/')
}
function Assert-NoReparseParents([string]$Path) {
    $current = $Path
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw 'Junctions and symbolic links are not supported for update paths.'
            }
        }
        $current = Split-Path -Parent $current
    }
}
$packageRoot = Split-Path -Parent $PSScriptRoot
$source = Get-FullDirectory (Join-Path $packageRoot 'site')
$old = Get-FullDirectory $ExistingSite
$target = Get-FullDirectory $NewSite
foreach ($path in @($source, $old, $target)) { Assert-NoReparseParents $path }
if (Test-Path -LiteralPath $target) { throw 'NewSite already exists. Choose a new empty version directory; nothing was overwritten.' }
foreach ($protected in @($old, $source, (Get-FullDirectory $packageRoot))) {
    if ($target.Equals($protected, [StringComparison]::OrdinalIgnoreCase) -or $target.StartsWith($protected + '\', [StringComparison]::OrdinalIgnoreCase) -or $protected.StartsWith($target + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'NewSite must be separate from both the old site and extracted package.'
    }
}
if (-not (Test-Path -LiteralPath (Join-Path $old 'Ls.Inventory.Api.dll'))) { throw 'ExistingSite must be an LS Inventory Lite published site.' }
$oldConfig = Join-Path $old 'appsettings.Production.json'
if (-not (Test-Path -LiteralPath $oldConfig)) { throw 'Existing production JSON configuration is required. For IIS/environment-only configuration, follow the manual upgrade guide.' }
Get-Content -LiteralPath $oldConfig -Raw | ConvertFrom-Json | Out-Null
foreach ($file in @($oldConfig, (Join-Path $old 'web.config'))) { Assert-NoReparseParents $file }
$files = @(Get-ChildItem -LiteralPath $source -Recurse -Force -File)
if (Get-ChildItem -LiteralPath $source -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Release contains unsupported links.' }
if ($files | Where-Object { $_.Name -match '(?i)(^appsettings\.(Production|Development|Local)\.json$|\.local\.json$|^\.env|\.(pfx|p12|key|dump|backup|log)$)' }) { throw 'Use a clean extracted release; it must not contain local configuration or data.' }
$manifest = Get-Content -LiteralPath (Join-Path $packageRoot 'site-manifest.json') -Raw | ConvertFrom-Json
$manifest = @($manifest)
if ($files.Count -ne $manifest.Count) { throw 'Release file count does not match manifest.' }
$seen = @{}
foreach ($item in $manifest) {
    $filePath = [IO.Path]::GetFullPath((Join-Path $source $item.path))
    if (-not $filePath.StartsWith($source + '\', [StringComparison]::OrdinalIgnoreCase) -or $seen.ContainsKey($filePath)) { throw 'Invalid release manifest path.' }
    $seen[$filePath] = $true
    if (-not (Test-Path -LiteralPath $filePath -PathType Leaf) -or (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash -ne $item.sha256) { throw "Release integrity check failed: $($item.path)" }
}
$oldWebConfig = Join-Path $old 'web.config'
$webXml = $null
if (Test-Path -LiteralPath $oldWebConfig) {
    $webXml = New-Object System.Xml.XmlDocument
    $webXml.XmlResolver = $null
    $webXml.Load($oldWebConfig)
    $hostNode = $webXml.SelectSingleNode('//system.webServer/aspNetCore')
    if (-not $hostNode) { throw 'Old web.config has no ASP.NET Core host; use the manual upgrade guide.' }
    $hostNode.SetAttribute('processPath', '.\Ls.Inventory.Api.exe')
    $hostNode.SetAttribute('arguments', '')
}
New-Item -ItemType Directory -Path $target | Out-Null
# Set the previous site's access policy before copying any production secrets.
$siteAcl = Get-Acl -LiteralPath $old
$siteAcl.SetAccessRuleProtection($true, $true)
Set-Acl -LiteralPath $target -AclObject $siteAcl
Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $target -Recurse
Copy-Item -LiteralPath $oldConfig -Destination (Join-Path $target 'appsettings.Production.json')
Set-Acl -LiteralPath (Join-Path $target 'appsettings.Production.json') -AclObject (Get-Acl -LiteralPath $oldConfig)
if ($webXml) {
    $webXml.Save((Join-Path $target 'web.config'))
    Set-Acl -LiteralPath (Join-Path $target 'web.config') -AclObject (Get-Acl -LiteralPath $oldWebConfig)
}
Copy-Item -LiteralPath (Join-Path $packageRoot 'release.json') -Destination (Join-Path $target 'release.json')
Write-Host "New version staged: $target"
Write-Host 'Existing site, database, passwords and IIS were not changed.'
Write-Host 'Next: stop writes, back up the database, initialize from the new site in Production, then switch the IIS physical path and validate HTTPS/health/login.'
Write-Host 'Keep the old site and database backup until acceptance. Read README-Windows.md for exact steps.'
