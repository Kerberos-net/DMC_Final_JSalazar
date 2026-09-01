# SmartNetWeb

SPA de bandeja de documentos, detalle de factura y configuración. Único cliente HTTP de
`SmartNetApi` (prefijo `/api/*`). El contexto del ecosistema (partición de datos, contratos entre
repos, despliegue) está en el `CLAUDE.md` de la carpeta padre — no se repite aquí.

## Stack

| Componente | Detalle |
|---|---|
| Framework | Angular 22 standalone (sin `NgModule`), zoneless implícito |
| Estado | Signals nativas. **Sin NgRx / sin librería de estado** (ADR 0009) |
| HTTP | `provideHttpClient(withFetch(), withInterceptors([httpErrorInterceptor]))` |
| Routing | `provideRouter`, todas las rutas con `loadComponent` (lazy) |
| Estilos | CSS plano con `@layer tokens, base, primitives`. **Sin SCSS**, sin Tailwind |
| Tests | Vitest sobre `jsdom` vía el builder `@angular/build:unit-test` |
| Node | v24.x (probado con 24.18). npm 11.16 (`packageManager` fijado) |

## Levantar en local

```bash
npm install
npm start          # ng serve --proxy-config proxy.conf.json  ->  http://localhost:4200
```

`npm start` ya incluye el proxy: `/api/*` se reenvía a `https://localhost:54848` (Kestrel de
`SmartNetApi`, `secure:false` porque el cert de dev es autofirmado). **La API debe estar corriendo
aparte** o toda ruta protegida rebota a `/login`.

```bash
npm test           # ng test (Vitest, una sola corrida)
npm run lint       # tsc --noEmit sobre tsconfig.app.json y tsconfig.spec.json  (NO hay ESLint)
npm run build      # ng build -> dist/spa/browser/  (config production por defecto)
```

## Arquitectura propia de este repo

- **Atomic / container-presentational por feature.** Cada feature (`inbox`, `detalle`,
  `configuracion`, `login`, `catalogos`) tiene la estructura `feature/` (páginas con estado y
  routing), `ui/` (componentes presentacionales, `input()`/`output()`, sin inyección de servicios
  de datos), `data-access/` (servicios `providedIn: 'root'`) y `models/`.
- **Patrón de servicio de datos (ADR 0009).** `signal` privada escribible + `asReadonly()` pública;
  `computed()` para lo derivado. El servicio hace `firstValueFrom` del `HttpClient` y expone
  `loading` / `error` como signals. No hay merge ni paginación en cliente: el endpoint ya devuelve
  la vista combinada y paginada.
- **Auth por cookie server-side.** La cookie `__Host-session` es HttpOnly + same-origin: el
  navegador la adjunta sola. `SessionService` **no lee ni escribe la cookie**, solo espeja lo que
  reporta `GET /api/sesion` (200 `{ nombreUsuario }` | 401). `authGuard` llama a
  `session.verificar()` antes de cada ruta protegida y redirige a `/login?returnUrl=`.
- **Capas de CSS.** `src/styles.css` define los tres `@layer` (tokens de color en dos niveles —
  rampa privada `--azul-*` + alias semánticos —, base, primitivos compartidos). Los `styleUrl` de
  componente quedan **fuera** del stack de `@layer` a propósito, así ganan sin `!important`. Ningún
  componente redefine un literal de color: `contraste.spec.ts` y `paleta.spec.ts` parsean
  `styles.css` y fallan en rojo si aparece uno.
- **Tema.** `data-tema` siempre explícito en `<html>`. `aplicarTemaInicial()` corre en `main.ts`
  **antes** de `bootstrapApplication` (sin DI de Angular) para que no haya flash de tema
  equivocado. `'sistema'` se resuelve en TS con `matchMedia`, nunca con un override
  `prefers-color-scheme` en CSS. `TemaService` es la contraparte reactiva del toggle del shell.

## Gotchas descubiertos aquí

- **`httpErrorInterceptor` exceptúa `POST /api/sesion`.** Un 401 de esa request concreta es
  "credenciales inválidas" con cuerpo `ProblemaDetails` legible por `LoginPage`, no "sesión
  expirada". Para cualquier otra request, un 401 limpia la sesión, redirige a `/login` y **descarta
  el cuerpo** (un 401 de auth no lleva contrato `ProblemaDetails` y no debe llegar al DOM). Todo
  otro status pasa intacto para que `detalle` lea el `ProblemaDetails` de 422/409/412/428.
- **Ruteo de UX de conflicto por status code**, no por `type` URI: 412 → recarga, 422 → inline,
  428 → precondición cliente, resto → banner de negocio (`categorizarProblema` en
  `detalle/data-access/problema-ux.ts`). El `type`/`title`/`detail` del `ProblemaDetails` sigue
  llevando el texto exacto del mensaje.
- **Excepción ratificada de color** (BACKLOG #18, decisión de usuario): el mismo azul se reutiliza
  para el fill de acción primaria, el chip "Pendiente" y el banner informativo P00000. Es una
  desviación deliberada de "un estado = un color". Un reviewer **no** debe "arreglarla" separándola
  en tres tonos sin re-ratificación. Está documentada en `src/styles.css`.
- **`reprocesar(procesamientoId)`** usa `ProcesamientoId`, no `InboxEventId` ni `FacturaId`:
  `POST /api/incidencias/{procesamientoId}/reprocesar`.
- **No hay ESLint** pese a que `CONVENTIONS.md` lo menciona; el gate de estilo es `prettier` +
  `tsc --noEmit`. `tsconfig.json` activa `noPropertyAccessFromIndexSignature`, así que
  `document.documentElement.dataset['tema']` va con corchetes, no con punto.
- **Idioma de identificadores** (`CONVENTIONS.md`): dominio contable en español
  (`AsientoContable`, `BasePEN`), andamiaje técnico en inglés. En TS el casing es `camelCase`
  para métodos/propiedades; sin acentos ni ñ en identificadores, sí en strings y comentarios.

## Relación con el ecosistema

Depende únicamente de `SmartNetApi` por HTTP bajo `/api/*` (sesión/login, `Asiento`, `Auditoria`,
`Bandeja`, `Catalogo`, `Configuracion`, `Documento`, `Factura`, `Integracion`, `TipoCambio`).
Nadie depende de `SmartNetWeb`: es la hoja del grafo. No habla con `SmartNetWorker` ni con
`SmartNetBD` directamente. En producción, API y SPA quedan detrás del mismo proxy inverso y mismo
origen — por eso no hay configuración de CORS.
