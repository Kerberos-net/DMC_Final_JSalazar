"""Analogo Python de `NoWriteToDboStructuralTests` (SmartNet.TiposCambio.Infrastructure.Tests):
escaneo literal, sin comentarios, del codigo fuente del worker — nunca debe mencionar `dbo.`
(ADR 0003: Python solo toca `fact.TipoCambio`, `fact.Email`, `fact.DocumentoRecibido`,
`fact.Configuracion` (solo lectura) y `fact.EstadoIntegracion`).

BACKLOG #5 amplia el escaneo con dos reglas nuevas, ambas sobre el mismo `_SRC` completo:
- Ninguna llamada de borrado/papelera de Gmail (`.delete(`/`.trash(`) — ADR 0017 nunca borra un
  correo, solo aplica su propia etiqueta de "procesado" (ver `gmail_client.ClienteGmail
  .aplicar_etiqueta`, la unica llamada mutante de Gmail del paquete).
- Ninguna mencion a una tabla propiedad de .NET (`fact.Factura`, `fact.AdjuntoManual`,
  `fact.FacturaExtraccion`) — la promocion a factura es responsabilidad de #7/.NET, nunca de este
  paquete (spec.md, Non-Goals). `fact.Procesamiento`/`fact.DatosExtraidos` YA NO estan en esta lista
  desde BACKLOG #6: ese item las escribe legitimamente (`procesamiento_repo.py`), la disciplina de
  particion de ADR 0003 las movio de "ajenas" a "propias" del paquete.

BACKLOG #6 amplia el escaneo con una tercera regla: ningun modulo del camino de extraccion importa
`requests`/`urllib`/`http`/`socket` — el escaneo mecanico de la decision de negocio resuelta "los
documentos nunca salen de la organizacion" (design.md, Decision 1). `cli_gmail.py`/`gmail_client.py`
quedan fuera de este escaneo especifico porque SI hablan con la API de Gmail por diseno (ADR 0017);
la regla aplica a los modulos que deciden y persisten sobre el contenido de un documento ya
descargado, nunca a la ingesta.

BACKLOG #14 (Fase 4, design.md Decision D6) amplia el escaneo con una cuarta regla, el
"import-graph test" de la tarea 4.3: el dispatcher destination-agnostic del consumidor
(`despacho_outbox.py`, y su Protocol `reclamo.py`) NUNCA debe mencionar `READPAST` ni importar
`outbox_repo`/`pyodbc` — la unica frontera SQL-Server-especifica del consumidor vive en
`outbox_repo.py` (spec.md, "Dispatcher depends only on the interface").
"""

import re
from pathlib import Path

_SRC = Path(__file__).resolve().parents[2] / "src" / "smartnet_worker"
_COMENTARIO_LINEA_RE = re.compile(r"#.*$", re.MULTILINE)

_LLAMADAS_BORRADO_GMAIL_PROHIBIDAS = (".delete(", ".trash(")

_TABLAS_AJENAS_PROHIBIDAS = (
    "fact.factura",
    "fact.adjuntomanual",
    "fact.facturaextraccion",
)

# BACKLOG #6, design.md Decision 1/Threat Matrix: el camino de extraccion (parseo XML/PDF,
# asociacion, persistencia) nunca debe abrir una conexion de red — "documentos nunca salen de la
# organizacion" es una decision de negocio resuelta, esta es su version mecanica.
_MODULOS_CAMINO_EXTRACCION = (
    "ubl.py",
    "pdf_texto.py",
    "pdf_lectura.py",
    "comprobante.py",
    "afectacion.py",
    "errores.py",
    "procesamiento_repo.py",
)
_IMPORTS_DE_RED_PROHIBIDOS = ("import requests", "import urllib", "import http", "import socket")

# BACKLOG #14, tarea 4.3: el dispatcher destination-agnostic (design.md Decision D6) nunca debe
# depender de la frontera SQL-Server-especifica del consumidor.
_MODULOS_DESTINATION_AGNOSTIC = ("reclamo.py", "despacho_outbox.py")
_TERMINOS_READPAST_PROHIBIDOS = ("readpast", "import pyodbc", "outbox_repo")


_DOCSTRING_RE = re.compile(r'""".*?"""|\'\'\'.*?\'\'\'', re.DOTALL)


def _sin_comentarios(texto: str) -> str:
    return _COMENTARIO_LINEA_RE.sub("", texto)


def _sin_docstrings_ni_comentarios(texto: str) -> str:
    """Igual que `_sin_comentarios`, mas elimina todo docstring (modulo/clase/funcion). BACKLOG
    #14, tarea 4.3: `reclamo.py`/`despacho_outbox.py` documentan en prosa su relacion con la
    frontera SQL-Server-especifica del consumidor (por que NO la implementan) — esa prosa
    legitimamente menciona los mismos terminos que esta prueba prohibe en CODIGO real."""
    return _DOCSTRING_RE.sub("", _sin_comentarios(texto))


