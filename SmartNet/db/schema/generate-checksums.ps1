# Regenera SmartNet/db/schema/checksums.txt -- el manifiesto de hashes que compensa el hueco de
# DbUp (task 5.1): DbUp anota el NOMBRE de un script en fact.SchemaVersions y nunca vuelve a mirar
# su contenido, asi que editar un script ya aplicado no falla en ningun lado -- la base y el
# repositorio divergen en silencio. Este manifiesto es lo unico que detecta esa edicion.
#
# Alcance: SOLO los scripts de nivel superior de SmartNet/db/schema/ (los que el runner realmente
# aplica). rollback/*.sql queda fuera a proposito -- nunca se aplica (design.md, Decision 4), asi
# que "editado despues de aplicado" no es una nocion que le corresponda; ChecksumManifestTests.cs
# no lo espera en el manifiesto.
#
# Formato: una linea por script, "<sha256 en hexadecimal minuscula>  <nombre de archivo>",
# ordenado por nombre de archivo. ChecksumManifestTests.cs (SmartNet/db/runner/
# SmartNet.Db.Runner.Tests/) lee este mismo formato de forma independiente, en C#, no ejecutando
# este script -- las dos implementaciones deben coincidir en su propio codigo, no compartir uno.
#
# Uso:
#   pwsh -File .\generate-checksums.ps1
# Se ejecuta a mano, deliberadamente, cada vez que un script de schema/ cambia o se agrega uno
# nuevo -- no hay paso automatico que lo dispare todavia (ver el resumen final de Work Unit 5 sobre
# que queda pendiente de decision del usuario para CI).

$ErrorActionPreference = 'Stop'

$schemaDir = $PSScriptRoot
$manifestPath = Join-Path $schemaDir 'checksums.txt'

$scripts = Get-ChildItem -Path $schemaDir -Filter '*.sql' -File |
    Sort-Object -Property Name

if ($scripts.Count -eq 0) {
    throw "No se encontraron scripts *.sql en $schemaDir -- nada que hashear."
}

$lines = foreach ($script in $scripts) {
    $bytes = [System.IO.File]::ReadAllBytes($script.FullName)
    $hash = [System.Security.Cryptography.SHA256]::HashData($bytes)
    $hex = ($hash | ForEach-Object { $_.ToString('x2') }) -join ''
    "$hex  $($script.Name)"
}

# CRLF explicito (CONVENTIONS.md): el contenido no pasa por .gitattributes' eol=crlf porque esa
# regla solo cubre *.sql, y este archivo es *.txt.
$content = ($lines -join "`r`n") + "`r`n"
[System.IO.File]::WriteAllText($manifestPath, $content, [System.Text.Encoding]::ASCII)

Write-Host "Escritas $($scripts.Count) entradas en $manifestPath"
