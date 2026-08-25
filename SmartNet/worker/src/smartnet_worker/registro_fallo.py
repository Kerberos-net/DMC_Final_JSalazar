"""Implementacion real de `despacho_outbox.RegistroDeFallo` (BACKLOG #17, design.md D2/D4) que
`cli_outbox.py` inyecta en `despachar_evento`. Compone tres piezas ya probadas por separado:
`outbox_repo.OutboxRepo` (persistencia), `politica_notificacion.debe_notificar` (disparo) y
`notificaciones.notificar` (envio) -- este modulo solo las orquesta.

`fabrica_canales` es una funcion inyectada, NUNCA construida eagerly en `__init__`: los canales
reales (`TelegramCanal`/`CorreoCanal`) necesitan credenciales de entorno + `fact.Configuracion`, y
la mayoria de los fallos (TRANSITORIO sin agotar, DIFERIBLE en un reintento posterior) no notifican
-- exigir esas credenciales siempre romperia un despliegue que todavia no las configuro, para un
evento que ni siquiera iba a notificar."""

from __future__ import annotations

from collections.abc import Callable, Sequence
from datetime import datetime

from smartnet_worker.clasificacion_despacho import ResultadoDespacho
from smartnet_worker.errores import Clasificacion
from smartnet_worker.notificaciones import CanalDeAviso
from smartnet_worker.politica_notificacion import debe_notificar, redactar


class RegistroDeFalloConNotificacion:
    def __init__(
        self,
        repo,
        *,
        cursor,
        fabrica_canales: Callable[[], Sequence[CanalDeAviso]],
        notificar: Callable[[Sequence[CanalDeAviso], str, datetime, object], None],
    ):
        self._repo = repo
        self._cursor = cursor
        self._fabrica_canales = fabrica_canales
        self._notificar = notificar

    def registrar(
        self,
        evento_id: int,
        integracion: str,
        resultado: ResultadoDespacho,
        mensaje: str,
        instante: datetime,
    ) -> None:
        clasificacion_previa_texto = self._repo.leer_clasificacion(evento_id, integracion)
        clasificacion_previa = (
            Clasificacion(clasificacion_previa_texto)
            if clasificacion_previa_texto is not None
            else None
        )

        self._repo.marcar_fallo(
            evento_id=evento_id,
            destino=integracion,
            clasificacion=resultado.clasificacion.value,
            mensaje=mensaje,
            proximo_intento_en=resultado.proximo_intento_en,
            ahora=instante,
        )

        notificar_ahora = debe_notificar(
            resultado.clasificacion,
            agotado=resultado.agotado,
            clasificacion_previa=clasificacion_previa,
        )
        if not notificar_ahora:
            return

        canales = self._fabrica_canales()
        texto = redactar(
            integracion=integracion,
            factura_id=evento_id,
            clasificacion=resultado.clasificacion,
            mensaje_error=mensaje,
        )
        self._notificar(canales, texto, instante, self._cursor)
