import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { CorreccionFacturaRequest, FacturaRespuesta } from '../../models/factura.model';
import { dosDecimales, importeOpcional } from '../../../shared/formato';

/**
 * Presentational (dumb) component: the factura header two-column field grid
 * (spa-visual-detalle-validacion / pantalla-detalle-validacion). Each label is secondary-style
 * text ABOVE its input, never a wrapping `<label>`.
 *
 * Field split by accounting cost (design "`factura-form` field grid — binding model"):
 *  - Editable, pure SPA binding (already on GET + PATCH): `monto` (`totalOrig`), `moneda`,
 *    `fechaEmision`, `proveedorCodigo`, `rucProveedor`. Emits one partial
 *    {@link CorreccionFacturaRequest} per edit; the container batches them and sends on
 *    "Guardar avance". `buscarProveedor` just asks the container to open its picker.
 *  - Editable via the .NET PATCH delta (BACKLOG #18 PR5): `tipoComprobante` (select of the 3
 *    comprobante types) and `numero`, bound through the same `cambios` → `borradorFactura` → PATCH
 *    path as every other editable field.
 *  - Read-only display: `base imponible` / `IGV` (from the `AsientoRespuesta.basePEN` / `igvPEN`
 *    projection — BACKLOG #18 PR6; `—` placeholder while there is no asiento vigente),
 *    `tipo de cambio (venta)` (design D6 — the rate the engine actually uses), and derived
 *    `mes` / `día` contable from `AsientoContable.FechaContable`.
 *
 * The duplicado / P00000 blocking banners live in `detalle/ui/indicadores-factura` (design D4);
 * only the per-field OCR-missing highlight and the dedicated TC-faltante indicator stay here.
 */
@Component({
  selector: 'app-factura-form',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './factura-form.html',
  styleUrl: './factura-form.css',
})
export class FacturaForm {
  readonly factura = input.required<FacturaRespuesta>();
  readonly tipoCambioVenta = input<number | null>(null);
  /** `AsientoContable.FechaContable` (`YYYY-MM-DD`) when an asiento exists — drives the derived
   * `mes` / `día` contable rows. Null when there is no asiento vigente yet. */
  readonly fechaContable = input<string | null>(null);
  /** `AsientoContable.BasePEN` / `IgvPEN` projected read-only on `AsientoRespuesta` (BACKLOG #18
   * PR6). Null when there is no asiento vigente yet — the row then shows the `—` placeholder. */
  readonly basePEN = input<number | null>(null);
  readonly igvPEN = input<number | null>(null);
  readonly editable = input(true);

  readonly cambios = output<CorreccionFacturaRequest>();
  readonly confirmarAfectacion = output<boolean>();
  /** Asks the container to open its proveedor picker (the container owns the actual lookup). */
  readonly buscarProveedor = output<void>();

  /** BACKLOG #18 PR5 — el conjunto aceptado por el backend (`fact.Factura.TipoComprobante`
   * CHAR(2), REGLAS.md §5); la validación real vive en `ValidacionDeCorreccion` server-side. */
  readonly tiposComprobante: readonly { readonly codigo: string; readonly etiqueta: string }[] = [
    { codigo: '01', etiqueta: 'Factura' },
    { codigo: '03', etiqueta: 'Boleta' },
    { codigo: '07', etiqueta: 'Nota de crédito' },
  ];

  readonly montoTexto = computed(() => dosDecimales(this.factura().totalOrig));

  /** Coarsest correct OCR-missing signal: `FacturaRespuesta` only exposes the invoice-wide
   * `tieneCamposNoExtraidos` boolean (no per-field granularity server-side yet), so every
   * OCR-sourced field carries the highlight together. */
  readonly campoResaltado = computed(() => this.factura().tieneCamposNoExtraidos);

  readonly afectacionNoVerificada = computed(() => this.factura().afectacionMixta === null);

  /** design D6 — dedicated indicator: foreign currency and the TC *venta* the engine uses is
   * absent, so the converted amount shows as 0.00. */
  readonly tipoCambioFaltante = computed(
    () => this.factura().moneda !== 'PEN' && this.tipoCambioVenta() === null
  );

  readonly tipoCambioTexto = computed(() => {
    const tc = this.tipoCambioVenta();
    if (tc !== null) return String(tc);
    return this.factura().moneda === 'PEN' ? 'No aplica' : '0.00';
  });

  readonly baseImponibleTexto = computed(() => importeOpcional(this.basePEN()));
  readonly igvTexto = computed(() => importeOpcional(this.igvPEN()));

  readonly mesContable = computed(() => this.fechaContable()?.slice(5, 7) ?? '—');
  readonly diaContable = computed(() => this.fechaContable()?.slice(8, 10) ?? '—');

  onCampoInput(campo: keyof CorreccionFacturaRequest, valor: string): void {
    this.cambios.emit({ [campo]: valor } as CorreccionFacturaRequest);
  }

  onMonto(valor: string): void {
    this.cambios.emit({ totalOrig: valor === '' ? null : Number(valor) });
  }
}
