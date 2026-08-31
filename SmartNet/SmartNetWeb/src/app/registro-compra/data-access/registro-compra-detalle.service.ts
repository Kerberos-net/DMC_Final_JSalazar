import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { LineaRegistro, RegistroCompraDetalle } from '../models/registro-compra.model';

/**
 * BACKLOG #23 (spa spec req 3) — lazy, per-`asientoId` fetch of an asiento's detail lines
 * (`GET /api/registro-compra/{asientoId}`). Results are MEMOISED in a `Map`, so re-expanding a row
 * costs nothing; an in-flight request is shared too. `limpiar()` drops the cache — the container
 * calls it whenever the listing's period or page changes. Read-only.
 */
@Injectable({ providedIn: 'root' })
export class RegistroCompraDetalleService {
  private readonly http = inject(HttpClient);

  private readonly cache = new Map<number, Promise<readonly LineaRegistro[]>>();

  obtener(asientoId: number): Promise<readonly LineaRegistro[]> {
    const enCache = this.cache.get(asientoId);
    if (enCache) {
      return enCache;
    }

    const promesa = firstValueFrom(
      this.http.get<RegistroCompraDetalle>(`/api/registro-compra/${asientoId}`)
    )
      .then((detalle) => [...detalle.lineas] as readonly LineaRegistro[])
      .catch((error) => {
        this.cache.delete(asientoId);
        throw error;
      });

    this.cache.set(asientoId, promesa);
    return promesa;
  }

  limpiar(): void {
    this.cache.clear();
  }
}
