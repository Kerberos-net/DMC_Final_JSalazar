import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { BandejaItem, IndicadoresFactura } from '../../models/bandeja-item.model';
import { PanelErrores } from '../panel-errores/panel-errores';

interface IndicadorChip {
  readonly clave: string;
  readonly etiqueta: string;
}

const ETIQUETAS: Record<keyof Omit<IndicadoresFactura, 'afectacionMixta'>, string> = {
  esProveedorGenerico: 'Proveedor genérico',
  posibleDuplicado: 'Posible duplicado',
  tieneCamposNoExtraidos: 'Campos no extraídos',
  fechaEnDomingo: 'Emitido en domingo',
};

/** Derives the visible indicator chips (design D5: 5 flags; `afectacionMixta` is tri-state). */
function chipsDe(indicadores: IndicadoresFactura | null): IndicadorChip[] {
  if (!indicadores) {
    return [];
  }
  const chips: IndicadorChip[] = [];
  for (const clave of Object.keys(ETIQUETAS) as (keyof typeof ETIQUETAS)[]) {
    if (indicadores[clave]) {
      chips.push({ clave, etiqueta: ETIQUETAS[clave] });
    }
  }
  if (indicadores.afectacionMixta === true) {
    chips.push({ clave: 'afectacionMixta', etiqueta: 'Afectación mixta' });
  }
  return chips;
}

type ClaseChipEstado = `chip chip--${'error' | 'alerta' | 'validada' | 'pendiente' | 'descartada'}`;

interface ChipEstado {
  readonly etiqueta: string;
  readonly clase: ClaseChipEstado;
}

/**
 * Derived Estado chip (item #20 PR2, design D3). Module-level pure function beside `chipsDe()`;
 * FIRST MATCH WINS. This is a presentation-only projection of already-existing #13 state
 * (`estadoConsumo`, `errores`, two indicator flags) -- NOT a change to `chipsDe()` or the #13
 * indicator surface. DESCARTADO ranks first: it is a terminal lifecycle fact and keeps
 * `.chip--descartada` unconditionally, even for a row that still carries error history.
 */
function chipEstadoDe(item: BandejaItem): ChipEstado {
  if (item.estadoConsumo === 'DESCARTADO') {
    return { etiqueta: 'Descartada', clase: 'chip chip--descartada' };
  }
  if (item.errores.length > 0) {
    return { etiqueta: 'Error', clase: 'chip chip--error' };
  }
  const indicadores = item.indicadores;
  if (indicadores !== null && (indicadores.esProveedorGenerico || indicadores.posibleDuplicado)) {
    return { etiqueta: 'Alerta', clase: 'chip chip--alerta' };
  }
  if (item.estadoConsumo === 'PROMOVIDO') {
    return { etiqueta: 'Validada', clase: 'chip chip--validada' };
  }
  return { etiqueta: 'Pendiente', clase: 'chip chip--pendiente' };
}

interface FilaInbox {
  readonly item: BandejaItem;
  readonly chips: IndicadorChip[];
  readonly chipEstado: ChipEstado;
  readonly reprocesarDisponible: boolean;
}

/**
 * Presentational (dumb) component: renders the Inbox rows. Read-only except for one action
 * (BACKLOG #13, inbox-screen spec.md "Read-only except the reprocesar action") -- `reprocesar` is
 * the ONLY control this template ever renders, gated to rows where it applies (`INCIDENCIA` rows
 * and already-promoted `FACTURA` rows with error history). No approve/edit/discard control exists
 * here or anywhere else in this component.
 */
@Component({
  selector: 'app-inbox-list',
  standalone: true,
  imports: [DatePipe, RouterLink, PanelErrores],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './inbox-list.html',
  styleUrl: './inbox-list.css',
})
export class InboxList {
  readonly items = input.required<BandejaItem[]>();
  /** Container-owned optimistic guard (design.md "Double click on reprocesar" edge case). */
  readonly reprocesandoId = input<number | null>(null);

  readonly reprocesarSolicitado = output<number>();

  readonly filas = computed<FilaInbox[]>(() =>
    this.items().map((item) => ({
      item,
      chips: chipsDe(item.indicadores),
      chipEstado: chipEstadoDe(item),
      reprocesarDisponible:
        item.reprocesarDisponibleEn === null || new Date(item.reprocesarDisponibleEn) <= new Date(),
    }))
  );

  reprocesarDeshabilitado(fila: FilaInbox): boolean {
    return !fila.reprocesarDisponible || this.reprocesandoId() === fila.item.procesamientoId;
  }

  onReprocesar(fila: FilaInbox): void {
    this.reprocesarSolicitado.emit(fila.item.procesamientoId);
  }
}
