# Exporta los catalogos maestros desde los .xlsx de la raiz del repositorio
# hacia CSV delimitados por barra vertical, listos para BULK INSERT.
#
# Normalizacion aplicada, y por que:
#   - TRIM en todo valor: el sistema de origen exporta columnas CHAR rellenadas con espacios.
#   - Se retira el prefijo 'DNI' de rucpro: 118 filas lo traen. El tipo de documento ya vive
#     en coddocide, de modo que el prefijo es informacion duplicada dentro del numero.
#   - Se decodifican entidades XML (&quot; &amp; &lt; &gt; &apos;) que el origen dejo escapadas
#     dentro del texto.
#   - Se descarta la fila de encabezado.
#
# NO carga nada en la base. Solo produce los CSV.

$ErrorActionPreference = 'Stop'

$repo    = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$destino = Join-Path $PSScriptRoot 'data'
$tmp     = Join-Path $env:TEMP ('catalogos-' + [guid]::NewGuid().ToString('N'))

New-Item -ItemType Directory -Path $destino -Force | Out-Null
New-Item -ItemType Directory -Path $tmp -Force | Out-Null

function ConvertFrom-XlsxSheet {
    param([string]$RutaXlsx)

    $carpeta = Join-Path $tmp ([IO.Path]::GetFileNameWithoutExtension($RutaXlsx))
    $copia   = Join-Path $tmp ([IO.Path]::GetFileNameWithoutExtension($RutaXlsx) + '.zip')
    Copy-Item $RutaXlsx $copia
    Expand-Archive -Path $copia -DestinationPath $carpeta -Force

    $compartidas = @()
    $rutaSs = Join-Path $carpeta 'xl\sharedStrings.xml'
    if (Test-Path $rutaSs) {
        [xml]$ss = Get-Content $rutaSs -Encoding UTF8
        foreach ($si in $ss.sst.si) {
            if ($si.t -is [string])   { $compartidas += $si.t }
            elseif ($si.t.'#text')    { $compartidas += $si.t.'#text' }
            else                      { $compartidas += (($si.r | ForEach-Object { $_.t }) -join '') }
        }
    }

    [xml]$hoja = Get-Content (Join-Path $carpeta 'xl\worksheets\sheet1.xml') -Encoding UTF8

    $filas = @()
    foreach ($r in $hoja.worksheet.sheetData.row) {
        $celdas = [ordered]@{}
        foreach ($c in $r.c) {
            $v = $c.v
            if ($c.t -eq 's' -and $null -ne $v) { $v = $compartidas[[int]$v] }
            $columna = ($c.r -replace '[0-9]', '')
            $celdas[$columna] = Normalize-Valor $v
        }
        $filas += ,$celdas
    }
    return $filas
}

function Normalize-Valor {
    param($Valor)
    if ($null -eq $Valor) { return '' }
    $s = [string]$Valor

    # Entidades XML que el origen dejo escapadas dentro del texto.
    $s = $s.Replace('&quot;', '"').Replace('&apos;', "'").Replace('&lt;', '<').Replace('&gt;', '>').Replace('&amp;', '&')

    # Corrupcion puntual del sistema de origen: la comilla doble quedo codificada como
    # '&AMPQUOT', que no es una entidad valida. Afecta a UNA sola fila (P00001).
    # Los otros 230 '&' del catalogo son legitimos -- nombres del tipo 'A & B S.A.' --
    # y no deben tocarse.
    $s = $s.Replace('&AMPQUOT', '"')

    return $s.Trim()
}

function Write-Csv {
    param([string]$Nombre, [string[]]$Columnas, $Filas)

    $salida = New-Object System.Collections.Generic.List[string]
    $maximos = @{}
    foreach ($c in $Columnas) { $maximos[$c] = 0 }

    $primera = $true
    foreach ($f in $Filas) {
        if ($primera) { $primera = $false; continue }   # encabezado
        $campos = @()
        foreach ($c in $Columnas) {
            $v = if ($f.Contains($c)) { $f[$c] } else { '' }
            if ($v.Length -gt $maximos[$c]) { $maximos[$c] = $v.Length }
            $campos += $v
        }
        $salida.Add(($campos -join '|'))
    }

    $ruta = Join-Path $destino "$Nombre.csv"
    [IO.File]::WriteAllLines($ruta, $salida, (New-Object Text.UTF8Encoding $false))

    $detalle = ($Columnas | ForEach-Object { "$_=$($maximos[$_])" }) -join ' '
    Write-Output ("{0,-20} {1,5} filas   longitudes maximas: {2}" -f $Nombre, $salida.Count, $detalle)
}

Write-Output '=== Exportando catalogos ==='

# --- DocumentoIdentidad: coddocide | nomdocide
Write-Csv 'DocumentoIdentidad' @('A','B') (ConvertFrom-XlsxSheet (Join-Path $repo 'DocumentoIdentidad.xlsx'))

# --- Origen: codigo | origen
Write-Csv 'Origen' @('A','B') (ConvertFrom-XlsxSheet (Join-Path $repo 'Origen.xlsx'))

# --- Motivo: codigo | motivo | cuenta (prefijos separados por coma)
Write-Csv 'Motivo' @('A','B','C') (ConvertFrom-XlsxSheet (Join-Path $repo 'Motivos.xlsx'))

# --- CuentaContable: cuenta | descripcion | nivel | ctarefleja | ctapuente
Write-Csv 'CuentaContable' @('A','B','C','D','E') (ConvertFrom-XlsxSheet (Join-Path $repo 'Cuentas.xlsx'))

# --- Proveedor: codpro | proveedor | coddocide | rucpro  (con el prefijo DNI retirado)
$proveedores = ConvertFrom-XlsxSheet (Join-Path $repo 'Proveedores.xlsx')
$conPrefijo = 0
foreach ($f in $proveedores) {
    if ($f.Contains('D') -and $f['D'].StartsWith('DNI')) {
        $f['D'] = $f['D'].Substring(3).Trim()
        $conPrefijo++
    }
}
Write-Csv 'Proveedor' @('A','B','C','D') $proveedores
Write-Output ("  prefijo DNI retirado en {0} filas" -f $conPrefijo)

Remove-Item $tmp -Recurse -Force
Write-Output ''
Write-Output ("CSV escritos en: " + $destino)
