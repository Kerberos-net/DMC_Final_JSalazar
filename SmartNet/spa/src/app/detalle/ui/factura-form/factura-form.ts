import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { CorreccionFacturaRequest, FacturaRespuesta } from '../../models/factura.model';

/**
 * Presentational (dumb) component: factura header display + editable fields (spec.md "Side-by-side
 * layout ... form MUST show fields for factura header data, TipoCambioVenta (when applicable)").
 * Emits one partial {@link CorreccionFacturaRequest} per edited field — the container accumulates
 * them into a draft and sends the merged patch on "Guardar avance" (this batch's own UX decision,
 * see apply-progress: líneas send eagerly per spec.md's línea scenario, factura fields batch).
 */
@Component({
  selector: 'app-factura-form',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './factura-form.html',
})
export class FacturaForm {
  readonly factura = input.required<FacturaRespuesta>();
  readonly tipoCambioVenta = input<number | null>(null);
  readonly editable = input(true);

  readonly cambios = output<CorreccionFacturaRequest>();

  onCampoInput(campo: keyof CorreccionFacturaRequest, valor: string): void {
    this.cambios.emit({ [campo]: valor } as CorreccionFacturaRequest);
  }
}
