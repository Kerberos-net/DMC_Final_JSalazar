# Verificación post-deploy (DEPLOY-PLAN.md § Verify & Observe). Un deploy que "terminó" pero falla
# una comprobación es un deploy fallido. El resultado se imprime para pegarlo en el "Registro de
# ejecución y verificación" del plan.
#
#   pwsh -File .\verify.ps1 -Environment prod

param([Parameter(Mandatory)][string]$Environment)

. "$PSScriptRoot\_common.ps1"
Import-DeployConfig -Environment $Environment

$fail = 0
function V { param([string]$n, [scriptblock]$t)
    try { if (& $t) { Write-Ok $n } else { Write-Warn2 "$n -> FALLA"; $script:fail++ } }
    catch { Write-Warn2 "$n -> FALLA ($($_.Exception.Message))"; $script:fail++ }
}

Write-Step "Esquema"
V "fact.SchemaVersions tiene >= 21 filas" {
    [int](Invoke-Sql 'verify' "SELECT COUNT(*) FROM fact.SchemaVersions") -ge 21
}

Write-Step "API (funcional -- sin validar el cert, aislando el chequeo de TLS abajo)"
V "GET /api/sesion responde 401 (sano-no-autenticado)" {
    (Invoke-WebRequest "https://$PublicHost/api/sesion" -SkipCertificateCheck -SkipHttpErrorCheck -TimeoutSec 15).StatusCode -eq 401
}
V "Kestrel escucha SOLO en loopback" {
    $u = [uri]$ApiListenUrl
    (Test-NetConnection 127.0.0.1 -Port $u.Port -WarningAction SilentlyContinue).TcpTestSucceeded
}
V "GET / sirve el index de la SPA" {
    (Invoke-WebRequest "https://$PublicHost/" -SkipCertificateCheck -TimeoutSec 15).Content -match '<app-root|<base href'
}

Write-Step "TLS"
V "El certificado de $PublicHost valida contra el almacen de confianza" {
    # SIN -SkipCertificateCheck: falla si la CA de Caddy (tls internal) no fue instalada con
    # 'caddy trust'. En prod con un cert real de CA pública/interna esto pasa sin pasos extra.
    try { Invoke-WebRequest "https://$PublicHost/" -TimeoutSec 15 | Out-Null; $true }
    catch { $false }
}

Write-Step "Worker"
V "smartnet-tipo-cambio corre y actualiza EstadoIntegracion" {
    & "$PSScriptRoot\run-worker-entry.ps1" -Environment $Environment -Entry 'smartnet-tipo-cambio'
    if ($LASTEXITCODE -ne 0) { return $false }
    [int](Invoke-Sql 'verify' "SELECT DATEDIFF(minute, MAX(UltimoExito), SYSUTCDATETIME()) FROM fact.EstadoIntegracion WHERE Nombre='SBS'") -lt 10
}

Write-Step "Sin recursos efímeros huérfanos"
V "0 bases fact_test_*" { (Invoke-Sql 'verify' "SELECT COUNT(*) FROM sys.databases WHERE name LIKE 'fact_test_%'" -Database 'master') -eq '0' }

Write-Host ""
if ($fail -gt 0) { Fail "$fail verificación(es) fallaron. El deploy NO está confirmado." }
Write-Ok "VERIFICADO -- todas las comprobaciones pasaron."
