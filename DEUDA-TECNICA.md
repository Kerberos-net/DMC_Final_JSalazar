# Deuda técnica — Gestor de Facturas de Compra

Lo que falta programar, derivado de `BACKLOG.md`, `SPRINT.md`, los reportes de verificación SDD y
`SECURITY-REPORT.md`. La columna **Observación** queda vacía para llenado manual.

Estado de referencia (SPRINT.md): 20 de 24 ítems del backlog cerrados. Sin ciclo SDD activo.

---

## 1. Ítems del backlog sin ciclo SDD abierto

| # | Ítem | Depende de | Contexto obligatorio | Observación |
|---|---|---|---|---|
| 10 | **Notas de crédito** — referencia interna y externa, herencia de los cuatro atributos, reparto proporcional, tope acumulado | #8 | ⚠ `REGLAS.md` §5, §7 | |
| 15 | **Publicación a Drive** — empaquetado desde el *payload*, `appProperties` como clave, adjuntos manuales y `DOCUMENTACION_ACTUALIZADA` | #14 | — | |
| 16 | **Publicación a Sheets** — *upsert* por `FacturaId`, columna de secuencia, corrección y anulación reflejadas | #14 | — | |
| 24 | **Conectar la composición del asiento al pipeline** — `ComposicionDeAsiento.Componer` no está cableado al flujo productivo; hoy solo lo llaman tests y varios invariantes §7 pasan de forma vacua porque `BasePEN` nunca se puebla | #12, #19 | ⚠ `REGLAS.md` §5–§10 | |

---

## 2. Follow-ups técnicos abiertos (arrastrados de verificación SDD)

| # | Origen | Qué falta programar | Observación |
|---|---|---|---|
| 2.1 | #19 WARNING-2 | Estrechar la exención de la guarda IGV: hoy aplica a **todo** tipo `07`; la spec la acota a NC `07` con referencia interna. Estrechar el predicado cuando aterrice la referenciación de NC (#10/#11) | |
| 2.2 | #19 WARNING-3 | Test a nivel JSON en `SmartNet.Api.Tests` para `FacturaRespuesta.CamposNoExtraidos` / `Glosa` sobre una respuesta real (hoy solo cobertura transitiva) | |
| 2.3 | #19 WARNING-4 | Exponer `IgvOrig` en `FacturaRespuesta` y sembrar los inputs editables de base/IGV desde `totalOrig − igvOrig`; hoy en moneda extranjera el input muestra magnitud PEN | |
| 2.4 | #19 SUGGESTION-2 | *Smoke* manual de "Guardar avance" en el primer *deploy* con BD sembrada | |
| 2.5 | #4 WARNING-1 | Prueba de integración delgada de `cli_tipo_cambio.ejecutar()` para la ruta de fallo (error de red/parseo/BD → `registrar_fallo`, sin fila de `TipoCambio`) | |
| 2.6 | #4 WARNING-2 | Capturar HTML real de SBS y reemplazar la fixture sintética `tests/fixtures/sbs_tipo_cambio.html`; revisar el parser contra la estructura real antes de producción | |
| 2.7 | #4 WARNING-3 | Validar el *job* `pruebas-de-worker-python` en un entorno GitHub Actions real (hoy solo verificado localmente) | |

---

## 3. Gap funcional detectado

| # | Área | Qué falta programar | Observación |
|---|---|---|---|
| 3.1 | Detalle — adjuntar archivos | `POST /adjuntos` solo registra metadata; no hay *upload* de bytes al almacenamiento, y la SPA no consume el endpoint ni tiene UI para adjuntar. El visor PDF sí está completo | |

---

## 4. Seguridad (`SECURITY-REPORT.md`, 2026-08-30 — sin CRITICAL/HIGH)

| # | ID | Hallazgo (MEDIO) | Observación |
|---|---|---|---|
| 4.1 | SP-01 | El token del bot de Telegram puede escribirse en claro en `fact.EstadoIntegracion.UltimoError` vía el mensaje de una excepción de `requests`, y `GET /api/integraciones/estado` lo expone a la SPA | |
| 4.2 | SP-02 | Llaves de Data Protection sin cifrar en disco: sin `ProtectKeysWithDpapi()`, quien lea el *key ring* puede forjar cookies de sesión | |
| 4.3 | SP-03 | Usuario desactivado (`Activo = 0`) sigue autenticándose: el campo se lee pero nunca se consulta en el login | |
| 4.4 | — | Sin límites de recursos sobre adjuntos de correo en el worker: sin tope de tamaño, sin límite de geometría de PDF, sin *timeout* por documento | |
| 4.5 | — | Defensa en profundidad del visor: `<iframe>` sin `sandbox`, sin CSP; el registro de adjuntos manuales confía en `RutaRelativa` / `MimeType` / `TamanoBytes` del cliente | |
| 4.6 | — | Endurecimientos menores: cabeceras de seguridad, *rate limiting*, *lockfile* del worker, patrones de `.gitignore`, permisos de CI | |

---

## 5. Condiciones de puesta en producción (no bloquean construir)

| # | Tema | Dónde está anotado | Observación |
|---|---|---|---|
| 5.1 | Las tres preguntas de respaldo: modelo de recuperación, cadena de `LOG BACKUP`, RPO efectivo de la instancia compartida | ADR 0014 | |
| 5.2 | Las seis reglas de `REGLAS.md` §12 sin ratificar por un contador (los puntos 1 y 5 afectan a todo asiento en moneda extranjera ya confirmado) | `REGLAS.md` §12 | #24 cablea §5–§7 al flujo productivo (`abrir`/promoción siembran el asiento vía `ComposicionDeAsiento.Componer`) pero **no ratifica ninguna regla**. Punto 1 (TC venta para el asiento) ya se ejecutaba en producción vía la proyección escalar del #19 (`ProyeccionDeImportes.Derivar`); #24 no amplía esa exposición. Punto 5 (la NC hereda el TC de la factura de origen) sigue **inalcanzable** este ciclo: la composición de notas de crédito es non-goal del #24 y `FacturaReferenciaId` nunca se puebla. Sigue siendo una nota, no un gate de ratificación (obs 309, decisión 7). |
| 5.3 | Topología, proxy inverso, TLS y entornos | ADR 0012 | |
| 5.4 | Secretos y agregador de logs (salvo `EstadoIntegracion`, ya en #17) | ADR 0015 | |
| 5.5 | No existe *pipeline* de despliegue en el repositorio | — | |
