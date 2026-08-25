"""Suite de `guarda_obsolescencia.py` (BACKLOG #14, Fase 4, tarea 4.2) — veredicto puro, sin
fake de DB: `evaluar` nunca abre una conexion ni lanza (design.md Decision D5)."""

from __future__ import annotations

from smartnet_worker.guarda_obsolescencia import VerdictoObsolescencia, evaluar


def test_sin_progreso_previo_el_evento_es_vigente():
    assert evaluar(secuencia=1, progreso=None) is VerdictoObsolescencia.VIGENTE


def test_secuencia_mayor_al_progreso_es_vigente():
    assert evaluar(secuencia=6, progreso=5) is VerdictoObsolescencia.VIGENTE


def test_secuencia_igual_al_progreso_es_obsoleta():
    assert evaluar(secuencia=5, progreso=5) is VerdictoObsolescencia.OBSOLETO


def test_secuencia_menor_al_progreso_es_obsoleta():
    assert evaluar(secuencia=3, progreso=5) is VerdictoObsolescencia.OBSOLETO


def test_evaluar_nunca_lanza_para_entradas_validas():
    for secuencia, progreso in ((0, None), (1, 0), (10**9, 10**9 - 1), (1, 10**9)):
        evaluar(secuencia, progreso)  # no debe lanzar
