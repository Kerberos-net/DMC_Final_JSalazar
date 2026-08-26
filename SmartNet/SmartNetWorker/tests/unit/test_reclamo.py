"""Suite de `reclamo.py` (BACKLOG #14, Fase 4, tarea 4.1) — puramente estructural: la constante
`ARRENDAMIENTO`, la forma del `EventoReclamado`, y que el modulo entero no importe `pyodbc`
(design.md Decision D6: el Protocol es "pure, no pyodbc"). No hay I/O ni reloj en este archivo."""

from __future__ import annotations

import inspect
from datetime import timedelta
from pathlib import Path

from smartnet_worker import reclamo
from smartnet_worker.reclamo import ARRENDAMIENTO, EventoReclamado, ReclamoDeLote

_SRC_FILE = Path(reclamo.__file__)


def test_arrendamiento_es_cinco_minutos():
    assert ARRENDAMIENTO == timedelta(minutes=5)


def test_evento_reclamado_es_dataclass_congelada_con_los_seis_campos():
    evento = EventoReclamado(
        outbox_event_id=1,
        integracion="DRIVE",
        factura_id=100,
        tipo="FACTURA_VALIDADA",
        payload='{"version":1}',
        secuencia=7,
    )
    assert evento.outbox_event_id == 1
    assert evento.integracion == "DRIVE"
    assert evento.factura_id == 100
    assert evento.tipo == "FACTURA_VALIDADA"
    assert evento.payload == '{"version":1}'
    assert evento.secuencia == 7
    try:
        evento.secuencia = 8  # type: ignore[misc]
    except Exception:
        pass
    else:
        raise AssertionError("EventoReclamado deberia ser inmutable (frozen=True).")


def test_reclamo_de_lote_es_un_protocol_con_los_tres_metodos_publicados():
    assert issubclass(ReclamoDeLote, object)
    assert hasattr(ReclamoDeLote, "_is_protocol") and ReclamoDeLote._is_protocol is True
    for nombre in ("reclamar", "progreso", "marcar"):
        assert hasattr(ReclamoDeLote, nombre), f"ReclamoDeLote no declara '{nombre}'."


def test_una_implementacion_estructural_satisface_el_protocol_sin_heredar():
    class _Fake:
        def reclamar(self, destinos, limite, ahora):
            return ()

        def progreso(self, factura_id, destino):
            return None

        def marcar(self, evento_id, destino, estado, ahora):
            return None

    fake: ReclamoDeLote = _Fake()
    assert isinstance(fake, ReclamoDeLote)


def _sin_docstring_de_modulo(codigo: str) -> str:
    """El docstring de modulo de `reclamo.py` discute en prosa por que el archivo NO usa
    `pyodbc`/`READPAST` (design.md Decision D6) — esas palabras aparecen legitimamente ahi. Esta
    prueba escanea solo CODIGO, nunca la explicacion en prosa de una regla que el propio codigo
    ya cumple."""
    if not codigo.lstrip().startswith('"""'):
        return codigo
    resto = codigo.lstrip()[3:]
    cierre = resto.find('"""')
    return resto[cierre + 3 :] if cierre != -1 else ""


def test_reclamo_no_importa_pyodbc():
    codigo = _sin_docstring_de_modulo(_SRC_FILE.read_text(encoding="utf-8"))
    assert "pyodbc" not in codigo, (
        "reclamo.py debe ser puro (design.md Decision D6): la implementacion READPAST vive "
        "unicamente en outbox_repo.py."
    )


def test_reclamo_no_declara_ninguna_funcion_con_sql():
    codigo = _sin_docstring_de_modulo(inspect.getsource(reclamo))
    for termino in ("SELECT", "UPDATE", "READPAST"):
        assert termino not in codigo, f"reclamo.py no debe contener SQL ('{termino}' encontrado)."
