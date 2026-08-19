"""Analogo Python de `NoWriteToDboStructuralTests` (SmartNet.TiposCambio.Infrastructure.Tests):
escaneo literal, sin comentarios, del codigo fuente del worker — nunca debe mencionar `dbo.`
(ADR 0003: Python solo toca `fact.TipoCambio`, `fact.Email`, `fact.DocumentoRecibido`,
`fact.Configuracion` (solo lectura) y `fact.EstadoIntegracion`).

BACKLOG #5 amplia el escaneo con dos reglas nuevas, ambas sobre el mismo `_SRC` completo:
- Ninguna llamada de borrado/papelera de Gmail (`.delete(`/`.trash(`) — ADR 0017 nunca borra un
  correo, solo aplica su propia etiqueta de "procesado" (ver `gmail_client.ClienteGmail
  .aplicar_etiqueta`, la unica llamada mutante de Gmail del paquete).
- Ninguna mencion a una tabla propiedad de .NET (`fact.Factura`, `fact.AdjuntoManual`,
  `fact.Procesamiento`, `fact.DatosExtraidos`) — este item se detiene en
  `DocumentoRecibido.Estado='DESCARGADO'` (spec.md, Non-Goals); `Procesamiento`/`DatosExtraidos`
  son responsabilidad del item #6, nunca de este paquete.
"""

import re
from pathlib import Path

_SRC = Path(__file__).resolve().parents[2] / "src" / "smartnet_worker"
_COMENTARIO_LINEA_RE = re.compile(r"#.*$", re.MULTILINE)

_LLAMADAS_BORRADO_GMAIL_PROHIBIDAS = (".delete(", ".trash(")

_TABLAS_AJENAS_PROHIBIDAS = (
    "fact.factura",
    "fact.adjuntomanual",
    "fact.procesamiento",
    "fact.datosextraidos",
)


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
                f"{archivo.name} menciona '{tabla}' fuera de un comentario — ese item es "
                "responsabilidad de #6 (o del lado .NET), nunca de este paquete (spec.md, "
                "Non-Goals)."
            )
            assert tabla not in contenido, mensaje
