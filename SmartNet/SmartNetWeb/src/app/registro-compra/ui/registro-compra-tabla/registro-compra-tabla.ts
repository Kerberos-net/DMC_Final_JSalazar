import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { AsientoDetalle } from '../asiento-detalle/asiento-detalle';
import { importeOpcional } from '../../../shared/formato';
import {
  LineaRegistro,
  RegistroCompraCabecera,
} from '../../models/registro-compra.model';

/** Round to the céntimo — NO epsilon (REGLAS.md §6 "no hay tolerancia"). */
const r2 = (n: number): number => Math.round(n * 100) / 100;

/**
 * BACKLOG #23 (spa spec req 4, design D6) — the inconsistency badge, as a PURE function. It NEVER
 * imports or calls domain code (`SmartNet.Contable.Core` / ADR 0019); it only re-checks two
 * arithmetic identities over the amounts the API already returned:
 *
 *   cabecera: round(basePEN + igvPEN, 2) !== round(netoPEN, 2)
 *   detalle : round(Σ debe, 2)           !== round(Σ haber, 2)
 *
 * ANY null term ⇒ NOT inconsistent (absence is not a mismatch). `lineas === null` means the detail
 * is not loaded yet, so the detail identity is not evaluated. Percepción (401131) nets out: it is
 * excluded from the cabecera `netoPEN` (§10.4) and appears on both debe and haber so it cancels.
 */
export function esInconsistente(
  cabecera: RegistroCompraCabecera,
  lineas: readonly LineaRegistro[] | null
): boolean {
  const { basePEN, igvPEN, netoPEN } = cabecera;
  const cabeceraDescuadrada =
    basePEN !== null &&
    igvPEN !== null &&
    netoPEN !== null &&
    r2(basePEN + igvPEN) !== r2(netoPEN);

  const detalleDescuadrado =
    lineas !== null &&
    r2(lineas.reduce((s, l) => s + l.debe, 0)) !== r2(lineas.reduce((s, l) => s + l.haber, 0));

  return cabeceraDescuadrada || detalleDescuadrado;
}

/**
 * BACKLOG #23 (spa spec req 2/3/4) — presentational table for the registro de compra listing.
 * Columns: N.º comprobante, Origen libro, N.º asiento, Proveedor (name or code), Fecha contable,
 * Base PEN, IGV PEN, Neto PEN. Each row has an expand toggle that emits `alternar(asientoId)`; the
 * container fetches the lines and feeds them back through `lineasPorAsiento`. Strictly read-only —
 * no edit / anular / reactivar control (spec req 7).
 */
@Component({
  selector: 'app-registro-compra-tabla',
  standalone: true,
  imports: [AsientoDetalle],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './registro-compra-tabla.html',
  styleUrl: './registro-compra-tabla.css',
})
export class RegistroCompraTabla {
  readonly filas = input.required<readonly RegistroCompraCabecera[]>();
  /** The single currently-expanded asiento id, or `null`. */
  readonly expandido = input.required<number | null>();
  readonly lineasPorAsiento = input.required<ReadonlyMap<number, readonly LineaRegistro[]>>();

  readonly alternar = output<number>();

  protected readonly importe = importeOpcional;

  /** asientoId -> whether its badge lights. Recomputed when rows or loaded lines change. */
  protected readonly inconsistencias = computed(() => {
    const mapa = new Map<number, boolean>();
    const lineas = this.lineasPorAsiento();
    for (const fila of this.filas()) {
      mapa.set(
        fila.asientoContableId,
        esInconsistente(fila, lineas.get(fila.asientoContableId) ?? null)
      );
    }
    return mapa;
  });

  protected proveedor(fila: RegistroCompraCabecera): string {
    return fila.proveedorNombre ?? fila.proveedorCodigo;
  }
}
