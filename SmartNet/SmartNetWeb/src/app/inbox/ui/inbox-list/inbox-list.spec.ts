import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { InboxList } from './inbox-list';
import { BandejaItem } from '../../models/bandeja-item.model';

describe('InboxList', () => {
  const promovido: BandejaItem = {
    inboxEventId: 1,
    procesamientoId: 101,
    origen: 'FACTURA',
    estadoConsumo: 'PROMOVIDO',
    creadoEn: '2026-08-10T10:00:00Z',
    facturaId: 42,
    proveedorCodigo: 'P00001',
    rucProveedor: '20100000001',
    indicadores: {
      esProveedorGenerico: true,
      posibleDuplicado: false,
      tieneCamposNoExtraidos: true,
      fechaEnDomingo: false,
      afectacionMixta: null,
    },
    motivoDescarte: null,
    errores: [],
    reprocesarDisponibleEn: null,
  };

  const descartado: BandejaItem = {
    inboxEventId: 2,
    procesamientoId: 102,
    origen: 'INCIDENCIA',
    estadoConsumo: 'DESCARTADO',
    creadoEn: '2026-08-09T08:00:00Z',
    facturaId: null,
    proveedorCodigo: null,
    rucProveedor: null,
    indicadores: null,
    motivoDescarte: 'Falta TotalOrig',
    errores: [],
    reprocesarDisponibleEn: null,
  };

  const pendiente: BandejaItem = {
    inboxEventId: 3,
    procesamientoId: 103,
    origen: 'INCIDENCIA',
    estadoConsumo: 'PENDIENTE',
    creadoEn: '2026-08-08T08:00:00Z',
    facturaId: null,
    proveedorCodigo: null,
    rucProveedor: null,
    indicadores: null,
    motivoDescarte: null,
    errores: [],
    reprocesarDisponibleEn: null,
  };

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

  const facturaConErroresBloqueada: BandejaItem = {
    inboxEventId: 5,
    procesamientoId: 105,
    origen: 'FACTURA',
    estadoConsumo: 'PROMOVIDO',
    creadoEn: '2026-08-06T08:00:00Z',
    facturaId: 43,
    proveedorCodigo: 'P00002',
    rucProveedor: '20999999999',
    indicadores: {
      esProveedorGenerico: false,
      posibleDuplicado: false,
      tieneCamposNoExtraidos: false,
      fechaEnDomingo: false,
      afectacionMixta: false,
    },
    motivoDescarte: null,
    errores: [
      {
        procesamientoErrorId: 2,
        integracion: 'SUNAT',
        mensaje: 'Reintento fallido',
        clasificacion: 'PERMANENTE',
        ocurridoEn: '2026-08-06T09:00:00Z',
      },
    ],
    reprocesarDisponibleEn: '2099-01-01T00:00:00Z',
  };

  const createComponent = (items: BandejaItem[]) => {
    const fixture = TestBed.createComponent(InboxList);
    fixture.componentRef.setInput('items', items);
    fixture.detectChanges();
    return fixture;
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [InboxList],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  it('shows the linked Factura id and indicator chips for a promoted item', () => {
    const fixture = createComponent([promovido]);
    const row: HTMLElement = fixture.nativeElement.querySelector(
      '[data-testid="inbox-row-1"]'
    );
    expect(row.textContent).toContain('42');
    const chips = Array.from(
      row.querySelectorAll('[data-testid="indicador-chip"]')
    ) as HTMLElement[];
    expect(chips.length).toBe(2);
  });

  it('links a promoted item to its detail screen (BACKLOG #12 Phase 5)', () => {
    const fixture = createComponent([promovido]);
    const enlace: HTMLAnchorElement = fixture.nativeElement.querySelector(
      '[data-testid="ir-a-detalle"]'
    );
    expect(enlace.getAttribute('href')).toBe('/detalle/42');
  });

  it('shows the discard reason for a discarded item', () => {
    const fixture = createComponent([descartado]);
    const row: HTMLElement = fixture.nativeElement.querySelector(
      '[data-testid="inbox-row-2"]'
    );
    expect(row.textContent).toContain('Falta TotalOrig');
  });

  it('renders a pending row with no Factura summary and no discard reason', () => {
    const fixture = createComponent([pendiente]);
    const row: HTMLElement = fixture.nativeElement.querySelector(
      '[data-testid="inbox-row-3"]'
    );
    expect(row.querySelector('[data-testid="factura-id"]')).toBeNull();
    expect(row.querySelector('[data-testid="motivo-descarte"]')).toBeNull();
  });

  it('never renders an approve/edit/discard control', () => {
    const fixture = createComponent([promovido, descartado, pendiente]);
    const controles = fixture.nativeElement.querySelectorAll(
      '[data-testid="aprobar"], [data-testid="editar"], [data-testid="descartar"]'
    );
    expect(controles.length).toBe(0);
  });

  it('renders panel-errores inside <details> for a row with error history, any origen', () => {
    const fixture = createComponent([incidenciaConErrores, facturaConErroresBloqueada]);
    const detallesIncidencia = fixture.nativeElement.querySelector(
      '[data-testid="inbox-row-4"] details'
    );
    const detallesFactura = fixture.nativeElement.querySelector(
      '[data-testid="inbox-row-5"] details'
    );
    expect(detallesIncidencia).not.toBeNull();
    expect(detallesFactura).not.toBeNull();
  });

  it('renders no <details> panel for a row with no error history', () => {
    const fixture = createComponent([promovido]);
    const detalles = fixture.nativeElement.querySelector('[data-testid="inbox-row-1"] details');
    expect(detalles).toBeNull();
  });

  it('renders an enabled reprocesar button when reprocesarDisponibleEn is null', () => {
    const fixture = createComponent([incidenciaConErrores]);
    const boton: HTMLButtonElement = fixture.nativeElement.querySelector(
      '[data-testid="reprocesar-4"]'
    );
    expect(boton).not.toBeNull();
    expect(boton.disabled).toBe(false);
  });

  it('renders a disabled reprocesar button when reprocesarDisponibleEn is in the future', () => {
    const fixture = createComponent([facturaConErroresBloqueada]);
    const boton: HTMLButtonElement = fixture.nativeElement.querySelector(
      '[data-testid="reprocesar-5"]'
    );
    expect(boton).not.toBeNull();
    expect(boton.disabled).toBe(true);
  });

  it('renders no reprocesar button for a row with no error history (e.g. plain PROMOVIDO)', () => {
    const fixture = createComponent([promovido]);
    const boton = fixture.nativeElement.querySelector('[data-testid="reprocesar-1"]');
    expect(boton).toBeNull();
  });

  it('emits reprocesarSolicitado with the procesamientoId when the reprocesar button is clicked', () => {
    const fixture = createComponent([incidenciaConErrores]);
    const emitted: number[] = [];
    fixture.componentInstance.reprocesarSolicitado.subscribe((id) => emitted.push(id));

    const boton: HTMLButtonElement = fixture.nativeElement.querySelector(
      '[data-testid="reprocesar-4"]'
    );
    boton.click();

    expect(emitted).toEqual([104]);
  });
});
