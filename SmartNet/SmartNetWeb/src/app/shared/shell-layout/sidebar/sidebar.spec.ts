import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { Sidebar } from './sidebar';

/**
 * spec `spa-shell-nav` (canvas replica, `Gestor de Facturas.dc.html`): the sidebar mirrors the
 * handoff navigation — a primary group (Bandeja principal, Registro de compra, Proveedores, Plan
 * contable, Tipo de cambio), one hairline divider, then a utility group (Errores y notificaciones,
 * Sincronización, Configuración). `Bandeja`, `Proveedores` (BACKLOG #22 PR6 →
 * `/catalogos/proveedores`), `Plan contable` (BACKLOG #22 PR4 → `/catalogos/plan-contable`),
 * `Tipo de cambio` (BACKLOG #22 PR8 → `/catalogos/tipo-cambio`) and `Configuración` resolve to a
 * route; `Registro de compra` (BACKLOG #23 → `/registro-compra`, a top-level fiscal report, NOT
 * under `catalogos/`) now resolves too. The rest — `Errores y notificaciones`, `Sincronización` —
 * render as inert entries (`aria-disabled`, not links) marked "disponible próximamente". Glyphs are
 * `<div>`/`<span>` only (no `<svg>`, no icon font). Below the nav: an "Apariencia" theme card and a
 * profile row.
 *
 * NOTE (BACKLOG #22 PR8, memory `shell-nav-canvas-replica`): the canvas has NO `Tipo de cambio`
 * entry. Adding it as the 8th destination is a ratified owner decision (design D5 / spec decision
 * 5) — a reviewer must NOT "restore" the 7-entry list or the prior 7-glyph sidebar rules.
 */
