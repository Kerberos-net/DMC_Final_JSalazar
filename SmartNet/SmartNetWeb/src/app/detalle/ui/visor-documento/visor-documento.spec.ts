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

  // BACKLOG (pdf-asociado-en-documento-factura) Phase 4 — spec.md "Factura with an XML and a PDF
  // document": the default selection must prefer the first inline-renderable document, not
  // strictly documentos[0].
  const docXml: DocumentoRespuesta = {
    id: 'ingesta-xml',
    origen: 'INGESTA',
    nombreArchivo: 'factura.xml',
    mimeType: 'application/xml',
    fecha: '2026-08-09T10:00:00Z',
  };
  const docPdf: DocumentoRespuesta = {
    id: 'ingesta-pdf',
    origen: 'INGESTA',
    nombreArchivo: 'factura.pdf',
    mimeType: 'application/pdf',
    fecha: '2026-08-10T10:00:00Z',
  };
  const docXmlSegundo: DocumentoRespuesta = {
    id: 'ingesta-xml-2',
    origen: 'INGESTA',
    nombreArchivo: 'anexo.xml',
    mimeType: 'application/xml',
    fecha: '2026-08-11T10:00:00Z',
  };

  it('selects the PDF by default when the list has an earlier non-renderable XML row', () => {
    const fixture = createComponent([docXml, docPdf]);

    const iframe: HTMLIFrameElement = fixture.nativeElement.querySelector('iframe');
    expect(iframe.src).toContain('/api/documentos/ingesta-pdf/contenido');
  });

  it('falls back to documentos[0] when no document in the list is inline-renderable', () => {
    const fixture = createComponent([docXml, docXmlSegundo]);

    const iframe: HTMLIFrameElement = fixture.nativeElement.querySelector('iframe');
    expect(iframe.src).toContain('/api/documentos/ingesta-xml/contenido');
  });

  it('keeps an explicit user selection even when a renderable document exists', () => {
    const fixture = createComponent([docXml, docPdf]);
    const select: HTMLSelectElement = fixture.nativeElement.querySelector('[data-testid="selector-documento"]');

    select.value = 'ingesta-xml';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const iframe: HTMLIFrameElement = fixture.nativeElement.querySelector('iframe');
    expect(iframe.src).toContain('/api/documentos/ingesta-xml/contenido');
  });
});
