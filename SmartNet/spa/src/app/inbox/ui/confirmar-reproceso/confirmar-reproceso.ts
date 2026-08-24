import { ChangeDetectionStrategy, Component, ElementRef, output, viewChild } from '@angular/core';

/**
 * Presentational (dumb) component: wraps a native `<dialog>` (design D6 -- no CDK/Material,
 * `window.confirm` is untestable under jsdom/vitest). `open()` toggles the `open` attribute
 * directly rather than calling `showModal()`/`close()`: jsdom 28 (this repo's test environment)
 * implements the `<dialog>` element's `open` IDL attribute but not those methods, and this stays
 * behaviorally identical (non-modal open/close) in a real browser too.
 */
@Component({
  selector: 'app-confirmar-reproceso',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './confirmar-reproceso.html',
})
export class ConfirmarReproceso {
  private readonly dialogo = viewChild.required<ElementRef<HTMLDialogElement>>('dialogo');

  readonly confirmar = output<void>();
  readonly cancelar = output<void>();

  open(): void {
    this.dialogo().nativeElement.open = true;
  }

  onConfirmar(): void {
    this.dialogo().nativeElement.open = false;
    this.confirmar.emit();
  }

  onCancelar(): void {
    this.dialogo().nativeElement.open = false;
    this.cancelar.emit();
  }
}
