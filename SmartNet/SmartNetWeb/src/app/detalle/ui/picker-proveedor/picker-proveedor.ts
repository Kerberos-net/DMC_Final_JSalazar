import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { ProveedorService } from '../../../catalogos/data-access/proveedor.service';
import { Proveedor } from '../../../catalogos/data-access/proveedor.model';

/**
 * Presentational picker dialog (BACKLOG #18 PR8, spa-picker-proveedor). Wraps a native
 * `<dialog>` toggled through its `open` IDL attribute — same jsdom-safe pattern as
 * `confirmar-reproceso` (no CDK/Material). Consumes {@link ProveedorService} for the debounced
 * search; owns only view state (the active-row index). On select it emits `{ codigo, ruc }` and
 * closes — it never issues a PATCH; `detalle-page` routes the choice through the existing
 * `borradorFactura` / `onCambiosFactura` draft path.
 *
 * Styling reuses PR1's modal radius/elevation tokens only — no new design token, so the palette
 * guard / `contraste.spec.ts` stay untouched.
 */
@Component({
  selector: 'app-picker-proveedor',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './picker-proveedor.html',
  styleUrl: './picker-proveedor.css',
})
export class PickerProveedor {
  private readonly proveedores = inject(ProveedorService);
  private readonly dialogo = viewChild.required<ElementRef<HTMLDialogElement>>('dialogo');

  readonly seleccionar = output<{ codigo: string; ruc: string | null }>();
  readonly cerrar = output<void>();

  readonly resultados = this.proveedores.resultados;
  readonly hayMas = this.proveedores.hayMas;
  readonly buscando = this.proveedores.buscando;
  readonly indiceActivo = signal(0);

  abrir(): void {
    this.proveedores.limpiar();
    this.indiceActivo.set(0);
    this.dialogo().nativeElement.open = true;
    queueMicrotask(() => this.enfocarBusqueda());
  }

  onBuscar(termino: string): void {
    this.indiceActivo.set(0);
    this.proveedores.buscar(termino);
  }

  masResultados(): void {
    void this.proveedores.masResultados();
  }

  elegir(proveedor: Proveedor): void {
    this.cerrarDialogo();
    this.seleccionar.emit({ codigo: proveedor.codigo, ruc: proveedor.ruc });
  }

  onKeydown(evento: KeyboardEvent): void {
    const filas = this.resultados();
    switch (evento.key) {
      case 'ArrowDown':
        evento.preventDefault();
        this.indiceActivo.update((i) => Math.min(i + 1, Math.max(filas.length - 1, 0)));
        break;
      case 'ArrowUp':
        evento.preventDefault();
        this.indiceActivo.update((i) => Math.max(i - 1, 0));
        break;
      case 'Enter': {
        const elegido = filas[this.indiceActivo()];
        if (elegido) {
          evento.preventDefault();
          this.elegir(elegido);
        }
        break;
      }
      case 'Escape':
        evento.preventDefault();
        this.cerrarDialogo();
        this.cerrar.emit();
        break;
      case 'Tab':
        this.atraparFoco(evento);
        break;
    }
  }

  onCerrar(): void {
    this.cerrarDialogo();
    this.cerrar.emit();
  }

  private cerrarDialogo(): void {
    this.dialogo().nativeElement.open = false;
  }

  private enfocarBusqueda(): void {
    this.dialogo().nativeElement.querySelector<HTMLInputElement>('[data-testid="picker-buscar"]')?.focus();
  }

  private focusables(): HTMLElement[] {
    return Array.from(
      this.dialogo().nativeElement.querySelectorAll<HTMLElement>(
        'input, button, [tabindex]:not([tabindex="-1"])'
      )
    ).filter((el) => !el.hasAttribute('disabled'));
  }

  private atraparFoco(evento: KeyboardEvent): void {
    const items = this.focusables();
    if (items.length === 0) {
      return;
    }
    const primero = items[0];
    const ultimo = items[items.length - 1];
    const activo = document.activeElement;
    if (evento.shiftKey && activo === primero) {
      evento.preventDefault();
      ultimo.focus();
    } else if (!evento.shiftKey && activo === ultimo) {
      evento.preventDefault();
      primero.focus();
    }
  }
}
