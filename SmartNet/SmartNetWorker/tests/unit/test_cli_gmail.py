"""Suite del orquestador `cli_gmail.py` (design.md, Testing Strategy) — `ClienteGmail` falso +
cursor falso, sin red ni DB real. Cubre: etiqueta solo tras commit; mensaje fallido no aborta el
run; mensaje sin candidatos -> 0 escrituras; `insertar_email -> None` -> sin descarga y etiqueta
reaplicada; etiqueta inexistente -> falla antes de `messages.list`.
"""

from __future__ import annotations

import base64
from datetime import UTC, datetime
from pathlib import Path

import pyodbc

from smartnet_worker.cli_gmail import ejecutar

_INSTANTE = datetime(2026, 8, 18, 10, 0, 0, tzinfo=UTC)

_CONFIGURACION_VALIDA = {
    "ETIQUETA_ORIGEN": "Facturas",
    "ETIQUETA_PROCESADO": "fact-procesado",
    "FECHA_INICIO": "2026-01-01",
    "EXTENSIONES_PERMITIDAS": "pdf,xml",
}

_ETIQUETAS = {"Facturas": "Label_1", "fact-procesado": "Label_2"}


def _payload_mensaje(mensaje_id: str, *, nombre_adjunto: str = "factura.pdf") -> dict:
    return {
        "id": mensaje_id,
        "internalDate": "1768730400000",
        "payload": {
            "headers": [
                {"name": "From", "value": "proveedor@example.com"},
                {"name": "Subject", "value": "Factura"},
            ],
            "body": {"size": 0},
            "parts": [
                {
                    "filename": nombre_adjunto,
                    "mimeType": "application/pdf",
                    "headers": [],
                    "body": {"attachmentId": f"att-{mensaje_id}", "size": 4},
                }
            ]
            if nombre_adjunto
            else [],
        },
    }


class _ClienteGmailFalso:
    """Sustituye `ClienteGmail`: registra cada llamada en `eventos` (orden real de invocacion) y
    responde con datos preconfigurados. `resolver_etiquetas` puede omitir una etiqueta esperada
    para ejercer el escenario 'etiqueta inexistente'."""

    def __init__(
        self,
        *,
        etiquetas: dict[str, str],
        mensajes_ids: list[str],
        payloads: dict[str, dict],
        adjuntos: dict[str, bytes] | None = None,
        eventos: list[str] | None = None,
    ):
        self._etiquetas = etiquetas
        self._mensajes_ids = mensajes_ids
        self._payloads = payloads
        self._adjuntos = adjuntos or {}
        self.eventos = eventos if eventos is not None else []

    def resolver_etiquetas(self) -> dict[str, str]:
        self.eventos.append("resolver_etiquetas")
        return self._etiquetas

    def buscar_mensajes(self, consulta: str) -> list[str]:
        self.eventos.append(f"buscar_mensajes:{consulta}")
        return list(self._mensajes_ids)

    def obtener_mensaje(self, mensaje_id: str) -> dict:
        self.eventos.append(f"obtener_mensaje:{mensaje_id}")
        return self._payloads[mensaje_id]

    def obtener_adjunto(self, mensaje_id: str, attachment_id: str) -> bytes:
        self.eventos.append(f"obtener_adjunto:{mensaje_id}:{attachment_id}")
        return self._adjuntos.get(attachment_id, b"contenido-pdf")

    def aplicar_etiqueta(self, mensaje_id: str, etiqueta_id: str) -> None:
        self.eventos.append(f"aplicar_etiqueta:{mensaje_id}:{etiqueta_id}")


