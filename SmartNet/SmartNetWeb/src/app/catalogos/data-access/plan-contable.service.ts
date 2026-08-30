import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { CuentaContable, PlanContableRespuesta } from '../models/cuenta-contable.model';

/**
 * Server state for the plan contable screen (ADR 0009: signals in a `providedIn: 'root'` service,
 * private writable signal + public `asReadonly()`). `GET /api/catalogos/plan-contable` returns the
 * WHOLE plan in one unpaged response (api spec req 4); the screen filters, sorts and paginates it
 * client-side (design D7/D8), so this service fetches exactly once and never re-queries. A failed
 * load leaves the guard open so the screen can retry.
 */
@Injectable({ providedIn: 'root' })
export class PlanContableService {
  private readonly http = inject(HttpClient);

  private readonly planSignal = signal<readonly CuentaContable[]>([]);
  private readonly cargandoSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);
  private cargado = false;

  readonly plan = this.planSignal.asReadonly();
  readonly cargando = this.cargandoSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();

  async cargar(): Promise<void> {
    if (this.cargado) {
      return;
    }
    this.cargandoSignal.set(true);
    this.errorSignal.set(null);
    try {
      const respuesta = await firstValueFrom(
        this.http.get<PlanContableRespuesta>('/api/catalogos/plan-contable')
      );
      this.planSignal.set([...respuesta.items]);
      this.cargado = true;
    } catch (err) {
      this.errorSignal.set('No se pudo cargar el plan contable.');
      throw err;
    } finally {
      this.cargandoSignal.set(false);
    }
  }
}
