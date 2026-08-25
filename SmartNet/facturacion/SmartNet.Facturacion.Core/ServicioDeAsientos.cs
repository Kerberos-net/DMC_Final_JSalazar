using System.Text.Json;
using SmartNet.Contable.Core;

namespace SmartNet.Facturacion.Core;

/// <summary>
/// design.md api-asientos requirements + D6 — comandos directos sobre un asiento ya cargado:
/// PATCH (corrección de campo con CAS), reabrir (motivo obligatorio, CONFIRMADO-&gt;BORRADOR),
/// anular (terminal, CONFIRMADO-&gt;ANULADO). Cada uno abre su propia <see cref="IUnidadDeTrabajo"/>
/// (design D1: una transacción por comando).
/// </summary>
public sealed class ServicioDeAsientos
{
    private readonly IFacturacionStore _store;

    public ServicioDeAsientos(IFacturacionStore store) => _store = store;

    public async Task<ResultadoComando> ActualizarAsync(
        long asientoId, byte[] versionEsperada, string campo, string? valorOriginal, string? valorNuevo,
        long usuarioId, DateTimeOffset ahora, CancellationToken ct)
    {
        await using var uow = await _store.AbrirAsync(ct);

        var persistido = await uow.CargarAsientoAsync(asientoId, ct);
        if (persistido is null)
        {
            return new ResultadoComando.NoEncontrado();
        }

        // design api-asientos: "editing CONFIRMADO asiento directly ... without reabrir -> 409".
        if (persistido.Estado != AsientoPersistido.Borrador)
        {
            return new ResultadoComando.Conflicto(
                CasoConflicto.AsientoYaConfirmado, "El asiento ya fue confirmado — reábralo antes de editarlo.");
        }

        var escritura = await uow.GuardarAsientoAsync(asientoId, versionEsperada, persistido, ct);
        var resultadoEscritura = ServicioDeFacturas.TraducirResultadoEscritura(escritura);
        if (resultadoEscritura is not null)
        {
            return resultadoEscritura;
        }

        await uow.RegistrarAuditoriaAsync(
            new EntradaAuditoria(
                EntradaAuditoria.EntidadTipos.Asiento, asientoId, EntradaAuditoria.Acciones.Correccion,
                campo, valorOriginal, valorNuevo, Motivo: null, usuarioId, ahora),
            ct);

        await uow.CommitAsync(ct);
        return new ResultadoComando.Aplicado();
    }

    public async Task<ResultadoComando> ReabrirAsync(
        long asientoId, byte[] versionEsperada, string motivo, long usuarioId, DateTimeOffset ahora, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(motivo))
        {
            throw new ArgumentException("El motivo es obligatorio para reabrir un asiento.", nameof(motivo));
        }

        await using var uow = await _store.AbrirAsync(ct);

        var persistido = await uow.CargarAsientoAsync(asientoId, ct);
        if (persistido is null)
        {
            return new ResultadoComando.NoEncontrado();
        }

        // spec.md: "reabrir a BORRADOR asiento -> 409 (nothing confirmed to reopen)". No hay un
        // CasoConflicto propio en la lista de 9 filas de ADR 0008 (design D4) para este caso; se
        // reutiliza AsientoYaConfirmado como "el estado del asiento no admite esta transición" — a
        // revisar junto con ProblemasDeNegocio.Map en Phase 2/3.
        if (persistido.Estado != AsientoPersistido.Confirmado)
        {
            return new ResultadoComando.Conflicto(
                CasoConflicto.AsientoYaConfirmado, "No hay nada confirmado que reabrir.");
        }

        var reabierto = persistido with { Estado = AsientoPersistido.Borrador };
        var escritura = await uow.GuardarAsientoAsync(asientoId, versionEsperada, reabierto, ct);
        var resultadoEscritura = ServicioDeFacturas.TraducirResultadoEscritura(escritura);
        if (resultadoEscritura is not null)
        {
            return resultadoEscritura;
        }

        await uow.RegistrarAuditoriaAsync(
            new EntradaAuditoria(
                EntradaAuditoria.EntidadTipos.Asiento, asientoId, EntradaAuditoria.Acciones.Reapertura,
                Campo: "Estado", ValorOriginal: AsientoPersistido.Confirmado, ValorNuevo: AsientoPersistido.Borrador,
                motivo, usuarioId, ahora),
            ct);

