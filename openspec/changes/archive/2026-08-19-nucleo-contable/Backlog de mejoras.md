# Pendientes — Ítem #8 Núcleo contable

Este documento registra lo que queda fuera de este ciclo (#8) o pendiente de resolver antes de
avanzar, para que nada se pierda entre exploración y propuesta. Se actualiza a medida que el
ciclo avanza; no reemplaza a `REGLAS.md` ni a `BACKLOG.md`, los referencia.

## 1. Actualización pendiente de `REGLAS.md` (discrepancia deliberada)

**§8 y §12 punto 4 — precondición de la nota de crédito.**

`REGLAS.md` §12 punto 4 dice hoy que la nota de crédito exige que la factura original esté
validada, y §8 la rechaza con `409` si no lo está. El dueño del proyecto decidió (2026-08-19)
**relajar esa precondición**: el sistema debe admitir notas de crédito que hoy se rechazan por
este motivo.

Esto es una discrepancia deliberada entre código y documento normativo (regla del proyecto:
nunca en silencio). Antes de que `sdd-spec`/`sdd-design` de #8 o del ítem #10 (Notas de crédito,
que es quien realmente implementa esta precondición) usen esta regla como base, hay que:

- Editar `REGLAS.md` §8 y §12 punto 4 para reflejar la precondición relajada, **o**
- Si no se edita todavía, dejar el override explícito y citado en la spec correspondiente,
  nunca asumirlo implícito.

Las otras cinco reglas de §12 (TC venta para pasivos, redondeo sin línea de ajuste, IGV de
boleta al costo, NC hereda TC de su factura, estructura de NC sobre boleta) se **ratificaron
tal cual están escritas** — sin cambios de código pendientes por este motivo.

## 2. Fuera de alcance de #8 (pertenece a otros ítems del backlog)

- **Persistencia y HTTP** del asiento generado — ítem #11 (API de facturas y asientos). #8 es
  puro: sin base de datos, sin HTTP, sin reloj (ADR 0019).
- **Sugerencia de cuenta** (cascada por frecuencia) — ítem #9. #8 solo consume candidatas ya
  resueltas del plan de cuentas (ítem #3), no las sugiere.
- **Notas de crédito** (referencia interna/externa, herencia de atributos, reparto proporcional,
  tope acumulado, y la precondición relajada del punto 1 de este documento) — ítem #10. #8 solo
  deja el punto de enganche (fase Componer/Validar) para que #10 lo use.
- **Catálogo de rechazo `409`/`412`/`422` completo** — ítem #11. #8 solo cubre las invariantes
  contables de §7; el catálogo operativo de rechazo de §8 es de #11 salvo lo que sea
  estrictamente una invariante contable.

## 2.1. Seguimiento detectado en `sdd-verify` (2026-08-19) — para ítem #11 (o #3)

**Discriminador PRINCIPAL/gravada sin test de corrección para el caso ilegal boleta+Gravada.**
`ComposicionDeAsiento.Componer` (~línea 59) e `InvariantesDeConfirmacion.EvaluarPrincipal`
(~línea 101) usan únicamente `AfectacionCongelada == Gravada` para decidir si el asiento lleva
línea `401111` (IGV). Ambos confían en que una boleta nunca llega marcada `Gravada` desde aguas
arriba (#3/#11) — supuesto documentado en comentario, correcto para el alcance de #8, pero sin
prueba de comportamiento: existe un caso `[InlineData(false, true)]` que ejercita boleta+Gravada,
pero solo verifica que no lanza excepción, no que el resultado sea correcto.

**Acción pendiente:** #11 (o quien valide `EntradaAsiento` antes de que llegue al núcleo) debe
agregar la guarda que impida el estado ilegal `TipoComprobante.Boleta` + `Afectacion.Gravada`,
con su propio test de rechazo. No bloquea el archivado de #8 — el supuesto es correcto dentro del
límite que `design.md` §4 trazó para #8, pero queda sin garantía estructural hasta que #11 lo
cierre.

## 3. Preguntas de diseño abiertas (resolver en `sdd-design`, no en `sdd-propose`)

De la exploración (2026-08-19):

1. Forma exacta del DTO de entrada de "la factura" — tipo propio de #8 vs. envolver/reusar los
   tipos de `SmartNet.Catalogos.Core` y `SmartNet.TiposCambio.Core` directamente.
2. Si la composición de la NC recibe el `Asiento` congelado de la factura referenciada como
   parámetro, o si el llamador debe pre-aplanar los atributos heredados.
3. Representación del rechazo: jerarquía cerrada tipo `ResultadoTipoCambio` vs. excepción.
4. Límite exacto entre las invariantes de §7 (dominio de #8) y el catálogo de rechazo de §8
   (dominio de #11), para que #8 no se meta en terreno de #11.
5. Representación del reparto proporcional multi-cuenta (división del cargo) en el modelo de
   entrada/salida.

## 4. Reglas marcadas para revisión futura (`REGLAS.md` §11, no bloquean #8)

Listadas en `REGLAS.md` §11 "Reglas que hay que revisar" — quedan fuera de alcance de #8 salvo
que su disparador ("cuándo revisarla") ya haya ocurrido:

1. 23 motivos de caja chica reclasificados a `02` — revisar antes de producción.
2. IGV siempre a `401111` — revisar si aparecen ventas no gravadas.
3. Facturas mixtas fuera de alcance — revisar si llegan comprobantes con líneas gravadas y no
   gravadas.
4. Feriados no controlados — revisar si la política se extiende más allá de los domingos.
5. Detracción, retención y diferencia de cambio fuera de alcance — revisar si el sistema llega a
   modelar pagos o cierres.
6. Reparto proporcional de la NC parcial — revisar si las devoluciones empiezan a corresponder
   sistemáticamente a una línea concreta.
7. Nota con referencia externa sin tope — revisar cuando dejen de llegar notas contra facturas
   anteriores al sistema.

## 5. Advertencia de alcance (recordatorio, no acción)

Las seis reglas de `REGLAS.md` §12 (incluida la ratificación de este ciclo) **no están
ratificadas por un contador**. Es una demostración académica; el sistema no debe operar con
contabilidad real sin esa revisión formal. Esto no bloquea #8, pero debe seguir citado en el
`design.md` del ítem para que no se pierda al archivar el cambio.
