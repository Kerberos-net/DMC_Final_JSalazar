import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('renders the theme control as a native <select> with system/light/dark options', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    const control = fixture.nativeElement.querySelector('[data-testid="selector-tema"]') as HTMLSelectElement;
    expect(control.tagName).toBe('SELECT');
    expect(Array.from(control.options).map((o) => o.value)).toEqual(['sistema', 'claro', 'oscuro']);
  });

  it('does not introduce a sun/moon toggle or an "Apariencia" sidebar control', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('[data-testid="toggle-tema"]')).toBeNull();
    expect(root.querySelector('button')).toBeNull();
    expect(root.textContent).not.toContain('Apariencia');
  });

  it('shows the product marca in the shell header', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    const marca = fixture.nativeElement.querySelector('.app-shell__marca') as HTMLElement;
    expect(marca.textContent).toContain('Gestor de Facturas de Compra');

    const badge = marca.querySelector('[data-testid="logo-badge"]') as HTMLElement;
    expect(badge.textContent?.trim()).toBe('GF');
  });
});
