import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ShellLayout } from './shell-layout';

/**
 * spec `spa-shell-nav` (canvas replica, `Gestor de Facturas.dc.html`): the authenticated shell has
 * NO top header bar. Product identity, the theme `<select>` ("Apariencia" card) and the profile
 * row live in the sidebar; the routed screen owns its own page title.
 */
describe('ShellLayout', () => {
  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [ShellLayout],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  afterEach(() => localStorage.clear());

  it('should create the shell', () => {
    const fixture = TestBed.createComponent(ShellLayout);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('places a sol/luna theme toggle button inside the sidebar "Apariencia" card', () => {
    const fixture = TestBed.createComponent(ShellLayout);
    fixture.detectChanges();

    const root: HTMLElement = fixture.nativeElement;
    const apariencia = root.querySelector('app-sidebar [data-testid="apariencia"]');
    expect(apariencia).not.toBeNull();
    expect(apariencia!.textContent).toContain('Apariencia');

    const boton = apariencia!.querySelector('[data-testid="toggle-tema"]') as HTMLButtonElement;
    expect(boton).not.toBeNull();
    expect(boton.tagName).toBe('BUTTON');
    expect(root.querySelector('[data-testid="selector-tema"]')).toBeNull();
  });

  it('the toggle flips the effective theme and persists an explicit choice', () => {
    localStorage.setItem('fact.tema', 'claro');
    const fixture = TestBed.createComponent(ShellLayout);
    fixture.detectChanges();

    const boton = fixture.nativeElement.querySelector(
      '[data-testid="toggle-tema"]'
    ) as HTMLButtonElement;
    boton.click();
    fixture.detectChanges();

    expect(localStorage.getItem('fact.tema')).toBe('oscuro');
    expect(document.documentElement.dataset['tema']).toBe('oscuro');
  });

  it('has no top header bar — the marca lives in the sidebar', () => {
    const fixture = TestBed.createComponent(ShellLayout);
    fixture.detectChanges();

    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('.app-shell__header')).toBeNull();

    const marca = root.querySelector('app-sidebar .sidebar__marca') as HTMLElement;
    expect(marca.textContent).toContain('Facturas de Compra');
    expect(marca.querySelector('[data-testid="logo-badge"]')).not.toBeNull();
  });

  it('renders a <router-outlet> in the main content area', () => {
    const fixture = TestBed.createComponent(ShellLayout);
    fixture.detectChanges();

    const main = fixture.nativeElement.querySelector('.app-shell__main') as HTMLElement;
    const outlet = main.querySelector('router-outlet') as HTMLElement;
    expect(outlet).not.toBeNull();
  });

  it('renders the sidebar navigation inside the shell', () => {
    const fixture = TestBed.createComponent(ShellLayout);
    fixture.detectChanges();

    const sidebar = fixture.nativeElement.querySelector('app-sidebar') as HTMLElement;
    expect(sidebar).not.toBeNull();
    expect(sidebar.querySelector('[data-testid="nav-bandeja"]')).not.toBeNull();
    expect(sidebar.querySelector('[data-testid="nav-configuracion"]')).not.toBeNull();
  });

  it('starts expanded with no stored preference and collapses when the sidebar toggle fires', () => {
    const fixture = TestBed.createComponent(ShellLayout);
    fixture.detectChanges();

    const shell = fixture.nativeElement.querySelector('.app-shell') as HTMLElement;
    expect(shell.classList.contains('app-shell--sidebar-colapsado')).toBe(false);

    (fixture.nativeElement.querySelector('[data-testid="nav-toggle"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(shell.classList.contains('app-shell--sidebar-colapsado')).toBe(true);
  });

  it('re-reads the persisted collapsed state on a fresh instance', () => {
    localStorage.setItem('fact.sidebar', 'colapsado');

    const fixture = TestBed.createComponent(ShellLayout);
    fixture.detectChanges();

    const shell = fixture.nativeElement.querySelector('.app-shell') as HTMLElement;
    expect(shell.classList.contains('app-shell--sidebar-colapsado')).toBe(true);
  });
});
