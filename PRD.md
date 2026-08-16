---
title: "Gestor de Facturas de Compra"
---

# PRD: Gestor de Facturas de Compra

## Problema

El registro de facturas de compra es un proceso manual y fragmentado entre varias herramientas: las facturas llegan a diario por Gmail junto con la orden de compra y los medios probatorios, luego deben descargarse, imprimirse, registrarse a mano en el sistema de gestión contable (que genera el asiento contable) y finalmente archivarse en una carpeta de Drive junto con sus respaldos. Al no existir un flujo centralizado, no hay visibilidad clara del estado de cada factura (pendiente, registrada, archivada, con error), el archivo en Drive depende de la disciplina manual del usuario, y no existe alerta inmediata cuando algo falla en el registro. Esto genera riesgo de facturas no procesadas a tiempo, pérdida de trazabilidad y falta de datos consolidados para análisis de gastos.

## Usuario objetivo

Un asistente contable que procesa entre 10 y 50 facturas de compra diarias, el registro se va a realizar en dos etapas: primero, se revisa y registra las facturas detectadas en el correo Gmail y luego , se confirma o corrige los datos extraídos automáticamente antes de dar por completado el registro.

## Objetivo / resultado esperado

Si el sistema funciona como se espera, el equipo cuenta con un software web único donde: las facturas se detectan automáticamente en Gmail junto con su OC y medios probatorios, sus datos clave (proveedor, tipo de comprobante, número, monto, moneda, fecha) se extraen automáticamente por OCR/IA quedando en estado "pendiente de validación", el asistente contable confirma o corrige esos datos, el sistema genera y almacena el asiento contable estructurado (cabecera con número de comprobante, origen del libro, proveedor, glosa, fecha contable, tipo de cambio, base imponible, IGV y neto; detalle navegable con las líneas del asiento), se genera automáticamente el archivo en Drive, se detectan duplicados y se alerta ante errores por correo o Telegram — todo con visibilidad consolidada en un dashboard de gastos en Looker Studio.

## Alcance (qué sí incluye esta versión)

- Conexión con la cuenta de Gmail del usuario para detectar correos entrantes con factura de compra, orden de compra y medios probatorios adjuntos.
- Descarga automática de los adjuntos (factura, OC, medios probatorios) hacia el software.
- Extracción automática de datos de la factura mediante OCR/IA: tipo de comprobante (01 Factura, 03 Boleta, 07 Nota de Crédito), número de comprobante, proveedor, monto, moneda y fecha de emisión. La factura queda en estado **"Pendiente de validación"**.
- En la interfaz web se muestra los comprobantes pendientes de validación, luego de ello el asistente contable podrá confirmar o corregir los datos extraídos para luego marcar la factura como **"Validada"**, habilitando el resto del flujo (generación del asiento contable, archivado en Drive). No hay separación de permisos por rol: el mismo usuario revisa y valida.
- Generación y almacenamiento del asiento contable dentro del software, en formato cabecera-detalle:
  - **Cabecera**: número de comprobante (el mismo número extraído de la factura), origen del libro (por defecto **02 Compras**), nombre del proveedor, glosa (descripción libre del asiento), fecha contable (año/mes/día, editable e independiente de la fecha de emisión de la factura), tipo de cambio, base imponible, IGV y neto. Todos los montos se registran en soles, convertidos con el tipo de cambio compra de la fecha de emisión.
  - **Detalle**: al navegar un registro de la lista de asientos generados se muestra el detalle del asiento (líneas contables asociadas, en débito/crédito).
  - Las líneas del detalle se generan automáticamente mapeando cada producto del catálogo a su cuenta contable asociada (y el IGV/proveedor a sus cuentas predefinidas); el asistente contable puede ajustar manualmente la cuenta de una línea antes de confirmar el asiento.
  - El plan contable y el catálogo de productos se cargan como datos maestros iniciales en la base de datos del software; en esta versión no hay pantalla de mantenimiento (alta/edición) para ellos.
  - El asiento generado puede editarse o anularse después de creado (ej. al corregir un proveedor P00000 por el proveedor real); toda corrección queda trazable (quién, cuándo, valor anterior vs. nuevo).
  - El asiento generado se guarda únicamente en la base de datos asignada al software; no hay integración ni migración hacia ningún sistema de gestión contable externo.
