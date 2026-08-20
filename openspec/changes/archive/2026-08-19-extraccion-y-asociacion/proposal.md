# Proposal: Extracción y asociación (BACKLOG #6)

## Intent

`fact.DocumentoRecibido` rows land as `Estado='DESCARGADO'` from item #5, but nothing yet extracts
structured data from them or tells the system which XML belongs to which PDF. `TipoDocumento` stays
`NULL` (item #5 never sets it, by design — ADR 0017 assigns that to this item). This item builds the
extraction/processing stage: parse XML as the authoritative source when present, OCR the PDF when
XML is missing or as evidence when both exist, associate XML↔PDF rows via the four-component key
(RUC emisor, tipo de comprobante, serie, número — normalized, exact match only), and compute
`AfectacionMixta` per ADR 0017's three-state rule. This is the direct prerequisite item #7 (inbox
and promotion) depends on.

## Decisiones ya resueltas (no son preguntas abiertas)

- **Frontera OCR (decisión de negocio)**: los documentos NO pueden salir de la organización. El
  motor de OCR debe correr local/on-premise (ej. Tesseract), sin llamada de red a un tercero — queda
  descartado cualquier servicio de OCR en la nube. Razón: ADR 0017 marcó esto como el riesgo técnico
  más alto del proyecto, sin decidir, exigiendo una decisión de negocio previa explícita; el usuario
  la tomó al proponer este cambio.
- **Persistencia de la asociación XML↔PDF**: columna FK nullable en `fact.Procesamiento` apuntando
  al `DocumentoRecibido` emparejado (no una tabla `fact.AsociacionDocumento` dedicada). Razón:
  cambio mínimo de esquema, calza con el framing "el XML es la autoridad" — la exploración
  identificó una tabla dedicada como alternativa con mejor rastro de auditoría, pero el usuario
  eligió el approach más acotado.

## Scope

### In Scope
- `SmartNet/db/schema/014_*.sql` — nueva migración: columna FK nullable en `fact.Procesamiento`
  para la asociación XML↔PDF, columna `AfectacionMixta` (BIT NULL) en `fact.DatosExtraidos`; fijar
  `TipoDocumento` en `fact.DocumentoRecibido` como parte del procesamiento.
- `SmartNet/worker/src/smartnet_worker/` — parser XML/UBL (fuente prioritaria), extracción de texto
  de PDF, adaptador OCR local/on-premise detrás de una interfaz intercambiable, lógica de asociación
  de cuatro componentes normalizados, calculador de `AfectacionMixta`, wiring de clasificación de
  errores (`PERMANENTE` para adjunto corrupto/encriptado/no soportado o XML inválido, reusando
  `fact.ProcesamientoError.Clasificacion` existente).
- `SmartNet/worker/src/smartnet_worker/documento_repo.py` — extender para escribir
  `Procesamiento`/`DatosExtraidos`/`ProcesamientoError`/`ProcesamientoIntentos`.
- `SmartNet/worker/pyproject.toml` — nuevas dependencias (parser XML, extracción de texto de PDF,
  librería OCR local — a confirmar en ronda de preguntas).
- `cli_procesamiento.py` (o equivalente) — punto de entrada single-run que consume
  `DocumentoRecibido.Estado='DESCARGADO'`, ejecuta extracción/asociación, deja el estado listo para
  la promoción del ítem #7.

### Out of Scope
- Decisión de promover a `Factura`/`FacturaExtraccion` — territorio de #7, no de este ítem.
- Persistencia de evidencia de extracción por campo/fuente en tabla (`fact.FacturaExtraccion` es
  privado de .NET, ADR 0003) — solo viaja en el payload de `InboxEvent` desde #6.
- Servicio OCR en la nube — descartado por la decisión de negocio ya resuelta.
- UI/pantalla de incidencias para documentos sin pareja — pertenece al panel de incidencias (#13);
  este ítem solo deja el estado/dato que ese panel consumirá.

## Capabilities

### New Capabilities
- `extraccion-asociacion`: parseo XML/UBL, extracción de texto y OCR local de PDF detrás de una
  interfaz intercambiable, asociación XML↔PDF por clave de cuatro componentes, cálculo de
  `AfectacionMixta`, clasificación de errores `PERMANENTE`. Cubre las extensiones nuevas de
  `SmartNet/worker/` y la migración `014_*.sql`.

### Modified Capabilities
None — no existe spec previa de `ingesta-gmail` con requisitos de procesamiento que este ítem
reabra; extiende el gancho que #5 dejó explícitamente pendiente (`TipoDocumento` NULL).

## Approach

Seguir el patrón interno que #4/#5 establecieron: módulo puro de parseo/decisión (XML/UBL,
normalización de los cuatro componentes, cálculo de `AfectacionMixta`) separado del punto de
entrada IO único (`cli_procesamiento.py`), más una extensión del repositorio existente. El motor OCR
vive detrás de una interfaz intercambiable (ADR 0017 lo exige explícitamente por el riesgo técnico
declarado), de forma que el motor concreto (a resolver en la ronda de preguntas) sea sustituible sin
tocar la lógica de asociación. La asociación de cuatro componentes usa coincidencia exacta
solamente — asunto, remitente, fecha o posición del correo nunca establecen asociación (regla ya
fijada en ADR 0017, no una decisión de este ítem).

