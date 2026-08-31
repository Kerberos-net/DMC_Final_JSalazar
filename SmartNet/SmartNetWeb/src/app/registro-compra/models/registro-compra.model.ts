/**
 * BACKLOG #23 — mirrors `SmartNet.Facturacion.Core.RegistroCompra` (`GET /api/registro-compra`,
 * `/{asientoId}`, `/export`). ASP.NET Core's default `System.Text.Json` camelCases the C# PascalCase
 * property names.
 *
 * Money / rate / `numeroAsiento` / `numeroComprobante` / `glosa` are NULLABLE on the wire (design
 * D4): the API echoes the stored column verbatim. A `null` amount MUST render as an em dash and MUST
 * NOT be coerced to 0 — that would manufacture a fake descuadre in the inconsistency badge.
 */
export interface RegistroCompraCabecera {
  readonly asientoContableId: number;
  readonly numeroComprobante: string | null;
  readonly numeroAsiento: string | null;
  readonly origenLibro: string;
  readonly proveedorCodigo: string;
  readonly proveedorNombre: string | null;
  readonly glosa: string | null;
  readonly fechaContable: string;
  readonly tipoCambioVenta: number | null;
  readonly basePEN: number | null;
  readonly igvPEN: number | null;
  readonly netoPEN: number | null;
}

export interface LineaRegistro {
  readonly orden: number;
  readonly bloque: string;
  readonly tipo: string;
  readonly debe: number;
  readonly haber: number;
  readonly cuentaCodigo: string | null;
  readonly cuentaDescripcion: string | null;
}

export interface RegistroCompraDetalle {
  readonly cabecera: RegistroCompraCabecera;
  readonly lineas: readonly LineaRegistro[];
}

/**
 * The listing envelope — the same field set `PaginaBandeja<T>` already exposes
 * (`items` / `pagina` / `tamanioPagina` / `totalRegistros` / `totalPaginas`), so the shared
 * `tabla-paginador` binds to it unchanged.
 */
export interface PaginaRegistroCompra {
  readonly items: readonly RegistroCompraCabecera[];
  readonly pagina: number;
  readonly tamanioPagina: number;
  readonly totalRegistros: number;
  readonly totalPaginas: number;
}
