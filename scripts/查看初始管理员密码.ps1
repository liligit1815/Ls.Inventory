$ErrorActionPreference = 'Stop'

$secretsFile = Join-Path (Split-Path -Parent $PSScriptRoot) '.dev-runtime\development-secrets.json'
$password = $null
$readFailed = $false
try {
    . (Join-Path $PSScriptRoot 'development-settings.ps1')
    $settings = Get-InventoryDevelopmentSettings
    $password = $settings['BootstrapAdmin:Password']
}
catch {
    $readFailed = $true
    Write-Host '无法读取本机管理员配置，请检查文件权限及 JSON 格式。' -ForegroundColor Red
    Write-Host "配置位置：$secretsFile" -ForegroundColor Gray
}

if (-not $readFailed -and [string]::IsNullOrWhiteSpace($password)) {
    Write-Host '当前 Windows 用户尚未配置初始管理员密码。' -ForegroundColor Red
    Write-Host "配置位置：$secretsFile" -ForegroundColor Gray
}
elseif (-not $readFailed) {
    Write-Host ''
    Write-Host 'LS 进销存初始管理员' -ForegroundColor Cyan
    Write-Host '用户名：admin'
    Write-Host "初始密码：$password" -ForegroundColor Yellow
    Write-Host ''
    Write-Host '首次登录后系统会要求立即修改密码；已改密的账号请使用修改后的密码。' -ForegroundColor Gray
}

Read-Host '按回车键关闭窗口'
