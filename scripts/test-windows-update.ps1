$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path -Parent $PSScriptRoot
$root = Join-Path $repo ('.artifacts/update-test-' + [guid]::NewGuid().ToString('N'))
$package = Join-Path $root 'package'
$site = Join-Path $package 'site'
$old = Join-Path $root 'old-site'
$target = Join-Path $root 'new-site'
New-Item -ItemType Directory -Path $site, $old, (Join-Path $package 'tools') | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'deploy/Prepare-Update.ps1') -Destination (Join-Path $package 'tools')
$tool = Join-Path $package 'tools/Prepare-Update.ps1'
'new-binary-fixture' | Set-Content -LiteralPath (Join-Path $site 'Ls.Inventory.Api.dll')
'old-binary-fixture' | Set-Content -LiteralPath (Join-Path $old 'Ls.Inventory.Api.dll')
'{"ConnectionStrings":{"Inventory":"TEST-ONLY"},"Jwt":{"SigningKey":"TEST-ONLY"}}' | Set-Content -LiteralPath (Join-Path $old 'appsettings.Production.json')
'<configuration><system.webServer><aspNetCore processPath="dotnet" arguments=".\Ls.Inventory.Api.dll"><environmentVariables><environmentVariable name="CUSTOM_TEST" value="retained" /></environmentVariables></aspNetCore><httpProtocol><customHeaders><add name="X-Example" value="preserved" /></customHeaders></httpProtocol></system.webServer></configuration>' | Set-Content -LiteralPath (Join-Path $old 'web.config')
'<configuration />' | Set-Content -LiteralPath (Join-Path $site 'web.config')
'{"version":"test"}' | Set-Content -LiteralPath (Join-Path $package 'release.json')
$manifest = @(Get-ChildItem -LiteralPath $site -File | ForEach-Object { @{path=$_.Name;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} })
ConvertTo-Json -InputObject $manifest | Set-Content -LiteralPath (Join-Path $package 'site-manifest.json')
$oldHashes = @(Get-ChildItem -LiteralPath $old -File | Get-FileHash | Select-Object Path,Hash | ConvertTo-Json)
& $tool -ExistingSite $old -NewSite $target
if ((Get-FileHash -LiteralPath (Join-Path $old 'appsettings.Production.json')).Hash -ne (Get-FileHash -LiteralPath (Join-Path $target 'appsettings.Production.json')).Hash) { throw 'Production configuration changed.' }
if ((Get-FileHash -LiteralPath (Join-Path $site 'Ls.Inventory.Api.dll')).Hash -ne (Get-FileHash -LiteralPath (Join-Path $target 'Ls.Inventory.Api.dll')).Hash) { throw 'New binary missing.' }
$newHashes = @(Get-ChildItem -LiteralPath $old -File | Get-FileHash | Select-Object Path,Hash | ConvertTo-Json)
if ($oldHashes[0] -ne $newHashes[0]) { throw 'Old site was modified.' }
[xml]$xml = Get-Content -LiteralPath (Join-Path $target 'web.config') -Raw
if ($xml.configuration.'system.webServer'.aspNetCore.processPath -ne '.\Ls.Inventory.Api.exe' -or $xml.configuration.'system.webServer'.aspNetCore.arguments -ne '') { throw 'IIS executable was not updated.' }
if ($xml.configuration.'system.webServer'.aspNetCore.environmentVariables.environmentVariable.value -ne 'retained' -or $xml.configuration.'system.webServer'.httpProtocol.customHeaders.add.value -ne 'preserved') { throw 'Custom IIS settings were lost.' }
if (-not (Get-Acl -LiteralPath $target).AreAccessRulesProtected) { throw 'New directory can inherit broader permissions.' }
foreach ($unsafeTarget in @($old, $target, (Join-Path $old 'nested'), (Join-Path $package 'nested'))) {
    $rejected = $false
    try { & $tool -ExistingSite $old -NewSite $unsafeTarget } catch { $rejected = $true }
    if (-not $rejected) { throw 'Unsafe destination was accepted.' }
}
'tampered' | Add-Content -LiteralPath (Join-Path $site 'Ls.Inventory.Api.dll')
$rejected = $false
try { & $tool -ExistingSite $old -NewSite (Join-Path $root 'tamper-target') } catch { $rejected = $true }
if (-not $rejected -or (Test-Path -LiteralPath (Join-Path $root 'tamper-target'))) { throw 'Tampered release was not rejected before copying.' }
Write-Host 'PASS: old files unchanged, new files copied, production config preserved, IIS settings retained, ACL protected, unsafe targets and tampering rejected.'
