# Verifica que el host cumple los prerequisitos de sistema ANTES de desplegar. Solo lectura:
# no instala ni configura nada. DEPLOY-PLAN.md § Deploy gates, punto 4.
#
#   pwsh -File .\preflight.ps1 -Environment prod

param([Parameter(Mandatory)][string]$Environment)

. "$PSScriptRoot\_common.ps1"
Import-DeployConfig -Environment $Environment

$problemas = 0
function Check { param([string]$Nombre, [scriptblock]$Test, [string]$Pista)
    try { if (& $Test) { Write-Ok $Nombre } else { Write-Warn2 "$Nombre -- $Pista"; $script:problemas++ } }
    catch { Write-Warn2 "$Nombre -- $Pista ($($_.Exception.Message))"; $script:problemas++ }
}

Write-Step "Runtimes"
Check "PowerShell 7+" { $PSVersionTable.PSVersion.Major -ge 7 } "Los scripts usan PS7. Instalá con 'winget install Microsoft.PowerShell' y corré con 'pwsh'."
Check ".NET 10 Runtime (ASP.NET Core)" { (dotnet --list-runtimes) -match 'Microsoft.AspNetCore.App 10\.' } "Instalá el ASP.NET Core Runtime 10 (Hosting Bundle no hace falta: no usamos IIS)."
Check "Python 3.13+" { (& python --version) -match 'Python 3\.(1[3-9]|[2-9]\d)' } "Instalá Python 3.13+ y agregalo al PATH."
Check "ODBC Driver 18 for SQL Server" { (Get-OdbcDriver -Name 'ODBC Driver 18 for SQL Server' -ErrorAction SilentlyContinue) -ne $null } "Instalá 'msodbcsql18'."
Check "Tesseract OCR" { Test-Path $TesseractCmd } "Instalá Tesseract en $TesseractCmd (o ajustá `$TesseractCmd)."
Check "Tesseract idioma 'spa'" { (& $TesseractCmd --list-langs 2>&1) -match '(^|\s)spa(\s|$)' } "Instalá el paquete de idioma español de Tesseract."
Check "sqlcmd" { (Get-Command sqlcmd -ErrorAction SilentlyContinue) -ne $null } "Instalá 'sqlcmd' (mssql-tools18)."
Check "Caddy" { Test-Path $CaddyExe } "Descargá caddy.exe a $CaddyExe."

Write-Step "Base de datos (instancia compartida -- NO la administra este proyecto)"
Check "BDSmartNet alcanzable" { (Invoke-Sql 'preflight' "SELECT DB_ID('BDSmartNet')") -match '\d' } "El administrador de la instancia debe crear la base BDSmartNet."
Check "LOGIN usr_api existe" { (Invoke-Sql 'preflight' "SELECT COUNT(*) FROM sys.server_principals WHERE name='usr_api'") -eq '1' } "El administrador debe crear el LOGIN usr_api (008 hace THROW si falta)."
Check "LOGIN usr_worker existe" { (Invoke-Sql 'preflight' "SELECT COUNT(*) FROM sys.server_principals WHERE name='usr_worker'") -eq '1' } "El administrador debe crear el LOGIN usr_worker."
# Solo falla si el USER de BD ya existe Y su SID no coincide con ningún LOGIN (huérfano, error
# 4060). Si el USER todavía no existe es correcto: lo crea 008 (CREATE USER ... FOR LOGIN) en el
# primer deploy.
Check "usr_api no huérfano (si ya existe)" { (Invoke-Sql 'preflight' "SELECT COUNT(*) FROM sys.database_principals dp WHERE dp.name='usr_api' AND dp.type IN ('S','U') AND NOT EXISTS (SELECT 1 FROM sys.server_principals sp WHERE sp.sid=dp.sid)") -eq '0' } "El USER de BD usr_api existe pero su SID no coincide con el login: ALTER USER usr_api WITH LOGIN = usr_api; en BDSmartNet."
Check "usr_worker no huérfano (si ya existe)" { (Invoke-Sql 'preflight' "SELECT COUNT(*) FROM sys.database_principals dp WHERE dp.name='usr_worker' AND dp.type IN ('S','U') AND NOT EXISTS (SELECT 1 FROM sys.server_principals sp WHERE sp.sid=dp.sid)") -eq '0' } "El USER de BD usr_worker existe pero su SID no coincide con el login: ALTER USER usr_worker WITH LOGIN = usr_worker; en BDSmartNet."

Write-Step "Red"
Check "$PublicHost resuelve" { try { [void][Net.Dns]::GetHostEntry($PublicHost); $true } catch { $false } } "Agregá una entrada en C:\Windows\System32\drivers\etc\hosts (p. ej. '127.0.0.1  $PublicHost') en la VM, y en las máquinas cliente."
Check "puerto 443 libre" { -not (Get-NetTCPConnection -LocalPort 443 -State Listen -ErrorAction SilentlyContinue) } "Algo ya escucha en :443 (IIS por defecto?). Pará ese servicio o cambiá el puerto de Caddy."

Write-Step "Rutas del host"
Check "Volumen de adjuntos escribible" { New-Item -ItemType Directory -Force -Path $StorageRoot | Out-Null; $t = Join-Path $StorageRoot '.w'; Set-Content $t 'x'; Remove-Item $t; $true } "Sin permiso de escritura en $StorageRoot."
Check "Carpeta de keyring escribible" { New-Item -ItemType Directory -Force -Path $KeyringPath | Out-Null; $t = Join-Path $KeyringPath '.w'; Set-Content $t 'x'; Remove-Item $t; $true } "Sin permiso de escritura en $KeyringPath."

Write-Host ""
if ($problemas -gt 0) { Fail "$problemas verificación(es) fallaron. Resolvé antes de desplegar." }
Write-Ok "Preflight OK -- el host cumple los prerequisitos."
