"""Lectura de `fact.Configuracion` (BACKLOG #17, design.md D4/D6) -- SOLO `SELECT`, nunca
`INSERT`/`UPDATE`: `fact_worker` tiene unicamente ese permiso sobre la tabla (008:131); escribir
una clave es responsabilidad exclusiva de `ConfiguracionEndpoints.cs` del lado .NET
(`configuracion-api-spa`, design.md D6). Este modulo nunca crea una fila -- una clave ausente es un
error de esquema (el 020/009 no se aplicaron), no algo que este repo deba reparar.

`Valor = NULL` es la codificacion explicita de "usar `ValorPorDefecto`" (007_publicacion.sql:29);
`obtener` resuelve esa caida aqui para que ningun llamador tenga que repetir la logica."""

from __future__ import annotations

from smartnet_worker.config import ConfiguracionError

_SELECT = "SELECT Valor, ValorPorDefecto FROM fact.Configuracion WHERE Seccion = ? AND Clave = ?"


def obtener(cursor, seccion: str, clave: str) -> str | None:
    cursor.execute(_SELECT, seccion, clave)
    fila = cursor.fetchone()
    if fila is None:
        return None
    valor, valor_por_defecto = fila
    return valor if valor is not None else valor_por_defecto


def obtener_destinatarios_correo(cursor) -> tuple[str, ...]:
    """`CORREO.DESTINATARIOS` (sembrada por 020, D1b) -- `Tipo = 'LISTA'`, items separados por
    coma. Lanza `ConfiguracionError` si sigue pendiente (`Valor` y `ValorPorDefecto` ambos NULL):
    "falla con ConfiguracionError explicito... nunca un envio silencioso a nadie" (design.md,
    Migration / Rollout)."""
    valor = obtener(cursor, "CORREO", "DESTINATARIOS")
    if valor is None:
        raise ConfiguracionError(
            "CORREO.DESTINATARIOS sigue en NULL -- configurar los destinatarios de respaldo desde "
            "la pantalla de Configuracion antes de que el notificador pueda usar el canal CORREO."
        )
    return tuple(item.strip() for item in valor.split(",") if item.strip())