class _FakeCursor:
    """Cursor falso compartido por las tres fases del run (config, por-mensaje, estado final).
    Traduce las mismas sentencias que `documento_repo.py`/`estado_integracion.py` emiten de
    verdad — ningun stub reemplaza esos modulos, solo el transporte pyodbc."""

    def __init__(
        self,
        *,
        valores_configuracion: dict[str, str],
        eventos: list[str],
        lanzar_integrity_error_email_para: set[str] | None = None,
        rowcount_estado: int = 1,
    ):
        self._valores_configuracion = valores_configuracion
        self._eventos = eventos
        self._lanzar_integrity_error_email_para = lanzar_integrity_error_email_para or set()
        self.rowcount = rowcount_estado
        self._ultimo_fetchall: list[tuple] | None = None
        self._ultimo_gmail_message_id: str | None = None
        self._siguiente_identity = 1

    def execute(self, sentencia: str, *parametros):
        texto = sentencia.lower()
        if "select clave, valor" in texto:
            self._ultimo_fetchall = [
                (clave, self._valores_configuracion.get(clave)) for clave in parametros
            ]
            return
        if "insert into fact.email" in texto:
            gmail_message_id = parametros[0]
            self._ultimo_gmail_message_id = gmail_message_id
            if gmail_message_id in self._lanzar_integrity_error_email_para:
                raise pyodbc.IntegrityError("23000", "duplicado UQ_Email_GmailMessageId")
            self._ultimo_fetchall = [(self._siguiente_identity,)]
            self._siguiente_identity += 1
            return
        if "insert into fact.documentorecibido" in texto:
            return
        if "update fact.estadointegracion" in texto:
            self._eventos.append(f"estado_integracion:{parametros[-1]}")
            return

    def fetchall(self):
        return self._ultimo_fetchall or []

    def fetchone(self):
        return self._ultimo_fetchall[0] if self._ultimo_fetchall else None


class _FakeConexion:
    def __init__(self, cursor: _FakeCursor, eventos: list[str]):
        self._cursor = cursor
        self._eventos = eventos

    def cursor(self) -> _FakeCursor:
        return self._cursor

    def commit(self) -> None:
        self._eventos.append("commit")

    def rollback(self) -> None:
        self._eventos.append("rollback")

    def close(self) -> None:
        self._eventos.append("close")


def _preparar_entorno(monkeypatch, tmp_path: Path) -> None:
    monkeypatch.setenv("SMARTNET_WORKER_ODBC_CONNECTION", "DRIVER={fake};")
    monkeypatch.setenv("SMARTNET_WORKER_STORAGE_ROOT", str(tmp_path))


def _conectar_fabrica(cursor: _FakeCursor, eventos: list[str]):
    def _conectar(_connection_string: str) -> _FakeConexion:
        return _FakeConexion(cursor, eventos)

    return _conectar


def test_etiqueta_se_aplica_solo_despues_del_commit(monkeypatch, tmp_path: Path):
    _preparar_entorno(monkeypatch, tmp_path)
    eventos: list[str] = []
    mensaje_id = "msg-1"
    cliente = _ClienteGmailFalso(
        etiquetas=_ETIQUETAS,
        mensajes_ids=[mensaje_id],
        payloads={mensaje_id: _payload_mensaje(mensaje_id)},
        eventos=eventos,
    )
    cursor = _FakeCursor(valores_configuracion=_CONFIGURACION_VALIDA, eventos=eventos)

    resultado = ejecutar(
        cliente=cliente, conectar=_conectar_fabrica(cursor, eventos), instante=_INSTANTE
    )

    assert resultado == 0
    indice_commit = eventos.index("commit")
    indice_etiqueta = next(i for i, e in enumerate(eventos) if e.startswith("aplicar_etiqueta:"))
    assert indice_commit < indice_etiqueta