describe('Sidebar', () => {
  async function crear(colapsado = false, usuario: string | null = null, temaEfectivo = 'claro') {
    await TestBed.configureTestingModule({
      imports: [Sidebar],
      providers: [provideRouter([])],
    }).compileComponents();

    const fixture = TestBed.createComponent(Sidebar);
    fixture.componentRef.setInput('colapsado', colapsado);
    fixture.componentRef.setInput('temaEfectivo', temaEfectivo);
    fixture.componentRef.setInput('usuario', usuario);
    fixture.detectChanges();
    return fixture;
  }

  it('renders the handoff navigation destinations in order', async () => {
    const fixture = await crear();
    const entradas = Array.from(
      fixture.nativeElement.querySelectorAll('[data-testid^="nav-"]:not([data-testid="nav-toggle"]):not([data-testid="nav-divisor"]):not([data-testid="nav-glifo"])')
    ) as HTMLElement[];

    expect(entradas.map((e) => e.textContent?.trim())).toEqual([
      'Bandeja principal',
      'Registro de compra',
      'Proveedores',
      'Plan contable',
      'Tipo de cambio',
      'Errores y notificaciones',
      'Sincronización',
      'Configuración',
    ]);
  });

  it('links only the destinations with a real route; the rest are inert', async () => {
    const fixture = await crear();
    const root: HTMLElement = fixture.nativeElement;

    const bandeja = root.querySelector('[data-testid="nav-bandeja"]')!;
    const configuracion = root.querySelector('[data-testid="nav-configuracion"]')!;
    const planContable = root.querySelector('[data-testid="nav-plan-contable"]')!;
    const proveedores = root.querySelector('[data-testid="nav-proveedores"]')!;
    const tipoCambio = root.querySelector('[data-testid="nav-tipo-cambio"]')!;
    const registro = root.querySelector('[data-testid="nav-registro"]')!;
    expect(bandeja.tagName).toBe('A');
    expect(configuracion.tagName).toBe('A');
    expect(planContable.tagName).toBe('A');
    expect(proveedores.tagName).toBe('A');
    expect(tipoCambio.tagName).toBe('A');
    expect(registro.tagName).toBe('A');
    expect(
      registro.getAttribute('ng-reflect-router-link') ?? registro.getAttribute('href')
    ).toContain('registro-compra');
    expect(
      tipoCambio.getAttribute('ng-reflect-router-link') ?? tipoCambio.getAttribute('href')
    ).toContain('catalogos/tipo-cambio');
    expect(
      bandeja.getAttribute('ng-reflect-router-link') ?? bandeja.getAttribute('href')
    ).toContain('bandeja');
    expect(
      configuracion.getAttribute('ng-reflect-router-link') ?? configuracion.getAttribute('href')
    ).toContain('configuracion');
    expect(
      planContable.getAttribute('ng-reflect-router-link') ?? planContable.getAttribute('href')
    ).toContain('catalogos/plan-contable');
    expect(
      proveedores.getAttribute('ng-reflect-router-link') ?? proveedores.getAttribute('href')
    ).toContain('catalogos/proveedores');

    for (const testid of ['nav-errores', 'nav-sincronizacion']) {
      const inerte = root.querySelector(`[data-testid="${testid}"]`)!;
      expect(inerte.tagName).not.toBe('A');
      expect(inerte.getAttribute('aria-disabled')).toBe('true');
    }
  });

  it('separates the primary and utility groups with exactly one hairline divider', async () => {
    const fixture = await crear();
    const root: HTMLElement = fixture.nativeElement;

    const divisores = root.querySelectorAll('[data-testid="nav-divisor"]');
    expect(divisores.length).toBe(1);

    const bandeja = root.querySelector('[data-testid="nav-bandeja"]')!;
    const divisor = divisores[0];
    const configuracion = root.querySelector('[data-testid="nav-configuracion"]')!;
    expect(bandeja.compareDocumentPosition(divisor) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(
      divisor.compareDocumentPosition(configuracion) & Node.DOCUMENT_POSITION_FOLLOWING
    ).toBeTruthy();
  });

  it('builds glyphs from div/span only — no svg, img, or icon font', async () => {
    const fixture = await crear();
    const root: HTMLElement = fixture.nativeElement;

    expect(root.querySelectorAll('svg').length).toBe(0);
    expect(root.querySelectorAll('img').length).toBe(0);
    const glifos = root.querySelectorAll('[data-testid="nav-glifo"]');
    expect(glifos.length).toBe(8);
    glifos.forEach((g) => expect(['DIV', 'SPAN']).toContain(g.tagName));
  });

  it('keeps each destination accessible-named when collapsed', async () => {
    const fixture = await crear(true);
    const root: HTMLElement = fixture.nativeElement;

    expect(root.querySelector('[data-testid="nav-bandeja"]')?.getAttribute('aria-label')).toBe(
      'Bandeja principal'
    );
    expect(
      root.querySelector('[data-testid="nav-configuracion"]')?.getAttribute('aria-label')
    ).toBe('Configuración');
  });

  it('emits alternar when the collapse toggle is activated', async () => {
    const fixture = await crear();
    let emitido = 0;
    fixture.componentInstance.alternar.subscribe(() => (emitido += 1));

    (fixture.nativeElement.querySelector('[data-testid="nav-toggle"]') as HTMLButtonElement).click();

    expect(emitido).toBe(1);
  });

  it('renders a sol/luna toggle button and emits alternarTema on click', async () => {
    const fixture = await crear();
    let veces = 0;
    fixture.componentInstance.alternarTema.subscribe(() => (veces += 1));

    const boton = fixture.nativeElement.querySelector(
      '[data-testid="toggle-tema"]'
    ) as HTMLButtonElement;
    expect(boton.tagName).toBe('BUTTON');
    boton.click();

    expect(veces).toBe(1);
  });

  it('labels the toggle "Cambiar a tema oscuro" while the effective theme is light', async () => {
    const fixture = await crear(false, null, 'claro');
    expect(
      fixture.nativeElement.querySelector('[data-testid="toggle-tema"]').getAttribute('aria-label')
    ).toBe('Cambiar a tema oscuro');
  });

  it('labels the toggle "Cambiar a tema claro" while the effective theme is dark', async () => {
    const fixture = await crear(false, null, 'oscuro');
    expect(
      fixture.nativeElement.querySelector('[data-testid="toggle-tema"]').getAttribute('aria-label')
    ).toBe('Cambiar a tema claro');
  });

  it('shows the session user in the profile row', async () => {
    const fixture = await crear(false, 'María Contadora');
    expect(
      fixture.nativeElement.querySelector('[data-testid="perfil"]')?.textContent
    ).toContain('María Contadora');
  });

  it('falls back to the role label when there is no session name', async () => {
    const fixture = await crear(false, null);
    expect(
      fixture.nativeElement.querySelector('[data-testid="perfil"]')?.textContent
    ).toContain('Asistente contable');
  });
});
