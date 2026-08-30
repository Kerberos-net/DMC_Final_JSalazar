/**
 * Mirrors `SmartNet.Api.CatalogoProveedoresRespuesta` / `ProveedorResultado`
 * (BACKLOG #22 PR5 -- `GET /api/catalogos/proveedores?modo=catalogo`). ASP.NET Core's default
 * `System.Text.Json` options camelCase the C# PascalCase property names.
 *
 * The `modo=catalogo` envelope is the same field set `PaginaBandeja<T>` already exposes
 * (`items` / `pagina` / `tamanioPagina` / `totalRegistros` / `totalPaginas`, design D6); the
 * picker mode keeps its own frozen `{ resultados, hayMas }` shape (see `proveedor.model.ts`).
 */
export interface ProveedorCatalogo {
  readonly codigo: string;
  readonly nombre: string;
  readonly ruc: string | null;
}

export interface PaginaProveedores {
  readonly items: readonly ProveedorCatalogo[];
  readonly pagina: number;
  readonly tamanioPagina: number;
  readonly totalRegistros: number;
  readonly totalPaginas: number;
}

/**
 * Server sort keys for the proveedores catalogo screen (design D7). These map 1:1 to the API
 * `orden` query parameter (`OrdenProveedor.Valores`), so the header key is sent verbatim:
 * `codigo` column -> `codigo`, "Razón social" column -> `proveedor`, `ruc` column -> `ruc`.
 */
export type ClaveOrdenProveedor = 'codigo' | 'proveedor' | 'ruc';