## Affected Areas

| Area | Impact | Description |
|------|--------|--------------|
| `SmartNet/db/schema/014_*.sql` | New | FK nullable de asociación XML↔PDF en `fact.Procesamiento` |
| `SmartNet/worker/src/smartnet_worker/` | Modified | Parser XML, extracción de texto/OCR de PDF, asociación, `AfectacionMixta`, errores |
| `SmartNet/worker/src/smartnet_worker/documento_repo.py` | Modified | Escritura de `Procesamiento`/`DatosExtraidos`/`ProcesamientoError`/`ProcesamientoIntentos` |
| `SmartNet/worker/pyproject.toml` | Modified | Dependencias XML/PDF/OCR nuevas |
| `cli_procesamiento.py` | New | Punto de entrada single-run de extracción/asociación |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Tesseract requiere binario de sistema, no solo paquete Python | Med | Documentar como prerequisito de despliegue (README.md del worker), mismo patrón que ODBC Driver 18 del ítem #4 |
| `AfectacionMixta` en `DatosExtraidos` significa que la migración 014 toca dos tablas, no una | Low | Ambos cambios son aditivos (columna nullable), sin romper filas existentes |
| PDF sin pareja promovido igual (con OCR) podría propagar un dato menos confiable a `Factura` sin bloquear | Med | El panel de incidencias (#13) es donde se cierra el círculo — este ítem deja la advertencia visible, no la resuelve; documentado como limitación aceptada, no oculta |
| OCR local de PDFs escaneados de baja calidad puede tener error de extracción alto | Low | Fuera del control de este ítem (calidad del insumo); clasificación `PERMANENTE` existente cubre el caso irrecuperable |

## Rollback Plan

Revertir `cli_procesamiento.py` y las extensiones de `smartnet_worker`; revertir la migración
`014_*.sql` (script de rollback que elimina la columna FK nueva). `DocumentoRecibido.Estado` vuelve
a quedar en `'DESCARGADO'` sin efectos secundarios, ya que ningún otro ítem cerrado depende todavía
de que este procesamiento haya corrido.

## Dependencies

- Item #5 (Ingesta Gmail) — ya cerrado; deja `DocumentoRecibido.Estado='DESCARGADO'` y
  `TipoDocumento` explícitamente en NULL para este ítem.

## Success Criteria

- [ ] Cuando existen XML y PDF emparejados, el XML es la fuente autoritativa de `DatosExtraidos`;
      el PDF queda como evidencia.
- [ ] Cuando solo hay PDF, se extrae texto y, si es necesario, se aplica OCR local/on-premise (sin
      llamada de red a terceros).
- [ ] Cuando solo hay XML, se usa el XML sin invocar OCR.
- [ ] La asociación XML↔PDF se establece únicamente por coincidencia exacta de los cuatro
      componentes normalizados (RUC emisor, tipo de comprobante, serie, número); asunto, remitente,
      fecha y posición del correo nunca la establecen.
- [ ] `AfectacionMixta` queda en `true` (rechazo 409, XML declara >1 código), `false` (un solo
      código, verificado) o `NULL` (sin XML, sin verificar, requiere confirmación posterior) según
      corresponda.
- [ ] `fact.DocumentoRecibido.TipoDocumento` queda fijado (`'XML'` o `'PDF'`) tras el procesamiento.
- [ ] Adjunto corrupto, encriptado, no soportado, o XML inválido se clasifica como error
      `PERMANENTE` en `fact.ProcesamientoError`.
- [ ] Ningún llamado de red sale hacia un servicio de OCR de terceros.
- [ ] La migración `014_*.sql` agrega la FK de asociación como nullable, sin romper filas
      `Procesamiento` existentes.

## Decisiones ya resueltas — ronda 2

- **Motor/librería OCR**: `pytesseract` + Tesseract OCR instalado en el host del worker (binario de
  sistema, no solo paquete Python).
- **Hogar de `AfectacionMixta`**: `fact.DatosExtraidos` (lado Python, ítem #6) — se calcula y
  persiste ahí mismo, junto al resto de los datos extraídos del XML. Requiere agregar la columna en
  la migración `014_*.sql`.
- **XML sin su PDF pareja**: se promueve igual — el XML es autoritativo y suficiente por sí solo; el
  PDF queda como evidencia opcional que puede llegar después o nunca. No bloquea.
- **PDF sin su XML pareja**: advertencia visible, no bloqueante — se promueve igual con los datos
  del OCR, pero queda marcado/visible en el panel de incidencias (#13) para revisión posterior.
- **Formato de "serie" en `DatosExtraidos.Numero`**: el número de comprobante peruano (SUNAT) es
  intrínsecamente compuesto — "serie-número" es un solo identificador lógico, no dos campos
  independientes. Se parsea "serie" desde el campo compuesto `Numero` (VARCHAR(20)) en tiempo de
  comparación de la clave de cuatro componentes; **no se agrega columna `Serie` separada** — hacerlo
  fragmentaría un dato que el dominio trata como una sola unidad.

## Proposal question round

Ninguna pendiente. Las cinco quedaron resueltas — ver arriba.
