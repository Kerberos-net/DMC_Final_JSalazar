import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { InboxList } from './inbox-list';
import { BandejaItem } from '../../models/bandeja-item.model';

describe('InboxList', () => {
  // BACKLOG #21 comprobante fields — null on INCIDENCIA rows, present on FACTURA rows.
  interface Campos21 {
    proveedorNombre: string | null;
    tipoComprobante: string | null;
    numero: string | null;
    totalOrig: number | null;
    moneda: string | null;
    fechaEmision: string | null;
  }
  const CAMPOS_21_NULOS: Campos21 = {
    proveedorNombre: null,
    tipoComprobante: null,
    numero: null,
    totalOrig: null,
    moneda: null,
    fechaEmision: null,
  };
  const camposFactura = (over: Partial<Campos21> = {}): Campos21 => ({
    proveedorNombre: 'Comercial Andina EIRL',
    tipoComprobante: '01',
    numero: 'F001-1',
    totalOrig: 1180,
    moneda: 'PEN',
    fechaEmision: '2026-08-10',
    ...over,
  });

  const promovido: BandejaItem = {
    ...camposFactura({ tipoComprobante: '07', numero: 'F123-456', totalOrig: 4200.5, moneda: 'USD' }),
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
    ...CAMPOS_21_NULOS,
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
    ...CAMPOS_21_NULOS,
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
    ...CAMPOS_21_NULOS,
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
    ...camposFactura({ numero: 'F001-5' }),
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

  const descartadoConErrores: BandejaItem = {
    ...CAMPOS_21_NULOS,
    inboxEventId: 6,
    procesamientoId: 106,
    origen: 'INCIDENCIA',
    estadoConsumo: 'DESCARTADO',
    creadoEn: '2026-08-05T08:00:00Z',
    facturaId: null,
    proveedorCodigo: null,
    rucProveedor: null,
    indicadores: null,
    motivoDescarte: 'Descartada tras fallos repetidos',
    errores: [
      {
        procesamientoErrorId: 3,
        integracion: 'SUNAT',
        mensaje: 'Error permanente',
        clasificacion: 'PERMANENTE',
        ocurridoEn: '2026-08-05T09:00:00Z',
      },
    ],
    reprocesarDisponibleEn: null,
  };

  const promovidoLimpio: BandejaItem = {
    ...camposFactura({ numero: 'F001-7' }),
    inboxEventId: 7,
    procesamientoId: 107,
    origen: 'FACTURA',
    estadoConsumo: 'PROMOVIDO',
    creadoEn: '2026-08-04T08:00:00Z',
    facturaId: 44,
    proveedorCodigo: 'P00003',
    rucProveedor: '20111111111',
    indicadores: {
      esProveedorGenerico: false,
      posibleDuplicado: false,
      tieneCamposNoExtraidos: false,
      fechaEnDomingo: false,
      afectacionMixta: false,
    },
    motivoDescarte: null,
    errores: [],
    reprocesarDisponibleEn: null,
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

  describe('tabular table + derived Estado chip column (BACKLOG #20 PR2)', () => {
    const chipEstado = (fixture: ReturnType<typeof createComponent>, rowId: number): HTMLElement =>
      fixture.nativeElement.querySelector(
        `[data-testid="inbox-row-${rowId}"] [data-testid="chip-estado"]`
      );

    it('renders the rows in a <table> that uses the global .tabla primitive', () => {
      const fixture = createComponent([promovido]);
      expect(fixture.nativeElement.querySelector('table.tabla')).not.toBeNull();
    });

    it('renders the handoff §2 compras columns in order (BACKLOG #21)', () => {
      const fixture = createComponent([promovido]);
      const headers = Array.from(
        fixture.nativeElement.querySelectorAll('thead th')
      ).map((h) => (h as HTMLElement).textContent?.trim());
      expect(headers).toEqual([
        'Recibido',
        'F. emisión',
        'Proveedor',
        'Tipo',
        'Número',
        'Monto',
        'Estado',
        'Detalle',
        'Indicadores',
        'Acciones',
      ]);
    });

    it('renders the compras cells for a FACTURA row, comprobante code mapped client-side', () => {
      const fixture = createComponent([promovido]);
      const row: HTMLElement = fixture.nativeElement.querySelector('[data-testid="inbox-row-1"]');
      expect(row.querySelector('[data-testid="celda-proveedor"]')!.textContent).toContain(
        'Comercial Andina EIRL'
      );
      expect(row.querySelector('[data-testid="celda-tipo"]')!.textContent?.trim()).toBe(
        'Nota de crédito'
      );
      expect(row.querySelector('[data-testid="celda-numero"]')!.textContent?.trim()).toBe('F123-456');
      expect(row.querySelector('[data-testid="celda-monto"]')!.textContent).toContain('USD');
      expect(row.querySelector('[data-testid="celda-fecha-emision"]')!.textContent?.trim().length)
        .toBeGreaterThan(0);
    });

    it('maps 01 -> Factura and 03 -> Boleta', () => {
      const f1 = createComponent([{ ...promovido, tipoComprobante: '01' }]);
      expect(
        f1.nativeElement.querySelector('[data-testid="celda-tipo"]').textContent?.trim()
      ).toBe('Factura');
      const f3 = createComponent([{ ...promovido, tipoComprobante: '03' }]);
      expect(
        f3.nativeElement.querySelector('[data-testid="celda-tipo"]').textContent?.trim()
      ).toBe('Boleta');
    });

    it('renders an unknown comprobante code verbatim', () => {
      const fixture = createComponent([{ ...promovido, tipoComprobante: '99' }]);
      expect(
        fixture.nativeElement.querySelector('[data-testid="celda-tipo"]').textContent?.trim()
      ).toBe('99');
    });

    it('renders "—" in every factura-only cell for an INCIDENCIA row', () => {
      const fixture = createComponent([pendiente]);
      const row: HTMLElement = fixture.nativeElement.querySelector('[data-testid="inbox-row-3"]');
      for (const testid of [
        'celda-fecha-emision',
        'celda-proveedor',
        'celda-tipo',
        'celda-numero',
        'celda-monto',
      ]) {
        expect(row.querySelector(`[data-testid="${testid}"]`)!.textContent?.trim()).toBe('—');
      }
    });

    it('gives the date and monto cells a component-scoped tabular-figures class, not the global one', () => {
      const fixture = createComponent([promovido]);
      const row: HTMLElement = fixture.nativeElement.querySelector('[data-testid="inbox-row-1"]');
      const fechaEmision = row.querySelector('[data-testid="celda-fecha-emision"]') as HTMLElement;
      const monto = row.querySelector('[data-testid="celda-monto"]') as HTMLElement;
      expect(fechaEmision.classList.contains('inbox-list__fecha')).toBe(true);
      expect(fechaEmision.classList.contains('tabular-nums')).toBe(false);
      expect(monto.classList.contains('inbox-list__monto')).toBe(true);
      expect(monto.classList.contains('tabular-nums')).toBe(false);
    });

    it('widens the empty-state colspan to 10', () => {
      const fixture = createComponent([]);
      const celda = fixture.nativeElement.querySelector('[data-testid="inbox-vacio"]') as HTMLElement;
      expect(celda.getAttribute('colspan')).toBe('10');
    });

    it('renders exactly one chip-estado per row', () => {
      const fixture = createComponent([promovido, pendiente, descartado]);
      expect(
        fixture.nativeElement.querySelectorAll('[data-testid="chip-estado"]').length
      ).toBe(3);
      expect(
        fixture.nativeElement
          .querySelector('[data-testid="inbox-row-1"]')
          .querySelectorAll('[data-testid="chip-estado"]').length
      ).toBe(1);
    });

    it('precedence 1: DESCARTADO wins unconditionally, even with error history', () => {
      const fixture = createComponent([descartadoConErrores]);
      const chip = chipEstado(fixture, 6);
      expect(chip.textContent?.trim()).toBe('Descartada');
      expect(chip.classList.contains('chip--descartada')).toBe(true);
    });

    it('precedence 2: a row with error history shows the Error chip', () => {
      const fixture = createComponent([facturaConErroresBloqueada]);
      const chip = chipEstado(fixture, 5);
      expect(chip.textContent?.trim()).toBe('Error');
      expect(chip.classList.contains('chip--error')).toBe(true);
    });

    it('precedence 3: a quality flag shows the Alerta chip (over Validada)', () => {
      const fixture = createComponent([promovido]);
      const chip = chipEstado(fixture, 1);
      expect(chip.textContent?.trim()).toBe('Alerta');
      expect(chip.classList.contains('chip--alerta')).toBe(true);
    });

    it('precedence 4: a clean PROMOVIDO row shows the Validada chip', () => {
      const fixture = createComponent([promovidoLimpio]);
      const chip = chipEstado(fixture, 7);
      expect(chip.textContent?.trim()).toBe('Validada');
      expect(chip.classList.contains('chip--validada')).toBe(true);
    });

    it('precedence 5: a PENDIENTE row shows the Pendiente chip', () => {
      const fixture = createComponent([pendiente]);
      const chip = chipEstado(fixture, 3);
      expect(chip.textContent?.trim()).toBe('Pendiente');
      expect(chip.classList.contains('chip--pendiente')).toBe(true);
    });

    it('does not throw for an INCIDENCIA row with indicadores null', () => {
      expect(() => createComponent([pendiente, incidenciaConErrores])).not.toThrow();
    });

    it('regression lock: chipsDe() indicator chips are byte-identical for a representative item', () => {
      const fixture = createComponent([promovido]);
      const chips = Array.from(
        fixture.nativeElement
          .querySelector('[data-testid="inbox-row-1"]')
          .querySelectorAll('[data-testid="indicador-chip"]')
      ).map((c) => (c as HTMLElement).textContent?.trim());
      expect(chips).toEqual(['Proveedor genérico', 'Campos no extraídos']);
    });

    it('renders an empty-state marker when there are no rows', () => {
      const fixture = createComponent([]);
      expect(fixture.nativeElement.querySelector('[data-testid="inbox-vacio"]')).not.toBeNull();
    });
  });
});
