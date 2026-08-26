import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { FacturaService } from '../../data-access/factura.service';
import { AsientoService } from '../../data-access/asiento.service';
import { DocumentoService } from '../../data-access/documento.service';
import { HistorialService } from '../../data-access/historial.service';
import { calcularCuadre } from '../../data-access/cuadre';
import { categorizarProblema } from '../../data-access/problema-ux';
import { CorreccionFacturaRequest } from '../../models/factura.model';
import { LineaAsientoRequest } from '../../models/asiento.model';
import { ProblemaDetails } from '../../../shared/problema.model';
import { FacturaForm } from '../../ui/factura-form/factura-form';
import { AsientoLineas } from '../../ui/asiento-lineas/asiento-lineas';
import { VisorDocumento } from '../../ui/visor-documento/visor-documento';
import { ConflictoBanner } from '../../ui/conflicto-banner/conflicto-banner';

/**
 * Container (smart) component: orchestrates document review, asiento editing, "Guardar avance",
 * and "Validar" (spec.md pantalla-detalle-validacion). Owns the factura-header draft (batched,
 * sent on "Guardar avance") and the last write's `problema`/`categoriaProblema` (design D6).
 * Línea edits are NOT batched — `AsientoLineas` already emits per-línea confirm events that this
 * container forwards to `AsientoService` immediately (spec.md línea scenario: "only that línea's
 * edit is sent"). See apply-progress for this documented UX split.
 */
@Component({
  selector: 'app-detalle-page',
  standalone: true,
  imports: [FacturaForm, AsientoLineas, VisorDocumento, ConflictoBanner],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './detalle-page.html',
  styleUrl: './detalle-page.css',
})
export class DetallePage {
  private readonly route = inject(ActivatedRoute);
  private readonly facturaService = inject(FacturaService);
  private readonly asientoService = inject(AsientoService);
  private readonly documentoService = inject(DocumentoService);
  private readonly historialService = inject(HistorialService);

  readonly factura = this.facturaService.factura;
  readonly asiento = this.asientoService.asiento;
  readonly documentos = this.documentoService.documentos;
  readonly historial = this.historialService.entradas;
  readonly loading = computed(
    () => this.facturaService.loading() || this.asientoService.loading() || this.documentoService.loading()
  );

  readonly cuadre = computed(() => calcularCuadre(this.asiento()?.lineas ?? []));

  readonly problema = signal<ProblemaDetails | null>(null);
  readonly categoriaProblema = computed(() => {
    const p = this.problema();
    return p ? categorizarProblema(p) : null;
  });

  readonly borradorFactura = signal<CorreccionFacturaRequest>({});

  /** `POST /api/facturas/{id}/validar?fechaCorteContable=` -- fecha de corte contable elegida por
   * el usuario (no la fecha de emisión de la factura, que es un dato distinto): design.md no fija
   * un origen para este valor, así que se modela como un campo propio de la pantalla, por defecto
   * la fecha de hoy (criterio razonable documentado en apply-progress). */
  readonly fechaCorteContable = signal(new Date().toISOString().slice(0, 10));

  private facturaId = 0;

  constructor() {
    this.route.paramMap.subscribe((params) => {
      const id = Number(params.get('id'));
      if (Number.isFinite(id) && id > 0) {
        this.facturaId = id;
        void this.cargarTodo();
      }
    });
  }

  private async cargarTodo(): Promise<void> {
    await Promise.all([
      this.facturaService.cargar(this.facturaId),
      this.asientoService.cargarPorFactura(this.facturaId),
      this.documentoService.cargar(this.facturaId),
      this.historialService.cargar(this.facturaId),
    ]);
  }

  onCambiosFactura(cambio: CorreccionFacturaRequest): void {
    this.borradorFactura.set({ ...this.borradorFactura(), ...cambio });
  }

  async guardarAvance(): Promise<void> {
    const cambios = this.borradorFactura();
    if (Object.keys(cambios).length === 0) {
      return;
    }
    try {
      await this.facturaService.guardar(this.facturaId, cambios);
      this.borradorFactura.set({});
      this.problema.set(null);
    } catch (err) {
      this.manejarError(err);
    }
  }

  async onEditarLinea(evento: { lineaId: number; linea: LineaAsientoRequest }): Promise<void> {
    const asientoId = this.asiento()?.asientoContableId;
    if (asientoId === undefined) {
      return;
    }
    try {
      await this.asientoService.actualizarLinea(asientoId, evento.lineaId, evento.linea);
      this.problema.set(null);
    } catch (err) {
      this.manejarError(err);
    }
  }

  async onAgregarLinea(linea: LineaAsientoRequest): Promise<void> {
    const asientoId = this.asiento()?.asientoContableId;
    if (asientoId === undefined) {
      return;
    }
    try {
      await this.asientoService.agregarLinea(asientoId, linea);
      this.problema.set(null);
    } catch (err) {
      this.manejarError(err);
    }
  }

  async onEliminarLinea(lineaId: number): Promise<void> {
    const asientoId = this.asiento()?.asientoContableId;
    if (asientoId === undefined) {
      return;
    }
    try {
      await this.asientoService.eliminarLinea(asientoId, lineaId);
      this.problema.set(null);
    } catch (err) {
      this.manejarError(err);
    }
  }

  async validar(fechaCorteContable: string): Promise<void> {
    try {
      await this.facturaService.validar(this.facturaId, fechaCorteContable);
      this.problema.set(null);
      await this.cargarTodo();
    } catch (err) {
      this.manejarError(err);
    }
  }

  /** design D10, tasks.md 4.8 -- forwards `factura-form`'s `confirmarAfectacion` output; only
   * registers the assistant's assertion, does NOT unblock `validar` (gate stays dormant). */
  async onConfirmarAfectacion(esMixta: boolean): Promise<void> {
    try {
      await this.facturaService.confirmarAfectacion(this.facturaId, esMixta);
      this.problema.set(null);
    } catch (err) {
      this.manejarError(err);
    }
  }

  /** spec.md 412 scenario: refetch factura, asiento, y sus If-Match, descartando ediciones locales. */
  async recargar(): Promise<void> {
    this.borradorFactura.set({});
    this.problema.set(null);
    await this.cargarTodo();
  }

  private manejarError(err: unknown): void {
    if (err instanceof HttpErrorResponse && err.error) {
      this.problema.set(err.error as ProblemaDetails);
    }
  }
}
