import { TestBed } from '@angular/core/testing';
import { PanelErrores } from './panel-errores';
import { ErrorProcesamiento } from '../../models/bandeja-item.model';

describe('PanelErrores', () => {
  const errores: ErrorProcesamiento[] = [
    {
      procesamientoErrorId: 1,
      integracion: 'SUNAT',
      mensaje: 'Timeout de conexión',
      clasificacion: 'TRANSITORIO',
      ocurridoEn: '2026-08-07T09:00:00Z',
    },
    {
      procesamientoErrorId: 2,
      integracion: 'SUNAT',
      mensaje: 'RUC no encontrado',
      clasificacion: 'PERMANENTE',
      ocurridoEn: '2026-08-08T09:00:00Z',
    },
  ];

  const createComponent = (valorErrores: ErrorProcesamiento[]) => {
    const fixture = TestBed.createComponent(PanelErrores);
    fixture.componentRef.setInput('errores', valorErrores);
    fixture.detectChanges();
    return fixture;
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [PanelErrores] }).compileComponents();
  });

  it('renders every error entry with Mensaje, Clasificacion and OcurridoEn', () => {
    const fixture = createComponent(errores);
    const filas = Array.from(
      fixture.nativeElement.querySelectorAll('[data-testid="error-item"]')
    ) as HTMLElement[];

    expect(filas.length).toBe(2);
    expect(filas[0].textContent).toContain('Timeout de conexión');
    expect(filas[0].textContent).toContain('TRANSITORIO');
    expect(filas[1].textContent).toContain('RUC no encontrado');
    expect(filas[1].textContent).toContain('PERMANENTE');
  });

  it('renders nothing for an empty errores array', () => {
    const fixture = createComponent([]);
    const filas = fixture.nativeElement.querySelectorAll('[data-testid="error-item"]');
    expect(filas.length).toBe(0);
    expect(fixture.nativeElement.querySelector('[data-testid="panel-errores"]')).toBeNull();
  });
});
