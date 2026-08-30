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
    proveedorNombre: null,
    tipoComprobante: null,
    numero: null,
    totalOrig: null,
    moneda: null,
    fechaEmision: null,
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

  it('loads the bandeja on init: orden desc, estadoDerivado=TODOS, and the current-month date range', () => {
    const fixture = TestBed.createComponent(InboxPage);
    fixture.detectChanges();

    const req = httpMock.expectOne(
      (r) => r.url === '/api/bandeja' && r.params.get('orden') === 'desc'
    );
    expect(req.request.params.has('estado')).toBe(false);
    // "Todos" must reach the wide predicate, not the API's narrow no-param default.
    expect(req.request.params.get('estadoDerivado')).toBe('TODOS');

    const hoy = new Date();
    const primero = new Date(hoy.getFullYear(), hoy.getMonth(), 1);
    const iso = (d: Date) =>
      `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
    expect(req.request.params.get('desde')).toBe(iso(primero));
    expect(req.request.params.get('hasta')).toBe(iso(hoy));
    req.flush({ items: [], pagina: 1, tamanioPagina: 20, totalRegistros: 0, totalPaginas: 0 });
  });

  it('re-fetches with estadoDerivado when a chip is picked, and with TODOS again on reset', () => {
    const fixture = TestBed.createComponent(InboxPage);
    fixture.detectChanges();
    httpMock
      .expectOne(() => true)
      .flush({ items: [], pagina: 1, tamanioPagina: 20, totalRegistros: 0, totalPaginas: 0 });

    fixture.componentInstance.onEstadoDerivadoChange('ERROR');
    fixture.detectChanges();
    httpMock
      .expectOne((r) => r.url === '/api/bandeja' && r.params.get('estadoDerivado') === 'ERROR')
      .flush({ items: [], pagina: 1, tamanioPagina: 20, totalRegistros: 0, totalPaginas: 0 });

    fixture.componentInstance.onEstadoDerivadoChange('TODOS');
    fixture.detectChanges();
    const req = httpMock.expectOne((r) => r.url === '/api/bandeja');
    expect(req.request.params.get('estadoDerivado')).toBe('TODOS');
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

    fixture.componentInstance.onEstadoDerivadoChange('VALIDADA');
    fixture.detectChanges();

    const req = httpMock.expectOne(
      (r) => r.params.get('estadoDerivado') === 'VALIDADA'
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

  it('renders a page header: h1 "Bandeja principal" plus a subtitle', () => {
    const fixture = TestBed.createComponent(InboxPage);
    fixture.detectChanges();
    httpMock
      .expectOne(() => true)
      .flush({ items: [], pagina: 1, tamanioPagina: 20, totalRegistros: 0, totalPaginas: 0 });
    fixture.detectChanges();

    const h1: HTMLHeadingElement = fixture.nativeElement.querySelector('header h1');
    expect(h1.textContent?.trim()).toBe('Bandeja principal');

    const subtitulo: HTMLElement = fixture.nativeElement.querySelector(
      '[data-testid="inbox-subtitulo"]'
    );
    expect(subtitulo.textContent?.trim().length).toBeGreaterThan(0);
  });

  it('renders the four global summary cards from the resumen aggregate, not derived from items', async () => {
    const fixture = TestBed.createComponent(InboxPage);
    fixture.detectChanges();
    httpMock.expectOne(() => true).flush({
      items: [],
      pagina: 1,
      tamanioPagina: 20,
      totalRegistros: 0,
      totalPaginas: 0,
      resumen: { pendientes: 12, validadas: 40, conError: 3, alertas: 5, descartadas: 7, total: 67 },
    });
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();

    const resumen = fixture.nativeElement.querySelector('app-inbox-resumen');
    expect(resumen).not.toBeNull();
    const valores = Array.from(
      resumen.querySelectorAll('[data-testid="tarjeta-valor"]')
    ).map((v: any) => v.textContent.trim());
    // 40 "Validadas" while items() is empty proves the numbers are not derived from the list.
    expect(valores).toEqual(['12', '40', '3', '5']);
  });

  it('keeps the card numbers stable when a filter changes and the server returns the same resumen', async () => {
    const fixture = TestBed.createComponent(InboxPage);
    fixture.detectChanges();
    const envelope = (over: object) => ({
      items: [],
      pagina: 1,
      tamanioPagina: 20,
      totalRegistros: 0,
      totalPaginas: 0,
      resumen: { pendientes: 9, validadas: 1, conError: 0, alertas: 2, descartadas: 0, total: 12 },
      ...over,
    });
    httpMock.expectOne(() => true).flush(envelope({}));
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();

    fixture.componentInstance.onEstadoDerivadoChange('VALIDADA');
    fixture.detectChanges();
    httpMock.expectOne((r) => r.params.get('estadoDerivado') === 'VALIDADA').flush(envelope({}));
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();

    const valores = Array.from(
      fixture.nativeElement.querySelectorAll('app-inbox-resumen [data-testid="tarjeta-valor"]')
    ).map((v: any) => v.textContent.trim());
    expect(valores).toEqual(['9', '1', '0', '2']);
  });

  it('estado chips filter: clicking a chip re-fetches with that estadoDerivado and marks it active', async () => {
    const fixture = TestBed.createComponent(InboxPage);
    fixture.detectChanges();
    httpMock.expectOne(() => true).flush({
      items: [],
      pagina: 1,
      tamanioPagina: 20,
      totalRegistros: 0,
      totalPaginas: 0,
      resumen: { pendientes: 4, validadas: 4, conError: 2, alertas: 2, descartadas: 0, total: 12 },
    });
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();

    const chips = fixture.nativeElement.querySelectorAll('[data-testid^="chip-filtro-"]');
    expect(Array.from(chips).map((c: any) => c.dataset.testid)).toEqual([
      'chip-filtro-TODOS',
      'chip-filtro-PENDIENTE',
      'chip-filtro-VALIDADA',
      'chip-filtro-ERROR',
      'chip-filtro-ALERTA',
      'chip-filtro-DESCARTADA',
    ]);

    const chipError = fixture.nativeElement.querySelector(
      '[data-testid="chip-filtro-ERROR"]'
    ) as HTMLButtonElement;
    expect(chipError.textContent).toContain('2');
    chipError.click();
    fixture.detectChanges();

    httpMock.expectOne((r) => r.params.get('estadoDerivado') === 'ERROR').flush({
      items: [],
      pagina: 1,
      tamanioPagina: 20,
      totalRegistros: 2,
      totalPaginas: 1,
      resumen: { pendientes: 4, validadas: 4, conError: 2, alertas: 2, descartadas: 0, total: 12 },
    });
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();

    expect(
      fixture.nativeElement
        .querySelector('[data-testid="chip-filtro-ERROR"]')
        .classList.contains('inbox-page__chip--activo')
    ).toBe(true);
    expect(
      fixture.nativeElement
        .querySelector('[data-testid="chip-filtro-TODOS"]')
        .classList.contains('inbox-page__chip--activo')
    ).toBe(false);
  });

  it('renders no summary strip before the first load completes', () => {
    const fixture = TestBed.createComponent(InboxPage);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-inbox-resumen')).toBeNull();
    httpMock.expectOne(() => true).flush({
      items: [],
      pagina: 1,
      tamanioPagina: 20,
      totalRegistros: 0,
      totalPaginas: 0,
      resumen: { pendientes: 0, validadas: 0, conError: 0, alertas: 0, descartadas: 0, total: 0 },
    });
  });

  it('lays out header -> filter -> list -> dialog in document order', () => {
    const fixture = TestBed.createComponent(InboxPage);
    fixture.detectChanges();
    httpMock
      .expectOne(() => true)
      .flush({ items: [], pagina: 1, tamanioPagina: 20, totalRegistros: 0, totalPaginas: 0 });
    fixture.detectChanges();

    const root: HTMLElement = fixture.nativeElement;
    const header = root.querySelector('header')!;
    const filter = root.querySelector('app-inbox-filter')!;
    const list = root.querySelector('app-inbox-list')!;
    const dialog = root.querySelector('app-confirmar-reproceso')!;
    const sigue = (a: Element, b: Element) =>
      Boolean(a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING);

    expect(sigue(header, filter)).toBe(true);
    expect(sigue(filter, list)).toBe(true);
    expect(sigue(list, dialog)).toBe(true);
  });

  it('renders the load error as a .banner .banner--error keeping role=alert and the testid', async () => {
    const fixture = TestBed.createComponent(InboxPage);
    fixture.detectChanges();
    httpMock
      .expectOne(() => true)
      .flush('boom', { status: 500, statusText: 'Server Error' });
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();

    const banner: HTMLElement = fixture.nativeElement.querySelector('[data-testid="inbox-error"]');
    expect(banner).not.toBeNull();
    expect(banner.getAttribute('role')).toBe('alert');
    expect(banner.classList.contains('banner')).toBe(true);
    expect(banner.classList.contains('banner--error')).toBe(true);
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
