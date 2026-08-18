"""Repositorio de `fact.TipoCambio` para el runtime Python — simetrico a
`SqlTipoCambioRepository.CargarManualAsync` del lado .NET (design.md, Decision 4).

`insertar_sbs` fija `Origen='SBS'` de forma hardcodeada, sin parametro: ADR 0003 dice que solo
Python escribe filas SBS, y hacer el origen no-pasable impone esa particion en la firma, no en un
comentario. La PK real `(Fecha, Origen)` es la que rechaza el duplicado (Decision 3) — este
adaptador solo traduce el `IntegrityError` a `False`, nunca hace un `SELECT` previo (evita la
ventana TOCTOU entre dos scrapes concurrentes del mismo dia).
"""

from __future__ import annotations

import pyodbc

from smartnet_worker.sbs import TipoCambioSbs

_INSERT_SBS = """
INSERT INTO fact.TipoCambio (Fecha, Origen, Compra, Venta, FechaConsulta)
VALUES (?, 'SBS', ?, ?, ?)
"""


def insertar_sbs(cursor, tc: TipoCambioSbs) -> bool:
    """Inserta la fila SBS del dia. Devuelve `False` (no lanza) si ya existia una fila para esa
    fecha — el llamador decide que hacer con un duplicado (por ejemplo, no tratarlo como fallo)."""
    try:
        cursor.execute(_INSERT_SBS, tc.fecha, tc.compra, tc.venta, tc.fecha_consulta)
        return True
    except pyodbc.IntegrityError:
        return False
