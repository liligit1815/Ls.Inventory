$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$apiProject = Join-Path $projectRoot 'src/Ls.Inventory.Api'
$lines = & dotnet user-secrets list --project $apiProject
$passwordLine = $lines | Where-Object { $_ -match '^BootstrapAdmin:Password\s*=' } | Select-Object -First 1

if (-not $passwordLine) {
    Write-Host '未找到初始管理员密码，请联系项目开发人员。' -ForegroundColor Red
}
else {
    $password = ($passwordLine -split '=', 2)[1].Trim()
    Write-Host ''
    Write-Host 'LS 进销存初始管理员' -ForegroundColor Cyan
    Write-Host '用户名：admin'
    Write-Host "初始密码：$password" -ForegroundColor Yellow
    Write-Host ''
    Write-Host '首次登录后系统会要求立即修改密码。' -ForegroundColor Gray
}

Read-Host '按回车键关闭窗口'
