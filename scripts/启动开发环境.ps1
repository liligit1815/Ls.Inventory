$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$runtimeDirectory = Join-Path $projectRoot '.dev-runtime'
$stateFile = Join-Path $runtimeDirectory 'processes.json'

New-Item -ItemType Directory -Path $runtimeDirectory -Force | Out-Null

function Test-ServicePort([int]$port, [string]$localAddress = '') {
    $listeners = Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue
    if ([string]::IsNullOrWhiteSpace($localAddress)) {
        return [bool]$listeners
    }

    return [bool]($listeners | Where-Object { $_.LocalAddress -eq $localAddress })
}

$savedState = if (Test-Path -LiteralPath $stateFile) {
    Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
}
else {
    $null
}
$state = [ordered]@{
    apiPid = if ((Test-ServicePort 5180 '127.0.0.1') -and $savedState) { $savedState.apiPid } else { 0 }
    webPid = if ((Test-ServicePort 5173 '127.0.0.1') -and $savedState) { $savedState.webPid } else { 0 }
    startedAt = if ($savedState) { $savedState.startedAt } else { [DateTimeOffset]::Now.ToString('O') }
}

if (-not (Test-ServicePort 5180 '127.0.0.1')) {
    Push-Location $projectRoot
    try {
        & dotnet build 'src/Ls.Inventory.Api/Ls.Inventory.Api.csproj' --nologo `
            *> (Join-Path $runtimeDirectory 'build.log')
        $buildExitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    if ($buildExitCode -ne 0) {
        throw '接口编译失败，请查看 .dev-runtime/build.log。'
    }

    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = 'http://127.0.0.1:5180'
    $api = Start-Process -FilePath (Join-Path $projectRoot 'src/Ls.Inventory.Api/bin/Debug/net10.0/Ls.Inventory.Api.exe') `
        -WorkingDirectory (Join-Path $projectRoot 'src/Ls.Inventory.Api') `
        -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $runtimeDirectory 'api.log') `
        -RedirectStandardError (Join-Path $runtimeDirectory 'api-error.log') `
        -PassThru
    $state.apiPid = $api.Id
}

if (-not (Test-ServicePort 5173 '127.0.0.1')) {
    $webDirectory = Join-Path $projectRoot 'src/Ls.Inventory.Web'
    if (-not (Test-Path -LiteralPath (Join-Path $webDirectory 'node_modules'))) {
        $install = Start-Process -FilePath 'npm.cmd' `
            -ArgumentList @('install') `
            -WorkingDirectory $webDirectory `
            -WindowStyle Hidden `
            -RedirectStandardOutput (Join-Path $runtimeDirectory 'install.log') `
            -RedirectStandardError (Join-Path $runtimeDirectory 'install-error.log') `
            -Wait `
            -PassThru
        if ($install.ExitCode -ne 0) {
            throw '网页依赖安装失败，请查看 .dev-runtime/install-error.log。'
        }
    }

    $nodeExecutable = (Get-Command 'node.exe').Source
    $web = Start-Process -FilePath $nodeExecutable `
        -ArgumentList @('node_modules/vite/bin/vite.js', '--host', '127.0.0.1', '--port', '5173', '--strictPort') `
        -WorkingDirectory $webDirectory `
        -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $runtimeDirectory 'web.log') `
        -RedirectStandardError (Join-Path $runtimeDirectory 'web-error.log') `
        -PassThru
    $state.webPid = $web.Id
}

$state | ConvertTo-Json | Set-Content -LiteralPath $stateFile -Encoding UTF8

$deadline = [DateTimeOffset]::Now.AddSeconds(40)
do {
    try {
        $apiReady = (Invoke-WebRequest -Uri 'http://127.0.0.1:5180/health/ready' -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200
    }
    catch {
        $apiReady = $false
    }

    try {
        $webReady = (Invoke-WebRequest -Uri 'http://127.0.0.1:5173/' -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200
    }
    catch {
        $webReady = $false
    }

    if ($apiReady -and $webReady) { break }
    Start-Sleep -Milliseconds 600
} while ([DateTimeOffset]::Now -lt $deadline)

if (-not ($apiReady -and $webReady)) {
    throw '系统未能在 40 秒内启动，请查看 .dev-runtime 目录中的日志。'
}

Start-Process 'http://127.0.0.1:5173'
