import { TestBed } from '@angular/core/testing';
import { ConflictoBanner } from './conflicto-banner';
import { ProblemaDetails } from '../../../shared/problema.model';

describe('ConflictoBanner', () => {
  const createComponent = (
    problema: ProblemaDetails | null,
    categoria: 'conflicto-concurrencia' | 'invariante' | 'negocio' | 'precondicion-cliente' | null
  ) => {
    const fixture = TestBed.createComponent(ConflictoBanner);
    fixture.componentRef.setInput('problema', problema);
    fixture.componentRef.setInput('categoria', categoria);
    fixture.detectChanges();
    return fixture;
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ConflictoBanner] }).compileComponents();
  });

  it('renders nothing when there is no problema', () => {
    const fixture = createComponent(null, null);
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it('shows a 412 conflict as a blocking banner with a single "recargar" action (spec.md scenario)', () => {
    const problema: ProblemaDetails = {
      type: 'https://smartnet.local/problemas/precondicion-fallida',
      title: 'El recurso fue modificado por otro cliente',
      status: 412,
      detail: 'Recargue e inténtelo de nuevo.',
    };
    const fixture = createComponent(problema, 'conflicto-concurrencia');

    const alerta = fixture.nativeElement.querySelector('[role="alert"]');
    expect(alerta.textContent).toContain('Recargue e inténtelo de nuevo.');
    expect(fixture.nativeElement.querySelector('[data-testid="recargar"]')).not.toBeNull();
  });

  it('shows a 422 invariante WITHOUT the reload action, keeping edits (spec.md scenario)', () => {
    const problema: ProblemaDetails = {
      type: 'https://smartnet.local/problemas/asiento-descuadrado',
      title: 'El asiento no cuadra',
      status: 422,
      detail: 'El asiento no cuadra.',
      importeEsperado: 118,
      importeReal: 100,
    };
    const fixture = createComponent(problema, 'invariante');

    const alerta = fixture.nativeElement.querySelector('[role="alert"]');
    expect(alerta.textContent).toContain('El asiento no cuadra.');
    expect(fixture.nativeElement.querySelector('[data-testid="recargar"]')).toBeNull();
  });

  /* tasks.md 4.11 (RED first), spa-visual-detalle-validacion "412 vs. 422 visually distinct":
   * dedicated token/class + distinct icon shape, never sharing color. */
  it('412 renders .banner--conflicto (violeta), not .banner--error', () => {
    const problema: ProblemaDetails = {
      type: 'https://smartnet.local/problemas/precondicion-fallida',
      title: 't',
      status: 412,
      detail: 'd',
    };
    const fixture = createComponent(problema, 'conflicto-concurrencia');

    expect(fixture.nativeElement.querySelector('.banner--conflicto')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('.banner--error')).toBeNull();
  });

  it('422 renders .banner--error (rojo), not .banner--conflicto', () => {
    const problema: ProblemaDetails = {
      type: 'https://smartnet.local/problemas/asiento-descuadrado',
      title: 't',
      status: 422,
      detail: 'd',
    };
    const fixture = createComponent(problema, 'invariante');

    expect(fixture.nativeElement.querySelector('.banner--error')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('.banner--conflicto')).toBeNull();
  });

  it('412 and 422 use distinct inline SVG icon shapes (redundant channel, not color alone)', () => {
    const conflicto = createComponent(
      { type: 't', title: 't', status: 412, detail: 'd' },
      'conflicto-concurrencia'
    );
    const error = createComponent({ type: 't', title: 't', status: 422, detail: 'd' }, 'invariante');

    const iconoConflicto = conflicto.nativeElement.querySelector('svg')?.getAttribute('data-icono');
    const iconoError = error.nativeElement.querySelector('svg')?.getAttribute('data-icono');
    expect(iconoConflicto).toBeTruthy();
    expect(iconoError).toBeTruthy();
    expect(iconoConflicto).not.toBe(iconoError);
  });

  it('emits recargar when the reload button is clicked', () => {
    const problema: ProblemaDetails = {
      type: 'https://smartnet.local/problemas/precondicion-fallida',
      title: 't',
      status: 412,
      detail: 'd',
    };
    const fixture = createComponent(problema, 'conflicto-concurrencia');
    let emitido = false;
    fixture.componentInstance.recargar.subscribe(() => (emitido = true));

    (fixture.nativeElement.querySelector('[data-testid="recargar"]') as HTMLButtonElement).click();

    expect(emitido).toBe(true);
  });
});
