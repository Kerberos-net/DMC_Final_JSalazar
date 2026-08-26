import { ComponentFixture, TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { ConfiguracionPage } from './configuracion-page';
import { ConfiguracionEntrada } from '../../models/configuracion.model';

describe('ConfiguracionPage', () => {
  let fixture: ComponentFixture<ConfiguracionPage>;
  let httpMock: HttpTestingController;

  const entradaTelegram: ConfiguracionEntrada = {
    seccion: 'TELEGRAM',
    clave: 'DESTINO_CHAT_ID',
    tipo: 'TEXTO',
    valor: null,
    valorPorDefecto: null,
    descripcion: 'Chat de Telegram al que se envian las alertas.',
  };
  const entradaCorreo: ConfiguracionEntrada = {
    seccion: 'CORREO',
    clave: 'DESTINATARIOS',
    tipo: 'LISTA',
    valor: null,
    valorPorDefecto: null,
    descripcion: 'Direcciones de correo para la alerta de respaldo.',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ConfiguracionPage],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    fixture = TestBed.createComponent(ConfiguracionPage);
    httpMock = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('loads every section on init', async () => {
    const req = httpMock.expectOne((r) => r.url === '/api/configuracion');
    req.flush([entradaTelegram, entradaCorreo]);
    await fixture.whenStable();

    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('app-configuracion-seccion').length).toBe(2);
  });

  it('operator edits TELEGRAM.DESTINO_CHAT_ID and it PUTs then reflects without a redeploy', async () => {
    httpMock.expectOne((r) => r.url === '/api/configuracion').flush([entradaTelegram]);
    fixture.detectChanges();

    const component = fixture.componentInstance;
    const guardando = component.onGuardar({ seccion: 'TELEGRAM', clave: 'DESTINO_CHAT_ID', valor: '-100200300' });

    const req = httpMock.expectOne('/api/configuracion/TELEGRAM/DESTINO_CHAT_ID');
    expect(req.request.body).toEqual({ valor: '-100200300' });
    req.flush({});
    await guardando;
    fixture.detectChanges();

    expect(component.entradas()[0].valor).toBe('-100200300');
  });

  it('surfaces a rejected write as a per-clave error without applying the invalid value', async () => {
    httpMock.expectOne((r) => r.url === '/api/configuracion').flush([entradaTelegram]);
    fixture.detectChanges();

    const component = fixture.componentInstance;
    const guardando = component.onGuardar({ seccion: 'TELEGRAM', clave: 'DESTINO_CHAT_ID', valor: 'x'.repeat(500) });

    const req = httpMock.expectOne('/api/configuracion/TELEGRAM/DESTINO_CHAT_ID');
    req.flush(
      { type: 'https://smartnet.local/problemas/configuracion-valor-invalido', title: 'invalido', status: 422 },
      { status: 422, statusText: 'Unprocessable Entity' }
    );
    await guardando;
    fixture.detectChanges();

    expect(component.erroresPorClave()['DESTINO_CHAT_ID']).toBeTruthy();
    expect(component.entradas()[0].valor).toBeNull();
  });
});
