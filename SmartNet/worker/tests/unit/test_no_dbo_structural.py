"""Analogo Python de `NoWriteToDboStructuralTests` (SmartNet.TiposCambio.Infrastructure.Tests):
escaneo literal, sin comentarios, del codigo fuente del worker — nunca debe mencionar `dbo.`
(ADR 0003: Python solo toca `fact.TipoCambio` y `fact.EstadoIntegracion`)."""

import re
from pathlib import Path

_SRC = Path(__file__).resolve().parents[2] / "src" / "smartnet_worker"
_COMENTARIO_LINEA_RE = re.compile(r"#.*$", re.MULTILINE)


def _sin_comentarios(texto: str) -> str:
    return _COMENTARIO_LINEA_RE.sub("", texto)


def test_ningun_modulo_del_worker_menciona_dbo():
    archivos = sorted(_SRC.glob("*.py"))
    assert archivos, "No se encontraron modulos en smartnet_worker — el escaneo no probaria nada."

    for archivo in archivos:
        contenido = _sin_comentarios(archivo.read_text(encoding="utf-8"))
        mensaje = f"{archivo.name} menciona 'dbo.' fuera de un comentario."
        assert "dbo." not in contenido.lower(), mensaje
