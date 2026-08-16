# ADR 0010: Política de reintentos y clasificación de errores

## Estado

Aceptado

## Contexto

El PRD establece que ante fallas de conexión con Gmail o Drive el sistema debe reintentar
automáticamente "hasta 3 veces antes de marcar la operación como error", y que la notificación de
error (por Telegram con respaldo por correo) debe enviarse en un máximo de 5 minutos con una tasa de
entrega ≥99%. Fija además que una factura recibida debe ser visible en el software en un máximo de
15 minutos desde la llegada del correo.

Sin embargo, la sección de casos borde agrupa bajo el mismo mecanismo dos clases de fallo de
naturaleza opuesta:

- **Fallos de infraestructura**: "Falla de conexión con la API de Gmail o de Google Drive". Son
  transitorios y es razonable esperar que un reintento posterior tenga éxito.
- **Fallos del contenido o de la configuración**: "Adjunto corrupto, protegido con contraseña, o en
  un formato no soportado". Estos no se resuelven por sí solos: reintentarlos reproduce
  exactamente el mismo fracaso y retrasa el aviso al usuario, consumiendo además el presupuesto de
  los 15 minutos de visibilidad y el de los 5 minutos de notificación que el propio PRD fija.

La política de reintentos es responsabilidad del worker Python, propietario del procesamiento y de
todas las integraciones externas (ADR 0003 y ADR 0004).

## Decisión

El worker **clasifica cada fallo antes de decidir si reintenta**:

- **Errores transitorios** — fallos de red, tiempos de espera agotados, respuestas 5xx de servicios
  externos y superación de cuota. Se reintentan **hasta 3 veces con espera creciente entre
  intentos**. Agotados los intentos, la operación se marca como `ERROR` y se dispara la
  notificación.
- **Errores permanentes** — adjunto corrupto, protegido con contraseña o en formato no soportado;
  credenciales revocadas o inválidas; y cualquier condición que no pueda resolverse repitiendo la
  misma operación. **No se reintentan**: la operación se marca como error de inmediato y se notifica
  sin esperar.

Cada intento queda registrado en `ProcesamientoIntentos`, y el error con su clasificación en
`ProcesamientoError`, de modo que el panel de errores del PRD pueda distinguir un fallo de conexión
de un documento imposible de procesar. La clasificación se combina con el estado independiente por
integración definido en la ADR 0004: un reintento nunca repite una integración ya completada con
éxito.

## Alternativas consideradas

- **Política uniforme de 3 reintentos para todo fallo**, con espera creciente y marcado de error al
  agotarlos. Era la lectura más literal del PRD y la más simple de implementar, explicar y probar,
  con una única regla sin clasificación que mantener. Se descartó porque aplica a fallos
  irrecuperables una política escrita textualmente para fallas de conexión: el usuario tardaría en
  enterarse de un adjunto protegido con contraseña que nunca iba a poder leerse, y las esperas entre
  reintentos inútiles consumirían el margen de los criterios de éxito de 15 minutos de visibilidad y
  5 minutos de notificación.

## Consecuencias

- Los fallos irrecuperables se notifican de inmediato, lo que mejora el cumplimiento del criterio de
  notificación en ≤5 minutos y devuelve antes el control al usuario, que es quien debe actuar sobre
  un adjunto ilegible.
- Los reintentos se concentran donde tienen probabilidad real de éxito, sin desperdiciar el
  presupuesto de tiempo que el PRD fija para la visibilidad de la factura.
- El panel de errores puede diferenciar "falló la conexión, se reintentó 3 veces" de "este documento
  no se puede procesar", que son dos mensajes que exigen acciones distintas del usuario.
- El registro por intento permite auditar el comportamiento real del sistema frente a los criterios
  de éxito del PRD, en lugar de suponerlo.
- **Costo:** hay que construir y mantener una clasificación de errores por cada integración externa
  (Gmail, Drive, Sheets, Telegram, correo, SBS y el motor OCR), y las APIs de terceros no siempre
  distinguen con claridad entre condiciones transitorias y permanentes.
- **Costo:** una clasificación equivocada tiene consecuencias asimétricas y reales: marcar como
  permanente un fallo transitorio pierde el reintento que habría resuelto la operación, mientras que
  lo contrario solo retrasa la notificación.
- **Costo:** la lógica de reintentos deja de ser una regla única y uniforme, por lo que probarla
  exige cubrir ambas ramas y las condiciones límite de cada clasificación.
