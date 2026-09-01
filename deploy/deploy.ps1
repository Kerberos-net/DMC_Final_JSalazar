# Despliega un paquete de release (smartnet-vX.Y.Z.zip producido por .github/workflows/deploy-build.yml)
# sobre el host, en el orden NO negociable de ADR 0012:
#
#   1. deshabilitar las 6 tareas del worker
#   2. SmartNet.Db.Runner  -> aplica el esquema (idempotente)
#   3. parar SmartNetApi -> intercambiar api\ -> arrancar
#   4. desplegar web\ -> recargar Caddy
#   5. actualizar el venv del worker con el wheel nuevo
#   6. re-habilitar las tareas del worker
#
# NO compila nada y NO hace git pull. NO toca la política de respaldo de la base (ADR 0014).
# Preserva SIEMPRE el keyring de Data Protection (nunca lo incluye en el árbol que reemplaza).
#
#   pwsh -File .\deploy.ps1 -Environment prod -PackageZip C:\ruta\smartnet-v1.2.3.zip
#
# Requiere consola de Administrador. Antes de la primera corrida: preflight.ps1, install-services.ps1,
# register-worker-tasks.ps1.

param(
    [Parameter(Mandatory)][string]$Environment,
    [Parameter(Mandatory)][string]$PackageZip,
    [switch]$SkipSchema  # solo para un re-deploy de solo-frontend; usar con criterio
)

. "$PSScriptRoot\_common.ps1"
Assert-Admin
Import-DeployConfig -Environment $Environment

if (-not (Test-Path $PackageZip)) { Fail "No existe el paquete $PackageZip." }

# --- 0. expandir el release --------------------------------------------------------------------
Write-Step "Expandiendo el paquete"
$name = [IO.Path]::GetFileNameWithoutExtension($PackageZip)   # smartnet-v1.2.3
$target = Join-Path $ReleasesDir $name
New-Item -ItemType Directory -Force -Path $target | Out-Null
Expand-Archive -Path $PackageZip -DestinationPath $target -Force
$version = (Get-Content (Join-Path $target 'VERSION') -Raw).Trim()
Write-Ok "Release: $version"

# --- respaldo pre-release (keyring + secretos) ----------------------------------------------
& "$PSScriptRoot\backup.ps1" -Environment $Environment

# --- 1. deshabilitar las tareas del worker --------------------------------------------------
Write-Step "1/6 Deshabilitando las tareas del worker"
foreach ($t in $WorkerTasks) {
    Disable-ScheduledTask -TaskName $t.Name -TaskPath '\SmartNet\' -ErrorAction SilentlyContinue | Out-Null
}
# Get-ScheduledTask lanza error terminante si no hay NINGUNA tarea bajo \SmartNet\ (primer deploy).
$smartnetTasks = Get-ScheduledTask -TaskPath '\SmartNet\' -ErrorAction SilentlyContinue
if ($smartnetTasks) {
    Write-Ok "Tareas deshabilitadas. Esperando a que no haya corridas en vuelo..."
    $deadline = (Get-Date).AddMinutes(5)
    while (($smartnetTasks | Get-ScheduledTaskInfo |
            Where-Object { $_.LastTaskResult -eq 267009 }) -and (Get-Date) -lt $deadline) {
        Start-Sleep 10
    }
} else {
    Write-Ok "No hay tareas \SmartNet\ todavía (primer deploy) -- nada que esperar."
}

# --- 2. esquema ---------------------------------------------------------------------------------
if ($SkipSchema) {
    Write-Warn2 "2/6 Esquema OMITIDO por -SkipSchema."
} else {
    if ($AplicarFixturesCatalogoDemo) {
        Write-Step "2/6 (a) Fixtures del catálogo externo dbo.* (demo -- ver config)"
        & "$PSScriptRoot\apply-catalog-fixtures.ps1" -Environment $Environment -PackageRoot $target
        if ($LASTEXITCODE -ne 0) { Fail "Los fixtures del catálogo fallaron. NO se corrió el runner." }
    } else {
        Write-Warn2 "2/6 (a) Fixtures del catálogo OMITIDOS (`$AplicarFixturesCatalogoDemo = `$false): se asume que las tablas dbo.* ya existen."
    }
    Write-Step "2/6 (b) Aplicando el esquema versionado (SmartNet.Db.Runner)"
    $runnerExe = Join-Path $target 'runner\SmartNet.Db.Runner.exe'
    $env:SMARTNET_DB_CONNECTION = $DbConnectionDeploy
    & $runnerExe --scripts-path (Join-Path $target 'schema')
    if ($LASTEXITCODE -ne 0) { Fail "El runner falló (exit $LASTEXITCODE). NO se tocó la API. Revisá el error arriba." }
    Write-Ok "Esquema al día."
}