- Manejo de moneda extranjera: se registra el monto y la moneda original de la factura; el sistema mantiene un registro diario del tipo de cambio (compra y venta); para efectos de conversión/reporte se usa el **tipo de cambio compra correspondiente a la fecha de emisión de la factura**.
- El tipo de cambio se extrae de la web de la sbs, si no existe el tipo de cambio de la fecha de emisión del documento se registra en 0.00 y se coloca la observación que falta el registro del tipo de cambio. 
- Si el proveedor existe, se registra con el nombre del proveedor. De no existir se elige el proveedor **P00000** (Varios). Se debe de mostrar un mensaje que falta registrar al proveedor.
- Detección de facturas duplicadas según el criterio: **ruc del proveedor + tipo de comprobante + número de comprobante**.
- Creación automática de una carpeta en Google Drive por factura validada, con la factura y los medios probatorios correspondientes.
- Manejo de fallas de conexión con Gmail/Drive: reintento automático hasta 3 veces antes de marcar la operación como error.
- Notificación automática de error (por correo y/o bot de Telegram) cuando el proceso falla, incluyendo agotar los 3 reintentos de conexión.
- Al confirmar (validar) el registro de una factura, la información se carga automáticamente desde la base de datos del software hacia una hoja de cálculo de Google Drive (Google Sheets), que sirve como fuente de datos para Looker Studio.
- Plantilla base de dashboard en Looker Studio conectada a esa hoja de cálculo, mostrando gastos por proveedor, periodo, moneda y estado.
- Dimensionamiento del sistema para un volumen de 10 a 50 facturas diarias.
- Autenticación con inicio de sesión: el usuario se registra con credenciales almacenadas en la base de datos del software.
- Conservación indefinida de las facturas y medios probatorios procesados (sin política de eliminación automática).

## No alcance (qué explícitamente no incluye esta versión)

- No incluye automatizar la impresión física de la factura; ese paso sigue siendo manual fuera del software.
- No incluye integración ni migración de datos con ningún sistema de gestión contable externo; el asiento contable se genera y almacena exclusivamente en la base de datos asignada al software.
- No incluye conciliación bancaria ni gestión de pagos a proveedores.
- No incluye separación de roles/permisos entre usuarios (ej. Cargador vs. Validador vs. Supervisor); un mismo usuario revisa y valida cada factura.
- No incluye soporte para múltiples empresas o multi-tenant.

## Criterios de éxito

- El 100% de los correos con factura de compra recibidos en la cuenta configurada quedan visibles en el software dentro de un máximo de 15 minutos desde su llegada.
- El tiempo de validación por factura (revisión + confirmación) es inferior a 5 minutos, frente a los 5-15 minutos que toma hoy el proceso manual completo.
- El 100% de las facturas marcadas como "Validadas" cuentan con su carpeta en Drive creada automáticamente, con la factura y los medios probatorios correctamente adjuntos.
- El 100% de las facturas marcadas como "Validadas" generan un asiento contable (cabecera + detalle) consultable en el software, sin necesidad de registro manual adicional dentro del sistema.
- Ante una factura duplicada (mismo ruc del proveedor + tipo de comprobante + número), el sistema la detecta y alerta antes de permitir un nuevo registro, sin excepciones.
- Ante fallas de conexión con Gmail/Drive, el sistema reintenta hasta 3 veces; si el error persiste, la notificación (correo o Telegram) se envía en un máximo de 5 minutos, con una tasa de entrega igual o mayor al 99%.
- El dashboard de Looker Studio refleja los gastos registrados con un desfase máximo de 24 horas respecto al registro en el software.
- La extracción automática por OCR/IA logra un mínimo del 90% de campos correctamente extraídos (tipo de comprobante, número, proveedor, monto, moneda, fecha de emisión).

## Casos borde a contemplar

