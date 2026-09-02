# Estructura del proyecto — Gestor de Facturas de Compra

Listado de archivos y carpetas de la raíz del repositorio con una descripción breve. Para
`SmartNet/` el detalle llega hasta el tercer nivel.

---

## Raíz del repositorio

| Estructura                     | Descripción                                                                                              |
| ------------------------------ | -------------------------------------------------------------------------------------------------------- |
| /                              |                                                                                                          |
| ├─ 📄 `BACKLOG`                | Despiece del PRD + TECH-DESIGN + ADRs + `REGLAS.md` en 26 ítems implementables; cada uno es un ciclo SDD |
| ├─ 📄 `CLAUDE`                 | Instrucciones del proyecto para el asistente: rol, stack, reglas normativas.                             |
| ├─ 📄 `CONVENTIONS`            | Nombres, idioma del código y estilo. Regla clave: el dominio contable se nombra en español.              |
| ├─ 📄 `DECISIONES REVISION`    | Registro incremental de las decisiones tomadas para cerrar los hallazgos de REVISION-ADVERSARIAL         |
| ├─ 📄 `DEPLOY PLAN`            |                                                                                                          |
| ├─ 📄 `DESIGN`                 | Paleta y tokens del tema visua                                                                           |
| ├─ 📄 `DESIGN BRIEF`           | Brief de diseño de las pantallas, basado en el PRD; entrada para generar prototipos.                     |
| ├─ 📄 `DEUDA TÉCNICA`          |                                                                                                          |
| ├─ 📄 `HARNESS`                | Índice de *harnesses* activos en el repo (flujos chicos de un solo propósito).                           |
| ├─ 📄 `PRD`                    | Product Requirements Document: qué resuelve el sistema y para quién.                                     |
| ├─ 📄 `REVISION ADVERSARIAL`   | Segunda revisión adversarial: TECH-DESIGN v3, ADRs 0001–0017 y PRD                                       |
| ├─ 📄 `SECURITY REPORT`        | Pase de seguridad (2026-08-30): hallazgos priorizados y triaje. Sin CRITICAL/HIGH.                       |
| ├─ 📄 `SPRINT`                 | Tablero de seguimiento del backlog: un ítem por sección, fases y evidencia de verificación.              |
| ├─ 📄 `TECH DESIGN`            | Technical Design Document v4, incorpora las decisiones de la segunda revisión adversarial.               |
| ├─ 📁 `ADRS`                   | Architecture Decision Records vigentes (0001–0019, formato MADR).                                        |
| ├─ 📁 `DEPLOY`                 |                                                                                                          |
| ├─ 📁 `DOCUMENTOS DEL NEGOCIO` | Información del core de las entidades del negocio                                                        |
| ├─ 📁 `HANDOFF`                | Entregable de diseño: `DESIGN_BRIEF.md`, `Gestor de Facturas.dc.html` (canvas), `support.js`.            |
| ├─ 📁 `SMARTNET`               |                                                                                                          |
| ......├─ 📁 `SMARTNETAPI`      | API y dominio en .NET.                                                                                   |
| ......├─ 📁 `SMARTNETBD`       | Esquema SQL versionado y fixtures de datos.                                                              |
| ......├─ 📁 `SMARTNETWEB`      | SPA en Angular con signals, sin librería de estado.                                                      |
| ......├─ 📁 `SMARTNETWORKER`   | Worker de ingesta y publicación en Python.                                                               |

### `SmartNet/SmartNetApi/`

