import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { CorreccionFacturaRequest, FacturaRespuesta } from '../../models/factura.model';

/**
 * Presentational (dumb) component: factura header display + editable fields (spec.md "Side-by-side
 * layout ... form MUST show fields for factura header data, TipoCambioVenta (when applicable)").
 * Emits one partial {@link CorreccionFacturaRequest} per edited field — the container accumulates
 * them into a draft and sends the merged patch on "Guardar avance" (this batch's own UX decision,
 * see apply-progress: líneas send eagerly per spec.md's línea scenario, factura fields batch).
 *
 * diseno-visual-spa-item-12 (design D9/D10, spa-visual-detalle-validacion spec): also renders the
 * 4 indicator fields as `.alerta--bloqueante`/`.alerta--informativa`, and the afectación
 * confirmation control -- emits `confirmarAfectacion`, stays presentational (the container is the
 * one that calls `POST /confirmar-afectacion`, matching the `cambios` output's own split).
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
  readonly editable = input(true);

  readonly cambios = output<CorreccionFacturaRequest>();
  readonly confirmarAfectacion = output<boolean>();

  /** item #18 PR3: the duplicado / P00000 blocking banners were hoisted OUT of this component into
   * `detalle/ui/indicadores-factura`, rendered by `detalle-page` above the split (design D4). Only
   * the informational treatment (OCR-missing / afectación-no-verificada) stays here for now. */
  readonly esInformativa = computed(
    () => this.factura().tieneCamposNoExtraidos || this.factura().afectacionMixta === null
  );
  readonly afectacionNoVerificada = computed(() => this.factura().afectacionMixta === null);

  onCampoInput(campo: keyof CorreccionFacturaRequest, valor: string): void {
    this.cambios.emit({ [campo]: valor } as CorreccionFacturaRequest);
  }
}
