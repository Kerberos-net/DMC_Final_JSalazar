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

  const backdrop = (fixture: ReturnType<typeof createComponent>) =>
    fixture.nativeElement.querySelector('[data-testid="confirmar-reproceso-fondo"]') as HTMLElement | null;

  it('renders no backdrop element while closed', () => {
    const fixture = createComponent();
    expect(backdrop(fixture)).toBeNull();
  });

  it('renders the backdrop element after open() and removes it after either close path', () => {
    const fixture = createComponent();
    fixture.componentInstance.open();
    fixture.detectChanges();
    expect(backdrop(fixture)).not.toBeNull();

    fixture.componentInstance.onCancelar();
    fixture.detectChanges();
    expect(backdrop(fixture)).toBeNull();

    fixture.componentInstance.open();
    fixture.detectChanges();
    expect(backdrop(fixture)).not.toBeNull();

    fixture.componentInstance.onConfirmar();
    fixture.detectChanges();
    expect(backdrop(fixture)).toBeNull();
  });

  it('emits cancelar (not confirmar) when the backdrop is clicked', () => {
    const fixture = createComponent();
    fixture.componentInstance.open();
    fixture.detectChanges();

    let confirmado = false;
    let cancelado = false;
    fixture.componentInstance.confirmar.subscribe(() => (confirmado = true));
    fixture.componentInstance.cancelar.subscribe(() => (cancelado = true));

    backdrop(fixture)!.click();
    fixture.detectChanges();

    expect(cancelado).toBe(true);
    expect(confirmado).toBe(false);
    expect(fixture.nativeElement.querySelector('dialog').open).toBe(false);
  });

  it('emits cancelar when Escape is pressed on the dialog', () => {
    const fixture = createComponent();
    fixture.componentInstance.open();
    fixture.detectChanges();

    let cancelado = false;
    fixture.componentInstance.cancelar.subscribe(() => (cancelado = true));

    const dialogo: HTMLDialogElement = fixture.nativeElement.querySelector('dialog');
    dialogo.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();

    expect(cancelado).toBe(true);
  });

  it('moves focus to the Cancelar button when opened', () => {
    const fixture = createComponent();
    fixture.componentInstance.open();
    fixture.detectChanges();

    const botonCancelar: HTMLButtonElement = fixture.nativeElement.querySelector(
      '[data-testid="confirmar-reproceso-cancelar"]'
    );
    expect(document.activeElement).toBe(botonCancelar);
  });
});
