# Registra (o actualiza) las tareas de Task Scheduler que dan recurrencia al worker Python. El
# worker es single-run por diseño: sin scheduler embebido, la recurrencia es concern de despliegue
# (SmartNetWorker/CLAUDE.md). Cada tarea corre un entry point del venv y registra su exit code en
# el historial de la tarea. Los secretos vienen de $SecretsDir\worker.env (cargado por un wrapper).
#
#   pwsh -File .\register-worker-tasks.ps1 -Environment prod
#
# Requiere consola de Administrador.

param([Parameter(Mandatory)][string]$Environment)

. "$PSScriptRoot\_common.ps1"
Assert-Admin
Import-DeployConfig -Environment $Environment

$runner = Join-Path $PSScriptRoot 'run-worker-entry.ps1'
$logsWorker = Join-Path $LogsDir 'worker'
New-Item -ItemType Directory -Force -Path $logsWorker | Out-Null

# pwsh.exe suele estar fuera del PATH del contexto SYSTEM: resolver la ruta absoluta ahora.
$pwshExe = (Get-Command pwsh.exe -ErrorAction SilentlyContinue).Source
if (-not $pwshExe) { Fail "pwsh.exe no está en el PATH. Instalá PowerShell 7." }

# Corren como SYSTEM (desatendido, sin sesión iniciada). run-worker-entry.ps1 carga los secretos
# desde $SecretsDir\worker.env, así que no necesitan el entorno del usuario.
$princ = New-ScheduledTaskPrincipal -UserId 'S-1-5-18' -LogonType ServiceAccount -RunLevel Highest

foreach ($t in $WorkerTasks) {
    $action = New-ScheduledTaskAction -Execute $pwshExe `
        -Argument "-NoProfile -File `"$runner`" -Environment $Environment -Entry $($t.Entry)"

    $trigger = if ($t.Schedule -eq 'Daily') {
        New-ScheduledTaskTrigger -Daily -At $t.At
    } else {
        # -RepetitionInterval sin -RepetitionDuration no persiste en varias versiones de Windows:
        # se fija una duración muy larga (10 años) como equivalente a "indefinida".
        New-ScheduledTaskTrigger -Once -At (Get-Date) `
            -RepetitionInterval (New-TimeSpan -Minutes $t.Interval) `
            -RepetitionDuration (New-TimeSpan -Days 3650)
    }

    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable `
        -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Hours 1)

    Register-ScheduledTask -TaskName $t.Name -Action $action -Trigger $trigger `
        -Settings $settings -Principal $princ -Force -TaskPath '\SmartNet\' | Out-Null
    Write-Ok "Tarea $($t.Name) ($($t.Entry)) registrada."
}

Write-Ok "6 tareas del worker registradas bajo \SmartNet\ . deploy.ps1 las habilita/deshabilita."
