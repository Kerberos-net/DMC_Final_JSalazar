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


def _sin_comentarios(texto: str) -> str:
    return _COMENTARIO_LINEA_RE.sub("", texto)


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
