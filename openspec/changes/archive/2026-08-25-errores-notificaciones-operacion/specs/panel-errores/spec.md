# Delta for panel-errores

## Resolution (owner decision, 2026-08-25)

The delta drafted in the first pass of this spec ("indica si la notificación de respaldo se
envió") is **withdrawn**. It conflicted with the ratified design (D7): outbox dispatch errors
(and their notification status) live at invoice level on `fact.OutboxEventIntegracion`, while
`panel-errores` renders the document-level `ErrorProcesamiento` projection from `#13`. Surfacing
invoice-level notification status there would require a redesign of how the bandeja reads across
that boundary — out of scope for item #17.

`panel-errores` ships **no changes** in this item. The existing behavior (already verified against
TECH-DESIGN.md L663-664 during exploration: `clasificacion`/`mensaje`/`ocurridoEn` render per
error, TRANSITORIO/DIFERIBLE/PERMANENTE distinction exists, OBSOLETO already excluded upstream by
`OrigenBandeja`/`SqlBandejaRepository`'s `Clasificacion <> 'OBSOLETO'` filter) stands unmodified.

No requirements are added, modified, or removed by this delta.
