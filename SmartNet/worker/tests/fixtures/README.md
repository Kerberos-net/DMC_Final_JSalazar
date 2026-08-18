# Fixtures de la pagina de la SBS

`sbs_tipo_cambio.html` y `sbs_tipo_cambio_malformado.html` son **fixtures sinteticos**, no una
copia guardada de la pagina real de `sbs.gob.pe`.

Se intento obtener la pagina real durante la implementacion de este ítem
(`curl https://www.sbs.gob.pe/app/pp/EstadisticasSAEEPortal/Paginas/TipoCambioPromedio.aspx`); la
respuesta llega detras de un WAF (Incapsula) que bloquea peticiones automatizadas sin un navegador
real, devolviendo solo un script de challenge, sin markup de la tabla. No se intento sortear ese
bloqueo.

En su lugar, `sbs_tipo_cambio.html` construye una estructura plausible y minima:

- `<table id="tblTipoCambio">` con tres columnas — Fecha, Compra, Venta — una fila de encabezado y
  una fila de datos.
- Un elemento `<span id="lblFechaConsulta">` con el instante en que la pagina fue generada
  (`dd/mm/aaaa hh:mm:ss`), del que `parse_tipo_cambio` deriva `fecha_consulta` sin tocar el reloj
  del sistema (la funcion es pura).

Si la pagina real de la SBS usa otro `id` o estructura de tabla distinta, `sbs.py` debera
ajustarse contra un fixture real capturado a mano (por ejemplo con las herramientas de desarrollo
del navegador) — nunca contra una suposicion silenciosa. Esto queda documentado explicitamente
como limitacion conocida, no como un hecho verificado.
