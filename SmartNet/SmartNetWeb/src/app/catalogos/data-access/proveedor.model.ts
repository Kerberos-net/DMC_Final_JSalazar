/**
 * Mirrors `SmartNet.Api.BusquedaProveedoresRespuesta` / `ProveedorResultado`
 * (BACKLOG #18 PR8 — `GET /api/catalogos/proveedores`). ASP.NET Core's default
 * `System.Text.Json` options camelCase the C# PascalCase property names.
 */
export interface Proveedor {
  readonly codigo: string;
  readonly nombre: string;
  readonly ruc: string | null;
}

export interface BusquedaProveedoresRespuesta {
  readonly resultados: readonly Proveedor[];
  readonly hayMas: boolean;
}
