# ADR 0002: Stack tecnológico por componente

## Estado

Aceptado. Revisión 2. Reemplaza la versión previa (`adrs - v1/0002`), cuya justificación del alcance
de Python se apoyaba en la palabra "coherencia". La revisión 2 añade una nota sobre por qué
PostgreSQL dejó de ser una alternativa real (revisión adversarial v2, S2).

## Contexto

ADR 0001 fija tres artefactos. Falta decidir con qué se construye cada uno y, sobre todo, **dónde se
traza la línea** entre el backend de dominio y el worker de integraciones.

La versión anterior de esta ADR fundamentaba Python por OCR y scraping —argumento sólido— y luego
extendía su alcance a Gmail, Drive, Sheets, Telegram y correo *"por coherencia con la propiedad del
procesamiento y de los reintentos"*. Esa justificación es circular: describe el resultado, no el
criterio. Y el costo que se paga con ella es real: dos runtimes, base de datos como canal entre
procesos, tipos duplicados y despliegue coordinado.

## Decisión

### El eje de separación es el modelo de ejecución

> **Python es el worker de integración y procesamiento asíncrono del sistema.**
> **.NET es el owner del dominio y de la API transaccional.**

| Componente | Naturaleza del trabajo |
|---|---|
| API .NET | Síncrono, transaccional, dueño de las invariantes del dominio |
| Worker Python | Asíncrono, tolerante a fallo, con reintentos y latencia variable frente a terceros |

Ante una integración nueva la pregunta es **si su trabajo es transaccional o asíncrono**, no si
"encaja mejor" en un lenguaje. Es un criterio que se puede aplicar sin discutir.

### Stack

| Componente | Tecnología |
|---|---|
| SPA | Angular con signals, sin librería de estado externa (ADR 0009) |
| API de negocio | ASP.NET Core sobre Kestrel |
| Worker | Python |
| Base de datos | SQL Server |
| Proxy inverso | A elegir en el despliegue (ADR 0012) |
| Versionado del esquema | Herramienta neutral sobre SQL plano (ADR 0016) |

## Alternativas consideradas

- **Un solo runtime .NET.** Elimina la frontera por completo. Se descartó por el ecosistema de
  extracción documental: la precisión del OCR es el mayor riesgo técnico declarado del proyecto, y
  Python es donde ese riesgo se mitiga mejor.
- **Python solo para OCR y SBS, .NET dueño del resto de integraciones.** Reduce la frontera a lo
  imprescindible. Se descartó porque partiría el trabajo asíncrono entre los dos componentes: los
  reintentos de Drive vivirían en .NET y los de OCR en Python, con dos políticas de reintento y dos
  lugares donde diagnosticar un fallo de integración. El criterio de separación adoptado agrupa por
  naturaleza del trabajo, no por servicio concreto.
- **PostgreSQL en lugar de SQL Server.** Menor huella de licencia y de infraestructura. Se descartó
  por decisión de plataforma de la organización; el diseño no depende de características exclusivas
  del motor salvo la sintaxis de reclamo de lote con `READPAST` (ADR 0004), que tiene equivalente.

  > **Nota posterior (revisión adversarial v2, S2).** Esta alternativa **dejó de serlo**. ADR 0003
  > revisión 3 estableció que los datos maestros los mantiene el sistema contable de la compañía en
  > esta misma base, que es SQL Server, y la revisión v2 confirmó que la base es **compartida**. El
  > diseño lee esas tablas con `SELECT` directo, sin réplica ni copia. Desde ese cambio de premisa,
  > PostgreSQL no es una alternativa descartada por preferencia de plataforma: es una imposibilidad.
  > La decisión no cambia; cambia su fundamento, y conviene que quien la revise dentro de dos años lo
  > sepa.

## Consecuencias

- La frontera tiene un criterio explícito y verificable. Una integración nueva no reabre el debate.
- Toda la política de reintentos vive en un solo componente, con una sola clasificación de errores
  (ADR 0010) y un solo lugar donde diagnosticarla.
- **Costo:** los tipos de las tablas de contrato se declaran en C# y en Python. Se mitiga con el
  versionado de esquema en SQL plano (ADR 0016), que es la definición autoritativa para ambos.
- **Costo:** probar el flujo completo exige levantar tres procesos más la base de datos y el gestor
  de secretos. Se mitiga con el entorno de pruebas de ADR 0012.
- **Costo:** SQL Server impone una huella de infraestructura mayor y ata el proyecto a las opciones
  de despliegue compatibles con su licencia. ADR 0012 decide qué se hace con esa restricción, que
  la versión anterior de esta ADR declaraba sin resolver.
