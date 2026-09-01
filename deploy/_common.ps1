# Helpers compartidos por los scripts de deploy. Dot-source: `. .\_common.ps1`
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step  { param([string]$m) Write-Host "==> $m" -ForegroundColor Cyan }
function Write-Ok    { param([string]$m) Write-Host "  OK  $m" -ForegroundColor Green }
function Write-Warn2 { param([string]$m) Write-Host "  !!  $m" -ForegroundColor Yellow }
function Fail        { param([string]$m) Write-Host "  XX  $m" -ForegroundColor Red; exit 1 }

function Import-DeployConfig {
    param([Parameter(Mandatory)][string]$Environment)
    $cfg = Join-Path $PSScriptRoot "config.$Environment.ps1"
    if (-not (Test-Path $cfg)) {
        Fail "No existe $cfg. Copiá config.example.ps1 a config.$Environment.ps1 y ajustá los valores."
    }
    # `. $cfg` corre el archivo EN EL SCOPE DE ESTA FUNCIÓN, no en el del script que la llamó.
    # Por eso, tras leerlo, promovemos cada variable conocida de config al scope del llamador
    # (Scope 1 = el script). Sin esto, el script no ve ninguna variable de config.
    . $cfg
    foreach ($n in @(
        'SmartNetRoot','ReleasesDir','CurrentDir','LogsDir','BackupsDir','StorageRoot',
        'KeyringPath','SecretsDir','WorkerVenv','KeepReleases','ApiListenUrl','PublicHost',
        'ApiServiceName','CaddyTaskName','CaddyExe','CaddyfilePath','DbConnectionDeploy',
        'DbConnectionApi','WorkerOdbcConnection','WorkerGmailCredentials','WorkerTelegramCreds',
        'WorkerSmtpCreds','TesseractCmd','WorkerTasks',
        'AplicarFixturesCatalogoDemo','FixturesDataDir')) {
        $v = Get-Variable -Name $n -Scope Local -ErrorAction SilentlyContinue
        if ($v) { Set-Variable -Name $n -Value $v.Value -Scope 1 }
    }
    Write-Ok "Config cargada: $cfg"
}

function Assert-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $p  = New-Object Security.Principal.WindowsPrincipal($id)
    if (-not $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Fail 'Ejecutá esta consola de PowerShell como Administrador.'
    }
}

function Invoke-Sql {
    # Consulta escalar de solo lectura vía sqlcmd (ya requerido por el host para los fixtures).
    param([Parameter(Mandatory)][string]$ConnDescription, [Parameter(Mandatory)][string]$Query,
          [string]$Server = 'localhost', [string]$Database = 'BDSmartNet')
    # -C = trust server certificate (ODBC Driver 18 cifra por defecto y valida la cadena; el cert
    # de una instancia local suele ser autofirmado -- mismo criterio que TrustServerCertificate=True
    # en las cadenas de conexión de config).
    $out = & sqlcmd -S $Server -d $Database -E -C -h -1 -W -Q "SET NOCOUNT ON; $Query" 2>&1
    if ($LASTEXITCODE -ne 0) { Fail "sqlcmd falló ($ConnDescription): $out" }
    return ($out | Select-Object -First 1).Trim()
}

function Restrict-Acl {
    # Deja el path accesible solo a Administradores + SYSTEM (secretos, keyring). Por SID, no por
    # nombre: en Windows en español los grupos built-in no se llaman 'Administrators'/'SYSTEM'.
    #   S-1-5-32-544 = BUILTIN\Administrators   ·   S-1-5-18 = NT AUTHORITY\SYSTEM
    param([Parameter(Mandatory)][string]$Path)
    icacls $Path /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Warn2 "icacls devolvió $LASTEXITCODE sobre $Path" }
    else { Write-Ok "ACL restringida en $Path" }
}
