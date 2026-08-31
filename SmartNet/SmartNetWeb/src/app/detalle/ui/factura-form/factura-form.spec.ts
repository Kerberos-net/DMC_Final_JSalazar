import { TestBed } from '@angular/core/testing';
import { FacturaForm } from './factura-form';
import { FacturaRespuesta } from '../../models/factura.model';

describe('FacturaForm', () => {
  const factura: FacturaRespuesta = {
    facturaId: 42,
    estado: 'ABIERTA',
    proveedorCodigo: 'P00123',
    rucProveedor: '20123456789',
    tipoComprobante: '01',
    numero: 'F001-100',
    totalOrig: 118,
    moneda: 'USD',
    fechaEmision: '2026-08-10',
    motivo: null,
    afectacion: 'Gravada',
    esProveedorGenerico: false,
    posibleDuplicado: false,
    tieneCamposNoExtraidos: false,
    afectacionMixta: false,
    camposNoExtraidos: [],
    glosa: null,
  };

  const createComponent = (
    f: FacturaRespuesta,
    tipoCambioVenta: number | null = null,
    fechaContable: string | null = null,
    editable = true,
    basePEN: number | null = null,
    igvPEN: number | null = null
  ) => {
    const fixture = TestBed.createComponent(FacturaForm);
    fixture.componentRef.setInput('factura', f);
    fixture.componentRef.setInput('tipoCambioVenta', tipoCambioVenta);
    fixture.componentRef.setInput('fechaContable', fechaContable);
    fixture.componentRef.setInput('editable', editable);
    fixture.componentRef.setInput('basePEN', basePEN);
    fixture.componentRef.setInput('igvPEN', igvPEN);
    fixture.detectChanges();
    return fixture;
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [FacturaForm] }).compileComponents();
  });

  describe('two-column field grid', () => {
    it('renders a grid container and labels as text above the input (no <label> wrap)', () => {
      const fixture = createComponent(factura);
      expect(fixture.nativeElement.querySelector('.factura-form__grid')).toBeTruthy();
      const proveedorInput: HTMLInputElement = fixture.nativeElement.querySelector(
        '[data-testid="campo-proveedorCodigo"]'
      );
      expect(proveedorInput.closest('label')).toBeNull();
      const campo = proveedorInput.closest('.campo')!;
      expect(campo.querySelector('.campo__etiqueta')?.textContent?.trim()).toBe('Proveedor');
    });

    it('renders the editable zero-backend fields bound to the factura', () => {
      const fixture = createComponent(factura);
      const q = (id: string): HTMLInputElement => fixture.nativeElement.querySelector(`[data-testid="${id}"]`);
      expect(q('campo-monto').value).toBe('118.00');
      expect(q('campo-moneda').value).toBe('USD');
      expect(q('campo-fechaEmision').value).toBe('2026-08-10');
      expect(q('campo-proveedorCodigo').value).toBe('P00123');
      expect(fixture.nativeElement.querySelector('[data-testid="abrir-picker-proveedor"]')).toBeTruthy();
    });

    it('renders tipoComprobante as an editable select of the 3 comprobante types (PR5 PATCH delta)', () => {
      const fixture = createComponent(factura);
      const tipo: HTMLSelectElement = fixture.nativeElement.querySelector('[data-testid="campo-tipoComprobante"]');
      expect(tipo.tagName).toBe('SELECT');
      expect(tipo.disabled).toBe(false);
      expect(tipo.value).toBe('01');
      expect(Array.from(tipo.options).map((o) => o.value)).toEqual(['01', '03', '07']);
    });

    it('renders numero as an editable text input (PR5 PATCH delta)', () => {
      const fixture = createComponent(factura);
      const numero: HTMLInputElement = fixture.nativeElement.querySelector('[data-testid="campo-numero"]');
      expect(numero.disabled).toBe(false);
      expect(numero.value).toBe('F001-100');
    });

    it('emits cambios for tipoComprobante and numero through the existing onCambiosFactura path', () => {
      const fixture = createComponent(factura);
      const emitidos: unknown[] = [];
      fixture.componentInstance.cambios.subscribe((c) => emitidos.push(c));
      const tipo: HTMLSelectElement = fixture.nativeElement.querySelector('[data-testid="campo-tipoComprobante"]');
      tipo.value = '07';
      tipo.dispatchEvent(new Event('change'));
      const numero: HTMLInputElement = fixture.nativeElement.querySelector('[data-testid="campo-numero"]');
      numero.value = 'FC01-9';
      numero.dispatchEvent(new Event('input'));
      expect(emitidos).toEqual([{ tipoComprobante: '07' }, { numero: 'FC01-9' }]);
    });

    it('disables tipoComprobante and numero when editable=false (VALIDADA)', () => {
      const fixture = createComponent(factura, null, null, false);
      const tipo: HTMLSelectElement = fixture.nativeElement.querySelector('[data-testid="campo-tipoComprobante"]');
      const numero: HTMLInputElement = fixture.nativeElement.querySelector('[data-testid="campo-numero"]');
      expect(tipo.disabled).toBe(true);
      expect(numero.disabled).toBe(true);
    });
  });

  describe('editable field bindings (pure SPA, existing PATCH contract)', () => {
    it('emits cambios with { totalOrig } as a number when monto changes', () => {
      const fixture = createComponent(factura);
      let emitido: unknown = null;
      fixture.componentInstance.cambios.subscribe((c) => (emitido = c));
      const input: HTMLInputElement = fixture.nativeElement.querySelector('[data-testid="campo-monto"]');
      input.value = '200.50';
      input.dispatchEvent(new Event('input'));
      expect(emitido).toEqual({ totalOrig: 200.5 });
    });

    it('emits cambios with only the edited field for moneda / fechaEmision / proveedorCodigo', () => {
      const fixture = createComponent(factura);
      const emitidos: unknown[] = [];
      fixture.componentInstance.cambios.subscribe((c) => emitidos.push(c));
      const setInput = (id: string, value: string) => {
        const el: HTMLInputElement = fixture.nativeElement.querySelector(`[data-testid="${id}"]`);
        el.value = value;
        el.dispatchEvent(new Event('input'));
      };
      setInput('campo-moneda', 'PEN');
      setInput('campo-fechaEmision', '2026-09-01');
      setInput('campo-proveedorCodigo', 'P00999');
      expect(emitidos).toEqual([
        { moneda: 'PEN' },
        { fechaEmision: '2026-09-01' },
        { proveedorCodigo: 'P00999' },
      ]);
    });

    it('emits buscarProveedor when the picker button is pressed', () => {
      const fixture = createComponent(factura);
      let emitido = false;
      fixture.componentInstance.buscarProveedor.subscribe(() => (emitido = true));
      (fixture.nativeElement.querySelector('[data-testid="abrir-picker-proveedor"]') as HTMLButtonElement).click();
      expect(emitido).toBe(true);
    });

    /* tasks.md 8.13 -- the picker slice must not change factura-form's save contract: the
     * buscarProveedor output stays a payload-less request, and the form owns no dialog itself. */
    it('emits buscarProveedor with no payload and renders no picker dialog of its own', () => {
      const fixture = createComponent(factura);
      let payload: unknown = 'sin-emitir';
      fixture.componentInstance.buscarProveedor.subscribe((v) => (payload = v));
      (fixture.nativeElement.querySelector('[data-testid="abrir-picker-proveedor"]') as HTMLButtonElement).click();
      expect(payload).toBeUndefined();
      expect(fixture.nativeElement.querySelector('dialog')).toBeNull();
    });

    it('disables editable inputs when editable=false (e.g. VALIDADA)', () => {
      const fixture = createComponent(factura, null, null, false);
      const input: HTMLInputElement = fixture.nativeElement.querySelector('[data-testid="campo-monto"]');
      const boton: HTMLButtonElement = fixture.nativeElement.querySelector('[data-testid="abrir-picker-proveedor"]');
      expect(input.disabled).toBe(true);
      expect(boton.disabled).toBe(true);
    });
  });

  describe('read-only display + derived rows', () => {
    it('renders base imponible / IGV formatted tabular from the asiento projection (BACKLOG #18 PR6)', () => {
      const fixture = createComponent(factura, null, null, true, 100, 18);
      const base = fixture.nativeElement.querySelector('[data-testid="valor-base"]');
      const igv = fixture.nativeElement.querySelector('[data-testid="valor-igv"]');
      expect(base.textContent.trim()).toBe('100.00');
      expect(igv.textContent.trim()).toBe('18.00');
      expect(base.classList.contains('tabular-nums')).toBe(true);
      expect(fixture.nativeElement.querySelector('[data-testid="valor-base"] input')).toBeNull();
    });

    it('renders base imponible / IGV as a neutral placeholder when there is no asiento value', () => {
      const fixture = createComponent(factura);
      expect(fixture.nativeElement.querySelector('[data-testid="valor-base"]').textContent.trim()).toBe('—');
      expect(fixture.nativeElement.querySelector('[data-testid="valor-igv"]').textContent.trim()).toBe('—');
    });

    it('shows the tipo de cambio (venta) value right-aligned tabular when present', () => {
      const fixture = createComponent(factura, 3.755);
      const tc = fixture.nativeElement.querySelector('[data-testid="valor-tc"]');
      expect(tc.textContent).toContain('3.755');
      expect(tc.classList.contains('tabular-nums')).toBe(true);
    });

    it('derives mes / día contable from fechaContable (read-only)', () => {
      const fixture = createComponent(factura, 3.5, '2026-08-09');
      expect(fixture.nativeElement.querySelector('[data-testid="valor-mes"]').textContent.trim()).toBe('08');
      expect(fixture.nativeElement.querySelector('[data-testid="valor-dia"]').textContent.trim()).toBe('09');
    });

    it('shows an em dash for mes / día when there is no asiento fechaContable', () => {
      const fixture = createComponent(factura, 3.5, null);
      expect(fixture.nativeElement.querySelector('[data-testid="valor-mes"]').textContent.trim()).toBe('—');
    });

    it('renders glosa read-only (em dash) when the factura is not PENDIENTE_VALIDACION', () => {
      const fixture = createComponent({ ...factura, estado: 'VALIDADA', glosa: null });
      const glosa = fixture.nativeElement.querySelector('[data-testid="valor-glosa"]');
      expect(glosa.textContent.trim()).toBe('—');
      expect(fixture.nativeElement.querySelector('[data-testid="campo-glosa"]')).toBeNull();
    });
  });

  describe('per-field OCR-missing highlight (BACKLOG #19)', () => {
    it('applies .campo--resaltado only to the fields named in camposNoExtraidos', () => {
      const fixture = createComponent({ ...factura, camposNoExtraidos: ['numero', 'total'] });
      const resaltado = (id: string): boolean =>
        fixture.nativeElement.querySelector(`[data-testid="${id}"]`).closest('.campo').classList.contains('campo--resaltado');
      expect(resaltado('campo-numero')).toBe(true);
      expect(resaltado('campo-monto')).toBe(true);
      expect(resaltado('campo-moneda')).toBe(false);
      expect(resaltado('campo-fechaEmision')).toBe(false);
      expect(fixture.nativeElement.querySelectorAll('.campo--resaltado').length).toBe(2);
    });

    it('applies no highlight when camposNoExtraidos is empty and tieneCamposNoExtraidos is false', () => {
      const fixture = createComponent(factura);
      expect(fixture.nativeElement.querySelector('.campo--resaltado')).toBeNull();
    });

    it('falls back to the coarse boolean (highlights multiple fields) for a pre-021 factura', () => {
      const fixture = createComponent({ ...factura, camposNoExtraidos: [], tieneCamposNoExtraidos: true });
      const resaltados = fixture.nativeElement.querySelectorAll('.campo--resaltado');
      expect(resaltados.length).toBeGreaterThan(1);
      expect(fixture.nativeElement.querySelector('.alerta--informativa')).toBeNull();
    });
  });

  describe('editable contable fields gated on PENDIENTE_VALIDACION (BACKLOG #19)', () => {
    const pendiente = (over: Partial<FacturaRespuesta> = {}): FacturaRespuesta => ({
      ...factura,
      estado: 'PENDIENTE_VALIDACION',
      ...over,
    });

    it('renders base imponible / IGV / glosa as editable inputs while PENDIENTE_VALIDACION', () => {
      const fixture = createComponent(pendiente(), null, null, true, 400, 72);
      const base: HTMLInputElement = fixture.nativeElement.querySelector('[data-testid="campo-baseImponible"]');
      const igv: HTMLInputElement = fixture.nativeElement.querySelector('[data-testid="campo-igv"]');
      const glosa: HTMLTextAreaElement = fixture.nativeElement.querySelector('[data-testid="campo-glosa"]');
      expect(base.value).toBe('400.00');
      expect(igv.value).toBe('72.00');
      expect(igv.disabled).toBe(false);
      expect(glosa).toBeTruthy();
      expect(fixture.nativeElement.querySelector('[data-testid="valor-base"]')).toBeNull();
    });

    it('renders base imponible / IGV as read-only outputs once VALIDADA', () => {
      const fixture = createComponent({ ...factura, estado: 'VALIDADA' }, null, null, false, 400, 72);
      expect(fixture.nativeElement.querySelector('[data-testid="campo-baseImponible"]')).toBeNull();
      expect(fixture.nativeElement.querySelector('[data-testid="valor-base"]').textContent.trim()).toBe('400.00');
    });

    it('emits the atomic { baseImponible, igv } pair when base imponible changes', () => {
      const fixture = createComponent(pendiente(), null, null, true, 400, 72);
      let emitido: unknown = null;
      fixture.componentInstance.cambios.subscribe((c) => (emitido = c));
      const base: HTMLInputElement = fixture.nativeElement.querySelector('[data-testid="campo-baseImponible"]');
      base.value = '450';
      base.dispatchEvent(new Event('input'));
      expect(emitido).toEqual({ baseImponible: 450, igv: 72 });
    });

    it('emits { glosa } when the glosa textarea changes', () => {
      const fixture = createComponent(pendiente(), null, null, true, 400, 72);
      let emitido: unknown = null;
      fixture.componentInstance.cambios.subscribe((c) => (emitido = c));
      const glosa: HTMLTextAreaElement = fixture.nativeElement.querySelector('[data-testid="campo-glosa"]');
      glosa.value = 'compra de insumos';
      glosa.dispatchEvent(new Event('input'));
      expect(emitido).toEqual({ glosa: 'compra de insumos' });
    });

    it('forces IGV to 0 and disables it for a boleta 03', () => {
      const fixture = createComponent(pendiente({ tipoComprobante: '03' }), null, null, true, 400, 72);
      const igv: HTMLInputElement = fixture.nativeElement.querySelector('[data-testid="campo-igv"]');
      expect(igv.value).toBe('0');
      expect(igv.disabled).toBe(true);
    });

    it('forces IGV to 0 and disables it for an EXONERADA operation', () => {
      const fixture = createComponent(pendiente({ afectacion: 'EXONERADA' }), null, null, true, 400, 0);
      const igv: HTMLInputElement = fixture.nativeElement.querySelector('[data-testid="campo-igv"]');
      expect(igv.disabled).toBe(true);
      expect(igv.value).toBe('0');
    });

    it('keeps IGV editable for a nota de crédito 07 even when non-gravada', () => {
      const fixture = createComponent(pendiente({ tipoComprobante: '07', afectacion: 'EXONERADA' }), null, null, true, 400, 18);
      const igv: HTMLInputElement = fixture.nativeElement.querySelector('[data-testid="campo-igv"]');
      expect(igv.disabled).toBe(false);
      expect(igv.value).toBe('18.00');
      let emitido: unknown = null;
      fixture.componentInstance.cambios.subscribe((c) => (emitido = c));
      igv.value = '20';
      igv.dispatchEvent(new Event('input'));
      expect(emitido).toEqual({ baseImponible: 400, igv: 20 });
    });
  });

  describe('dedicated tipo-de-cambio-faltante indicator', () => {
    it('shows the red "0.00" indicator for a foreign-currency factura with no TC venta', () => {
      const fixture = createComponent({ ...factura, moneda: 'USD' }, null);
      const indicador = fixture.nativeElement.querySelector('[data-testid="indicador-tc-faltante"]');
      expect(indicador).toBeTruthy();
      expect(indicador.textContent).toContain('0.00');
      expect(fixture.nativeElement.querySelector('[data-testid="valor-tc"]').textContent).toContain('0.00');
    });

    it('does not show the indicator for a PEN factura', () => {
      const fixture = createComponent({ ...factura, moneda: 'PEN' }, null);
      expect(fixture.nativeElement.querySelector('[data-testid="indicador-tc-faltante"]')).toBeNull();
    });

    it('does not show the indicator once a TC venta is available', () => {
      const fixture = createComponent({ ...factura, moneda: 'USD' }, 3.72);
      expect(fixture.nativeElement.querySelector('[data-testid="indicador-tc-faltante"]')).toBeNull();
    });
  });

  describe('indicator banners were hoisted to indicadores-factura (PR3)', () => {
    it('does NOT render a blocking banner for a duplicate invoice', () => {
      const fixture = createComponent({ ...factura, posibleDuplicado: true });
      expect(fixture.nativeElement.querySelector('.alerta--bloqueante')).toBeNull();
    });

    it('does NOT render a blocking banner for an unregistered provider P00000', () => {
      const fixture = createComponent({ ...factura, esProveedorGenerico: true });
      expect(fixture.nativeElement.querySelector('.alerta--bloqueante')).toBeNull();
    });
  });

  describe('afectación confirmation control', () => {
    it('is visible when afectacionMixta is null (unverified)', () => {
      const fixture = createComponent({ ...factura, afectacionMixta: null });
      expect(fixture.nativeElement.querySelector('[data-testid="confirmar-afectacion"]')).toBeTruthy();
    });

    it('is not rendered once afectacionMixta has a value', () => {
      const fixture = createComponent({ ...factura, afectacionMixta: true });
      expect(fixture.nativeElement.querySelector('[data-testid="confirmar-afectacion"]')).toBeNull();
    });

    it('emits confirmarAfectacion with the chosen esMixta value', () => {
      const fixture = createComponent({ ...factura, afectacionMixta: null });
      let emitido: unknown = 'sin-emitir';
      fixture.componentInstance.confirmarAfectacion.subscribe((v) => (emitido = v));
      (fixture.nativeElement.querySelector('[data-testid="confirmar-afectacion-mixta"]') as HTMLButtonElement).click();
      expect(emitido).toBe(true);
    });
  });
});