- Correo de factura que llega sin OC o sin medios probatorios adjuntos.
- El OCR/IA no logra extraer uno o más campos clave: la factura queda en "Pendiente de validación" con esos campos vacíos, resaltados para carga manual.
- El proveedor de la factura no existe en el sistema: se registra con el proveedor genérico **P00000 (Varios)** y se muestra un mensaje indicando que falta registrar al proveedor real.
- Factura duplicada (mismo ruc + tipo de comprobante + número ya registrados): el sistema debe alertar y bloquear el registro antes de continuar.
- Adjunto corrupto, protegido con contraseña, o en un formato no soportado.
- Falla de conexión con la API de Gmail o de Google Drive: el sistema reintenta hasta 3 veces antes de marcar error y notificar.
- Falla al enviar la propia notificación de error por Telegram (ej. bot caído): el sistema envía la alerta por correo electrónico como canal de respaldo.
- Llega una factura en moneda extranjera pero el tipo de cambio compra del día de emisión no existe en la web de la SBS: se registra en 0.00 y se coloca la observación de que falta el registro del tipo de cambio.
- Correo mal etiquetado, en spam, o que no corresponde a una factura de compra real (falso positivo de detección).
- Un mismo correo con múltiples facturas adjuntas.
- Corrección manual de un dato mal extraído por el asistente contable, que debe quedar trazable (quién corrigió, cuándo, valor original vs. valor corregido).
- El asiento contable se genera con el proveedor genérico **P00000 (Varios)** por no detección automática; al corregir el proveedor después, el asiento ya generado debe editarse y el cambio quedar trazable (quién, cuándo, valor anterior vs. nuevo).
- Inconsistencia entre cabecera y detalle del asiento generado (ej. base imponible + IGV no cuadra con el neto) que deba quedar señalada antes de dar el asiento por consistente.
- Un producto de la factura no existe en el catálogo (o no tiene cuenta contable mapeada): la línea de detalle queda sin cuenta asignada, pendiente de que el asistente contable la complete manualmente antes de confirmar el asiento.
- Corrección manual de la cuenta contable de una línea del detalle (ajuste sobre el mapeo automático) o anulación de un asiento ya generado, ambas trazables.

## Supuestos y riesgos abiertos

- Confirmado: el asiento contable completo (cabecera + detalle) se genera y almacena en la base de datos asignada al software; no hay integración ni migración con ningún sistema de gestión contable externo.
- Confirmado: el asiento contable se estructura en cabecera (número de comprobante, origen del libro —por defecto **02 Compras**—, proveedor, glosa, fecha contable, tipo de cambio, base imponible, IGV, neto) y detalle navegable con las líneas contables.
- Confirmado: el plan contable y el catálogo de productos se cargan como datos maestros iniciales dentro de la base de datos del software; en esta versión no hay mantenimiento (alta/edición) desde la interfaz.
- Confirmado: el detalle del asiento (líneas débito/crédito) se genera automáticamente mapeando cada producto del catálogo a su cuenta contable, con IGV y proveedor en cuentas predefinidas; el asistente contable puede ajustar manualmente la cuenta de una línea antes de confirmar.
- Confirmado: el número de comprobante de la cabecera del asiento es el mismo número extraído de la factura (no un correlativo propio del software).
- Confirmado: el asiento se registra siempre en soles, convertido con el tipo de cambio compra de la fecha de emisión, independientemente de la moneda original de la factura.
- Confirmado: el asiento generado es editable/anulable después de creado, con trazabilidad de quién corrigió, cuándo y el valor anterior vs. el nuevo.
- Confirmado: la extracción de datos de la factura es automática vía OCR/IA, con estado "Pendiente de validación" hasta la confirmación del rol Validador.
- Confirmado: volumen esperado de 10 a 50 facturas diarias.
- Confirmado: las notificaciones de error se envían por correo y/o bot de Telegram (no WhatsApp, a diferencia del planteamiento inicial).
- Confirmado: un mismo usuario revisa y valida cada factura; no hay separación de roles/permisos en esta versión.
- Confirmado: para moneda extranjera se registra el monto original y se usa el tipo de cambio compra de la fecha de emisión, extraído automáticamente de la web de la SBS.
- Confirmado: duplicados se detectan por ruc proveedor + tipo de comprobante + número.
- Confirmado: ante fallas de conexión con Gmail/Drive, se reintenta automáticamente 3 veces antes de notificar.
- Confirmado: si un proveedor no existe en el sistema, se usa el proveedor genérico P00000 (Varios) y se alerta que falta registrarlo.
- Confirmado: meta de precisión de la extracción OCR/IA es ≥90% de campos correctos.
- Confirmado: si falla el envío por Telegram, el sistema envía la alerta por correo electrónico como respaldo.
- Confirmado: acceso a Gmail API, Google Drive API y cuenta de Google Workspace compatible con Looker Studio ya está disponible.
- Pendiente: el bot de Telegram (token, chat/canal destino, administrador) se configurará conforme avance el proyecto, antes de implementar la integración de notificaciones.
- Confirmado: la impresión de la factura se realiza manualmente, de ser necesario, fuera del sistema.
- Confirmado: el sistema requiere inicio de sesión, con credenciales gestionadas en la base de datos del software.
- Confirmado: el dashboard de Looker Studio es de uso personal (solo el usuario), no se comparte con jefatura ni otras áreas por ahora.
- Confirmado: las facturas y medios probatorios se conservan indefinidamente, sin política de retención/eliminación.
- Confirmado: no existe una fecha límite fija para tener el sistema funcionando; se puede avanzar por etapas.
- Confirmado: la sincronización hacia Looker Studio se resuelve cargando automáticamente una hoja de cálculo de Google Sheets en Drive a partir de lo registrado en la base de datos del software; esa hoja es la fuente de datos del dashboard.
- Confirmado: la carga hacia la hoja de cálculo se dispara justo después de que el asistente contable confirma (valida) el registro de la factura, no por un intervalo programado.