def _archivos_fuente() -> list[Path]:
    archivos = sorted(_SRC.glob("*.py"))
    assert archivos, "No se encontraron modulos en smartnet_worker — el escaneo no probaria nada."
    return archivos


def test_ningun_modulo_del_worker_menciona_dbo():
    for archivo in _archivos_fuente():
        contenido = _sin_comentarios(archivo.read_text(encoding="utf-8"))
        mensaje = f"{archivo.name} menciona 'dbo.' fuera de un comentario."
        assert "dbo." not in contenido.lower(), mensaje


def test_ningun_modulo_del_worker_llama_delete_o_trash_de_gmail():
    for archivo in _archivos_fuente():
        contenido = _sin_comentarios(archivo.read_text(encoding="utf-8"))
        for llamada in _LLAMADAS_BORRADO_GMAIL_PROHIBIDAS:
            mensaje = (
                f"{archivo.name} menciona '{llamada}' fuera de un comentario — ADR 0017 nunca "
                "borra ni mueve a papelera un correo, solo aplica su etiqueta de procesado."
            )
            assert llamada not in contenido, mensaje


def test_ningun_modulo_del_worker_menciona_tablas_propiedad_de_dotnet():
    for archivo in _archivos_fuente():
        contenido = _sin_comentarios(archivo.read_text(encoding="utf-8")).lower()
        for tabla in _TABLAS_AJENAS_PROHIBIDAS:
            mensaje = (
                f"{archivo.name} menciona '{tabla}' fuera de un comentario — la promocion a "
                "factura es responsabilidad de #7 (o del lado .NET), nunca de este paquete "
                "(spec.md, Non-Goals)."
            )
            assert tabla not in contenido, mensaje


def test_ningun_modulo_del_camino_de_extraccion_importa_bibliotecas_de_red():
    for nombre in _MODULOS_CAMINO_EXTRACCION:
        archivo = _SRC / nombre
        assert archivo.exists(), f"{nombre} no existe en {_SRC} — el escaneo no probaria nada."
        contenido = _sin_comentarios(archivo.read_text(encoding="utf-8")).lower()
        for importacion in _IMPORTS_DE_RED_PROHIBIDOS:
            mensaje = (
                f"{nombre} menciona '{importacion}' fuera de un comentario — el camino de "
                "extraccion nunca abre una conexion de red (design.md, Decision 1: "
                "\"documentos nunca salen de la organizacion\")."
            )
            assert importacion not in contenido, mensaje


def test_dispatcher_destination_agnostic_nunca_importa_outbox_repo_ni_readpast():
    for nombre in _MODULOS_DESTINATION_AGNOSTIC:
        archivo = _SRC / nombre
        assert archivo.exists(), f"{nombre} no existe en {_SRC} — el escaneo no probaria nada."
        contenido = _sin_docstrings_ni_comentarios(archivo.read_text(encoding="utf-8")).lower()
        for termino in _TERMINOS_READPAST_PROHIBIDOS:
            mensaje = (
                f"{nombre} menciona '{termino}' fuera de un comentario — la unica frontera "
                "SQL-Server-especifica del consumidor (READPAST, ADR 0002) vive en "
                "outbox_repo.py, nunca en el dispatcher destination-agnostic (design.md "
                "Decision D6; spec.md 'Dispatcher depends only on the interface')."
            )
            assert termino not in contenido, mensaje


# BACKLOG #17, Fase 4 (tasks.md 4.5): el consumidor de CommandQueue solo puede tocar sus tres
# tablas propias -- CommandQueue (contrato), Procesamiento (privada de Python) y EstadoIntegracion
# (compartida). `_TERMINOS_READPAST_PROHIBIDOS` (arriba) ya prueba, para el escaneo generico de
# `dbo.`/tablas .NET, que ningun modulo del worker (incluido este) menciona `fact.Factura`.
_TABLAS_PERMITIDAS_COMMAND_QUEUE = (
    "fact.commandqueue",
    "fact.procesamiento",
    "fact.estadointegracion",
)


def test_consumidor_command_queue_solo_toca_sus_tres_tablas():
    archivo = _SRC / "cli_command_queue.py"
    assert archivo.exists(), "cli_command_queue.py no existe -- el escaneo no probaria nada."
    contenido = _sin_comentarios(archivo.read_text(encoding="utf-8")).lower()

    import re as _re

    referencias = set(_re.findall(r"fact\.[a-z]+", contenido))
    permitidas = set(_TABLAS_PERMITIDAS_COMMAND_QUEUE)
    inesperadas = referencias - permitidas
    assert not inesperadas, (
        f"cli_command_queue.py referencia tablas fuera de las tres permitidas: {inesperadas} "
        "(ADR 0003 -- CommandQueue/Procesamiento/EstadoIntegracion, nunca fact.Factura ni otra)."
    )