| Nivel 2            | Nivel 3                                                                                    | Descripción                                                                                                                                   |
| ------------------ | ------------------------------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------- |
|                    | `SmartNet.sln`                                                                             | Solución con todos los proyectos .NET.                                                                                                        |
|                    | `CLAUDE.md`                                                                                | Instrucciones del asistente para la API.                                                                                                      |
| `admin/`           | `SmartNet.Admin/`, `SmartNet.Admin.Tests/`                                                 | CLI de administración: crear usuario, restablecer clave, purgar sesiones.                                                                     |
| `api/`             | `SmartNet.Api/`, `SmartNet.Api.Tests/`                                                     | Host HTTP mínimo: endpoints `/api/*`, cookie de sesión, `PATCH` con `If-Match`.                                                               |
| `auth/`            | `SmartNet.Auth.Core/`, `.Tests/`, `SmartNet.Auth.Infrastructure/`, `.Tests/`               | Identidad y sesión: dominio puro (bloqueo por intentos, códec PHC) + adaptadores Argon2id/SQL.                                                |
| `catalogos/`       | `SmartNet.Catalogos.Core/`, `.Tests/`, `SmartNet.Catalogos.Infrastructure/`, `.Tests/`     | Catálogos externos y satélites propios + `ResolverCandidatas` (`REGLAS.md` §3).                                                               |
| `contable/`        | `SmartNet.Contable.Core/`, `.Tests/`                                                       | Núcleo contable puro: generación del asiento, bloques `PRINCIPAL`/`DESTINO`, invariantes §7, conversión de moneda. Sin BD ni HTTP (ADR 0019). |
| `db/`              | `runner/`, `test-bootstrap/`                                                               | Runner DbUp del esquema versionado y arnés de bases de prueba desechables.                                                                    |
| `exportacion/`     | `SmartNet.Exportacion.Infrastructure/`, `.Tests/`                                          | Exportador XLSX (`ExportadorXlsx`).                                                                                                           |
| `facturacion/`     | `SmartNet.Facturacion.Core/`, `.Tests/`, `SmartNet.Facturacion.Infrastructure/`, `.Tests/` | Facturas y asientos: proyección contable, contrato de escritura de campos editables, `SqlUnidadDeTrabajo`, auditoría de corrección.           |
| `inbox/`           | `SmartNet.Inbox.Core/`, `.Tests/`, `SmartNet.Inbox.Infrastructure/`, `.Tests/`             | Inbox y bandeja: consumo del inbox, promoción a factura, vista lógica combinada, chip de estado derivado, filtros.                            |
| `sugerencia/`      | `SmartNet.Sugerencia.Core/`, `.Tests/`                                                     | Sugerencia de cuenta: cascada por frecuencia, desempate determinista (`REGLAS.md` §3).                                                        |
| `tipos-de-cambio/` | `SmartNet.TiposCambio.Core/`, `.Tests/`, `SmartNet.TiposCambio.Infrastructure/`, `.Tests/` | Tipo de cambio: selección SBS>MANUAL en dominio puro, `SqlTipoCambioRepository`.                                                              |

### ### `SmartNet/SmartNetWeb/`

| Nivel 2         | Nivel 3                                                               | Descripción                                                                                                                                     |
| --------------- | --------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------- |
|                 | `angular.json`, `package.json`, `package-lock.json`, `tsconfig*.json` | Configuración de Angular, dependencias y compilación TS.                                                                                        |
|                 | `proxy.conf.json`                                                     | Proxy de desarrollo hacia la API.                                                                                                               |
|                 | `CLAUDE.md`, `README.md`                                              | Instrucciones y guía de la SPA.                                                                                                                 |
| `src/`          | `app/`                                                                | Código de la aplicación: *features* por dominio (inbox, detalle, catálogos, configuración), *data-access*, *ui*, guard de auth e interceptores. |
| `src/`          | `main.ts`, `index.html`, `styles.css`                                 | *Bootstrap* de la aplicación y estilos globales (tokens del tema).                                                                              |
| `public/`       | `favicon.ico`                                                         | Activos estáticos servidos tal cual.                                                                                                            |
| `dist/`         | (compilado)                                                           | Salida del *build* de producción.                                                                                                               |
| `node_modules/` | (dependencias)                                                        | Paquetes npm instalados.                                                                                                                        |

### `SmartNet/SmartNetWorker/`

| Nivel 2  | Nivel 3                              | Descripción                                                                                                                                                                 |
| -------- | ------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
|          | `pyproject.toml`                     | Definición del paquete Python y dependencias.                                                                                                                               |
|          | `CLAUDE.md`, `README.md`             | Instrucciones y guía del worker.                                                                                                                                            |
| `src/`   | `smartnet_worker/`                   | Módulos del worker: ingesta Gmail, extracción XML/OCR, asociación, *scraper* SBS, repos SQL bajo `usr_worker`, consumidores de CommandQueue/Outbox, clientes Telegram/SMTP. |
| `src/`   | `smartnet_worker.egg-info/`          | Metadata de instalación editable.                                                                                                                                           |
| `tests/` | `unit/`, `integration/`, `fixtures/` | Pruebas unitarias, de integración (contra `pyodbc` + login efímero) y fixtures.                                                                                             |
