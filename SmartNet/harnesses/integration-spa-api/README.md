# Harness: integracion SPA <-> API

Comprueba que **SmartNetWeb y SmartNetApi funcionan juntos** sobre el contrato
HTTP `/api/*`, usando componentes reales siempre que sea razonable y sin mocks
que terminen probando solo mocks.

Un solo proposito: la costura HTTP SPA <-> API. No cubre el worker, el volumen de
adjuntos ni el navegador real.

## Piezas

| Pieza | Archivo | Para que |
|---|---|---|
| Skill `integration-spa-api` | `SKILL.md` | Doctrina (que es real vs doble), flujos en alcance (sesion/login/401, bandeja+detalle), procedimiento de corrida y formato de reporte pass/fail. El agente la corre bajo pedido. |

Sin hook (no hay disparo automatico), sin regla en `CLAUDE.md`, sin sub-agente.
Es cross-repo, por eso el bundle vive en `SmartNet/harnesses/` y no dentro de un
repo hijo.

## Doctrina en una linea

API real (`WebApplicationFactory<Program>`) + base `fact_test_<guid>` desechable
con el esquema versionado real + cookie de sesion real + Argon2id real. Unicos
dobles permitidos, y solo en el borde externo de la API: `FakeTimeProvider`,
decorador contador de `IPasswordHasher`, dir temporal de storage. Prohibido:
mockear el backend del lado SPA, repos en memoria, saltear el login, servicios
externos reales.

## Guardrails

- No introducir dependencias nuevas (Playwright, Testcontainers, WireMock, etc.).
- No modificar tests ni codigo existente sin aprobacion — reporta y propone.

## Activarlo en otra maquina

1. Copia `SKILL.md` a `.claude/skills/integration-spa-api/SKILL.md` del repo (o al
   directorio de skills que uses).
2. No hay hook ni regla que registrar.
3. Prerrequisito de corrida: SQL Server local accesible (el fixture crea/borra
   `fact_test_<guid>` contra `master`; override con `SMARTNET_TEST_MASTER_CONNECTION`)
   y `dotnet` en el PATH.

## Correrlo

`/integration-spa-api`, o pedir "corre el harness de integracion SPA-API".
