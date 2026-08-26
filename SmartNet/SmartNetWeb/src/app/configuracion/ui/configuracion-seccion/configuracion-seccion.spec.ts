import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ConfiguracionSeccion } from './configuracion-seccion';
import { ConfiguracionEntrada } from '../../models/configuracion.model';

describe('ConfiguracionSeccion', () => {
  let fixture: ComponentFixture<ConfiguracionSeccion>;
  let component: ConfiguracionSeccion;

  const entradas: ConfiguracionEntrada[] = [
    {
      seccion: 'TELEGRAM',
      clave: 'DESTINO_CHAT_ID',
      tipo: 'TEXTO',
      valor: null,
      valorPorDefecto: null,
      descripcion: 'Chat de Telegram.',
    },
  ];

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [ConfiguracionSeccion] });
    fixture = TestBed.createComponent(ConfiguracionSeccion);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('seccion', 'TELEGRAM');
    fixture.componentRef.setInput('entradas', entradas);
    fixture.detectChanges();
  });

  it('renders the seccion heading', () => {
    expect(fixture.nativeElement.textContent).toContain('TELEGRAM');
  });

  it('renders one app-campo-configuracion per entrada', () => {
    const campos = fixture.nativeElement.querySelectorAll('app-campo-configuracion');
    expect(campos.length).toBe(1);
  });

  it('re-emits guardar from a campo-configuracion child', () => {
    const emitidos: unknown[] = [];
    component.guardar.subscribe((evento) => emitidos.push(evento));

    component.onGuardar({ seccion: 'TELEGRAM', clave: 'DESTINO_CHAT_ID', valor: '-1' });

    expect(emitidos).toEqual([{ seccion: 'TELEGRAM', clave: 'DESTINO_CHAT_ID', valor: '-1' }]);
  });
});
