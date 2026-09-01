# Delta for pantalla-detalle-validacion

## MODIFIED Requirements

### Requirement: Side-by-side layout shows document and editable form

The screen MUST render the source document (left, ~42% static) and the factura/asiento edit form (right, flex:1) simultaneously, per DESIGN_BRIEF.md's "documento + formulario" pattern. The form MUST show the full factura header field set defined in "`factura-form` renders and binds the factura header field set", `TipoCambioVenta` (when applicable), and the asiento líneas.

When a factura has more than one associated INGESTA/MANUAL document, the viewer's default selected document MUST be the first document whose MIME type is in the inline allow-list (`application/pdf`, `image/png`, `image/jpeg`), falling back to the first document when none is renderable. Default selection MUST NOT be strictly the earliest-fecha document.
(Previously: with multiple documents "one is shown by default" with the default being `documentos[0]` ordered by fecha — an XML row could be selected and render as a download-only placeholder.)

#### Scenario: Opening a factura with a rendered document
- **Given** a factura with an associated document
- **When** the user opens the detail screen for that factura
- **Then** the document renders on the left and the factura/asiento data
  populates the form on the right, both loaded before the user can edit

#### Scenario: Factura with an XML and a PDF document
- **Given** a factura whose `GET /api/facturas/{id}/documentos` returns both an INGESTA XML row and an INGESTA PDF row
- **When** the screen loads
- **Then** the viewer offers a selector to switch between documents
- **And** the PDF is selected and rendered inline by default, not the XML

#### Scenario: Factura with only a non-renderable document
- **Given** a factura whose only document is an XML row (no renderable MIME)
- **When** the screen loads
- **Then** the viewer selects that row and shows the existing non-renderable placeholder / download affordance, unchanged

#### Scenario: Factura with multiple documents
- **Given** a factura with more than one associated document (recibido and/or
  manual)
- **When** the screen loads
- **Then** the viewer offers a way to switch between documents; one is shown
  by default
