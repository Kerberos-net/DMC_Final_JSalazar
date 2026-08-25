import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { InboxPage } from './inbox-page';
import { BandejaItem } from '../../models/bandeja-item.model';

describe('InboxPage', () => {
  let httpMock: HttpTestingController;

  const incidenciaConErrores: BandejaItem = {
    inboxEventId: 4,
    procesamientoId: 104,
    origen: 'INCIDENCIA',
    estadoConsumo: 'PENDIENTE',
    creadoEn: '2026-08-07T08:00:00Z',
    facturaId: null,
    proveedorCodigo: null,
    rucProveedor: null,
    indicadores: null,
    motivoDescarte: null,
    errores: [
      {
        procesamientoErrorId: 1,
        integracion: 'SUNAT',
        mensaje: 'Timeout de conexión',
        clasificacion: 'TRANSITORIO',
        ocurridoEn: '2026-08-07T09:00:00Z',
      },
    ],
    reprocesarDisponibleEn: null,
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [InboxPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  it('loads the bandeja on init with the default order (desc) and no estado filter', () => {
    const fixture = TestBed.createComponent(InboxPage);
    fixture.detectChanges();

    const req = httpMock.expectOne(
      (r) => r.url === '/api/bandeja' && r.params.get('orden') === 'desc'
    );
    expect(req.request.params.has('estado')).toBe(false);
    req.flush({ items: [], pagina: 1, tamanioPagina: 20, totalRegistros: 0, totalPaginas: 0 });
  });

  it('re-fetches with the new estado when the filter control emits a change', () => {
    const fixture = TestBed.createComponent(InboxPage);
    fixture.detectChanges();
    httpMock
      .expectOne(() => true)
      .flush({ items: [], pagina: 1, tamanioPagina: 20, totalRegistros: 0, totalPaginas: 0 });

    fixture.componentInstance.onEstadoChange('DESCARTADO');
    fixture.detectChanges();

    const req = httpMock.expectOne(
      (r) => r.url === '/api/bandeja' && r.params.get('estado') === 'DESCARTADO'
    );
    req.flush({ items: [], pagina: 1, tamanioPagina: 20, totalRegistros: 0, totalPaginas: 0 });
  });

  it('re-fetches with the new orden when the sort control emits a change', () => {
    const fixture = TestBed.createComponent(InboxPage);
    fixture.detectChanges();
    httpMock
      .expectOne(() => true)
      .flush({ items: [], pagina: 1, tamanioPagina: 20, totalRegistros: 0, totalPaginas: 0 });

    fixture.componentInstance.onOrdenChange('asc');
    fixture.detectChanges();

    const req = httpMock.expectOne(
      (r) => r.url === '/api/bandeja' && r.params.get('orden') === 'asc'
    );
    req.flush({ items: [], pagina: 1, tamanioPagina: 20, totalRegistros: 0, totalPaginas: 0 });
  });

  it('never renders an approve/edit/discard control', () => {
    const fixture = TestBed.createComponent(InboxPage);
    fixture.detectChanges();
    httpMock
      .expectOne(() => true)
      .flush({ items: [], pagina: 1, tamanioPagina: 20, totalRegistros: 0, totalPaginas: 0 });
    fixture.detectChanges();

    const controles = fixture.nativeElement.querySelectorAll(
      '[data-testid="aprobar"], [data-testid="editar"], [data-testid="descartar"]'
    );
    expect(controles.length).toBe(0);
  });

  it('resets pagina to 1 when a filter changes while on a later page', () => {
    const fixture = TestBed.createComponent(InboxPage);
    fixture.detectChanges();
    httpMock
      .expectOne(() => true)
      .flush({ items: [], pagina: 1, tamanioPagina: 20, totalRegistros: 0, totalPaginas: 0 });

    fixture.componentInstance.onPaginaChange(3);
    fixture.detectChanges();
    httpMock
      .expectOne((r) => r.params.get('pagina') === '3')
      .flush({ items: [], pagina: 3, tamanioPagina: 20, totalRegistros: 0, totalPaginas: 3 });

    fixture.componentInstance.onEstadoChange('PROMOVIDO');
    fixture.detectChanges();

    const req = httpMock.expectOne(
      (r) => r.params.get('estado') === 'PROMOVIDO'
    );
    expect(req.request.params.has('pagina')).toBe(false);
    req.flush({ items: [], pagina: 1, tamanioPagina: 20, totalRegistros: 0, totalPaginas: 0 });
  });

  it('reprocesar flow: click opens confirmar-reproceso, confirm calls reprocesar then refetches', async () => {
    const fixture = TestBed.createComponent(InboxPage);
    fixture.detectChanges();
    httpMock
      .expectOne(() => true)
      .flush({
        items: [incidenciaConErrores],
        pagina: 1,
        tamanioPagina: 20,
        totalRegistros: 1,
        totalPaginas: 1,
      });
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();

    fixture.componentInstance.onReprocesarSolicitado(104);
    fixture.detectChanges();

    let dialogo: HTMLDialogElement = fixture.nativeElement.querySelector('dialog');
    expect(dialogo.open).toBe(true);

    const botonConfirmar: HTMLButtonElement = fixture.nativeElement.querySelector(
      '[data-testid="confirmar-reproceso-confirmar"]'
    );
    botonConfirmar.click();
    fixture.detectChanges();

    const reprocesarReq = httpMock.expectOne('/api/incidencias/104/reprocesar');
    expect(reprocesarReq.request.method).toBe('POST');
    reprocesarReq.flush({});
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();

    const refetchReq = httpMock.expectOne(() => true);
    refetchReq.flush({ items: [], pagina: 1, tamanioPagina: 20, totalRegistros: 0, totalPaginas: 0 });
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();

    dialogo = fixture.nativeElement.querySelector('dialog');
    expect(dialogo.open).toBe(false);
  });

  it('reprocesar flow: cancel sends no request', async () => {
    const fixture = TestBed.createComponent(InboxPage);
    fixture.detectChanges();
    httpMock
      .expectOne(() => true)
      .flush({
        items: [incidenciaConErrores],
        pagina: 1,
        tamanioPagina: 20,
        totalRegistros: 1,
        totalPaginas: 1,
      });
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();

    fixture.componentInstance.onReprocesarSolicitado(104);
    fixture.detectChanges();

    const botonCancelar: HTMLButtonElement = fixture.nativeElement.querySelector(
      '[data-testid="confirmar-reproceso-cancelar"]'
    );
    botonCancelar.click();
    fixture.detectChanges();

    httpMock.expectNone('/api/incidencias/104/reprocesar');
  });

  it('reprocesandoId disables the action immediately after confirm, independent of the server flag', async () => {
    const fixture = TestBed.createComponent(InboxPage);
    fixture.detectChanges();
    httpMock
      .expectOne(() => true)
      .flush({
        items: [incidenciaConErrores],
        pagina: 1,
        tamanioPagina: 20,
        totalRegistros: 1,
        totalPaginas: 1,
      });
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();

    fixture.componentInstance.onReprocesarSolicitado(104);
    fixture.detectChanges();
    const botonConfirmar: HTMLButtonElement = fixture.nativeElement.querySelector(
      '[data-testid="confirmar-reproceso-confirmar"]'
    );
    botonConfirmar.click();
    fixture.detectChanges();

    expect(fixture.componentInstance.reprocesandoId()).toBe(104);

    const boton: HTMLButtonElement = fixture.nativeElement.querySelector(
      '[data-testid="reprocesar-4"]'
    );
    expect(boton.disabled).toBe(true);

    httpMock.expectOne('/api/incidencias/104/reprocesar').flush({});
    await new Promise((resolve) => setTimeout(resolve, 0));
    httpMock
      .expectOne(() => true)
      .flush({ items: [], pagina: 1, tamanioPagina: 20, totalRegistros: 0, totalPaginas: 0 });
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
});
