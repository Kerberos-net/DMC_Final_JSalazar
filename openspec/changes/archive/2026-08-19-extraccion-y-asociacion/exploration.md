# Exploration: Extracción y asociación (BACKLOG #6)

## Current State
- **ADR 0017** (rev 2) gobierna este ítem: XML como fuente estructurada prioritaria (XML+PDF → XML
  autoritativo, PDF como evidencia; solo PDF → extracción de texto + OCR; solo XML → XML); la clave
  de asociación de 4 componentes (RUC emisor, tipo de comprobante, serie, número — todos
  normalizados, coincidencia exacta solamente; asunto/remitente/fecha/posición **nunca** establecen
  asociación); `AfectacionMixta` (tres estados: `true`=el XML declara >1 código de afectación →
  rechazo 409, `false`=un solo código→verificado, `NULL`=sin XML→sin verificar, necesita
  confirmación del asistente antes de validar).
- **ADR 0003**: `fact.DatosExtraidos` es privado de Python (este ítem lo escribe).
  `fact.FacturaExtraccion` es privado de .NET (territorio de #7/#11, no de este ítem) — la
  evidencia de extracción por campo/fuente solo necesita viajar en el payload de `InboxEvent` desde
  #6, no persistirse ahí.
- **ADR 0010**: las cuatro clases de error ya existen como CHECK constraint en
  `fact.ProcesamientoError.Clasificacion` desde el esquema del ítem #1. Este ítem es donde
  `PERMANENTE` se dispara de verdad (adjunto corrupto/encriptado/no soportado, XML inválido) —
  reusa la tabla existente, no define ningún mecanismo de clasificación nuevo.
- **Realidad del esquema**, confirmada leyendo directamente `SmartNet/db/schema/003_ingesta_y_procesamiento.sql`:
  - `fact.DocumentoRecibido.TipoDocumento` (VARCHAR(10) NULL, CHECK IN ('XML','PDF')) hoy siempre es
    NULL — el ítem #5 nunca lo fija. `openspec/changes/ingesta-gmail/design.md:248` documenta
    explícitamente esto como trabajo del ítem #6.
  - `fact.Procesamiento` tiene un único FK `DocumentoRecibidoId` — **ninguna columna/tabla existente
    vincula el `DocumentoRecibido` de un XML con el de su PDF asociado.** Es un gap de esquema real
    y sin resolver que el ítem #6 debe cerrar (migración nueva `014_*.sql`).
  - `fact.DatosExtraidos` no tiene columna `AfectacionMixta` ni una 4ta columna "serie" —
    `TipoComprobante`/`Numero`/`RucProveedor` existen, pero "serie" parece estar embebida en
    `Numero` (VARCHAR(20), "serie(4)+'-'+hasta 8 dígitos" per design.md ítem 2), no como campo
    separado, como sugiere la prosa de ADR 0017 ("RUC, tipo, serie y número").
  - `013_configuracion_etiqueta_procesado.sql` agregó deliberadamente
    `UQ_DocumentoRecibido_Email_Hash (EmailId, HashContenido)` de forma temprana, con un comentario
    explícito: se agregó ahí "para no dejarle una dependencia de esquema no declarada" al ítem #6.
    **Esto confirma directamente que el índice que dejó el ítem #5 es deliberadamente suficiente
    para las necesidades de reproceso de #6; no se le debe ningún índice de identidad adicional.**
- `SmartNet/worker/pyproject.toml` no tiene dependencias de XML, PDF, ni OCR hoy — el ítem #6 es
  terreno nuevo para las tres.
- ADR 0017 marca explícitamente el motor de OCR como **sin decidir y el riesgo técnico declarado
  más alto del proyecto**, exigiendo una interfaz intercambiable y una decisión de negocio previa
  explícita sobre si los documentos pueden salir de la organización.

## Affected Areas
- `SmartNet/db/schema/014_*.sql` (nueva) — vínculo de asociación XML↔PDF, posiblemente persistencia
  de `AfectacionMixta` (pendiente de qué lado la posee).
- `SmartNet/worker/src/smartnet_worker/` (módulos nuevos) — parser XML/UBL, extracción de texto de
  PDF, adaptador OCR detrás de una interfaz, lógica de asociación de 4 componentes, calculador de
  `AfectacionMixta`, wiring de clasificación de errores.
- `SmartNet/worker/src/smartnet_worker/documento_repo.py` — extender para
  `Procesamiento`/`DatosExtraidos`/`ProcesamientoError`/`ProcesamientoIntentos` (hoy solo tiene
  `Email`/`DocumentoRecibido`).
- `SmartNet/worker/pyproject.toml` — nuevas dependencias (lxml, librería de texto de PDF,
  librería/servicio de OCR — pendiente).
- `openspec/changes/ingesta-gmail/design.md:44,248` — los ganchos explícitos para el ítem #6 que
  dejó el ítem #5 ya cerrado.

## Approaches
1. **Columna de asociación en `Procesamiento`** (FK nullable al `DocumentoRecibido` emparejado) —
   Pros: cambio mínimo, calza con el framing "el XML es la autoridad". Contras: no representa
   limpiamente el caso de PDF sin emparejar que necesita revisión. Esfuerzo: Medio.
2. **Tabla dedicada `fact.AsociacionDocumento`** — Pros: rastro de auditoría durable de cada intento
   de asociación, alimenta limpio al futuro panel de incidencias (#13). Contras: más diseño previo,
   otra superficie de FK/permisos. Esfuerzo: Medio-Alto.
3. **Sin asociación persistida** — Pros: sin cambio de esquema. Contras: contradice el propio
   criterio de aceptación de TECH-DESIGN.md de que los PDF sin emparejar necesitan una incidencia de
   revisión; pospone un problema real. No es una opción seria.

## Recommendation
Inclinarse por el Approach 1 para el caso común, pero no dejar que `sdd-propose` elija la forma del
esquema en silencio — esto necesita una decisión explícita del usuario, junto con la selección del
motor de OCR.

## Risks
- Motor/servicio de OCR sin decidir — el riesgo técnico declarado más alto de ADR 0017; una API de
  OCR en la nube requiere una decisión de negocio previa explícita de "los documentos salen de la
  organización" que todavía no se tomó.
- Gap de esquema: no existe vínculo entre las filas `DocumentoRecibido` emparejadas de XML/PDF.
- El hogar durable de `AfectacionMixta` es ambiguo — puede pertenecer a `Factura` del lado .NET
  (ítems #7/#8) en vez de necesitar una columna del lado Python.
- "Serie" puede no ser un campo distinto en el esquema actual — la comparación de 4 componentes
  necesita reconciliar esto con el formato real de `DatosExtraidos.Numero`.
- Sin contexto ⚠ requerido per BACKLOG.md; única dependencia es #5 (cerrado).

## Decisiones ya resueltas (no son preguntas abiertas)

- **Frontera OCR (decisión de negocio)**: los documentos NO pueden salir de la organización. El
  motor de OCR debe correr local/on-premise (ej. Tesseract), sin llamada de red a un tercero. Queda
  descartado cualquier servicio de OCR en la nube.
- **Persistencia de la asociación XML↔PDF**: columna FK nullable en `fact.Procesamiento` apuntando
  al `DocumentoRecibido` emparejado (Approach 1) — cambio mínimo, no una tabla dedicada.

## Ready for Proposal
Sí. Las dos preguntas bloqueantes fueron resueltas por el usuario — ver arriba.
