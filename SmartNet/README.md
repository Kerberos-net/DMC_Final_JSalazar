# SmartNet

Código del Gestor de Facturas de Compra. Todo lo ejecutable vive aquí; la raíz del repositorio
queda para la documentación, los ADRs y los datos maestros.

## Estructura prevista

```
SmartNet/
  db/schema/          SQL versionado — el contrato entre los dos runtimes (ADR 0016)
  api/                .NET — dominio y API transaccional
  worker/             Python — ingesta, extracción y publicación
  web/                Angular — SPA con signals
```

## El orden importa

`db/schema/` va primero y no es un detalle de implementación: **el esquema es el contrato de
integración del sistema** (ADR 0016). La API y el worker lo consumen, ninguno lo define. Por eso no
se genera desde EF Core ni desde Alembic, y por eso el despliegue lo aplica **antes** que los otros
dos artefactos (ADR 0012).

## Antes de crear un proyecto aquí

- `CONVENTIONS.md` — nombres, idioma y estilo. El dominio contable se nombra en español.
- `BACKLOG.md` — qué se construye y en qué orden.
- `CLAUDE.md` — las cinco reglas que es fácil violar sin darse cuenta.
