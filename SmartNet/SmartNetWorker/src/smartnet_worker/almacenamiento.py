"""Escritura IO-only en el volumen compartido (BACKLOG #5, design.md Decision 5).

`escribir` es el unico punto del paquete que toca disco fuera de la lectura de configuracion. Su
unica decision es la guarda de contencion: `gmail.sanitizar_nombre_archivo`/`ruta_relativa` ya
hacen la traversal imposible por construccion, pero esta funcion vuelve a comprobarlo aqui —
"defensa en profundidad, porque la garantia de arriba es una propiedad de codigo que puede
editarse" (design.md, Decision 5). Reescribir la misma ruta con los mismos bytes es un no-op:
el nombre en disco ya incluye el hash del contenido (Decision 5), asi que un reintento tras un
commit fallido sobrescribe el mismo archivo en vez de acumular copias.
"""

from __future__ import annotations

from pathlib import Path


class ContencionError(Exception):
    """La ruta resuelta cae fuera de la raiz del volumen configurado — nunca se escribe."""


def escribir(raiz: Path, ruta_relativa: str, datos: bytes) -> Path:
    """Escribe `datos` en `raiz / ruta_relativa`, creando los directorios intermedios que hagan
    falta. Lanza `ContencionError` sin escribir nada si la ruta resuelta no queda contenida dentro
    de `raiz` — una ruta relativa con `..` o una ruta ya absoluta que apunte fuera."""
    raiz_resuelta = raiz.resolve()
    destino = (raiz / ruta_relativa).resolve()
    if not destino.is_relative_to(raiz_resuelta):
        raise ContencionError(
            f"La ruta '{ruta_relativa}' resuelve fuera de la raiz configurada '{raiz_resuelta}'."
        )

    destino.parent.mkdir(parents=True, exist_ok=True)
    destino.write_bytes(datos)
    return destino
