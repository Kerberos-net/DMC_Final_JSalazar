import { TestBed } from '@angular/core/testing';
import { InboxFilter } from './inbox-filter';

describe('InboxFilter', () => {
  const createComponent = () => {
    const fixture = TestBed.createComponent(InboxFilter);
    fixture.componentRef.setInput('orden', 'desc');
    fixture.componentRef.setInput('desde', null);
    fixture.componentRef.setInput('hasta', null);
    fixture.componentRef.setInput('proveedor', null);
    fixture.detectChanges();
    return fixture;
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [InboxFilter],
    }).compileComponents();
  });

  it('no longer carries the estado control — it moved to the estado-chip row', () => {
    const fixture = createComponent();
    expect(fixture.nativeElement.querySelector('[data-testid="estado-select"]')).toBeNull();
    expect('estadoChange' in fixture.componentInstance).toBe(false);
  });

  it('is one inline row of unlabelled controls, each accessible-named and on the shared campo class', () => {
    const fixture = createComponent();
    const root: HTMLElement = fixture.nativeElement;

    // Handoff §2: no visible <label> text — the search uses a placeholder, the rest aria-label.
    expect(root.querySelectorAll('.inbox-filter label')).toHaveLength(0);

    const controles = Array.from(
      root.querySelectorAll('.inbox-filter .campo')
    ) as HTMLElement[];
    expect(controles).toHaveLength(4);
    for (const c of controles) {
      expect(c.getAttribute('aria-label') ?? c.getAttribute('placeholder')).toBeTruthy();
    }
  });

  it('emits ordenChange with the selected order', () => {
    const fixture = createComponent();
    const emitted: string[] = [];
    fixture.componentInstance.ordenChange.subscribe((v) => emitted.push(v));

    const select: HTMLSelectElement = fixture.nativeElement.querySelector(
      'select[data-testid="orden-select"]'
    );
    select.value = 'asc';
    select.dispatchEvent(new Event('change'));

    expect(emitted).toEqual(['asc']);
  });

  it('emits desdeChange on change, not on every keystroke', () => {
    const fixture = createComponent();
    const emitted: (string | null)[] = [];
    fixture.componentInstance.desdeChange.subscribe((v) => emitted.push(v));

    const input: HTMLInputElement = fixture.nativeElement.querySelector(
      'input[data-testid="desde-input"]'
    );
    input.value = '2026-01-01';
    input.dispatchEvent(new Event('input'));
    expect(emitted).toEqual([]);

    input.dispatchEvent(new Event('change'));
    expect(emitted).toEqual(['2026-01-01']);
  });

  it('emits hastaChange on change', () => {
    const fixture = createComponent();
    const emitted: (string | null)[] = [];
    fixture.componentInstance.hastaChange.subscribe((v) => emitted.push(v));

    const input: HTMLInputElement = fixture.nativeElement.querySelector(
      'input[data-testid="hasta-input"]'
    );
    input.value = '2026-01-31';
    input.dispatchEvent(new Event('change'));

    expect(emitted).toEqual(['2026-01-31']);
  });

  it('emits proveedorChange on change and on Enter, not on every keystroke', () => {
    const fixture = createComponent();
    const emitted: (string | null)[] = [];
    fixture.componentInstance.proveedorChange.subscribe((v) => emitted.push(v));

    const input: HTMLInputElement = fixture.nativeElement.querySelector(
      'input[data-testid="proveedor-input"]'
    );
    input.value = 'P001';
    input.dispatchEvent(new Event('input'));
    expect(emitted).toEqual([]);

    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    expect(emitted).toEqual(['P001']);
  });
});
