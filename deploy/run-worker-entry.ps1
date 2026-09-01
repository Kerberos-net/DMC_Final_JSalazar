# Wrapper que Task Scheduler invoca por cada entry point del worker. Carga los secretos desde
# $SecretsDir\worker.env (formato KEY=VALUE, una por línea), activa el venv, corre el entry point
# UNA vez y propaga su exit code (0 éxito / 1 fallo). Redirige salida a un log con rotación simple.

param(
    [Parameter(Mandatory)][string]$Environment,
    [Parameter(Mandatory)][string]$Entry
)

. "$PSScriptRoot\_common.ps1"
Import-DeployConfig -Environment $Environment

# --- secretos -------------------------------------------------------------------------------
$envFile = Join-Path $SecretsDir 'worker.env'
if (-not (Test-Path $envFile)) { Fail "No existe $envFile (lo escribe deploy.ps1)." }
foreach ($line in Get-Content $envFile) {
    if ($line -notmatch '^\s*[^#].*?=') { continue }
    $k, $v = $line -split '=', 2
    [Environment]::SetEnvironmentVariable($k.Trim(), $v, 'Process')
}
$env:SMARTNET_WORKER_STORAGE_ROOT = $StorageRoot
if ($TesseractCmd) { $env:SMARTNET_WORKER_TESSERACT_CMD = $TesseractCmd }

# --- log con rotación simple (10 archivos) --------------------------------------------------
$logDir = Join-Path $LogsDir 'worker'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$log = Join-Path $logDir "$Entry.log"
if ((Test-Path $log) -and ((Get-Item $log).Length -gt 5MB)) {
    for ($i = 9; $i -ge 1; $i--) {
        $a = "$log.$i"; $b = "$log.$($i+1)"
        if (Test-Path $a) { Move-Item $a $b -Force }
    }
    Move-Item $log "$log.1" -Force
}

# --- ejecución -----------------------------------------------------------------------------
$exe = Join-Path $WorkerVenv "Scripts\$Entry.exe"
if (-not (Test-Path $exe)) { Fail "No existe $exe -- ¿corrió deploy.ps1 el paso 5 (pip install del wheel)?" }
"[{0}] START {1}" -f (Get-Date -Format o), $Entry | Tee-Object -FilePath $log -Append
& $exe *>> $log
$code = $LASTEXITCODE
"[{0}] END   {1} exit={2}" -f (Get-Date -Format o), $Entry, $code | Tee-Object -FilePath $log -Append
exit $code
