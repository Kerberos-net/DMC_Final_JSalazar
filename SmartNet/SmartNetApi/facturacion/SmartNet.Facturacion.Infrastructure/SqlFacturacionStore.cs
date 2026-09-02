using Microsoft.Data.SqlClient;
using SmartNet.Facturacion.Core;
using SmartNet.Sugerencia.Core;
using SmartNet.TiposCambio.Core;
using SmartNet.TiposCambio.Infrastructure;

namespace SmartNet.Facturacion.Infrastructure;

/// <summary>
/// design D1 — fábrica de <see cref="IUnidadDeTrabajo"/>: abre una <see cref="SqlConnection"/> +
/// <see cref="SqlTransaction"/> nuevas por comando. Nunca las reutiliza entre llamadas — cada
/// <c>AbrirAsync</c> es una transacción de negocio distinta.
///
/// PR 5 (Phase 5, SinTipoCambio gap closure) — recibe (o construye por defecto) un
/// <see cref="ITipoCambioRepository"/> para pasárselo a cada <see cref="SqlUnidadDeTrabajo"/>
/// nueva. El constructor de un solo parámetro se conserva sin cambio de firma para no romper los
/// ~20 call sites existentes (PR 1-3 test suites + <c>Program.cs</c>) — construye internamente un
/// <see cref="SqlTipoCambioRepository"/> sobre la misma cadena de conexión (misma base, mismo
/// esquema <c>fact</c>, ninguna partición de ADR 0003 involucrada).
/// </summary>
public sealed class SqlFacturacionStore : IFacturacionStore
{
    private readonly string _connectionString;
    private readonly ITipoCambioRepository _tipoCambioRepository;
    private readonly ServicioDeSugerencia? _servicioDeSugerencia;

    public SqlFacturacionStore(string connectionString)
        : this(connectionString, new SqlTipoCambioRepository(connectionString))
    {
    }

    // BACKLOG #24 Phase 4.1: the third parameter stays optional so the ~20 existing 1-/2-arg call
    // sites (infra test suites) keep compiling and simply get the design-A2 placeholder path.
    // Program.cs passes the DI-resolved ServicioDeSugerencia.
    public SqlFacturacionStore(
        string connectionString,
        ITipoCambioRepository tipoCambioRepository,
        ServicioDeSugerencia? servicioDeSugerencia = null)
    {
        _connectionString = connectionString;
        _tipoCambioRepository = tipoCambioRepository;
        _servicioDeSugerencia = servicioDeSugerencia;
    }

    public async Task<IUnidadDeTrabajo> AbrirAsync(CancellationToken ct)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        return new SqlUnidadDeTrabajo(connection, transaction, _tipoCambioRepository, _servicioDeSugerencia);
    }
}