def test_mensaje_fallido_no_se_etiqueta_y_no_aborta_el_run(monkeypatch, tmp_path: Path):
    _preparar_entorno(monkeypatch, tmp_path)
    eventos: list[str] = []
    mensaje_id = "msg-roto"

    class _ClienteRompeAdjunto(_ClienteGmailFalso):
        def obtener_adjunto(self, mensaje_id: str, attachment_id: str) -> bytes:
            raise RuntimeError("Gmail no respondio el adjunto")

    cliente = _ClienteRompeAdjunto(
        etiquetas=_ETIQUETAS,
        mensajes_ids=[mensaje_id],
        payloads={mensaje_id: _payload_mensaje(mensaje_id)},
        eventos=eventos,
    )
    cursor = _FakeCursor(valores_configuracion=_CONFIGURACION_VALIDA, eventos=eventos)

    resultado = ejecutar(
        cliente=cliente, conectar=_conectar_fabrica(cursor, eventos), instante=_INSTANTE
    )

    assert resultado == 1
    assert not any(e.startswith("aplicar_etiqueta:") for e in eventos)
    assert "rollback" in eventos
    assert any(e.startswith("estado_integracion:") for e in eventos)


def test_mensaje_sin_candidatos_no_produce_escrituras_ni_etiqueta(monkeypatch, tmp_path: Path):
    _preparar_entorno(monkeypatch, tmp_path)
    eventos: list[str] = []
    mensaje_id = "msg-sin-adjuntos"
    cliente = _ClienteGmailFalso(
        etiquetas=_ETIQUETAS,
        mensajes_ids=[mensaje_id],
        payloads={mensaje_id: _payload_mensaje(mensaje_id, nombre_adjunto="")},
        eventos=eventos,
    )
    cursor = _FakeCursor(valores_configuracion=_CONFIGURACION_VALIDA, eventos=eventos)

    resultado = ejecutar(
        cliente=cliente, conectar=_conectar_fabrica(cursor, eventos), instante=_INSTANTE
    )

    assert resultado == 0
    assert not any(e.startswith("aplicar_etiqueta:") for e in eventos)
    # el unico commit del run es el de EstadoIntegracion — ninguna transaccion por-mensaje se abrio
    assert eventos.count("commit") == 1
    assert any(e.startswith("estado_integracion:") for e in eventos)


def test_insertar_email_none_salta_descarga_y_reaplica_etiqueta(monkeypatch, tmp_path: Path):
    _preparar_entorno(monkeypatch, tmp_path)
    eventos: list[str] = []
    mensaje_id = "msg-ya-ingestado"

    class _ClienteCuentaDescargas(_ClienteGmailFalso):
        def __init__(self, **kwargs):
            super().__init__(**kwargs)
            self.descargas = 0

        def obtener_adjunto(self, mensaje_id: str, attachment_id: str) -> bytes:
            self.descargas += 1
            return super().obtener_adjunto(mensaje_id, attachment_id)

    cliente = _ClienteCuentaDescargas(
        etiquetas=_ETIQUETAS,
        mensajes_ids=[mensaje_id],
        payloads={mensaje_id: _payload_mensaje(mensaje_id)},
        eventos=eventos,
    )
    cursor = _FakeCursor(
        valores_configuracion=_CONFIGURACION_VALIDA,
        eventos=eventos,
        lanzar_integrity_error_email_para={mensaje_id},
    )

    resultado = ejecutar(
        cliente=cliente, conectar=_conectar_fabrica(cursor, eventos), instante=_INSTANTE
    )

    assert resultado == 0
    assert cliente.descargas == 0
    etiqueta_esperada = f"aplicar_etiqueta:{mensaje_id}:{_ETIQUETAS['fact-procesado']}"
    assert any(e == etiqueta_esperada for e in eventos)


def test_etiqueta_inexistente_falla_antes_de_buscar_mensajes(monkeypatch, tmp_path: Path):
    _preparar_entorno(monkeypatch, tmp_path)
    eventos: list[str] = []
    etiquetas_incompletas = {"Facturas": "Label_1"}  # falta 'fact-procesado'
    cliente = _ClienteGmailFalso(
        etiquetas=etiquetas_incompletas,
        mensajes_ids=["no-deberia-verse"],
        payloads={},
        eventos=eventos,
    )
    cursor = _FakeCursor(valores_configuracion=_CONFIGURACION_VALIDA, eventos=eventos)

    resultado = ejecutar(
        cliente=cliente, conectar=_conectar_fabrica(cursor, eventos), instante=_INSTANTE
    )

    assert resultado == 1
    assert not any(e.startswith("buscar_mensajes") for e in eventos)
    assert any(e.startswith("estado_integracion:") for e in eventos)


