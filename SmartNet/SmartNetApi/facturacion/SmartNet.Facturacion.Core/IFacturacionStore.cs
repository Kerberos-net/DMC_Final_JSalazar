namespace SmartNet.Facturacion.Core;

/// <summary>
/// design D1 — fábrica de <see cref="IUnidadDeTrabajo"/>: cada llamada abre una conexión + una
/// transacción SQL nuevas (Infrastructure). Un <c>ServicioDe*</c> nunca ve <c>SqlConnection</c>
/// directamente — solo este puerto y el que devuelve (ADR 0019).
/// </summary>
public interface IFacturacionStore
{
    Task<IUnidadDeTrabajo> AbrirAsync(CancellationToken ct);
}
