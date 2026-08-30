import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { Sidebar } from './sidebar';

/**
 * tasks.md 1.3 (RED first) — spec `spa-shell-nav`: the sidebar lists exactly the two destinations
 * that have a real route today (`Bandeja`, `Configuración`), grouped by one hairline divider, with
 * `<div>`-only glyphs (no `<svg>`, no icon font), and every entry keeps its accessible name when
 * collapsed.
 */
describe('Sidebar', () => {
  async function crear(colapsado = false) {
    await TestBed.configureTestingModule({
      imports: [Sidebar],
      providers: [provideRouter([])],
    }).compileComponents();

    const fixture = TestBed.createComponent(Sidebar);
    fixture.componentRef.setInput('colapsado', colapsado);
    fixture.detectChanges();
    return fixture;
  }

  it('renders exactly the two routed destinations, Bandeja then Configuración', async () => {
    const fixture = await crear();
    const enlaces = Array.from(
      fixture.nativeElement.querySelectorAll('a[data-testid^="nav-"]')
    ) as HTMLAnchorElement[];

    expect(enlaces.map((a) => a.textContent?.trim())).toEqual(['Bandeja', 'Configuración']);
  });

  it('links to the existing routes and adds no disabled or placeholder entries', async () => {
    const fixture = await crear();
    const root: HTMLElement = fixture.nativeElement;

    expect(
      root.querySelector('[data-testid="nav-bandeja"]')?.getAttribute('ng-reflect-router-link') ??
        root.querySelector('[data-testid="nav-bandeja"]')?.getAttribute('href')
    ).toContain('bandeja');
    expect(
      root
        .querySelector('[data-testid="nav-configuracion"]')
        ?.getAttribute('ng-reflect-router-link') ??
        root.querySelector('[data-testid="nav-configuracion"]')?.getAttribute('href')
    ).toContain('configuracion');

    expect(root.querySelector('[disabled]')).toBeNull();
    expect(root.textContent).not.toMatch(/pr[oó]ximamente|coming soon|en construcci[oó]n/i);
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

  it('builds glyphs from <div> only — no svg, img, or icon font', async () => {
    const fixture = await crear();
    const root: HTMLElement = fixture.nativeElement;

    expect(root.querySelectorAll('svg').length).toBe(0);
    expect(root.querySelectorAll('img').length).toBe(0);
    const glifos = root.querySelectorAll('[data-testid="nav-glifo"]');
    expect(glifos.length).toBeGreaterThanOrEqual(2);
    glifos.forEach((g) => expect(g.tagName).toBe('DIV'));
  });

  it('keeps each destination accessible-named when collapsed', async () => {
    const fixture = await crear(true);
    const root: HTMLElement = fixture.nativeElement;

    expect(root.querySelector('[data-testid="nav-bandeja"]')?.getAttribute('aria-label')).toBe(
      'Bandeja'
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
});
