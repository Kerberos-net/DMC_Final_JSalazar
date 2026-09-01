# Aplica los fixtures del catálogo externo dbo.* (fixtures/010 DDL + fixtures/020 datos) contra
# BDSmartNet, ANTES del runner. SOLO para la demo: en producción real esas tablas las mantiene el
# sistema contable y este script NO debe correr (config: $AplicarFixturesCatalogoDemo).
#
# 020 usa BULK INSERT, que lee los CSV desde el filesystem del SERVIDOR. Este script:
#   1. corre 010 (DDL, idempotente)
#   2. copia los CSV del paquete a $FixturesDataDir (ruta local que SQL Server puede leer) y le
#      da lectura a Everyone -- son datos de catálogo público, no secretos
#   3. reescribe el DECLARE @ruta de 020 apuntando a esa carpeta y lo corre
#   4. 020 trae su propia verificación de conteos (THROW si no cuadra)
#
#   pwsh -File .\apply-catalog-fixtures.ps1 -Environment prod -PackageRoot C:\SmartNet\releases\smartnet-v1.0.0

param(
    [Parameter(Mandatory)][string]$Environment,
    [Parameter(Mandatory)][string]$PackageRoot
)

. "$PSScriptRoot\_common.ps1"
Import-DeployConfig -Environment $Environment

$fixturesDir = Join-Path $PackageRoot 'fixtures'
$ddl  = Join-Path $fixturesDir '010_dbo_catalogos_ddl.sql'
$data = Join-Path $fixturesDir '020_dbo_catalogos_datos.sql'
$srcData = Join-Path $fixturesDir 'data'
foreach ($p in $ddl, $data, $srcData) {
    if (-not (Test-Path $p)) { Fail "Falta $p en el paquete. ¿El .zip incluye fixtures/?" }
}

# Servidor/base para sqlcmd -- por defecto localhost/BDSmartNet (Integrated Security, principal de
# despliegue). Si $DbConnectionDeploy apunta a otro server, ajustá acá.
$server = 'localhost'

Write-Step "Catálogo externo (a) -- DDL (fixtures/010)"
& sqlcmd -S $server -d BDSmartNet -E -C -b -i $ddl
if ($LASTEXITCODE -ne 0) { Fail "fixtures/010 falló (exit $LASTEXITCODE)." }
Write-Ok "Tablas dbo.* creadas (o ya existían)."

Write-Step "Catálogo externo (b) -- copia de CSV a $FixturesDataDir"
New-Item -ItemType Directory -Force -Path $FixturesDataDir | Out-Null
Copy-Item (Join-Path $srcData '*.csv') $FixturesDataDir -Force
# Lectura para Everyone (S-1-1-0): SQL Server (cuenta de servicio) tiene que poder leer estos CSV.
# Son catálogos públicos (tipos de documento, plan de cuentas, proveedores), no secretos.
icacls $FixturesDataDir /grant '*S-1-1-0:(OI)(CI)R' | Out-Null
Write-Ok "CSV copiados y legibles por SQL Server."

Write-Step "Catálogo externo (c) -- datos (fixtures/020)"
$rutaSql = $FixturesDataDir.TrimEnd('\') + '\'
$patched = (Get-Content $data -Raw) -replace
    "DECLARE\s+@ruta\s+NVARCHAR\(\d+\)\s*=\s*N'[^']*';",
    "DECLARE @ruta NVARCHAR(400) = N'$rutaSql';"
if ($patched -notmatch [regex]::Escape($rutaSql)) {
    Fail "No se pudo reescribir el DECLARE @ruta en fixtures/020. Revisá el script."
}
$tmp = Join-Path $env:TEMP "smartnet-020-$([guid]::NewGuid().ToString('N')).sql"
Set-Content -Path $tmp -Value $patched -Encoding UTF8
try {
    & sqlcmd -S $server -d BDSmartNet -E -C -b -i $tmp
    if ($LASTEXITCODE -ne 0) { Fail "fixtures/020 falló (exit $LASTEXITCODE). Ver el error arriba." }
} finally {
    Remove-Item $tmp -Force -ErrorAction SilentlyContinue
}
Write-Ok "Catálogos dbo.* cargados y verificados."
exit 0
