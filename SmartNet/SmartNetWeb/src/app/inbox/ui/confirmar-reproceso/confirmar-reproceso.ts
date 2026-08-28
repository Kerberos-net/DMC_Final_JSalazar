import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  output,
  signal,
  viewChild,
} from '@angular/core';

/**
 * Presentational (dumb) component: wraps a native `<dialog>` (design D6 -- no CDK/Material,
 * `window.confirm` is untestable under jsdom/vitest). `open()` toggles the `open` attribute
 * directly rather than calling `showModal()`: jsdom 28 (this repo's test environment) implements
 * the `<dialog>` `open` IDL attribute but not `showModal()`, and `::backdrop` only paints for a
 * dialog in the top layer -- which `showModal()` alone creates. So the scrim is a real element
 * (`confirmar-reproceso__fondo`) driven by the additive `abierto` signal, kept in lockstep with
 * every `nativeElement.open` write (design D4). Backdrop click and Escape both route to
 * `onCancelar()` -- a new trigger for the existing cancel affordance, no new logic path.
 */
@Component({
  selector: 'app-confirmar-reproceso',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './confirmar-reproceso.html',
  styleUrl: './confirmar-reproceso.css',
})
export class ConfirmarReproceso {
  private readonly dialogo = viewChild.required<ElementRef<HTMLDialogElement>>('dialogo');
  private readonly botonCancelar = viewChild.required<ElementRef<HTMLButtonElement>>('botonCancelar');

  readonly confirmar = output<void>();
  readonly cancelar = output<void>();

  /** Additive (design D4): drives the manual backdrop element, mirrors `nativeElement.open`. */
  readonly abierto = signal(false);

  private elementoPrevioConFoco: HTMLElement | null = null;

  open(): void {
    this.elementoPrevioConFoco = document.activeElement as HTMLElement | null;
    this.dialogo().nativeElement.open = true;
    this.abierto.set(true);
    this.botonCancelar().nativeElement.focus();
  }

  onConfirmar(): void {
    this.cerrar();
    this.confirmar.emit();
  }

  onCancelar(): void {
    this.cerrar();
    this.cancelar.emit();
  }

  private cerrar(): void {
    this.dialogo().nativeElement.open = false;
    this.abierto.set(false);
    this.elementoPrevioConFoco?.focus();
    this.elementoPrevioConFoco = null;
  }
}
