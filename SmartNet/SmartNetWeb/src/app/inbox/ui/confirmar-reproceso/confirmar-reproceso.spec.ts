import { TestBed } from '@angular/core/testing';
import { ConfirmarReproceso } from './confirmar-reproceso';

describe('ConfirmarReproceso', () => {
  const createComponent = () => {
    const fixture = TestBed.createComponent(ConfirmarReproceso);
    fixture.detectChanges();
    return fixture;
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ConfirmarReproceso] }).compileComponents();
  });

  it('renders a closed <dialog> until open() is invoked', () => {
    const fixture = createComponent();
    const dialogo: HTMLDialogElement = fixture.nativeElement.querySelector('dialog');
    expect(dialogo).not.toBeNull();
    expect(dialogo.open).toBe(false);
  });

  it('opens the native dialog when open() is called', () => {
    const fixture = createComponent();
    fixture.componentInstance.open();
    fixture.detectChanges();

    const dialogo: HTMLDialogElement = fixture.nativeElement.querySelector('dialog');
    expect(dialogo.open).toBe(true);
  });

  it('emits confirmar and closes the dialog when the confirm button is clicked', () => {
    const fixture = createComponent();
    fixture.componentInstance.open();
    fixture.detectChanges();

    let confirmado = false;
    fixture.componentInstance.confirmar.subscribe(() => (confirmado = true));

    const botonConfirmar: HTMLButtonElement = fixture.nativeElement.querySelector(
      '[data-testid="confirmar-reproceso-confirmar"]'
    );
    botonConfirmar.click();
    fixture.detectChanges();

    expect(confirmado).toBe(true);
    const dialogo: HTMLDialogElement = fixture.nativeElement.querySelector('dialog');
    expect(dialogo.open).toBe(false);
  });

  it('emits cancelar and closes the dialog when the cancel button is clicked, without emitting confirmar', () => {
    const fixture = createComponent();
    fixture.componentInstance.open();
    fixture.detectChanges();

    let confirmado = false;
    let cancelado = false;
    fixture.componentInstance.confirmar.subscribe(() => (confirmado = true));
    fixture.componentInstance.cancelar.subscribe(() => (cancelado = true));

    const botonCancelar: HTMLButtonElement = fixture.nativeElement.querySelector(
      '[data-testid="confirmar-reproceso-cancelar"]'
    );
    botonCancelar.click();
    fixture.detectChanges();

    expect(cancelado).toBe(true);
    expect(confirmado).toBe(false);
    const dialogo: HTMLDialogElement = fixture.nativeElement.querySelector('dialog');
    expect(dialogo.open).toBe(false);
  });
});
