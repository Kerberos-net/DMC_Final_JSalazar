# Se corre UNA vez en CADA máquina cliente (Windows), como Administrador, para acceder a
# https://facturas.empresa.local sin advertencia de certificado.
#
# Es autónomo: no depende del resto de deploy/. Copiá este archivo + el smartnet-root-ca.crt
# (exportado en la VM con export-ca.ps1) a la máquina cliente.
#
#   pwsh -File .\trust-ca-client.ps1 -CaCert .\smartnet-root-ca.crt -VmIp 192.168.x.y
#
# Deshacer:
#   - quitar la línea de C:\Windows\System32\drivers\etc\hosts
#   - Get-ChildItem Cert:\LocalMachine\Root | ? Subject -like '*Caddy*' | Remove-Item

param(
    [Parameter(Mandatory)][string]$CaCert,
    [Parameter(Mandatory)][string]$VmIp,
    [string]$HostName = 'facturas.empresa.local'
)

$ErrorActionPreference = 'Stop'

$id = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error 'Ejecutá esta consola como Administrador.'; exit 1
}
if (-not (Test-Path $CaCert)) { Write-Error "No existe $CaCert."; exit 1 }

# 1. hosts -> IP de la VM
$hostsFile = "$env:WINDIR\System32\drivers\etc\hosts"
if ((Get-Content $hostsFile -Raw) -match [regex]::Escape($HostName)) {
    Write-Host "hosts: ya hay una entrada para $HostName. Verificá que apunte a $VmIp." -ForegroundColor Yellow
} else {
    Add-Content $hostsFile "`n$VmIp`t$HostName"
    Write-Host "hosts: $VmIp -> $HostName agregado." -ForegroundColor Green
}

# 2. CA raíz de Caddy -> almacén de confianza del equipo
Import-Certificate -FilePath $CaCert -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
Write-Host "CA importada en 'Entidades de certificación raíz de confianza' (equipo local)." -ForegroundColor Green

# 3. prueba
try {
    $r = Invoke-WebRequest "https://$HostName/" -TimeoutSec 15
    Write-Host "OK  https://$HostName/ responde $($r.StatusCode) con certificado válido." -ForegroundColor Green
} catch {
    Write-Host "!!  Todavía falla: $($_.Exception.Message)" -ForegroundColor Yellow
    Write-Host "    Revisá que $VmIp sea la IP correcta y que el :443 de la VM sea alcanzable."
}
