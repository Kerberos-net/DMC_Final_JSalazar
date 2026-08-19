import { TestBed } from '@angular/core/testing';
import { InboxFilter } from './inbox-filter';

describe('InboxFilter', () => {
  const createComponent = () => {
    const fixture = TestBed.createComponent(InboxFilter);
    fixture.componentRef.setInput('estado', null);
    fixture.componentRef.setInput('orden', 'desc');
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
});
