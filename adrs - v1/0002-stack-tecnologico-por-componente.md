# ADR 0002: Stack tecnológico por componente

## Estado

Aceptado

## Contexto

El PRD no fija ningún motor de base de datos ni lenguaje: describe qué debe hacer el sistema
(integraciones con Gmail, Drive y Sheets, extracción OCR/IA, scraping del tipo de cambio de la SBS,
notificaciones por Telegram/correo, dashboard en Looker Studio) sin imponer implementación. La
decisión de componentes (ADR 0001) definió tres artefactos: SPA, API de negocio y worker de
procesamiento e integraciones.

Restricciones reales del proyecto:

- Equipo de una sola persona, sin fecha límite fija.
- Un único usuario concurrente y un volumen de 10 a 50 facturas diarias: el rendimiento no es un
  criterio discriminante entre stacks.
- Ya existe licencia de SQL Server en la organización, por lo que adoptarlo no implica costo
  incremental de licenciamiento.
- El riesgo técnico real del proyecto está concentrado en la extracción OCR/IA de PDF/XML y en el
  scraping del tipo de cambio de la SBS, no en la capa transaccional.
- El desarrollador tiene experiencia consolidada en Angular y en el ecosistema .NET.

En un proyecto de un solo desarrollador, el dominio previo del stack pesa más que las ventajas
teóricas de una alternativa desconocida, salvo en las áreas donde el ecosistema marca una
diferencia sustantiva de esfuerzo.

## Decisión

Se adopta un stack heterogéneo, asignando cada tecnología al componente donde aporta ventaja real:

| Componente | Tecnología |
|---|---|
| Aplicación web (SPA) | **Angular** |
| API de negocio | **ASP.NET Core** |
| Worker de procesamiento e integraciones | **Python** |
| Base de datos | **SQL Server** |

El criterio de asignación es explícito: Angular y ASP.NET Core por dominio previo del desarrollador
y madurez para lógica transaccional y de seguridad; Python exclusivamente donde su ecosistema es
determinante (visión por computador y modelos para la extracción de facturas, y scraping web para el
tipo de cambio de la SBS), extendido al resto de integraciones externas por coherencia con la
propiedad del procesamiento y de los reintentos (ver ADR 0003 y ADR 0004); SQL Server por licencia
ya disponible.

## Alternativas consideradas

- **Python de punta a punta (FastAPI para la API de negocio + worker) con PostgreSQL** — Era viable
  y concentraba todo en un solo lenguaje, con el mejor ecosistema disponible para el riesgo técnico
  principal del proyecto. Se descartó porque el desarrollador no tiene en Python la soltura que
  tiene en .NET para la capa transaccional y de seguridad, y porque adoptar PostgreSQL desperdiciaría
  una licencia de SQL Server ya pagada.
- **Node.js/TypeScript de punta a punta con React** — Ofrecía un único lenguaje para frontend,
  backend y worker, con tipado estático útil en el modelo contable. Se descartó por doble motivo:
  el desarrollador domina Angular y no React, y el ecosistema de Node para OCR/visión y scraping es
  notablemente más pobre que el de Python, que es justamente donde se concentra el riesgo del
  proyecto.
- **.NET de punta a punta, incluyendo el procesamiento OCR y el scraping** — Habría eliminado el
  segundo runtime y simplificado el despliegue a un solo ecosistema. Se descartó porque obligaría a
  resolver la extracción OCR/IA y el scraping mediante servicios externos o bibliotecas menos
  maduras, trasladando el riesgo técnico principal a la parte más débil del stack en lugar de a la
  más fuerte.

## Consecuencias

- Cada parte del sistema se construye en el ecosistema donde presenta menor fricción: la lógica
  contable y de seguridad en un stack que el desarrollador domina, y el procesamiento de documentos
  en el ecosistema con mejores bibliotecas para ese problema.
- La licencia de SQL Server existente se aprovecha sin costo incremental, y la capa de acceso a
  datos de la API de negocio puede usar herramientas maduras del ecosistema .NET.
- El worker Python queda desacoplado del dominio contable, lo que permite evolucionar o reemplazar
  el motor de extracción OCR/IA sin tocar la API de negocio.
- **Costo:** el proyecto mantiene **dos runtimes** con cadenas de herramientas, dependencias,
  empaquetado y despliegue independientes. Para un desarrollador solo, esto duplica el
  mantenimiento de entornos y de pipelines respecto a un stack homogéneo.
- **Costo:** no hay reutilización de código ni de modelos entre la API de negocio y el worker; las
  entidades compartidas en la frontera de la base de datos deben mantenerse sincronizadas
  manualmente en ambos lenguajes, y una divergencia solo se detecta en tiempo de ejecución.
- **Costo:** SQL Server impone una huella de infraestructura mayor que alternativas más ligeras, y
  ata el proyecto a las opciones de despliegue compatibles con esa licencia.
- **Costo:** al ser Angular un artefacto compilado por separado, el flujo de trabajo local requiere
  levantar tres procesos (SPA, API y worker) más la base de datos para probar el flujo completo.
