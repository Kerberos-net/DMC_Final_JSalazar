import { TestBed } from '@angular/core/testing';
import { InboxFilter } from './inbox-filter';

describe('InboxFilter', () => {
  const createComponent = () => {
    const fixture = TestBed.createComponent(InboxFilter);
    fixture.componentRef.setInput('estado', null);
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

  it('renders an option for every EstadoConsumo plus "todos"', () => {
    const fixture = createComponent();
    const options = Array.from(
      fixture.nativeElement.querySelectorAll('select[data-testid="estado-select"] option')
    ) as HTMLOptionElement[];
    const values = options.map((o) => o.value);
    expect(values).toEqual(['', 'PROMOVIDO', 'DESCARTADO', 'PENDIENTE']);
  });

  it('emits estadoChange with the selected estado', () => {
    const fixture = createComponent();
    const emitted: (string | null)[] = [];
    fixture.componentInstance.estadoChange.subscribe((v) => emitted.push(v));

    const select: HTMLSelectElement = fixture.nativeElement.querySelector(
      'select[data-testid="estado-select"]'
    );
    select.value = 'DESCARTADO';
    select.dispatchEvent(new Event('change'));

    expect(emitted).toEqual(['DESCARTADO']);
  });

  it('emits estadoChange(null) when "todos" is selected', () => {
    const fixture = createComponent();
    const emitted: (string | null)[] = [];
    fixture.componentInstance.estadoChange.subscribe((v) => emitted.push(v));

    const select: HTMLSelectElement = fixture.nativeElement.querySelector(
      'select[data-testid="estado-select"]'
    );
    select.value = '';
    select.dispatchEvent(new Event('change'));

    expect(emitted).toEqual([null]);
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
