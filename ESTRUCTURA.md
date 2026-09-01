# Estructura del proyecto — Gestor de Facturas de Compra

Listado de archivos y carpetas de la raíz del repositorio con una descripción breve. Para
`SmartNet/` el detalle llega hasta el tercer nivel.

---

## Raíz del repositorio

| Archivo / carpeta | Descripción |
|---|---|
| `PRD.md` | Product Requirements Document: qué resuelve el sistema y para quién. |
| `TECH-DESIGN.md` | Technical Design Document v4, incorpora las decisiones de la segunda revisión adversarial. |
| `DESIGN.md` | Paleta y tokens del tema visual ("macOS Ledger Blue"). |
| `DESIGN_BRIEF.md` | Brief de diseño de las pantallas, basado en el PRD; entrada para generar prototipos. |
| `BACKLOG.md` | Despiece del PRD + TECH-DESIGN + ADRs + `REGLAS.md` en 24 ítems implementables; cada uno es un ciclo SDD. |
| `SPRINT.md` | Tablero de seguimiento del backlog: un ítem por sección, fases y evidencia de verificación. |
| `DEUDA-TECNICA.md` | Lo que falta programar (ítems sin ciclo, follow-ups, seguridad, condiciones de producción). |
| `CONVENTIONS.md` | Nombres, idioma del código y estilo. Regla clave: el dominio contable se nombra en español. |
| `CLAUDE.md` | Instrucciones del proyecto para el asistente: rol, stack, reglas normativas. |
| `HARNESS.md` | Índice de *harnesses* activos en el repo (flujos chicos de un solo propósito). |
| `REVISION-ADVERSARIAL.md` | Primera revisión adversarial: TECH-DESIGN y ADRs 0001–0010. |
| `REVISION-ADVERSARIAL_v1.md` | Copia histórica de la primera revisión adversarial. |
| `REVISION-ADVERSARIAL-V2.md` | Segunda revisión adversarial: TECH-DESIGN v3, ADRs 0001–0017 y PRD. |
| `DECISIONES-REVISION.md` | Registro incremental de las decisiones tomadas para cerrar los hallazgos de las revisiones. |
| `SECURITY-REPORT.md` | Pase de seguridad (2026-08-30): hallazgos priorizados y triaje. Sin CRITICAL/HIGH. |
| `Arquitectura SmartNet.png` | Diagrama de arquitectura del sistema. |
| `skills-lock.json` | *Lockfile* de las skills instaladas para el proyecto. |
| `adrs/` | Architecture Decision Records vigentes (0001–0019, formato MADR). |
| `adrs - v1/` | Versión histórica de los ADRs (0001–0010). |
| `adrs - v2/` | Versión histórica de los ADRs (0001–0017). |
| `Documentación del negocio/` | Insumos normativos del negocio (ver detalle abajo). |
| `handoff/` | Entregable de diseño: `DESIGN_BRIEF.md`, `Gestor de Facturas.dc.html` (canvas), `support.js`. |
| `harnesses/` | *Harnesses* de nivel repo. Contiene `lecciones-aprendidas/`. |
| `openspec/` | Persistencia SDD: `config.yaml`, `specs/` (38 specs por capacidad), `changes/` (cambios activos y `archive/`). |

### `Documentación del negocio/`

| Archivo | Descripción |
|---|---|
| `REGLAS.md` | Documento normativo de contabilidad (v2); sus siete ejemplos numéricos son casos de prueba. |
| `Cuentas.xlsx` | Plan de cuentas real de la compañía (907 hojas imputables). |
| `Motivos.xlsx` | Catálogo de motivos de compra. |
| `MOTIVOS-CLASIFICACION.md` | Reclasificación de motivos (23 marcados con `†`) que siembra el script `010`. |
| `Origen.xlsx` | Catálogo de orígenes del libro. |
| `DocumentoIdentidad.xlsx` | Catálogo de tipos de documento de identidad. |
| `Proveedores.xlsx` | Catálogo de proveedores. |
| `PREGUNTAS-CONTABLES.md` | Preguntas contables abiertas planteadas durante el diseño. |

---

## `SmartNet/` — código de la solución

| Nivel 1 | Descripción |
|---|---|
| `SmartNet/README.md` | Guía de arranque de la solución. |
| `SmartNet/CLAUDE.md` | Instrucciones del asistente para la carpeta de código. |
| `SmartNet/SmartNetApi/` | API y dominio en .NET. |
| `SmartNet/SmartNetBD/` | Esquema SQL versionado y fixtures de datos. |
| `SmartNet/SmartNetWeb/` | SPA en Angular con signals, sin librería de estado. |
| `SmartNet/SmartNetWorker/` | Worker de ingesta y publicación en Python. |
| `SmartNet/harnesses/` | *Harnesses* propios de la solución. |

### `SmartNet/SmartNetApi/` (nivel 2 → 3)

