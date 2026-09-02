import { TestBed } from '@angular/core/testing';
import { AsientoLineas } from './asiento-lineas';
import { LineaRespuesta } from '../../models/asiento.model';
import { EntradaAuditoriaRespuesta } from '../../models/historial.model';
import { Cuadre, calcularCuadre } from '../../data-access/cuadre';

describe('AsientoLineas', () => {
  const lineaD: LineaRespuesta = {
    lineaId: 1,
    orden: 1,
    bloque: 'PRINCIPAL',
    tipo: 'D',
    debe: 118,
    haber: 0,
    cuentaCodigo: '639915',
    cuentaDescripcion: null,
    ctaReflejaCodigo: null,
    ctaPuenteCodigo: null,
  };
  const lineaH: LineaRespuesta = {
    lineaId: 2,
    orden: 2,
    bloque: 'PRINCIPAL',
    tipo: 'H',
    debe: 0,
    haber: 118,
    cuentaCodigo: '421001',
    cuentaDescripcion: null,
    ctaReflejaCodigo: null,
    ctaPuenteCodigo: null,
  };

  const createComponent = (lineas: LineaRespuesta[], editable = true, cuadre?: Cuadre) => {
    const fixture = TestBed.createComponent(AsientoLineas);
    fixture.componentRef.setInput('lineas', lineas);
    fixture.componentRef.setInput('editable', editable);
    fixture.componentRef.setInput('cuadre', cuadre ?? calcularCuadre(lineas));
    fixture.detectChanges();
    return fixture;
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [AsientoLineas] }).compileComponents();
  });

  /* tasks.md 3.7 (RED first), spa-visual-detalle-validacion "asiento-lineas renders as a tabular
   * Debe/Haber grid with a total row and cuadre pill". Totals + pill state come from the `cuadre`
   * already computed in `detalle-page.ts` -- this component never recomputes accounting. */
  describe('tabular totals and cuadre pill', () => {
    it('renders a Total row with the per-column sums from the cuadre input, 2-decimal formatted', () => {
      const fixture = createComponent([lineaD, lineaH]);
      expect(fixture.nativeElement.querySelector('[data-testid="total-debe"]').textContent.trim()).toBe('118.00');
      expect(fixture.nativeElement.querySelector('[data-testid="total-haber"]').textContent.trim()).toBe('118.00');
    });

    it('shows a balanced cuadre pill when the cuadre input is cuadrado', () => {
      const fixture = createComponent([lineaD, lineaH], true, { debe: 118, haber: 118, cuadrado: true });
      expect(fixture.nativeElement.querySelector('[data-testid="cuadre-pill"]').textContent).toContain('Cuadra');
    });

    it('shows an unbalanced cuadre pill when the cuadre input is not cuadrado', () => {
      const fixture = createComponent([lineaD], true, { debe: 118, haber: 0, cuadrado: false });
      const pill = fixture.nativeElement.querySelector('[data-testid="cuadre-pill"]');
      expect(pill.textContent).toContain('No cuadra');
      expect(fixture.nativeElement.querySelector('[data-testid="total-haber"]').textContent.trim()).toBe('0.00');
    });

    it('labels the add-línea affordance "+ Agregar línea"', () => {
      const fixture = createComponent([lineaD, lineaH]);
      const agregar = fixture.nativeElement.querySelector('[data-testid="agregar-linea"]');
      expect(agregar.textContent.replace(/\s+/g, ' ').trim()).toBe('+ Agregar línea');
    });
  });

  it('renders one row per línea with its cuentaCodigo and debe/haber', () => {
    const fixture = createComponent([lineaD, lineaH]);
    const filas = fixture.nativeElement.querySelectorAll('[data-testid^="linea-"]');
    expect(filas.length).toBe(2);
    expect(fixture.nativeElement.querySelector('[data-testid="linea-1"]').textContent).toContain('639915');
  });

  it('editing a línea inline emits editarLinea with only that línea, and confirming closes edit mode', () => {
    const fixture = createComponent([lineaD, lineaH]);
    let emitido: { lineaId: number; linea: unknown } | null = null;
    fixture.componentInstance.editarLinea.subscribe((e) => (emitido = e));

    (fixture.nativeElement.querySelector('[data-testid="editar-1"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    const debeInput: HTMLInputElement = fixture.nativeElement.querySelector('[data-testid="editar-debe-1"]');
    debeInput.value = '100';
    debeInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('[data-testid="confirmar-edicion-1"]') as HTMLButtonElement).click();

    expect(emitido).not.toBeNull();
    expect(emitido!.lineaId).toBe(1);
    expect((emitido!.linea as { debe: number }).debe).toBe(100);
  });

  it('editing a línea inline can assign its cuentaCodigo (engine seed left it SinCuenta)', () => {
    const sinCuenta: LineaRespuesta = { ...lineaD, cuentaCodigo: null };
    const fixture = createComponent([sinCuenta, lineaH]);
    let emitido: { lineaId: number; linea: unknown } | null = null;
    fixture.componentInstance.editarLinea.subscribe((e) => (emitido = e));

    (fixture.nativeElement.querySelector('[data-testid="editar-1"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    const cuentaInput: HTMLInputElement = fixture.nativeElement.querySelector('[data-testid="editar-cuentaCodigo-1"]');
    expect(cuentaInput).not.toBeNull();
    cuentaInput.value = '639915';
    cuentaInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('[data-testid="confirmar-edicion-1"]') as HTMLButtonElement).click();

    expect((emitido!.linea as { cuentaCodigo: string }).cuentaCodigo).toBe('639915');
  });

  it('adding a línea emits agregarLinea with the composed request', () => {
    const fixture = createComponent([lineaD, lineaH]);
    let emitido: unknown = null;
    fixture.componentInstance.agregarLinea.subscribe((e) => (emitido = e));

    (fixture.nativeElement.querySelector('[data-testid="agregar-linea"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    const cuentaInput: HTMLInputElement = fixture.nativeElement.querySelector(
      '[data-testid="nueva-cuentaCodigo"]'
    );
    cuentaInput.value = '639916';
    cuentaInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('[data-testid="confirmar-nueva"]') as HTMLButtonElement).click();

    expect(emitido).toEqual(
      expect.objectContaining({ cuentaCodigo: '639916' })
    );
  });

  it('deleting a línea requires confirmation: canceling leaves the línea unchanged and emits nothing', () => {
    const fixture = createComponent([lineaD, lineaH]);
    let emitido = false;
    fixture.componentInstance.eliminarLinea.subscribe(() => (emitido = true));

    (fixture.nativeElement.querySelector('[data-testid="eliminar-1"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="confirmar-eliminar-1"]')).not.toBeNull();

    (fixture.nativeElement.querySelector('[data-testid="cancelar-eliminar-1"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(emitido).toBe(false);
    expect(fixture.nativeElement.querySelector('[data-testid="linea-1"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('[data-testid="confirmar-eliminar-1"]')).toBeNull();
  });

  it('deleting a línea: confirming emits eliminarLinea with the lineaId', () => {
    const fixture = createComponent([lineaD, lineaH]);
    let emitido: number | null = null;
    fixture.componentInstance.eliminarLinea.subscribe((id) => (emitido = id));

    (fixture.nativeElement.querySelector('[data-testid="eliminar-1"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('[data-testid="confirmar-eliminar-1"]') as HTMLButtonElement).click();

    expect(emitido).toBe(1);
  });

  it('hides edit/add/delete controls when editable=false (CONFIRMADO asiento)', () => {
    const fixture = createComponent([lineaD, lineaH], false);
    expect(fixture.nativeElement.querySelector('[data-testid="editar-1"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-testid="eliminar-1"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-testid="agregar-linea"]')).toBeNull();
  });

  /* tasks.md 4.9: wires `<app-historial-correccion>` -- panel stays closed by default (D4). */
  it('wires the historial-correccion panel with the received historial input', () => {
    const fixture = TestBed.createComponent(AsientoLineas);
    fixture.componentRef.setInput('lineas', [lineaD, lineaH]);
    fixture.componentRef.setInput('editable', true);
    fixture.componentRef.setInput('cuadre', calcularCuadre([lineaD, lineaH]));
    const historial: EntradaAuditoriaRespuesta[] = [
      {
        entidadTipo: 'ASIENTO',
        entidadId: 7,
        accion: 'CORRECCION',
        campo: 'cuentaCodigo',
        valorOriginal: '639910',
        valorNuevo: '639915',
        motivo: null,
        usuarioId: 1,
        ocurridoEn: '2026-08-20T15:00:00Z',
      },
    ];
    fixture.componentRef.setInput('historial', historial);
    fixture.detectChanges();

    const details: HTMLDetailsElement = fixture.nativeElement.querySelector('app-historial-correccion details');
    expect(details).toBeTruthy();
    expect(details.open).toBe(false);
    expect(fixture.nativeElement.textContent).toContain('639915');
  });
});
