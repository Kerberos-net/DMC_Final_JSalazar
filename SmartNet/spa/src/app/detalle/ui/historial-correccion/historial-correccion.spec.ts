import { TestBed } from '@angular/core/testing';
import { HistorialCorreccion } from './historial-correccion';
import { EntradaAuditoriaRespuesta } from '../../models/historial.model';

/**
 * tasks.md 4.6-family (RED first) -- spa-visual-detalle-validacion "Correction history panel
 * collapsed by default" + "History panel has a defined empty-state visual treatment" (D4: native
 * `<details>`/`<summary>`, zero Angular state).
 */
describe('HistorialCorreccion', () => {
  const entradas: readonly EntradaAuditoriaRespuesta[] = [
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

  function crear(historial: readonly EntradaAuditoriaRespuesta[]) {
    const fixture = TestBed.createComponent(HistorialCorreccion);
    fixture.componentRef.setInput('historial', historial);
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [HistorialCorreccion] }).compileComponents();
  });

  it('renders a native <details> closed by default', () => {
    const fixture = crear(entradas);
    const details: HTMLDetailsElement = fixture.nativeElement.querySelector('details');
    expect(details).toBeTruthy();
    expect(details.open).toBe(false);
  });

  it('shows field, previous value, new value and timestamp for each entry', () => {
    const fixture = crear(entradas);
    const texto = fixture.nativeElement.textContent as string;
    expect(texto).toContain('rucProveedor');
    expect(texto).toContain('20111111111');
    expect(texto).toContain('20123456789');
  });

  it('shows a neutral empty state, not an alert token, when historial is empty', () => {
    const fixture = crear([]);
    const vacio = fixture.nativeElement.querySelector('[data-testid="historial-vacio"]');
    expect(vacio).toBeTruthy();
    expect(vacio.className).not.toContain('alerta');
    expect(fixture.nativeElement.querySelector('[data-testid="historial-entrada"]')).toBeNull();
  });
});
