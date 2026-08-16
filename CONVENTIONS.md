# Convenciones de código

## Idioma de los identificadores

**Híbrido, con una frontera clara: el dominio contable en español, el andamiaje técnico en inglés.**

| | Español | Inglés |
|---|---|---|
| **Qué** | Entidades, propiedades, operaciones y conceptos del negocio contable | Interfaces, repositorios, controladores, DTOs, utilidades, infraestructura |
| **Ejemplos** | `AsientoContable`, `LineaAsiento`, `MotivoCompra`, `BasePEN`, `IgvPEN`, `CtaReflejaCodigo`, `Confirmar()`, `AgregarLinea()`, `CalcularDestino()` | `IAsientoRepository`, `FacturaController`, `GetByIdAsync()`, `ToDto()`, `OutboxDispatcher`, `RetryPolicy` |

### Por qué el dominio va en español

`REGLAS.md` es el documento **normativo** del proyecto y está en español. Si el código dice
`asiento.BasePEN`, mapea 1:1 con la regla que lo define. Si dijera `entry.taxableBase`, cada
revisión contra las reglas exigiría traducir mentalmente.

Y hay términos sin traducción establecida: `ctarefleja`, `ctapuente`, `motivo`, `percepción`,
`detracción`, `afectación`, `IGV`. Traducirlos no los aclara — inventa vocabulario que no existe en
el dominio y que nadie de la compañía reconocería.

### La frontera, cuando dudes

> ¿Aparece este término en `REGLAS.md`, en el plan de cuentas o en una conversación con el
> contador? → **español**. ¿Existiría igual en cualquier otro proyecto? → **inglés**.

`Factura` es dominio. `FacturaRepository` es la misma palabra en los dos lados: el sustantivo es del
dominio, el sufijo es técnico. Esa mezcla es correcta y esperada.

**No traduzcas a medias.** `AsientoEntity`, `LineaItem` o `getMotivo()` son lo peor de las dos
opciones.

## Casing

**El de cada lenguaje.** No hay una regla global, y forzarla haría que los analizadores protesten en
cada archivo hasta que alguien los desactive — perdiendo también las reglas que sí importan.

| | Clases / Tipos | Métodos / Funciones | Propiedades / Campos | Locales |
|---|---|---|---|---|
| **C#** | `PascalCase` | `PascalCase` | `PascalCase` | `camelCase`, privados `_camelCase` |
| **Python** | `PascalCase` | `snake_case` | `snake_case` | `snake_case` |
| **TypeScript** | `PascalCase` | `camelCase` | `camelCase` | `camelCase` |
| **SQL** | `PascalCase` para tablas y columnas | — | — | — |

Los términos en español siguen la misma regla que los ingleses: `AsientoContable` en C#,
`asiento_contable` en Python, `asientoContable` en TypeScript. **La misma entidad se escribe distinto
en cada capa, y está bien.**

Esto lo imponen las herramientas —`.editorconfig`, analizadores de .NET, `ruff`, ESLint—, no la
disciplina. Si el linter y este documento discrepan, gana el linter y se corrige el documento.

## Acentos y ñ

**No se usan en identificadores**, aunque los lenguajes los admitan: `Afectacion`, no `Afectación`;
`Ano`… mejor `Anio`. Rompen herramientas, rutas y comparaciones de forma impredecible.

**Sí se usan en cadenas de texto, comentarios y documentación**, con ortografía correcta.

## Tipos y valores

- **Dinero:** `DECIMAL(18,2)` en SQL, `decimal` en C#, `Decimal` en Python. **Nunca `float`, `real`
  ni `double`.** Ni siquiera para un cálculo intermedio.
- **Tipo de cambio:** `DECIMAL(12,6)`.
- **Sufijo de moneda explícito** cuando el mismo concepto existe en dos monedas: `totalOrig` /
  `totalPEN`. Un importe sin sufijo en un contexto multimoneda es una ambigüedad esperando a fallar.
- **Fechas contables** son fecha, no fecha-hora: `DATE` / `DateOnly`.

## Comentarios

El código explica **qué** hace; el comentario, **por qué**. Un comentario que repite el nombre del
método sobra.

Donde sí hacen falta: cuando una línea implementa una regla contable no evidente. Cita la fuente.

```csharp
// La base se DERIVA, no se convierte: anclar total e IGV hace que
// base + IGV = total sea cierto por construcción (REGLAS.md §6).
var basePEN = totalPEN - igvPEN;
```

## Pruebas

Nombre en español cuando describen una regla del dominio, porque se leen como la regla:

```
NotaCreditoSobreBoleta_NoGeneraLineaDeIgv
NotaCreditoDel100PorCiento_DejaElPasivoEnCero
ValidacionFallida_NoConsumeCorrelativo
```

Los siete ejemplos de `REGLAS.md` §10 y las invariantes de §7 son **normativos**: si el código y el
ejemplo discrepan, se corrige uno de los dos deliberadamente, nunca en silencio (ADR 0019).

## Commits

Conventional commits, en **inglés**, sin atribución de IA.

El *scope* es el componente o el ítem del backlog: `feat(asiento):`, `fix(outbox):`,
`test(reglas):`, `docs(adr):`.