def test_configuracion_ingesta_faltante_falla_antes_de_crear_cliente(monkeypatch, tmp_path: Path):
    _preparar_entorno(monkeypatch, tmp_path)
    eventos: list[str] = []
    llamado = {"valor": False}

    class _ClienteQueNoDeberiaLlamarse(_ClienteGmailFalso):
        def resolver_etiquetas(self) -> dict[str, str]:
            llamado["valor"] = True
            return super().resolver_etiquetas()

    cliente = _ClienteQueNoDeberiaLlamarse(
        etiquetas=_ETIQUETAS, mensajes_ids=[], payloads={}, eventos=eventos
    )
    configuracion_incompleta = dict(_CONFIGURACION_VALIDA)
    configuracion_incompleta["FECHA_INICIO"] = None
    cursor = _FakeCursor(valores_configuracion=configuracion_incompleta, eventos=eventos)

    resultado = ejecutar(
        cliente=cliente, conectar=_conectar_fabrica(cursor, eventos), instante=_INSTANTE
    )

    assert resultado == 1
    assert llamado["valor"] is False


def test_ruta_relativa_se_escribe_de_verdad_bajo_la_raiz_configurada(monkeypatch, tmp_path: Path):
    _preparar_entorno(monkeypatch, tmp_path)
    eventos: list[str] = []
    mensaje_id = "msg-escritura"
    cliente = _ClienteGmailFalso(
        etiquetas=_ETIQUETAS,
        mensajes_ids=[mensaje_id],
        payloads={mensaje_id: _payload_mensaje(mensaje_id)},
        adjuntos={f"att-{mensaje_id}": b"bytes-reales-del-pdf"},
        eventos=eventos,
    )
    cursor = _FakeCursor(valores_configuracion=_CONFIGURACION_VALIDA, eventos=eventos)

    resultado = ejecutar(
        cliente=cliente, conectar=_conectar_fabrica(cursor, eventos), instante=_INSTANTE
    )

    assert resultado == 0
    archivos_escritos = list(tmp_path.rglob("*.pdf"))
    assert len(archivos_escritos) == 1
    assert archivos_escritos[0].read_bytes() == b"bytes-reales-del-pdf"


def test_credenciales_json_no_se_leen_cuando_se_inyecta_un_cliente_falso(
    monkeypatch, tmp_path: Path
):
    """`config.obtener_credenciales_gmail_json` no deberia invocarse en pruebas (no hay red ni
    variable de entorno de credenciales real): confirma que `cliente=...` evita ese camino."""
    _preparar_entorno(monkeypatch, tmp_path)
    monkeypatch.delenv("SMARTNET_WORKER_GMAIL_CREDENTIALS", raising=False)
    eventos: list[str] = []
    cliente = _ClienteGmailFalso(
        etiquetas=_ETIQUETAS, mensajes_ids=[], payloads={}, eventos=eventos
    )
    cursor = _FakeCursor(valores_configuracion=_CONFIGURACION_VALIDA, eventos=eventos)

    resultado = ejecutar(
        cliente=cliente, conectar=_conectar_fabrica(cursor, eventos), instante=_INSTANTE
    )

    assert resultado == 0


def test_base64_helper_no_usado_directamente_aqui_sanity_import():
    # Sanity: asegura que este archivo de pruebas compila con el import de base64 disponible para
    # quien extienda estos fakes con datos codificados reales de Gmail.
    assert base64.urlsafe_b64decode(base64.urlsafe_b64encode(b"x")) == b"x"
