import { TestBed } from '@angular/core/testing';
import { VisorDocumento } from './visor-documento';
import { DocumentoRespuesta } from '../../models/documento.model';

describe('VisorDocumento', () => {
  const docA: DocumentoRespuesta = {
    id: 'ingesta-1',
    origen: 'INGESTA',
    nombreArchivo: 'factura.pdf',
    mimeType: 'application/pdf',
    fecha: '2026-08-10T10:00:00Z',
  };
  const docB: DocumentoRespuesta = {
    id: 'manual-2',
    origen: 'MANUAL',
    nombreArchivo: 'anexo.pdf',
    mimeType: 'application/pdf',
    fecha: '2026-08-11T10:00:00Z',
  };

  const createComponent = (documentos: DocumentoRespuesta[]) => {
    const fixture = TestBed.createComponent(VisorDocumento);
    fixture.componentRef.setInput('documentos', documentos);
    fixture.detectChanges();
    return fixture;
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [VisorDocumento] }).compileComponents();
  });

  it('renders no iframe when there are no documents', () => {
    const fixture = createComponent([]);
    expect(fixture.nativeElement.querySelector('iframe')).toBeNull();
  });

  it('renders the first document by default, same-origin', () => {
    const fixture = createComponent([docA, docB]);
    const iframe: HTMLIFrameElement = fixture.nativeElement.querySelector('iframe');
    expect(iframe.src).toContain('/api/documentos/ingesta-1/contenido');
  });

  it('offers a selector to switch between multiple documents, and switching updates the iframe', () => {
    const fixture = createComponent([docA, docB]);
    const select: HTMLSelectElement = fixture.nativeElement.querySelector('[data-testid="selector-documento"]');
    expect(select.options.length).toBe(2);

    select.value = 'manual-2';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const iframe: HTMLIFrameElement = fixture.nativeElement.querySelector('iframe');
    expect(iframe.src).toContain('/api/documentos/manual-2/contenido');
  });
});