# --- 3. API -----------------------------------------------------------------------------------
Write-Step "3/6 Intercambiando la API"
if (Get-Service $ApiServiceName -ErrorAction SilentlyContinue) { Stop-Service $ApiServiceName -Force }
New-Item -ItemType Directory -Force -Path $CurrentDir | Out-Null
foreach ($d in 'api', 'admin', 'runner') {
    $dst = Join-Path $CurrentDir $d
    if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
    Copy-Item (Join-Path $target $d) $dst -Recurse -Force
}
if (Get-Service $ApiServiceName -ErrorAction SilentlyContinue) {
    Start-Service $ApiServiceName
    Write-Ok "$ApiServiceName arrancado."
} else {
    Write-Warn2 "$ApiServiceName no existe todavía. Corré install-services.ps1 y luego 'Start-Service $ApiServiceName'."
}

# --- 4. SPA + Caddy --------------------------------------------------------------------------
Write-Step "4/6 Desplegando la SPA"
$webDst = Join-Path $CurrentDir 'web'
if (Test-Path $webDst) { Remove-Item $webDst -Recurse -Force }
Copy-Item (Join-Path $target 'web') $webDst -Recurse -Force
$caddyTask = Get-ScheduledTask -TaskPath '\SmartNet\' -TaskName $CaddyTaskName -ErrorAction SilentlyContinue
if ($caddyTask) {
    if ((Get-ScheduledTask -TaskPath '\SmartNet\' -TaskName $CaddyTaskName).State -eq 'Running') {
        & $CaddyExe reload --config $CaddyfilePath --adapter caddyfile 2>$null
        if ($LASTEXITCODE -ne 0) {
            Stop-ScheduledTask  -TaskPath '\SmartNet\' -TaskName $CaddyTaskName -ErrorAction SilentlyContinue
            Start-ScheduledTask -TaskPath '\SmartNet\' -TaskName $CaddyTaskName
        }
        Write-Ok "Caddy recargado."
    } else {
        Start-ScheduledTask -TaskPath '\SmartNet\' -TaskName $CaddyTaskName
        Write-Ok "Caddy arrancado."
    }
} else {
    Write-Warn2 "La tarea $CaddyTaskName no existe todavía. Corré install-services.ps1."
}

# --- 5. worker ------------------------------------------------------------------------------
Write-Step "5/6 Actualizando el venv del worker"
if (-not (Test-Path (Join-Path $WorkerVenv 'Scripts\python.exe'))) {
    & python -m venv $WorkerVenv
}
$wheel = Get-ChildItem (Join-Path $target 'worker') -Filter *.whl | Select-Object -First 1
& (Join-Path $WorkerVenv 'Scripts\python.exe') -m pip install --upgrade --force-reinstall $wheel.FullName
if ($LASTEXITCODE -ne 0) { Fail "pip install del wheel falló." }
Write-Ok "Worker: $($wheel.Name)"

# secretos del worker -> $SecretsDir\worker.env
New-Item -ItemType Directory -Force -Path $SecretsDir | Out-Null
$envLines = @("SMARTNET_WORKER_ODBC_CONNECTION=$WorkerOdbcConnection",
              "SMARTNET_WORKER_GMAIL_CREDENTIALS=$WorkerGmailCredentials")
if ($WorkerTelegramCreds) { $envLines += "SMARTNET_WORKER_TELEGRAM_CREDENTIALS=$WorkerTelegramCreds" }
if ($WorkerSmtpCreds)     { $envLines += "SMARTNET_WORKER_SMTP_CREDENTIALS=$WorkerSmtpCreds" }
Set-Content -Path (Join-Path $SecretsDir 'worker.env') -Value $envLines -Encoding UTF8
Restrict-Acl $SecretsDir

# --- 6. re-habilitar las tareas -----------------------------------------------------------
Write-Step "6/6 Re-habilitando las tareas del worker"
foreach ($t in $WorkerTasks) {
    Enable-ScheduledTask -TaskName $t.Name -TaskPath '\SmartNet\' -ErrorAction SilentlyContinue | Out-Null
}
Write-Ok "Tareas habilitadas."

# --- retención de releases -------------------------------------------------------------------
Get-ChildItem $ReleasesDir -Directory | Sort-Object CreationTime -Descending |
    Select-Object -Skip $KeepReleases | ForEach-Object {
        Remove-Item $_.FullName -Recurse -Force; Write-Ok "Release viejo purgado: $($_.Name)"
    }

Write-Host ""
Write-Ok "Deploy de $version completo. Corré:  pwsh -File .\verify.ps1 -Environment $Environment"
