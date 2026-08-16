# Brief de diseño — Gestor de Facturas de Compra

> Texto de entrada para generar prototipos de las pantallas. Basado en `PRD.md` (v1, confirmado). Complementa, no reemplaza, la lectura del PRD completo.

## Contexto del producto

Software web de uso interno para un **único usuario** (asistente contable) que gestiona entre **10 y 50 facturas de compra al día**. Reemplaza un proceso manual disperso entre Gmail, impresión física, sistema contable externo y Drive. El sistema detecta facturas por Gmail, extrae sus datos por OCR/IA, y deja el registro en estado "Pendiente de validación" hasta que el mismo usuario lo revisa, corrige si hace falta, y lo marca como "Validada" — lo cual dispara el archivado en Drive, la generación del asiento contable (cabecera + detalle, almacenado íntegramente en la base de datos del software, sin integración ni migración con ningún sistema externo) y la sincronización hacia el dashboard de gastos.

Es una herramienta de trabajo diario, no un producto de cara al público: prioriza velocidad de escaneo, densidad de información correcta y confianza en los datos, por encima de cualquier tratamiento vistoso.

## Usuario y contexto de uso

- Una sola persona, sin roles ni permisos diferenciados.
- Usa el sistema en jornada laboral, en escritorio, revisando facturas en lotes (no una por una de forma aislada).
- El flujo tiene dos momentos: (1) mirar qué llegó y qué necesita atención, (2) confirmar o corregir datos de una factura puntual.
- Debe poder detectar de un vistazo: qué está pendiente, qué tiene error, qué está duplicado o con proveedor sin registrar.

## Pantallas a prototipar

### 1. Inicio de sesión
- Usuario y contraseña (credenciales validadas contra SQL Server).
- Mensaje de error claro ante credenciales inválidas.

### 2. Bandeja principal / Dashboard de estado
Vista por defecto al ingresar. Debe responder "¿qué necesito atender hoy?".
- Lista o tabla de facturas con columnas: proveedor, tipo de comprobante, número, monto, moneda, fecha de emisión, estado.
- Estados a distinguir visualmente (chip o etiqueta de color, no solo texto): **Pendiente de validación**, **Validada**, **Error** (fallo de conexión agotó los 3 reintentos), **Alerta** (proveedor no encontrado / posible duplicado).
- Filtros mínimos: por estado, por rango de fechas, por proveedor.
- Contador o resumen rápido: cuántas pendientes, cuántas con error/alerta.
- Cada fila lleva a la pantalla de detalle/validación.

### 3. Detalle y validación de factura
La pantalla central del producto — aquí se pasa la mayor parte del tiempo.
- Vista lado a lado: imagen/PDF de la factura escaneada a la izquierda, formulario de datos extraídos a la derecha (patrón "documento + formulario", para poder verificar visualmente cada campo contra el original).
- Campos editables: tipo de comprobante (01 Factura / 03 Boleta / 07 Nota de Crédito), número, proveedor, monto, moneda, fecha de emisión, tipo de cambio compra aplicado.
- Campos que el OCR/IA no logró extraer: resaltados visualmente (no solo vacíos) para forzar la atención del usuario.
- Indicador de tipo de cambio: si se tomó automáticamente de la SBS, mostrarlo con su fecha; si no había dato disponible, mostrar 0.00 con la observación de que falta el registro.
- Indicador de proveedor: si el proveedor no existe, mostrar que se asignó **P00000 (Varios)** con un aviso explícito para corregirlo más adelante.
- Indicador de posible duplicado (mismo RUC + tipo de comprobante + número): alerta visible antes de permitir validar.
- Adjuntos secundarios visibles y accesibles: orden de compra, medios probatorios.
- Historial de corrección trazable: qué campo se corrigió, valor original vs. corregido, cuándo (puede ser un detalle secundario, tipo tooltip o panel expandible, no protagonismo visual).
- Acción principal: **Validar** (deshabilitada o con confirmación extra si hay alerta de duplicado sin resolver).
- Acción secundaria: guardar avance sin validar todavía.

### 4. Registro de compra (asientos contables)
Vista de consulta de los asientos contables generados automáticamente por el software para las facturas ya validadas — no hay sistema externo ni registro manual involucrado.
- Lista de facturas validadas en formato **cabecera**: número de comprobante, origen del libro (por defecto 02 Compras), proveedor, glosa, tipo de cambio, base imponible, IGV, neto y estado del asiento.
- Al navegar/abrir un registro se muestra el **detalle**: líneas contables (cuenta, débito/crédito) generadas automáticamente por mapeo del catálogo de productos al plan contable, con opción de ajustar manualmente la cuenta de una línea.
- El asiento generado puede editarse o anularse (y reactivarse) después de creado; toda corrección queda trazable (quién, cuándo, valor anterior vs. nuevo), visible en un historial por asiento.
- Señala visualmente inconsistencias entre cabecera y detalle (ej. base imponible + IGV no cuadra con el neto).

### 5. Panel de errores y notificaciones
- Lista de incidencias: fallas de conexión con Gmail/Drive que agotaron los 3 reintentos, fallas de envío de notificación por Telegram (con caída a correo).
- Cada incidencia muestra: qué falló, cuándo, cuántos reintentos se hicieron, y si la notificación de respaldo (correo) se envió con éxito.
- Debe transmitir urgencia sin sobrecargar de rojo toda la interfaz — reservar el color de alerta real para esto y para las alertas de duplicado/proveedor en el detalle.

### 6. (Opcional, menor prioridad) Configuración
- Estado de conexión con Gmail, Drive y Google Sheets (conectado / con error).
- Configuración del bot de Telegram (token, chat destino) — pantalla simple, puede quedar como placeholder ya que su configuración técnica está pendiente en el proyecto.

## Notas para el diseño visual

- Tratamiento utilitario: herramienta de trabajo diario, no landing page. Prioriza jerarquía tipográfica clara, buen espaciado y una paleta funcional — no necesita un hero ni elementos decorativos grandes.
- El color debe ser semántico y limitado: un estado = un color consistente en toda la app (pendiente, validada, error/alerta). No usar el mismo color semántico como acento decorativo en otro lugar.
- Tipografía y alineación numérica cuidada donde hay montos y fechas en columna (alineación tabular).
- Diseñar para claro y oscuro si el prototipo lo permite; si no, priorizar un tema claro legible en jornada de oficina.
- Evitar iconografía genérica de "IA" (chispas, robots) — el producto no vende IA, la usa como mecanismo interno.

## Fuera de alcance para estos prototipos

- No incluir pantallas de impresión (proceso manual fuera del sistema).
- No incluir gestión de roles/permisos (usuario único).
- No incluir conciliación bancaria ni pagos a proveedores.
- No incluir soporte multi-empresa.
