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
