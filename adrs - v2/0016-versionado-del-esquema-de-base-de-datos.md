# ADR 0016: Versionado del esquema de base de datos

## Estado

Aceptado. Decisión nueva. El diseño anterior lo listaba como riesgo abierto sin resolver.

> **Nota de terminología.** "Versionado del esquema" es la evolución controlada de las tablas de la
> base de datos. **No tiene ninguna relación** con la restricción del proyecto según la cual no hay
> migración de datos hacia ningún sistema contable externo. Se evita deliberadamente la palabra
> "migración" en este documento para que no se lea como lo contrario de lo decidido.

## Contexto

Dos runtimes escriben la misma base de datos (ADR 0002, ADR 0003). ADR 0001, ADR 0003 y ADR 0008
repiten que un cambio en la frontera del esquema *"debe desplegarse de forma coordinada"*, pero
ninguna define **cómo**: ni la herramienta, ni el orden, ni qué ocurre si un artefacto queda
desactualizado respecto del otro.

El punto delicado son las **tablas de contrato** —`OutboxEvent`, `CommandQueue`, `InboxEvent`,
`TipoCambio`, `Configuracion`—: son justo las que ambos componentes usan. Si las versionan los dos,
se pisan; si ninguno, quedan huérfanas.

## Decisión

### El esquema es SQL plano, versionado, con herramienta neutral

```
db/schema/
  001_esquema_inicial.sql
  002_motivo_y_sugerencia_cuenta.sql
  003_contratos_outbox_command_inbox.sql
  004_adjunto_manual_y_origen_tipo_cambio.sql
  ...
```

Aplicado por una herramienta **independiente de ambos runtimes** —DbUp, Flyway o equivalente— como
paso previo al despliegue de los artefactos.

**Un solo dueño del esquema completo.** Ni EF Core Migrations ni Alembic versionan nada.

### Por qué neutral

El esquema **es el contrato de integración del sistema**. Sobre él se apoyan ADR 0003, ADR 0004 y
ADR 0005 por completo.

Definirlo con clases C# haría que las tablas de Python fueran un efecto colateral del ORM de .NET, y
revisar un cambio de la frontera obligaría a leer C# en vez de SQL. En un sistema contable, un
esquema legible y auditable no es un detalle menor.

Además, el SQL versionado es la **definición autoritativa** de la que ambos runtimes derivan sus
tipos, lo que acota el costo declarado en ADR 0002 de mantener los mismos tipos en dos lenguajes.

### Orden de despliegue

```
1. versionado del esquema
2. API .NET
3. worker Python
```

Los cambios deben ser **compatibles hacia atrás** dentro de un despliegue: añadir columnas antes de
usarlas, y retirar las viejas en un despliegue posterior. Así un artefacto momentáneamente
desactualizado sigue funcionando.

## Alternativas consideradas

- **EF Core Migrations, con .NET como dueño único.** Una sola herramienta, integrada con el
  desarrollo del dominio, que el desarrollador ya usa. Se descartó porque las tablas de Python
  quedarían definidas por clases C# que ningún código .NET utiliza, y porque el contrato de
  integración pasaría a depender del ORM de uno de los dos participantes.
- **Cada componente versiona sus propias tablas: EF Core y Alembic.** Autonomía por lado. Se
  descartó por las tablas de contrato: son las que ambos usan y no tienen dueño natural bajo este
  esquema. Es exactamente el escenario que hay que evitar.
- **Scripts aplicados a mano.** Cero herramientas. Se descartó porque no hay registro de qué se
  aplicó ni en qué entorno, y con dos entornos (ADR 0012) la divergencia es cuestión de semanas.

## Consecuencias

- Existe **una sola verdad** sobre el esquema, legible sin ejecutar código y revisable en un *diff*.
- El despliegue coordinado que tres ADRs exigían tiene por fin un procedimiento.
- La divergencia de tipos entre C# y Python tiene una referencia común contra la que verificarse.
- **Costo:** el desarrollador escribe SQL a mano en lugar de generar los cambios desde el modelo del
  ORM. Es trabajo real, especialmente al principio.
- **Costo:** la regla de compatibilidad hacia atrás obliga a dividir en dos despliegues los cambios
  que retiran columnas.
- **Costo:** una herramienta más en la cadena de despliegue.
