import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { EstadoOrden, flechaOrden } from '../orden';
import { ClaveOrdenProveedor, ProveedorCatalogo } from '../../models/proveedor-catalogo.model';

interface ColumnaOrdenable {
  readonly clave: ClaveOrdenProveedor;
  readonly etiqueta: string;
}

/**
 * BACKLOG #22 PR6, design D8 -- presentational table for the proveedores catalogo screen. Columns
 * "Código" (`codigo`), "Razón social" (`nombre`) and "RUC" (`ruc`). Headers are sortable but the
 * component owns no state: it emits `ordenar` with the server sort key and the container feeds back
 * the rows already sorted+paged BY THE SERVER (design D7 -- no client-side reorder). Read-only.
 */
@Component({
  selector: 'app-proveedores-tabla',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './proveedores-tabla.html',
  styleUrl: './proveedores-tabla.css',
})
export class ProveedoresTabla {
  readonly filas = input.required<readonly ProveedorCatalogo[]>();
  readonly orden = input.required<EstadoOrden<ClaveOrdenProveedor> | null>();

  readonly ordenar = output<ClaveOrdenProveedor>();

  protected readonly columnas: readonly ColumnaOrdenable[] = [
    { clave: 'codigo', etiqueta: 'Código' },
    { clave: 'proveedor', etiqueta: 'Razón social' },
    { clave: 'ruc', etiqueta: 'RUC' },
  ];

  protected readonly flechas = computed<Record<ClaveOrdenProveedor, string>>(() => {
    const actual = this.orden();
    return {
      codigo: actual ? flechaOrden(actual, 'codigo') : '',
      proveedor: actual ? flechaOrden(actual, 'proveedor') : '',
      ruc: actual ? flechaOrden(actual, 'ruc') : '',
    };
  });
}
