function Get-InventoryDevelopmentSettings {
    $settings = @{}
    $userSecrets = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Microsoft\UserSecrets\ls-inventory-api-development\secrets.json'
    $localSecrets = Join-Path (Split-Path -Parent $PSScriptRoot) '.dev-runtime\development-secrets.json'
    # The workspace file is shared by desktop launches and packaged-app terminals.
    # Local values override User Secrets; neither file belongs in source control.
    foreach ($path in @($userSecrets, $localSecrets)) {
        if (-not (Test-Path -LiteralPath $path)) { continue }
        $document = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($key in @('ConnectionStrings:Inventory', 'ConnectionStrings:InventoryMigration', 'Jwt:SigningKey', 'BootstrapAdmin:Password')) {
            $value = $document.$key
            if ($null -eq $value) {
                $parts = $key.Split(':')
                $section = $document.($parts[0])
                if ($null -ne $section) { $value = $section.($parts[1]) }
            }
            if ($null -ne $value) { $settings[$key] = [string]$value }
        }
    }
    return $settings
}
