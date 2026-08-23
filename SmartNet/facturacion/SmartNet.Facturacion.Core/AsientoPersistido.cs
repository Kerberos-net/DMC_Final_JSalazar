using SmartNet.Contable.Core;

namespace SmartNet.Facturacion.Core;

/// <summary>
/// design.md Interfaces/Contracts — lo que <see cref="IUnidadDeTrabajo.CargarAsientoAsync"/>
/// devuelve: espejo de <c>fact.AsientoContable</c> CON sus campos de ciclo de vida
/// (<see cref="AsientoContableId"/>, <see cref="Estado"/>, <see cref="NumeroAsiento"/>,
/// <see cref="Version"/>) que <c>SmartNet.Contable.Core.AsientoContable</c> (#8) deliberadamente
/// excluye ("sin campos de ciclo de vida ... son de #11"), más los <see cref="Hechos"/>
/// pre-calculados para el gate D4.
/// </summary>
public sealed record AsientoPersistido(
    long AsientoContableId,
    long FacturaId,
    string Estado,
    string? NumeroAsiento,
    byte[] Version,
    AsientoContable Asiento,
    HechosDeConflicto Hechos)
{
    public const string Borrador = "BORRADOR";
    public const string Confirmado = "CONFIRMADO";
    public const string Anulado = "ANULADO";
}
