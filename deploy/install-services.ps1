# Instala/configura los dos Servicios de Windows: SmartNetApi (Kestrel, loopback) y SmartNetCaddy
# (proxy inverso, TLS). Idempotente: si el servicio ya existe, actualiza su binPath y su config.
# Se corre UNA VEZ al preparar el host, y de nuevo solo si cambia la topología (no en cada release
# -- deploy.ps1 solo hace stop/start del servicio ya instalado).
#
#   pwsh -File .\install-services.ps1 -Environment prod
#
# Requiere consola de Administrador.

param([Parameter(Mandatory)][string]$Environment)

. "$PSScriptRoot\_common.ps1"
Assert-Admin
Import-DeployConfig -Environment $Environment

$apiExe = Join-Path $CurrentDir 'api\SmartNet.Api.exe'
if (-not (Test-Path $apiExe)) { Fail "No existe $apiExe. Corré deploy.ps1 al menos una vez primero." }

# --- SmartNetApi ------------------------------------------------------------------------------
Write-Step "Servicio $ApiServiceName"
$apiArgs = "--urls `"$ApiListenUrl`""
if (Get-Service $ApiServiceName -ErrorAction SilentlyContinue) {
    Stop-Service $ApiServiceName -Force -ErrorAction SilentlyContinue
    sc.exe config $ApiServiceName binPath= "`"$apiExe`" $apiArgs" start= auto | Out-Null
} else {
    New-Service -Name $ApiServiceName -BinaryPathName "`"$apiExe`" $apiArgs" `
        -DisplayName 'SmartNet API' -StartupType Automatic `
        -Description 'SmartNet API (Kestrel, solo loopback, detrás de Caddy).' | Out-Null
}

# Variables de entorno del servicio (secretos + config). ACL del registro: solo Administrators/SYSTEM.
$envBlock = @(
    "ASPNETCORE_ENVIRONMENT=Production"
    "ASPNETCORE_URLS=$ApiListenUrl"
    "SMARTNET_API_DB_CONNECTION=$DbConnectionApi"
    "SMARTNET_API_STORAGE_ROOT=$StorageRoot"
    "SMARTNET_API_KEYRING_PATH=$KeyringPath"
)
# Observabilidad (mínimo pragmático): al correr como Servicio de Windows, UseWindowsService()
# registra automáticamente el logger de Event Log. Los fallos de arranque quedan en el Registro de
# eventos de Windows. Un logger a archivo con rotación es un cambio de código pendiente
# (DEPLOY-PLAN.md § Deuda de puesta en producción).
$regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ApiServiceName"
Set-ItemProperty -Path $regPath -Name Environment -Value $envBlock -Type MultiString
Write-Ok "$ApiServiceName configurado (binPath + Environment)."

# --- Caddy (tarea programada AtStartup, no servicio: caddy run no habla el protocolo del SCM) --
Write-Step "Tarea programada $CaddyTaskName"
$caddyDir = Split-Path $CaddyfilePath -Parent
New-Item -ItemType Directory -Force -Path $caddyDir, (Join-Path $LogsDir 'caddy') | Out-Null
if (-not (Test-Path $CaddyfilePath)) {
    Copy-Item (Join-Path $PSScriptRoot 'Caddyfile.example') $CaddyfilePath
    Write-Warn2 "Se copió Caddyfile.example a $CaddyfilePath -- revisá el host y el bloque tls."
}
& $CaddyExe validate --config $CaddyfilePath --adapter caddyfile
if ($LASTEXITCODE -ne 0) { Fail "El Caddyfile no valida." }

$caddyAction  = New-ScheduledTaskAction -Execute $CaddyExe `
    -Argument "run --config `"$CaddyfilePath`" --adapter caddyfile" -WorkingDirectory $caddyDir
$caddyTrigger = New-ScheduledTaskTrigger -AtStartup
$caddyPrinc   = New-ScheduledTaskPrincipal -UserId 'S-1-5-18' -LogonType ServiceAccount -RunLevel Highest
$caddySettings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) `
    -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName $CaddyTaskName -TaskPath '\SmartNet\' -Force `
    -Action $caddyAction -Trigger $caddyTrigger -Principal $caddyPrinc -Settings $caddySettings | Out-Null
Write-Ok "$CaddyTaskName registrada (AtStartup, SYSTEM, reinicio automático)."

Write-Ok "Listo. Arrancá la API con 'Start-Service $ApiServiceName' y Caddy con 'Start-ScheduledTask -TaskPath \SmartNet\ -TaskName $CaddyTaskName'."
