import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { HistorialService } from './historial.service';
import { EntradaAuditoriaRespuesta } from '../models/historial.model';

/**
 * tasks.md 4.3 (RED first) -- `GET /api/facturas/{id}/historial` (design.md D7), ADR 0009 signals
 * pattern (private writable signal + `asReadonly()`, matching `FacturaService`/`AsientoService`).
 */
describe('HistorialService', () => {
  let service: HistorialService;
  let httpMock: HttpTestingController;

  const entradas: EntradaAuditoriaRespuesta[] = [
    {
      entidadTipo: 'FACTURA',
      entidadId: 42,
      accion: 'CORRECCION',
      campo: 'rucProveedor',
      valorOriginal: '20111111111',
      valorNuevo: '20123456789',
      motivo: null,
      usuarioId: 1,
      ocurridoEn: '2026-08-20T15:00:00Z',
    },
  ];

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(HistorialService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('cargar() GETs the historial for a factura and mirrors it as a signal', async () => {
    const promise = service.cargar(42);

    const req = httpMock.expectOne('/api/facturas/42/historial');
    expect(req.request.method).toBe('GET');
    req.flush(entradas);

    await promise;
    expect(service.entradas()).toEqual(entradas);
  });

  it('cargar() mirrors an empty array for a factura with no corrections', async () => {
    const promise = service.cargar(42);
    httpMock.expectOne('/api/facturas/42/historial').flush([]);

    await promise;
    expect(service.entradas()).toEqual([]);
  });
});
