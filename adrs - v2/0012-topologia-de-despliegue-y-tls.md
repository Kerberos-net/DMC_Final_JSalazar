# ADR 0012: Topología de despliegue, TLS y entornos

## Estado

Aceptado. Decisión nueva. El diseño anterior tenía diez ADRs, tres artefactos desplegables y **cero
decisiones sobre dónde y cómo se ejecutan**.

## Contexto

La ausencia de esta decisión bloqueaba otras tres:

- El valor de `SameSite` depende de si SPA y API comparten origen, y eso depende de si hay proxy
  inverso (ADR 0007).
- Que Python escriba los archivos y .NET los sirva obliga a un volumen compartido, lo que restringe
  la topología posible (ADR 0013).
- El *redirect URI* de OAuth de Google exige HTTPS salvo en `localhost` (ADR 0015).

Además, sin un entorno distinto de producción, probar el flujo completo significa apuntar a la
cuenta de Gmail real, crear carpetas en el Drive real y escribir en el Sheets que alimenta el
dashboard.

## Decisión

### Mismo origen tras proxy inverso

```
https://facturas.empresa.local
    /        → SPA Angular compilada (estáticos)
    /api/*   → ASP.NET Core (Kestrel)
```

Un proxy inverso sirve la SPA compilada y enruta `/api` hacia Kestrel. Todo bajo un mismo host y
puerto.

Consecuencias directas: `SameSite=Lax` funciona, no hace falta CORS ni `withCredentials`, y el
`<iframe>` del visor de documentos recibe la cookie de sesión.

> **La topología de mismo origen es un requisito de seguridad, no una preferencia.** Separar los
> orígenes en el futuro rompe la sesión, y la solución aparente —`SameSite=None`— reabre la
> exposición a CSRF que ADR 0007 cierra.

### Volumen compartido

El worker y la API acceden al **mismo volumen de documentos**. La ruta se compone a partir de una
**raíz configurable** entregada a ambos runtimes; la base almacena la parte relativa. La topología
de despliegue debe garantizar ese acceso compartido (ADR 0013).

### TLS

El certificado se **gestiona y termina en el proxy inverso**, en un solo lugar. Kestrel escucha en
la red interna del host.

TLS es obligatorio por tres razones independientes: la cookie `Secure` de ADR 0007, las credenciales
del usuario que viajan en el cuerpo del POST de inicio de sesión, y los *redirect URI* de OAuth de
Google.

**Pendiente:** el origen del certificado —autoridad interna, Let's Encrypt o comprado— y quién lo
renueva.

### Orden de despliegue

```
1. versionado del esquema (ADR 0016)
2. API .NET
3. worker Python
```

Resuelve el *"debe desplegarse de forma coordinada"* que ADR 0001, ADR 0003 y ADR 0008 repetían sin
definir nunca.

### Entornos

Existe un entorno de pruebas separado de producción, con **su propia cuenta de Google, su carpeta de
Drive y su hoja de cálculo**.

## Alternativas consideradas

- **La API sirve la SPA compilada, sin proxy.** Menos piezas móviles, mismo origen igualmente. Se
  descartó porque ataría la renovación del certificado al ciclo de vida de la aplicación, obligaría
  a desplegar el backend para publicar un cambio de frontend, y dejaría sin lugar donde poner
  límites de tasa, compresión y cabeceras comunes.
- **Orígenes distintos con CORS y credenciales.** Permite desplegar y escalar SPA y API por
  separado. Se descartó por su costo de seguridad: `SameSite=None; Secure`, `AllowCredentials` con
  lista explícita de orígenes, `withCredentials` en cada llamada y un token antiforgery obligatorio.
  Tres modos de fallo garantizados si falta cualquier pieza, y el visor de documentos es donde se
  manifiestan como un panel en blanco.
- **Contenedores con orquestador.** Resolvería el volumen compartido y el reinicio automático. Se
  descartó por desproporcionado para tres artefactos y un usuario, y porque la licencia de SQL
  Server ata las opciones de despliegue.

## Consecuencias

- Tres decisiones que estaban bloqueadas quedan resueltas: `SameSite`, la autorización del visor y
  el *redirect URI* de OAuth.
- El certificado se renueva en un solo sitio.
- El flujo completo se puede probar sin tocar el Drive, el Gmail ni el Sheets reales.
- **Costo:** un componente más que configurar y mantener.
- **Costo:** el volumen compartido acopla la ubicación de los dos runtimes y erosiona la
  independencia de despliegue que reivindica ADR 0001.
- **Costo:** el entorno de pruebas duplica credenciales de Google, carpetas y hojas de cálculo que
  hay que crear y mantener sincronizadas en estructura.
- **Pendiente:** origen y renovación del certificado TLS.
