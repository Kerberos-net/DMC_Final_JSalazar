import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { InboxList } from './inbox-list';
import { BandejaItem } from '../../models/bandeja-item.model';

describe('InboxList', () => {
  const promovido: BandejaItem = {
    inboxEventId: 1,
    estadoConsumo: 'PROMOVIDO',
    creadoEn: '2026-08-10T10:00:00Z',
    facturaId: 42,
    indicadores: {
      esProveedorGenerico: true,
      posibleDuplicado: false,
      tieneCamposNoExtraidos: true,
      fechaEnDomingo: false,
      afectacionMixta: null,
    },
    motivoDescarte: null,
  };

  const descartado: BandejaItem = {
    inboxEventId: 2,
    estadoConsumo: 'DESCARTADO',
    creadoEn: '2026-08-09T08:00:00Z',
    facturaId: null,
    indicadores: null,
    motivoDescarte: 'Falta TotalOrig',
  };

  const pendiente: BandejaItem = {
    inboxEventId: 3,
    estadoConsumo: 'PENDIENTE',
    creadoEn: '2026-08-08T08:00:00Z',
    facturaId: null,
    indicadores: null,
    motivoDescarte: null,
  };

  const createComponent = (items: BandejaItem[]) => {
    const fixture = TestBed.createComponent(InboxList);
    fixture.componentRef.setInput('items', items);
    fixture.detectChanges();
    return fixture;
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [InboxList],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  it('shows the linked Factura id and indicator chips for a promoted item', () => {
    const fixture = createComponent([promovido]);
    const row: HTMLElement = fixture.nativeElement.querySelector(
      '[data-testid="inbox-row-1"]'
    );
    expect(row.textContent).toContain('42');
    const chips = Array.from(
      row.querySelectorAll('[data-testid="indicador-chip"]')
    ) as HTMLElement[];
    expect(chips.length).toBe(2);
  });

  it('links a promoted item to its detail screen (BACKLOG #12 Phase 5)', () => {
    const fixture = createComponent([promovido]);
    const enlace: HTMLAnchorElement = fixture.nativeElement.querySelector(
      '[data-testid="ir-a-detalle"]'
    );
    expect(enlace.getAttribute('href')).toBe('/detalle/42');
  });

  it('shows the discard reason for a discarded item', () => {
    const fixture = createComponent([descartado]);
    const row: HTMLElement = fixture.nativeElement.querySelector(
      '[data-testid="inbox-row-2"]'
    );
    expect(row.textContent).toContain('Falta TotalOrig');
  });

  it('renders a pending row with no Factura summary and no discard reason', () => {
    const fixture = createComponent([pendiente]);
    const row: HTMLElement = fixture.nativeElement.querySelector(
      '[data-testid="inbox-row-3"]'
    );
    expect(row.querySelector('[data-testid="factura-id"]')).toBeNull();
    expect(row.querySelector('[data-testid="motivo-descarte"]')).toBeNull();
  });

  it('never renders an approve/edit/re-trigger control', () => {
    const fixture = createComponent([promovido, descartado, pendiente]);
    const buttons = fixture.nativeElement.querySelectorAll('button, [role="button"]');
    expect(buttons.length).toBe(0);
  });
});
