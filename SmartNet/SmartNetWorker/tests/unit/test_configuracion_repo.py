"""RED primero (BACKLOG #17, Fase 3, tasks.md 3.5): `smartnet_worker.configuracion_repo` todavia
no existe. Solo lectura (`fact_worker` tiene SELECT sobre `fact.Configuracion`, 008:131, nunca
INSERT/UPDATE -- ese es el camino de `ConfiguracionEndpoints.cs` en .NET). `obtener` devuelve
`Valor` si no es NULL, si no `ValorPorDefecto`; `obtener_destinatarios_correo` exige que la fila NO
este pendiente (ambos NULL) y lanza `ConfiguracionError` explicito si lo esta (design.md:
"falla con ConfiguracionError explicito si CORREO.DESTINATARIOS sigue en NULL al arrancar -- nunca
un envio silencioso a nadie")."""

from __future__ import annotations

from smartnet_worker.config import ConfiguracionError
from smartnet_worker.configuracion_repo import obtener, obtener_destinatarios_correo


class _FakeCursor:
    def __init__(self, fila: tuple | None):
        self._fila = fila
        self.llamadas: list[tuple] = []

    def execute(self, sql, *parametros):
        self.llamadas.append((sql, parametros))

    def fetchone(self):
        return self._fila


def test_obtener_devuelve_valor_cuando_no_es_null():
    cursor = _FakeCursor(("+51999999999", "TEXTO"))

    resultado = obtener(cursor, "TELEGRAM", "DESTINO_CHAT_ID")

    assert resultado == "+51999999999"
    sql, parametros = cursor.llamadas[0]
    assert "fact.configuracion" in sql.lower()
    assert "select" in sql.lower()
    assert parametros == ("TELEGRAM", "DESTINO_CHAT_ID")


def test_obtener_cae_a_valor_por_defecto_cuando_valor_es_null(monkeypatch):
    class _CursorConDefault(_FakeCursor):
        def fetchone(self):
            return (None, "default-x")

    cursor = _CursorConDefault(None)

    resultado = obtener(cursor, "TELEGRAM", "DESTINO_CHAT_ID")

    assert resultado == "default-x"


def test_obtener_sin_fila_devuelve_none():
    cursor = _FakeCursor(None)

    assert obtener(cursor, "TELEGRAM", "DESTINO_CHAT_ID") is None


def test_obtener_destinatarios_correo_parsea_lista_separada_por_coma():
    class _CursorConLista(_FakeCursor):
        def fetchone(self):
            return ("a@x.com,b@x.com", None)

    cursor = _CursorConLista(None)

    assert obtener_destinatarios_correo(cursor) == ("a@x.com", "b@x.com")


def test_obtener_destinatarios_correo_lanza_si_pendiente():
    cursor = _FakeCursor((None, None))

    try:
        obtener_destinatarios_correo(cursor)
        raise AssertionError("se esperaba ConfiguracionError")
    except ConfiguracionError:
        pass
