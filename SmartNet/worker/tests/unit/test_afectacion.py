"""RED primero (BACKLOG #6, WU1): `smartnet_worker.afectacion` todavia no existe.

REGLAS.md §8: AfectacionMixta -- true si el XML declara mas de un codigo de afectacion (rechazo
409), false si declara uno solo (verificada), None si no hay XML (sin verificar)."""

from smartnet_worker.afectacion import calcular_afectacion_mixta


def test_dos_codigos_distintos_es_mixta():
    assert calcular_afectacion_mixta(["10", "20"]) is True


def test_un_solo_codigo_no_es_mixta():
    assert calcular_afectacion_mixta(["10"]) is False


def test_sin_codigos_es_no_verificada():
    assert calcular_afectacion_mixta([]) is None


def test_codigos_repetidos_cuenta_distintos_no_cantidad():
    # ['10', '10'] -> False: la regla cuenta CODIGOS DISTINTOS, no lineas (REGLAS.md §8).
    assert calcular_afectacion_mixta(["10", "10"]) is False


def test_tres_lineas_dos_codigos_distintos_sigue_siendo_mixta():
    assert calcular_afectacion_mixta(["10", "10", "20"]) is True
