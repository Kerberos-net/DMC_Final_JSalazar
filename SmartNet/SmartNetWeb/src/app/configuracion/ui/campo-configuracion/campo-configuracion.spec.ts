import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CampoConfiguracion } from './campo-configuracion';
import { ConfiguracionEntrada } from '../../models/configuracion.model';

describe('CampoConfiguracion', () => {
  let fixture: ComponentFixture<CampoConfiguracion>;
  let component: CampoConfiguracion;

  const entrada: ConfiguracionEntrada = {
    seccion: 'TELEGRAM',
    clave: 'DESTINO_CHAT_ID',
    tipo: 'TEXTO',
    valor: '-1',
    valorPorDefecto: null,
    descripcion: 'Chat de Telegram al que se envian las alertas.',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [CampoConfiguracion] });
    fixture = TestBed.createComponent(CampoConfiguracion);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('entrada', entrada);
    fixture.detectChanges();
  });

  it('renders the current valor in the input', () => {
    const input: HTMLInputElement = fixture.nativeElement.querySelector(
      '[data-testid="campo-valor"]'
    );
    expect(input.value).toBe('-1');
  });

  it('emits guardar with the seccion/clave/valor when the operator saves', () => {
    const emitidos: { seccion: string; clave: string; valor: string | null }[] = [];
    component.guardar.subscribe((evento) => emitidos.push(evento));

    const input: HTMLInputElement = fixture.nativeElement.querySelector('[data-testid="campo-valor"]');
    input.value = '-100200300';
    input.dispatchEvent(new Event('input'));
    fixture.nativeElement.querySelector('[data-testid="campo-guardar"]').click();

    expect(emitidos).toEqual([{ seccion: 'TELEGRAM', clave: 'DESTINO_CHAT_ID', valor: '-100200300' }]);
  });

  it('shows the server-side error message when one is set', () => {
    fixture.componentRef.setInput('error', 'El valor no cumple el tipo declarado de la clave');
    fixture.detectChanges();

    const error: HTMLElement = fixture.nativeElement.querySelector('[data-testid="campo-error"]');
    expect(error.textContent).toContain('El valor no cumple el tipo declarado de la clave');
  });

  it('does not render an error element when there is none', () => {
    const error = fixture.nativeElement.querySelector('[data-testid="campo-error"]');
    expect(error).toBeNull();
  });
});
