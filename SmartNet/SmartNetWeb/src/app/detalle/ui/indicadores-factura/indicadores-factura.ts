import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Presentational (dumb) component: the up-to-three factura-level indicator banners, hoisted OUT of
 * `factura-form` and rendered by `detalle-page` above the documento/formulario split
 * (spa-visual-detalle-validacion "Indicator banners rendered above the split", design D4).
 *
 * Pure inputs, zero logic: `detalle-page` owns the conditions (`posibleDuplicado` /
 * `esProveedorGenerico` come straight from `FacturaRespuesta`; `tipoCambioFaltante` is the
 * container's `moneda !== 'PEN' && asiento.tipoCambioVenta === null` computed, design D6). This
 * component never decides whether a banner blocks "Validar" -- that gate lives in the container.
 */
@Component({
  selector: 'app-indicadores-factura',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './indicadores-factura.html',
  styleUrl: './indicadores-factura.css',
})
export class IndicadoresFactura {
  readonly posibleDuplicado = input(false);
  readonly esProveedorGenerico = input(false);
  readonly tipoCambioFaltante = input(false);
}
