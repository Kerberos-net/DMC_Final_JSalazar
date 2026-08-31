---
name: integration-spa-api
description: >-
  Verifica que la SPA (SmartNetWeb) y la API (SmartNetApi) funcionan juntas sobre
  el contrato HTTP /api/* real — API levantada con WebApplicationFactory, cookie
  de sesion real y base fact_test_<guid> desechable con el esquema versionado
  real, sin mockear el backend ni los repositorios. Use when the user wants to
  check the SPA and API integrate correctly, run the SPA<->API integration
  harness, or asks whether a /api/* flow still works end to end.
---

# Harness de integracion SPA <-> API

## Proposito (uno solo)

Comprobar que **SmartNetWeb y SmartNetApi funcionan juntos** sobre el contrato
HTTP `/api/*`. No cubre el worker, ni el volumen de adjuntos, ni el navegador
real: solo la costura HTTP entre la SPA y la API.

## Doctrina: que es real y que puede ser doble

El ecosistema **no usa contenedores** y `SmartNetApi` y `SmartNetWorker` no se
llaman por HTTP (ver `SmartNet/CLAUDE.md`). Para esta costura:

### Siempre real (nunca lo sustituyas)

| Componente | Como | Por que |
|---|---|---|
| Host de la API | `WebApplicationFactory<Program>` (`SmartNetApiFactory`) | Program.cs entero: DI, middleware, auth, endpoints. |
| Base de datos | `TestDatabaseFixture.CreateAsync()` → `fact_test_<guid>` + `RunMigrations()` | Esquema versionado real aplicado por `SmartNet.Db.Runner` (ADR 0016). Nunca un repositorio en memoria. |
| Autenticacion | Login real contra `/api/sesion`, cookie `__Host-session`, `fact.Sesion` | La sesion server-side es el contrato (ADR 0007). Nunca inyectar un principal falso. |
| Hash de clave | `Argon2idPasswordHasher` (el de produccion) | `SesionEndpointsTestBase` ya lo hace: "never a shortcut". |
| Logica de dominio | La real, transitiva via `facturacion` | Es el punto de la prueba. |
| Data Protection key ring | Bytes reales en dir temporal por instancia | Sin esto la cookie no sobrevive; es parte del flujo. |

### Dobles permitidos (solo en el borde externo de la API, nunca en el camino SPA->API)

| Doble | Cuando | Mecanismo existente |
|---|---|---|
| `TimeProvider` → `FakeTimeProvider` | Flujos que dependen del reloj: escalada de lockout, expiracion/sliding de sesion | Parametro de `SmartNetApiFactory` |
| `IPasswordHasher` decorador contador | Solo para asertar numero de llamadas | `CountingPasswordHasher` |
| `SMARTNET_API_STORAGE_ROOT` → dir temporal | Descarga de adjuntos | Parametro `storageRoot` de `SmartNetApiFactory` |

### Prohibido (esto seria "probar solo mocks")

- Mockear el backend HTTP del lado SPA (`HttpTestingController`, interceptores
  stub) y llamar a eso "integracion".
- Repositorios `fact.*` en memoria o fakes en la API.
- Saltear el login / inyectar cookie o usuario falso.
- Reimplementar el split del esquema en vez de `RunMigrations()`.
- Correr contra `BDSmartNet` o cualquier base que no sea `fact_test_<guid>`.
- Servicios externos reales (Gmail, SBS, SMTP, Telegram) aunque haya credenciales
  en el entorno — ningun flujo en alcance los toca.

## Flujos en alcance

1. **Sesion / login / 401** — precondicion de todo lo demas.
   - `POST /api/sesion` con credenciales correctas → `204`, `Set-Cookie:
     __Host-session=` con `HttpOnly; Secure; SameSite=Lax`, fila en `fact.Sesion`,
     `IntentosFallidos` vuelve a 0.
   - Clave incorrecta / usuario inexistente → status de error, sin cookie.
   - Peticion a endpoint protegido sin cookie → `401` con status plano (nunca
     redirect a login).
   - `DELETE /api/sesion` (logout) → la cookie deja de servir para el siguiente
     request (invalidacion server-side).
2. **Bandeja + detalle de factura** — con sesion valida:
   - `GET /api/bandeja` con y sin filtros → `200`, forma de payload que consume la
     SPA.
   - `GET /api/factura/{id}` y `GET /api/asiento/{id}` → `200`, **ETag en el
     header** (nunca en el cuerpo).
   - Concurrencia optimista: dos clientes, `If-Match` desactualizado → `412`
     (patron de `ConcurrenciaDosClientesTests`).
   - `GET` de id inexistente → `404`.
3. **Consultas de catalogos (BACKLOG #22)** — con sesion valida, todo `GET`,
   solo lectura, camelCase, y cada `/exportacion` responde `.xlsx`
   (`application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`) con
   `Content-Disposition: attachment` y nombre de archivo de forma constante (sin
   input del usuario). Sin cookie → `401` con status plano y sin archivo.
   - `GET /api/catalogos/plan-contable` → `200`, `{items:[{cuenta,descripcion,
     nivel,esHojaImputable}]}` sin paginar, orden `cuenta` asc; cliente HTTP:
     `PlanContableService` (`plan-contable.service.ts`, fetch unico).
     `GET /api/catalogos/plan-contable/exportacion?q=` → `.xlsx`.
   - `GET /api/catalogos/proveedores?modo=catalogo` → `200`, envelope
     `PaginaBandeja` `{items,pagina,tamanioPagina,totalRegistros,totalPaginas}`,
     sort servidor `orden`/`direccion`, `tamanio` en {6,10,20,50}; `400` ante
     `modo`/`orden`/`direccion`/`tamanio` desconocido. REGRESION: `modo` ausente o
     `picker` mantiene `{resultados,hayMas}` byte-frozen (#18). Cliente HTTP:
     `CatalogoProveedorService` (`catalogo-proveedor.service.ts`).
     `GET /api/catalogos/proveedores/exportacion?q=&orden=&direccion=` → `.xlsx`.
   - `GET /api/tipos-cambio?desde=&hasta=` (ambos requeridos) → `200`,
     `{items:[{fecha,origen,compra,venta,fechaConsulta}]}`, `origen` como string
     "SBS"/"MANUAL", orden `fecha` luego `origen`; `400` ante rango faltante /
     no parseable / invertido / span > 366d. Cliente HTTP: `TipoCambioService`
     (`tipo-cambio.service.ts`). `GET /api/tipos-cambio/exportacion?desde=&hasta=`
     → `.xlsx`.
4. **Registro de compra (BACKLOG #23)** — con sesion valida, todo `GET`, solo
   lectura, camelCase. Sin cookie en cualquiera de las tres rutas → `401` plano.
   - `GET /api/registro-compra?periodo=YYYY-MM&pagina=&tamanioPagina=` → `200`,
     envelope `{items,pagina,tamanioPagina,totalRegistros,totalPaginas}`. Solo
     filas `fact.Factura.Estado='VALIDADA'` y asiento vigente `<> 'ANULADO'`;
     `totalRegistros` via `COUNT(*) OVER()`. Periodo vacio → `200`, `items:[]`,
     `totalRegistros:0` (nunca `404`). `periodo` mal formado / ausente
     (`2026-13`, `agosto`, `2026-8`) → `400` RFC 9457; `tamanioPagina` fuera de
     {6,10,20,50} → `400`. Cliente HTTP: `RegistroCompraService`
     (`registro-compra/data-access/registro-compra.service.ts`).
   - `GET /api/registro-compra/{asientoId}` → `200`, `{cabecera,lineas[]}`
     (lineas por `orden`); asiento `ANULADO` / de factura no `VALIDADA` /
     inexistente → `404` indistinguible (no es canal lateral). Asiento valido
     sin lineas → `200`, `lineas:[]`. Cliente HTTP:
     `RegistroCompraDetalleService` (memoizado por `asientoId`).
   - `GET /api/registro-compra/export?periodo=YYYY-MM` → `.xlsx`
     (`application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`),
     `Content-Disposition: attachment; filename=registro-compra-YYYY-MM.xlsx`
     (nombre reconstruido de los enteros parseados, ADR 0021 decision 4).
     `periodo=2026-08%0D%0AX` → `400`. El boton "Exportar" reusa
     `catalogos/data-access/descarga-xlsx.ts`.

## Procedimiento de corrida

1. **Contexto obligatorio** — lee antes de correr:
   - `SmartNet/CLAUDE.md` (contratos del ecosistema) y
     `SmartNet/SmartNetApi/CLAUDE.md` (decisiones de la API).
   - `SmartNet/SmartNetApi/api/SmartNet.Api.Tests/`:
     `SmartNetApiFactory.cs`, `SesionEndpointsTestBase.cs`, `BandejaEndpointsTests.cs`,
     `FacturaEndpointsTests.cs`, `AsientoEndpointsTests.cs`, `ConcurrenciaDosClientesTests.cs`,
     `FacturaTestDataHelper.cs`.
   - Del lado SPA, el cliente HTTP real de cada flujo (servicios en
     `SmartNetWeb/src/app/**` que llaman a `/api/bandeja`, `/api/factura`,
     `/api/asiento`, `/api/sesion`) — para confirmar que ruta, headers (ETag,
     If-Match) y forma de payload que la SPA **espera** coinciden con lo que la
     API responde.
2. **Prerrequisito de entorno**: SQL Server local accesible (el fixture crea/borra
   `fact_test_<guid>` contra `master`; override con `SMARTNET_TEST_MASTER_CONNECTION`).
   Si no hay SQL Server, la corrida no puede validar nada real → reporta
   `BLOCKED`, no inventes un PASS.
3. **Ejecuta** los tests HTTP existentes que cubren los flujos en alcance:
   ```
   cd SmartNet/SmartNetApi
   dotnet test api/SmartNet.Api.Tests
   ```
   Acota con `--filter` a las clases de sesion / bandeja / factura / asiento /
   concurrencia cuando solo quieras revisar esos.
4. **Chequeo de contrato SPA↔API** (lo que los tests de la API solos no ven):
   para cada flujo en alcance, compara la expectativa del cliente SPA contra la
   respuesta real observada en el test (ruta, metodo, status, header ETag/If-Match,
   nombres y tipos de campos del payload). Una discrepancia aqui es un FAIL de
   integracion aunque ambos lados pasen sus tests por separado.
5. **Deteccion de sobre-mockeo**: si un flujo en alcance solo esta cubierto por
   un test de componente SPA con backend stubbeado (o no esta cubierto por HTTP
   real en absoluto), marcalo como brecha en el reporte.

## Salida: reporte pass/fail por flujo

Formato fijo, en el chat:

```
# Integracion SPA <-> API — <fecha>

## Resumen
<N> flujos verificados — <P> PASS, <F> FAIL, <B> BLOCKED

## Flujos
### Sesion / login / 401 — PASS | FAIL | BLOCKED
- <sub-caso>: <resultado> — <evidencia: status/headers observados, estado de DB>
...

### Bandeja + detalle de factura — PASS | FAIL | BLOCKED
- ...

### Consultas de catalogos (BACKLOG #22) — PASS | FAIL | BLOCKED
- plan-contable / proveedores?modo=catalogo / tipos-cambio (+ /exportacion): <resultado> — <evidencia>

## Chequeo de contrato SPA↔API
- <flujo>: <coincide | discrepancia concreta entre lo que la SPA espera y lo que la API responde>

## Brechas / sobre-mockeo
- <flujo sin cobertura HTTP real, o cubierto solo con backend stub>

## Dobles usados vs componentes reales
- Reales: WebApplicationFactory<Program>, fact_test_<guid> (esquema real), cookie de sesion real, Argon2id real
- Dobles: <FakeTimeProvider si aplico / dir temporal de storage / ninguno>
```

## Guardrails (duros)

- **No introducir dependencias nuevas** (Playwright, Testcontainers, WireMock,
  paquetes npm/NuGet). Usar solo la infra que ya existe (`SmartNetApiFactory`,
  `TestDatabaseFixture`, xUnit, `HttpClient`). Si el flujo genuinamente no se
  puede verificar sin algo nuevo, decilo en el reporte y **para** — no lo agregues.
- **No modificar tests ni codigo existente sin aprobacion.** Si un flujo falla,
  el harness reporta la causa y **propone** el arreglo; el usuario decide.
- Escribir tests nuevos de integracion está permitido solo si el usuario lo pide
  explicitamente en esa corrida; por defecto el harness solo ejecuta y reporta.
