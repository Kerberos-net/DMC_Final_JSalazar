# Copia lo que es responsabilidad propia de este proyecto ANTES de cada release (ADR 0014):
#   - el anillo de claves de Data Protection (si se pierde, toda cookie de sesión viva deja de
#     descifrar en el próximo reinicio -- ADR 0014 ítem #2)
#   - los secretos del worker ($SecretsDir)
# NO copia la base de datos: es compartida con el sistema contable y su respaldo NO es de este
# proyecto (ADR 0014 § "Este proyecto no define la política de respaldo de la instancia").
# NO copia el volumen de adjuntos aquí: esa copia diaria va ANTES que la de la base, coordinada
# con el administrador de la instancia -- este script es el paso pre-release, no el respaldo diario.
#
#   pwsh -File .\backup.ps1 -Environment prod

param([Parameter(Mandatory)][string]$Environment)

. "$PSScriptRoot\_common.ps1"
Import-DeployConfig -Environment $Environment

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$dest  = Join-Path $BackupsDir "pre-release-$stamp"
New-Item -ItemType Directory -Force -Path $dest | Out-Null

Write-Step "Anillo de claves de Data Protection"
if (Test-Path $KeyringPath) {
    Copy-Item $KeyringPath -Destination (Join-Path $dest 'dataprotection-keys') -Recurse -Force
    Write-Ok "keyring -> $dest"
} else { Write-Warn2 "No existe $KeyringPath todavía (primer deploy)." }

Write-Step "Secretos"
if (Test-Path $SecretsDir) {
    Copy-Item $SecretsDir -Destination (Join-Path $dest 'secrets') -Recurse -Force
    Restrict-Acl (Join-Path $dest 'secrets')
    Write-Ok "secretos -> $dest"
} else { Write-Warn2 "No existe $SecretsDir todavía (primer deploy)." }

Restrict-Acl $dest
Write-Ok "Respaldo pre-release completo en $dest"
