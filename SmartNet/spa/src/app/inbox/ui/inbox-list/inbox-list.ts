import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { BandejaItem, IndicadoresFactura } from '../../models/bandeja-item.model';

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

interface FilaInbox {
  readonly item: BandejaItem;
  readonly chips: IndicadorChip[];
}

/**
 * Presentational (dumb) component: renders the Inbox rows. Read-only per spec.md's "Read-only
 * in this item" requirement — the template never renders a button or a role="button" control.
 */
@Component({
  selector: 'app-inbox-list',
  standalone: true,
  imports: [DatePipe, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './inbox-list.html',
})
export class InboxList {
  readonly items = input.required<BandejaItem[]>();

  readonly filas = computed<FilaInbox[]>(() =>
    this.items().map((item) => ({ item, chips: chipsDe(item.indicadores) }))
  );
}