---

## Reversiones respecto de la versión original

Esta sección se añade tras el diseño técnico. **El PRD sigue siendo el documento contractual**, y en
cinco puntos el diseño decidió lo contrario de lo que este documento pedía. Cada reversión tiene su
ADR con contexto, alternativas y costos.

Se registran aquí, y no se reescribe el cuerpo del documento, para que quede el rastro de qué se
decidió primero, qué cambió y con qué fundamento. Sin ese rastro, en una revisión formal nadie puede
distinguir un cambio deliberado de un error de transcripción.

| # | El PRD dice | Vigente | Respaldo |
|---|---|---|---|
| 1 | Tipo de cambio **compra** de la fecha de emisión | Tipo de cambio **venta** | [ADR 0018](adrs/0018-tipo-de-cambio-aplicable.md) |
| 2 | Sin tipo de cambio del día se registra **0.00** con observación | La factura **no se abre para edición**; `409`. La salida es la carga manual | [ADR 0018](adrs/0018-tipo-de-cambio-aplicable.md) |
| 3 | El asiento **se genera** con `P00000 (Varios)` y se corrige después | `409` al validar. El proveedor se registra en el sistema externo antes | [ADR 0006](adrs/0006-asiento-contable-como-entidad-propia.md) |
| 4 | Detalle generado **mapeando cada producto** del catálogo a su cuenta | El **motivo de compra** determina la cuenta. `FacturaDetalle` y `Producto` eliminados | [ADR 0011](adrs/0011-motivo-de-compra-y-sugerencia-de-cuenta.md) |
| 5 | Reintento **3 veces** para todo fallo | Tres clases de error con política propia, más una clase terminal | [ADR 0010](adrs/0010-politica-de-reintentos-y-clasificacion-de-errores.md) |

### El fundamento de cada una, en una línea

1. **Una compra genera un pasivo** en moneda extranjera, y los pasivos se convierten a tipo de cambio
   venta. El compra corresponde a los activos. Es la reversión de mayor impacto económico: afecta a
   **todo** asiento en moneda extranjera.
2. Un asiento con tipo de cambio `0.00` **cuadra y no significa nada**. El caso real que motivaba la
   regla —que la SBS no publique— tiene salida propia: cargar la fila a mano y seguir al instante.
3. `421211` es una cuenta por pagar **por proveedor**. Un saldo acumulado contra "Varios" **no se
   puede conciliar ni pagar**, porque no se sabe a quién se le debe.
4. **Nada alimentaba esas tablas**: ni el XML, ni el OCR, ni el prototipo contemplaban líneas de
   producto, y la compañía no lleva catálogo de productos.
5. Una **cuota de API agotada** no se resuelve reintentando tres veces en segundos, y un **adjunto
   corrupto** no se arregla nunca: gastar tres intentos en él retrasa el aviso al usuario.

### Qué queda pendiente de estas cinco

Los puntos **1 y 2** están en la lista de ratificación formal por un contador de `REGLAS.md` §12. Si
el criterio correcto resultara ser el del PRD, la corrección **no es un ajuste de código**: es
reprocesar todo asiento en moneda extranjera ya confirmado.

El punto **3** se apoya en una premisa que ningún documento del proyecto establece: que el asistente
contable **pueda dar de alta un proveedor en el sistema contable, y que sea inmediato**. Está
declarada como premisa a verificar en `TECH-DESIGN.md`. Si resulta falsa, la factura queda bloqueada
por tiempo indefinido y hace falta un indicador propio y un criterio de aceptación para esa espera.

### Un criterio de éxito que cambia de lectura

> *"El 100% de las facturas marcadas como Validadas generan un asiento contable."*

Sigue siendo cierto y se mide igual, pero el enunciado exacto es ahora **"exactamente un asiento
vigente"**: una factura puede acumular asientos anulados a lo largo del tiempo, y todos permanecen en
el libro con su número. Anular ya no es reversible.
