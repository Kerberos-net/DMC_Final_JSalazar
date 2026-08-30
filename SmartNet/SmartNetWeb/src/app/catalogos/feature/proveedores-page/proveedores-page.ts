import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { CatalogoProveedorService } from '../../data-access/catalogo-proveedor.service';
import { DescargaXlsx } from '../../data-access/descarga-xlsx';
import { ProveedoresTabla } from '../../ui/proveedores-tabla/proveedores-tabla';
import { TablaPaginador } from '../../ui/tabla-paginador/tabla-paginador';
import { BotonExportar } from '../../ui/boton-exportar/boton-exportar';
import { alternarOrden, type EstadoOrden } from '../../ui/orden';
import { ClaveOrdenProveedor } from '../../models/proveedor-catalogo.model';

/**
 * Container (smart) component for the proveedores catalogo screen (spa spec req 2, design D6/D7).
 * The data-access service holds ALL of the state server-side: every header click, search keystroke,
 * page step and rows-per-page change re-queries `GET /api/catalogos/proveedores?modo=catalogo`.
 * Sort and search reset to page 1; search keeps the active sort. "Exportar a Excel" delegates to
 * the shared `descarga-xlsx` helper with the current search + sort
 * (`/api/catalogos/proveedores/exportacion?q=&orden=&direccion=`). Strictly read-only.
 */
@Component({
  selector: 'app-proveedores-page',
  standalone: true,
  imports: [ProveedoresTabla, TablaPaginador, BotonExportar],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './proveedores-page.html',
  styleUrl: './proveedores-page.css',
})
export class ProveedoresPage {
  private readonly servicio = inject(CatalogoProveedorService);
  private readonly descarga = inject(DescargaXlsx);

  protected readonly items = this.servicio.items;
  protected readonly pagina = this.servicio.pagina;
  protected readonly totalPaginas = this.servicio.totalPaginas;
  protected readonly tamanioPagina = this.servicio.tamanioPagina;
  protected readonly cargando = this.servicio.cargando;
  protected readonly error = this.servicio.error;
  protected readonly descargando = this.descarga.descargando;

  protected readonly filtro = signal('');
  /** Server default is `proveedor asc` (`OrdenProveedor`); the arrow UI starts there too. */
  protected readonly orden = signal<EstadoOrden<ClaveOrdenProveedor>>({
    campo: 'proveedor',
    direccion: 'asc',
  });

  constructor() {
    this.servicio.cargar();
  }

  onFiltro(valor: string): void {
    this.filtro.set(valor);
    this.servicio.buscar(valor);
  }

  onOrdenar(campo: ClaveOrdenProveedor): void {
    const siguiente = alternarOrden(this.orden(), campo);
    this.orden.set(siguiente);
    this.servicio.ordenar(siguiente.campo, siguiente.direccion);
  }

  onPagina(pagina: number): void {
    this.servicio.irAPagina(pagina);
  }

  onTamanio(tamanio: number): void {
    this.servicio.cambiarTamanio(tamanio);
  }

  exportar(): void {
    const actual = this.orden();
    void this.descarga
      .descargar('/api/catalogos/proveedores/exportacion', {
        q: this.filtro().trim(),
        orden: actual.campo,
        direccion: actual.direccion,
      })
      .catch(() => undefined);
  }
}
