"""smartnet_worker — worker Python de SmartNet (primer paquete Python del repositorio).

Ambito de este paquete (BACKLOG #4): un scraper de un solo run que lee el tipo de cambio
publicado por la SBS e inserta una fila `Origen='SBS'` en `fact.TipoCambio`, dejando registro del
intento en `fact.EstadoIntegracion`. Sin scheduler, sin polling, sin reintentos automaticos — ver
spec.md Non-Goals.
"""