| Nivel 2 | Nivel 3 | Descripción |
|---|---|---|
| | `SmartNet.sln` | Solución con todos los proyectos .NET. |
| | `CLAUDE.md` | Instrucciones del asistente para la API. |
| `admin/` | `SmartNet.Admin/`, `SmartNet.Admin.Tests/` | CLI de administración: crear usuario, restablecer clave, purgar sesiones. |
| `api/` | `SmartNet.Api/`, `SmartNet.Api.Tests/` | Host HTTP mínimo: endpoints `/api/*`, cookie de sesión, `PATCH` con `If-Match`. |
| `auth/` | `SmartNet.Auth.Core/`, `.Tests/`, `SmartNet.Auth.Infrastructure/`, `.Tests/` | Identidad y sesión: dominio puro (bloqueo por intentos, códec PHC) + adaptadores Argon2id/SQL. |
| `catalogos/` | `SmartNet.Catalogos.Core/`, `.Tests/`, `SmartNet.Catalogos.Infrastructure/`, `.Tests/` | Catálogos externos y satélites propios + `ResolverCandidatas` (`REGLAS.md` §3). |
| `contable/` | `SmartNet.Contable.Core/`, `.Tests/` | Núcleo contable puro: generación del asiento, bloques `PRINCIPAL`/`DESTINO`, invariantes §7, conversión de moneda. Sin BD ni HTTP (ADR 0019). |
| `db/` | `runner/`, `test-bootstrap/` | Runner DbUp del esquema versionado y arnés de bases de prueba desechables. |
| `exportacion/` | `SmartNet.Exportacion.Infrastructure/`, `.Tests/` | Exportador XLSX (`ExportadorXlsx`). |
| `facturacion/` | `SmartNet.Facturacion.Core/`, `.Tests/`, `SmartNet.Facturacion.Infrastructure/`, `.Tests/` | Facturas y asientos: proyección contable, contrato de escritura de campos editables, `SqlUnidadDeTrabajo`, auditoría de corrección. |
| `inbox/` | `SmartNet.Inbox.Core/`, `.Tests/`, `SmartNet.Inbox.Infrastructure/`, `.Tests/` | Inbox y bandeja: consumo del inbox, promoción a factura, vista lógica combinada, chip de estado derivado, filtros. |
| `sugerencia/` | `SmartNet.Sugerencia.Core/`, `.Tests/` | Sugerencia de cuenta: cascada por frecuencia, desempate determinista (`REGLAS.md` §3). |
| `tipos-de-cambio/` | `SmartNet.TiposCambio.Core/`, `.Tests/`, `SmartNet.TiposCambio.Infrastructure/`, `.Tests/` | Tipo de cambio: selección SBS>MANUAL en dominio puro, `SqlTipoCambioRepository`. |

### `SmartNet/SmartNetBD/` (nivel 2 → 3)

| Nivel 2 | Nivel 3 | Descripción |
|---|---|---|
| | `CLAUDE.md` | Instrucciones del asistente para la base de datos. |
| `schema/` | `001_esquema_fact.sql` … `021_glosa_y_campos_no_extraidos.sql` | 21 scripts SQL versionados del esquema `fact` (estructura, seguridad, ingesta, satélites, negocio, contratos, publicación, permisos, datos base, y migraciones aditivas). |
| `schema/` | `checksums.txt`, `generate-checksums.ps1` | Manifiesto de *checksums* (DbUp no los tiene) y su generador. |
| `schema/` | `rollback/` | Migraciones compensatorias consultivas (`NNN_down.sql`), acotadas a `fact`; el runner nunca las recoge. |
| `fixtures/` | `010_dbo_catalogos_ddl.sql`, `020_dbo_catalogos_datos.sql` | DDL y datos de los catálogos externos `dbo.*` para entornos locales. |
| `fixtures/` | `data/`, `exportar-catalogos.ps1`, `README.md` | CSVs fuente de los catálogos y script de exportación. |

### `SmartNet/SmartNetWeb/` (nivel 2 → 3)

| Nivel 2 | Nivel 3 | Descripción |
|---|---|---|
| | `angular.json`, `package.json`, `package-lock.json`, `tsconfig*.json` | Configuración de Angular, dependencias y compilación TS. |
| | `proxy.conf.json` | Proxy de desarrollo hacia la API. |
| | `CLAUDE.md`, `README.md` | Instrucciones y guía de la SPA. |
| `src/` | `app/` | Código de la aplicación: *features* por dominio (inbox, detalle, catálogos, configuración), *data-access*, *ui*, guard de auth e interceptores. |
| `src/` | `main.ts`, `index.html`, `styles.css` | *Bootstrap* de la aplicación y estilos globales (tokens del tema). |
| `public/` | `favicon.ico` | Activos estáticos servidos tal cual. |
| `dist/` | (compilado) | Salida del *build* de producción. |
| `node_modules/` | (dependencias) | Paquetes npm instalados. |

### `SmartNet/SmartNetWorker/` (nivel 2 → 3)

| Nivel 2 | Nivel 3 | Descripción |
|---|---|---|
| | `pyproject.toml` | Definición del paquete Python y dependencias. |
| | `CLAUDE.md`, `README.md` | Instrucciones y guía del worker. |
| `src/` | `smartnet_worker/` | Módulos del worker: ingesta Gmail, extracción XML/OCR, asociación, *scraper* SBS, repos SQL bajo `usr_worker`, consumidores de CommandQueue/Outbox, clientes Telegram/SMTP. |
| `src/` | `smartnet_worker.egg-info/` | Metadata de instalación editable. |
| `tests/` | `unit/`, `integration/`, `fixtures/` | Pruebas unitarias, de integración (contra `pyodbc` + login efímero) y fixtures. |

### `SmartNet/harnesses/` (nivel 2 → 3)

| Nivel 2 | Nivel 3 | Descripción |
|---|---|---|
| `integration-spa-api/` | `SKILL.md`, `README.md` | Harness de integración SPA↔API sobre el contrato HTTP `/api/*` real (API con `WebApplicationFactory`, cookie real, base `fact_test_<guid>` desechable). |
