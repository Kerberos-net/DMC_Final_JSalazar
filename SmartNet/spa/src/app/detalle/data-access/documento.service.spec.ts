import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { DocumentoService } from './documento.service';
import { DocumentoRespuesta } from '../models/documento.model';

describe('DocumentoService', () => {
  let service: DocumentoService;
  let httpMock: HttpTestingController;

  const documento: DocumentoRespuesta = {
    id: 'ingesta-9',
    origen: 'INGESTA',
    nombreArchivo: 'factura.pdf',
    mimeType: 'application/pdf',
    fecha: '2026-08-10T10:00:00Z',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(DocumentoService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('cargar() GETs the unified document list for a factura', async () => {
    const promise = service.cargar(42);

    const req = httpMock.expectOne('/api/facturas/42/documentos');
    expect(req.request.method).toBe('GET');
    req.flush([documento]);

    await promise;
    expect(service.documentos()).toEqual([documento]);
  });

  it('contenidoUrl() builds the same-origin content URL for a given document id', () => {
    expect(service.contenidoUrl('ingesta-9')).toBe('/api/documentos/ingesta-9/contenido');
  });
});
