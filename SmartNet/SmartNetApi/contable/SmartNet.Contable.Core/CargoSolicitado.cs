using SmartNet.Catalogos.Core;

namespace SmartNet.Contable.Core;

/// <summary>
/// Un cargo del bloque PRINCIPAL, en importe absoluto PEN (design.md Decisión 5 — no proporción:
/// una proporción haría que "los cargos igualan la base" se cumpliera por construcción y borraría
/// una invariante que REGLAS.md §7 manda comprobar). Lista de uno = caso normal; lista de N =
/// "División del cargo" (REGLAS.md §5).
/// </summary>
public sealed record CargoSolicitado(CuentaContable Cuenta, decimal ImportePEN)
{
    public CuentaContable Cuenta { get; } = Cuenta ?? throw new ArgumentNullException(nameof(Cuenta));
}