        await uow.CommitAsync(ct);
        return new ResultadoComando.Aplicado();
    }

    public async Task<ResultadoComando> AnularAsync(
        long asientoId, byte[] versionEsperada, string motivo, long usuarioId, DateTimeOffset ahora, CancellationToken ct)
    {
        await using var uow = await _store.AbrirAsync(ct);

        var persistido = await uow.CargarAsientoAsync(asientoId, ct);
        if (persistido is null)
        {
            return new ResultadoComando.NoEncontrado();
        }

        // ANULADO es terminal (ADR 0006) — spec.md "anular already-ANULADO asiento -> 409".
        if (persistido.Estado != AsientoPersistido.Confirmado)
        {
            return new ResultadoComando.Conflicto(
                CasoConflicto.AsientoYaConfirmado, "Solo un asiento CONFIRMADO puede anularse.");
        }

        var anulado = persistido with { Estado = AsientoPersistido.Anulado };
        var escritura = await uow.GuardarAsientoAsync(asientoId, versionEsperada, anulado, ct);
        var resultadoEscritura = ServicioDeFacturas.TraducirResultadoEscritura(escritura);
        if (resultadoEscritura is not null)
        {
            return resultadoEscritura;
        }

        await uow.RegistrarAuditoriaAsync(
            new EntradaAuditoria(
                EntradaAuditoria.EntidadTipos.Asiento, asientoId, EntradaAuditoria.Acciones.Anulacion,
                Campo: "Estado", ValorOriginal: AsientoPersistido.Confirmado, ValorNuevo: AsientoPersistido.Anulado,
                motivo, usuarioId, ahora),
            ct);

        // outbox-mensajeria (BACKLOG #14, design D1/D2) -- asientoContableId pasado EXPLÍCITO: tras
        // anular, ObtenerAsientoVigenteIdAsync ya no lo encontraría ("vigente" excluye ANULADO).
        var payload = await PayloadOutbox.ConstruirAsync(
            uow, TipoEventoAsientoAnulado, persistido.FacturaId, asientoId, ct);
        await uow.EmitirOutboxAsync(TipoEventoAsientoAnulado, persistido.FacturaId, payload, ct);

        await uow.CommitAsync(ct);
        return new ResultadoComando.Aplicado();
    }

    private const string TipoEventoAsientoAnulado = "ASIENTO_ANULADO";

    // -----------------------------------------------------------------------------------------
    // PR 3 (Phase 3) — líneas por LineaId (spec.md api-asientos: "never position"). Los tres
    // comandos comparten forma: cargar (gate BORRADOR) -> snapshot ANTES -> escritura CAS de línea
    // -> snapshot DESPUÉS -> UNA fila AuditoriaCorreccion(Accion=REPARTO_MANUAL, Campo="Cargos",
    // Motivo=null) -- design D6, fila 7. Ningún <see cref="InvarianteContable"/> se evalúa aquí
    // (igual que <see cref="PatchAsync"/> para facturas): las invariantes solo corren al validar.
    // -----------------------------------------------------------------------------------------

    /// <summary>design D2/D6 -- <c>POST /api/asientos/{id}/lineas</c>: inserta una línea nueva.
    /// Bloqueado si el asiento no está BORRADOR (mismo gate que <see cref="ActualizarAsync"/>).</summary>
    public async Task<(ResultadoComando Resultado, long? LineaId)> AgregarLineaAsync(
        long asientoId, byte[] versionEsperada, LineaAsiento linea, long usuarioId, DateTimeOffset ahora, CancellationToken ct)
    {
        await using var uow = await _store.AbrirAsync(ct);

        var persistido = await uow.CargarAsientoAsync(asientoId, ct);
        if (persistido is null)
        {
            return (new ResultadoComando.NoEncontrado(), null);
        }

        if (persistido.Estado != AsientoPersistido.Borrador)
        {
            return (
                new ResultadoComando.Conflicto(
                    CasoConflicto.AsientoYaConfirmado, "El asiento ya fue confirmado — reábralo antes de editar sus líneas."),
                null);
        }

        var antes = await uow.CargarLineasPersistidasAsync(asientoId, ct);
        var resultadoLinea = await uow.AgregarLineaAsync(asientoId, versionEsperada, linea, ct);

        var resultadoEscritura = ServicioDeFacturas.TraducirResultadoEscritura(resultadoLinea.Resultado);
        if (resultadoEscritura is not null)
        {
            return (resultadoEscritura, null);
        }

        await RegistrarRepartoManualAsync(uow, asientoId, antes, usuarioId, ahora, ct);

        await uow.CommitAsync(ct);
        return (new ResultadoComando.Aplicado(), resultadoLinea.LineaId);
    }

    /// <summary>design D2/D6 -- <c>PATCH /api/asientos/{id}/lineas/{lineaId}</c>: reemplaza una línea
    /// existente por <see cref="LineaId"/> (nunca por posición). Mismo gate BORRADOR.</summary>
    public async Task<ResultadoComando> ActualizarLineaAsync(
        long asientoId, long lineaId, byte[] versionEsperada, LineaAsiento linea, long usuarioId, DateTimeOffset ahora,
        CancellationToken ct)
    {
        await using var uow = await _store.AbrirAsync(ct);

        var persistido = await uow.CargarAsientoAsync(asientoId, ct);
        if (persistido is null)
        {
            return new ResultadoComando.NoEncontrado();
        }

        if (persistido.Estado != AsientoPersistido.Borrador)
        {
            return new ResultadoComando.Conflicto(
                CasoConflicto.AsientoYaConfirmado, "El asiento ya fue confirmado — reábralo antes de editar sus líneas.");
        }

        var antes = await uow.CargarLineasPersistidasAsync(asientoId, ct);
        var escritura = await uow.ActualizarLineaAsync(lineaId, asientoId, versionEsperada, linea, ct);
        var resultadoEscritura = ServicioDeFacturas.TraducirResultadoEscritura(escritura);
        if (resultadoEscritura is not null)
        {
            return resultadoEscritura;
        }

        await RegistrarRepartoManualAsync(uow, asientoId, antes, usuarioId, ahora, ct);

        await uow.CommitAsync(ct);
        return new ResultadoComando.Aplicado();
    }

    /// <summary>design D2/D6 -- <c>DELETE /api/asientos/{id}/lineas/{lineaId}</c>: elimina una línea
    /// por <see cref="LineaId"/> (nunca por posición). Mismo gate BORRADOR.</summary>
    public async Task<ResultadoComando> EliminarLineaAsync(
        long asientoId, long lineaId, byte[] versionEsperada, long usuarioId, DateTimeOffset ahora, CancellationToken ct)
    {
        await using var uow = await _store.AbrirAsync(ct);

        var persistido = await uow.CargarAsientoAsync(asientoId, ct);
        if (persistido is null)
        {
            return new ResultadoComando.NoEncontrado();
        }

        if (persistido.Estado != AsientoPersistido.Borrador)
        {
            return new ResultadoComando.Conflicto(
                CasoConflicto.AsientoYaConfirmado, "El asiento ya fue confirmado — reábralo antes de editar sus líneas.");
        }

        var antes = await uow.CargarLineasPersistidasAsync(asientoId, ct);
        var escritura = await uow.EliminarLineaAsync(lineaId, asientoId, versionEsperada, ct);
        var resultadoEscritura = ServicioDeFacturas.TraducirResultadoEscritura(escritura);
        if (resultadoEscritura is not null)
        {
            return resultadoEscritura;
        }

        await RegistrarRepartoManualAsync(uow, asientoId, antes, usuarioId, ahora, ct);

        await uow.CommitAsync(ct);
        return new ResultadoComando.Aplicado();
    }

    /// <summary>design D6, fila 7: "manual split override -&gt; REPARTO_MANUAL, Campo=Cargos, JSON
    /// array -&gt; JSON array, Motivo=null". Recarga las líneas DESPUÉS de la escritura para construir
    /// el snapshot <c>ValorNuevo</c> -- <paramref name="antes"/> ya trae el snapshot previo.</summary>
    private static async Task RegistrarRepartoManualAsync(
        IUnidadDeTrabajo uow, long asientoId, IReadOnlyList<LineaPersistida> antes, long usuarioId, DateTimeOffset ahora,
        CancellationToken ct)
    {
        var despues = await uow.CargarLineasPersistidasAsync(asientoId, ct);
        await uow.RegistrarAuditoriaAsync(
            new EntradaAuditoria(
                EntradaAuditoria.EntidadTipos.Asiento, asientoId, EntradaAuditoria.Acciones.RepartoManual,
                Campo: "Cargos", ValorOriginal: SerializarLineas(antes), ValorNuevo: SerializarLineas(despues),
                Motivo: null, usuarioId, ahora),
            ct);
    }

    private static string SerializarLineas(IReadOnlyList<LineaPersistida> lineas) => JsonSerializer.Serialize(
        lineas.Select(l => new
        {
            l.LineaId,
            Bloque = l.Linea.Bloque.ToString(),
            Tipo = l.Linea.Tipo.ToString(),
            l.Linea.Debe,
            l.Linea.Haber,
            l.Linea.CuentaCodigo,
        }));
}
