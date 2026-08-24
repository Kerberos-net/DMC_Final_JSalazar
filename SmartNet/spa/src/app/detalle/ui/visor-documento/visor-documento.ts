import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { DocumentoRespuesta } from '../../models/documento.model';

/**
 * Presentational (dumb) component: same-origin `<iframe>` viewer (design D2/ADR 0013) with a
 * selector when a factura has more than one document (spec.md "Factura with multiple documents").
 * `bypassSecurityTrustResourceUrl` is safe here ONLY because the URL is always built from this
 * component's own `contenidoUrl` pattern (`/api/documentos/{id}/contenido`), never from
 * server-supplied HTML/text — Angular's XSS sink is the URL scheme, not this same-origin path.
 */
@Component({
  selector: 'app-visor-documento',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './visor-documento.html',
  styleUrl: './visor-documento.css',
})
export class VisorDocumento {
  private readonly sanitizer = inject(DomSanitizer);

  readonly documentos = input.required<readonly DocumentoRespuesta[]>();

  private readonly seleccionadoIdSignal = signal<string | null>(null);

  readonly seleccionado = computed<DocumentoRespuesta | null>(() => {
    const documentos = this.documentos();
    if (documentos.length === 0) {
      return null;
    }
    const id = this.seleccionadoIdSignal();
    return documentos.find((d) => d.id === id) ?? documentos[0];
  });

  readonly urlSegura = computed<SafeResourceUrl | null>(() => {
    const documento = this.seleccionado();
    return documento
      ? this.sanitizer.bypassSecurityTrustResourceUrl(`/api/documentos/${encodeURIComponent(documento.id)}/contenido`)
      : null;
  });

  onSeleccionar(id: string): void {
    this.seleccionadoIdSignal.set(id);
  }
}
