import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { PickerProveedor } from './picker-proveedor';
import { ProveedorService } from '../../../catalogos/data-access/proveedor.service';
import { BusquedaProveedoresRespuesta } from '../../../catalogos/data-access/proveedor.model';

describe('PickerProveedor', () => {
  let httpMock: HttpTestingController;

  const respuesta: BusquedaProveedoresRespuesta = {
    resultados: [
      { codigo: 'P00011', nombre: 'ACME ANDINA EIRL', ruc: '20100000002' },
      { codigo: 'P00010', nombre: 'ACME PERU SAC', ruc: '20100000001' },
    ],
    hayMas: false,
  };

  const esperar = (ms: number) => new Promise((r) => setTimeout(r, ms));

  async function crear() {
    await TestBed.configureTestingModule({
      imports: [PickerProveedor],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    TestBed.inject(ProveedorService).debounceMs = 5;
    httpMock = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(PickerProveedor);
    fixture.componentInstance.abrir();
    fixture.detectChanges();
    return fixture;
  }

  afterEach(() => httpMock.verify());

  async function buscarYFlush(fixture: Awaited<ReturnType<typeof crear>>, termino = 'ACME') {
    const input: HTMLInputElement = fixture.nativeElement.querySelector('[data-testid="picker-buscar"]');
    input.value = termino;
    input.dispatchEvent(new Event('input'));
    await esperar(20);
    httpMock.expectOne((r) => r.url === '/api/catalogos/proveedores').flush(respuesta);
    await esperar(0);
    fixture.detectChanges();
  }

  it('opens a dialog with an accessible role/label and a debounced search input', async () => {
    const fixture = await crear();
    const dialogo: HTMLDialogElement = fixture.nativeElement.querySelector('[data-testid="picker-proveedor"]');
    expect(dialogo.open).toBe(true);
    expect(dialogo.getAttribute('role')).toBe('dialog');
    expect(dialogo.getAttribute('aria-modal')).toBe('true');
    expect(dialogo.getAttribute('aria-label')?.length).toBeGreaterThan(0);
    expect(fixture.nativeElement.querySelector('[data-testid="picker-buscar"]')).toBeTruthy();
  });

  it('renders each result row with nombre, codigo and ruc', async () => {
    const fixture = await crear();
    await buscarYFlush(fixture);
    const filas = fixture.nativeElement.querySelectorAll('[data-testid="picker-fila"]');
    expect(filas.length).toBe(2);
    expect(filas[0].textContent).toContain('ACME ANDINA EIRL');
    expect(filas[0].textContent).toContain('P00011');
    expect(filas[0].textContent).toContain('20100000002');
  });

  it('emits { codigo, ruc } and closes when a row is clicked', async () => {
    const fixture = await crear();
    await buscarYFlush(fixture);
    let elegido: unknown = null;
    fixture.componentInstance.seleccionar.subscribe((v) => (elegido = v));

    fixture.nativeElement.querySelectorAll('[data-testid="picker-fila"]')[1].click();

    expect(elegido).toEqual({ codigo: 'P00010', ruc: '20100000001' });
    expect(fixture.nativeElement.querySelector('[data-testid="picker-proveedor"]').open).toBe(false);
  });

  it('moves the active row with ArrowDown and selects it with Enter', async () => {
    const fixture = await crear();
    await buscarYFlush(fixture);
    let elegido: unknown = null;
    fixture.componentInstance.seleccionar.subscribe((v) => (elegido = v));
    const dialogo: HTMLElement = fixture.nativeElement.querySelector('[data-testid="picker-proveedor"]');

    dialogo.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    dialogo.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    fixture.detectChanges();
    const activa = fixture.nativeElement.querySelector('[data-testid="picker-fila"].picker-proveedor__fila--activa');
    expect(activa.textContent).toContain('ACME PERU SAC');

    dialogo.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    expect(elegido).toEqual({ codigo: 'P00010', ruc: '20100000001' });
  });

  it('closes on Escape without emitting a selection', async () => {
    const fixture = await crear();
    await buscarYFlush(fixture);
    let emitido = false;
    let cerrado = false;
    fixture.componentInstance.seleccionar.subscribe(() => (emitido = true));
    fixture.componentInstance.cerrar.subscribe(() => (cerrado = true));
    const dialogo: HTMLElement = fixture.nativeElement.querySelector('[data-testid="picker-proveedor"]');

    dialogo.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));

    expect(emitido).toBe(false);
    expect(cerrado).toBe(true);
    expect(fixture.nativeElement.querySelector('[data-testid="picker-proveedor"]').open).toBe(false);
  });

  it('keeps focus inside the dialog when Tab reaches the last focusable element', async () => {
    const fixture = await crear();
    await buscarYFlush(fixture);
    const dialogo: HTMLElement = fixture.nativeElement.querySelector('[data-testid="picker-proveedor"]');
    const cerrarBtn: HTMLButtonElement = fixture.nativeElement.querySelector('[data-testid="picker-cerrar"]');
    cerrarBtn.focus();

    dialogo.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }));

    expect(dialogo.contains(document.activeElement)).toBe(true);
  });

  it('issues only the search GET — never a PATCH', async () => {
    const fixture = await crear();
    await buscarYFlush(fixture);
    fixture.nativeElement.querySelectorAll('[data-testid="picker-fila"]')[0].click();
    httpMock.expectNone((r) => r.method === 'PATCH');
  });
});
