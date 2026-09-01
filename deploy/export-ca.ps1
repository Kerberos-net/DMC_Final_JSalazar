# Se corre EN LA VM. Prepara el acceso desde máquinas cliente a https://facturas.empresa.local:
#   1. exporta la CA raíz de Caddy (tls internal) a un .crt para distribuir
#   2. abre el firewall entrante TCP 443
#   3. imprime la(s) IP(s) de la VM para el hosts de los clientes
#
# Con `tls internal` cada cliente tiene que confiar en esta CA (o se usa un cert real -- ADR 0012).
#
#   pwsh -File .\export-ca.ps1 [-OutFile C:\SmartNet\smartnet-root-ca.crt]
#
# Requiere consola de Administrador.

param([string]$OutFile = 'C:\SmartNet\smartnet-root-ca.crt')

. "$PSScriptRoot\_common.ps1"
Assert-Admin

Write-Step "CA raíz de Caddy"
$src = 'C:\SmartNet\caddy\data\pki\authorities\local\root.crt'
if (-not (Test-Path $src)) {
    Fail "No existe $src. Confirmá que el Caddyfile tiene 'storage file_system { root C:\SmartNet\caddy\data }' y que Caddy corrió al menos una vez."
}
Copy-Item $src $OutFile -Force
Write-Ok "CA exportada a $OutFile"

Write-Step "Firewall entrante TCP 443"
if (Get-NetFirewallRule -DisplayName 'SmartNet HTTPS 443' -ErrorAction SilentlyContinue) {
    Write-Ok "La regla 'SmartNet HTTPS 443' ya existe."
} else {
    New-NetFirewallRule -DisplayName 'SmartNet HTTPS 443' -Direction Inbound -Protocol TCP `
        -LocalPort 443 -Action Allow -Profile Any | Out-Null
    Write-Ok "Regla 'SmartNet HTTPS 443' creada (entrante, TCP 443)."
}

Write-Step "IP(s) de la VM para el hosts de los clientes"
Get-NetIPAddress -AddressFamily IPv4 |
    Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } |
    ForEach-Object { Write-Host "   $($_.IPAddress)   ($($_.InterfaceAlias))" }

Write-Host ""
Write-Ok "Copiá $OutFile a cada máquina cliente y corré ahí trust-ca-client.ps1 con esa IP."
