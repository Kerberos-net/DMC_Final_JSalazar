# Fixtures de la pagina de la SBS

`sbs_tipo_cambio.html` y `sbs_tipo_cambio_malformado.html` son fixtures **REALES**: capturan el
subarbol real (tabla + span de fecha) de
`https://www.sbs.gob.pe/app/pp/SISTIP_PORTAL/Paginas/Publicacion/TipoCambioPromedio.aspx`, tomado
el 18/08/2026.

## Por que antes eran sinteticos, y por que ahora no

Durante la implementacion original de este ítem se intento obtener la pagina con `curl`/WebFetch
(clientes HTTP sin motor JS). La respuesta llega detras de un WAF (Incapsula) que bloquea ese tipo
de peticion automatizada, devolviendo solo un script de challenge, sin markup de la tabla — por
eso el fixture original era una estructura plausible construida a mano, documentada como tal.

Un navegador real (usado aqui via Claude in Chrome) sí renderiza la pagina sin problema: el WAF
solo bloquea clientes sin motor JS, no navegadores reales. Con eso se capturo el markup real de la
pagina y se reemplazaron ambos fixtures.

## Estructura real de la pagina

La tabla es un grid Telerik RadGrid, no una tabla HTML simple con `id="tblTipoCambio"` como asumia
el fixture sintetico anterior:

- `<table class="rgMasterTable" id="ctl00_cphContent_rgTipoCambio_ctl00">` — la tabla completa.
- Encabezado: `<thead><tr><th class="rgHeader APLI_fila1">MONEDA</th>...COMPRA (S/)...VENTA
  (S/)...</th></tr></thead>` — tres columnas, **sin** columna de fecha por fila.
- Cuerpo: `<tbody>` con una fila por moneda, alternando `class="rgRow"` / `class="rgAltRow"`, cada
  una con `id="ctl00_cphContent_rgTipoCambio_ctl00__N"` (N=0,1,2...). La fila del dolar (USD,
  "Dólar de N.A.") no siempre es la fila `__0` de forma garantizada por contrato de la pagina, asi
  que `sbs.py` la busca por el texto de la celda MONEDA en vez de depender del indice.
- Celdas: `<td class="APLI_fila3">` para el nombre de moneda, `<td class="APLI_fila2">` para cada
  valor numerico (compra, venta, en ese orden).
- La fecha de la pagina vive en `<span id="ctl00_cphContent_lblFecha">Tipo de Cambio al
  dd/mm/aaaa</span>` — **sin hora**, a diferencia de lo que asumia el fixture sintetico anterior
  (`dd/mm/aaaa hh:mm:ss`). Es tambien la unica fecha que publica la pagina: no hay una columna de
  fecha independiente por fila, asi que `fecha` y `fecha_consulta` en `sbs.py` se derivan del mismo
  texto (ver el docstring de `parse_tipo_cambio` para la decision sobre la hora faltante).

`sbs_tipo_cambio.html` incluye dos filas reales: la fila `__0` (Dólar de N.A. / USD, la que le
interesa a este ítem) y la fila `__1` (Dólar Canadiense), para que las pruebas verifiquen que el
parser ignora monedas que no son USD. `sbs_tipo_cambio_malformado.html` reproduce una falla real de
estructura: la tabla esta presente pero ninguna fila corresponde a "Dólar de N.A." (el escenario de
que la SBS reordene o quite temporalmente esa fila).

Si la pagina real cambia de estructura otra vez, `sbs.py` debera ajustarse contra un fixture
recapturado a mano (por ejemplo con las herramientas de desarrollo del navegador o Claude in
Chrome) — nunca contra una suposicion silenciosa.
