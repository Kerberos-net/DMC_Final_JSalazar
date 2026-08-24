import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { Bloque, LineaAsientoRequest, LineaRespuesta, TipoLinea } from '../../models/asiento.model';
import { EntradaAuditoriaRespuesta } from '../../models/historial.model';
import { HistorialCorreccion } from '../historial-correccion/historial-correccion';

interface BorradorLinea {
  bloque: Bloque;
  tipo: TipoLinea;
  debe: number;
  haber: number;
  cuentaCodigo: string;
}

const BORRADOR_VACIO: BorradorLinea = { bloque: 'PRINCIPAL', tipo: 'D', debe: 0, haber: 0, cuentaCodigo: '' };

function aLineaRequest(orden: number, borrador: BorradorLinea): LineaAsientoRequest {
  return {
    orden,
    bloque: borrador.bloque,
    tipo: borrador.tipo,
    debe: borrador.debe,
    haber: borrador.haber,
    cuentaCodigo: borrador.cuentaCodigo || null,
    cuentaDescripcion: null,
    ctaReflejaCodigo: null,
    ctaPuenteCodigo: null,
  };
}

/**
 * Presentational (dumb) component: inline edit-in-place per línea, explicit "add línea", and
 * delete-with-confirmation (spec.md "Asiento líneas are editable inline"). Per that spec's own
 * scenario ("only that línea's edit is sent"), every confirm here emits IMMEDIATELY — no local
 * batching, unlike `FacturaForm`'s draft (see apply-progress for that documented UX split).
 * Línea order is NOT a persisted invariant (spec.md) — `Orden` sent is just `1..n` by row position.
 */
@Component({
  selector: 'app-asiento-lineas',
  standalone: true,
  imports: [HistorialCorreccion],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './asiento-lineas.html',
  styleUrl: './asiento-lineas.css',
})
export class AsientoLineas {
  readonly lineas = input.required<readonly LineaRespuesta[]>();
  readonly editable = input(true);
  /** tasks.md 4.9 -- wires the D4 `<details>` panel; empty by default when no historial loaded yet. */
  readonly historial = input<readonly EntradaAuditoriaRespuesta[]>([]);

  readonly editarLinea = output<{ lineaId: number; linea: LineaAsientoRequest }>();
  readonly agregarLinea = output<LineaAsientoRequest>();
  readonly eliminarLinea = output<number>();

  readonly editandoId = signal<number | null>(null);
  readonly borradorEdicion = signal<BorradorLinea>(BORRADOR_VACIO);
  readonly pendienteEliminarId = signal<number | null>(null);
  readonly agregandoNueva = signal(false);
  readonly borradorNueva = signal<BorradorLinea>(BORRADOR_VACIO);

  iniciarEdicion(linea: LineaRespuesta): void {
    this.editandoId.set(linea.lineaId);
    this.borradorEdicion.set({
      bloque: linea.bloque,
      tipo: linea.tipo,
      debe: linea.debe,
      haber: linea.haber,
      cuentaCodigo: linea.cuentaCodigo ?? '',
    });
  }

  cancelarEdicion(): void {
    this.editandoId.set(null);
  }

  onCampoEdicion(campo: keyof BorradorLinea, valor: string): void {
    const actual = this.borradorEdicion();
    const numerico = campo === 'debe' || campo === 'haber';
    this.borradorEdicion.set({ ...actual, [campo]: numerico ? Number(valor) : valor });
  }

  confirmarEdicion(linea: LineaRespuesta): void {
    this.editarLinea.emit({ lineaId: linea.lineaId, linea: aLineaRequest(linea.orden, this.borradorEdicion()) });
    this.editandoId.set(null);
  }

  iniciarAgregar(): void {
    this.agregandoNueva.set(true);
    this.borradorNueva.set(BORRADOR_VACIO);
  }

  cancelarAgregar(): void {
    this.agregandoNueva.set(false);
  }

  onCampoNueva(campo: keyof BorradorLinea, valor: string): void {
    const actual = this.borradorNueva();
    const numerico = campo === 'debe' || campo === 'haber';
    this.borradorNueva.set({ ...actual, [campo]: numerico ? Number(valor) : valor });
  }

  confirmarAgregar(): void {
    const siguienteOrden = this.lineas().length + 1;
    this.agregarLinea.emit(aLineaRequest(siguienteOrden, this.borradorNueva()));
    this.agregandoNueva.set(false);
  }

  pedirEliminar(lineaId: number): void {
    this.pendienteEliminarId.set(lineaId);
  }

  cancelarEliminar(): void {
    this.pendienteEliminarId.set(null);
  }

  confirmarEliminar(lineaId: number): void {
    this.eliminarLinea.emit(lineaId);
    this.pendienteEliminarId.set(null);
  }
}
