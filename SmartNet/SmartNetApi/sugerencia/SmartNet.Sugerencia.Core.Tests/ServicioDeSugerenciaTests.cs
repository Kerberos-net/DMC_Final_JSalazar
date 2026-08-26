using SmartNet.Catalogos.Core;

namespace SmartNet.Sugerencia.Core.Tests;

/// <summary>
/// spec.md "An orchestration method exposes cuenta + motivo + fundamento for item #11" / design.md
/// Interfaces-Contracts, Data Flow. Fake in-memory ports — no DB/HTTP/clock (ADR 0019). tasks.md
/// Phase 5 (+ reactivated 4.3).
/// </summary>
public class ServicioDeSugerenciaTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed class FakeSugerenciaCuentaRepository : ISugerenciaCuentaRepository
    {
        public List<SugerenciaCuenta> Filas { get; } = new();
        public int RegistrarUsoAsyncCallCount { get; private set; }

        public Task<IReadOnlyList<SugerenciaCuenta>> ListarPorProveedorYMotivoAsync(
            string proveedorCodigo, int motivo, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SugerenciaCuenta>>(Filas
                .Where(f => f.ProveedorCodigo == proveedorCodigo && f.Motivo == motivo)
                .ToList());

        public Task<IReadOnlyList<SugerenciaCuenta>> ListarPorMotivoAsync(int motivo, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SugerenciaCuenta>>(Filas
                .Where(f => f.Motivo == motivo)
                .ToList());

        public Task<IReadOnlyList<SugerenciaCuenta>> ListarPorProveedorAsync(
            string proveedorCodigo, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SugerenciaCuenta>>(Filas
                .Where(f => f.ProveedorCodigo == proveedorCodigo)
                .ToList());

        public Task RegistrarUsoAsync(
            string proveedorCodigo, int motivo, string cuentaCodigo, DateTimeOffset instante, CancellationToken ct)
        {
            // Spy only — tasks.md 5.8/5.9: ServicioDeSugerencia must never call this. Writing
            // usage back is item #11's job (design.md Data Flow).
            RegistrarUsoAsyncCallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCuentaContableRepository : ICuentaContableRepository
    {
        public List<CuentaContable> PlanDeCuentas { get; } = new();

        public Task<IReadOnlyList<CuentaContable>> ListarPlanCompletoAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<CuentaContable>>(PlanDeCuentas);

        public Task<CuentaContable?> ObtenerAsync(string cuenta, CancellationToken ct) =>
            Task.FromResult(PlanDeCuentas.FirstOrDefault(c => c.Cuenta == cuenta));
    }

    private sealed class FakeMotivoRepository : IMotivoRepository
    {
        public List<Motivo> Motivos { get; } = new();

        public Task<Motivo?> ObtenerAsync(int codigo, CancellationToken ct) =>
            Task.FromResult(Motivos.FirstOrDefault(m => m.Codigo == codigo));

        public Task<IReadOnlyList<Motivo>> ListarAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Motivo>>(Motivos);
    }

    private sealed class FakeMotivoAtributoRepository : IMotivoAtributoRepository
    {
        public List<MotivoAtributo> Atributos { get; } = new();

        public Task<MotivoAtributo?> ObtenerAsync(int motivo, CancellationToken ct) =>
            Task.FromResult(Atributos.FirstOrDefault(a => a.Motivo == motivo));

        public Task<IReadOnlyList<MotivoAtributo>> ListarAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MotivoAtributo>>(Atributos);

        public Task GuardarAsync(MotivoAtributo atributo, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Fixture
    {
        public FakeSugerenciaCuentaRepository SugerenciaCuenta { get; } = new();
        public FakeCuentaContableRepository CuentaContable { get; } = new();
        public FakeMotivoRepository Motivo { get; } = new();
        public FakeMotivoAtributoRepository MotivoAtributo { get; } = new();

        public ServicioDeSugerencia CrearServicio() => new(
            SugerenciaCuenta, CuentaContable, Motivo, MotivoAtributo);
    }

    [Fact]
    public async Task SugerirParaFacturaAsync_ReturnsCombinedResult_ForGivenProveedorAndMotivo()
    {
        // spec.md "Orchestration returns a combined result for a given provider and motivo".
        var fixture = new Fixture();
        fixture.Motivo.Motivos.Add(new Motivo(10, "Compra de insumos", "6011"));
        fixture.CuentaContable.PlanDeCuentas.Add(new CuentaContable("601111", "Insumos A", null, null, null));
        fixture.CuentaContable.PlanDeCuentas.Add(new CuentaContable("601112", "Insumos B", null, null, null));
        fixture.SugerenciaCuenta.Filas.Add(new SugerenciaCuenta("P1", 10, "601112", 7, T0));
        fixture.SugerenciaCuenta.Filas.Add(new SugerenciaCuenta("P1", 10, "601111", 3, T0));

        var servicio = fixture.CrearServicio();

        var resultado = await servicio.SugerirParaFacturaAsync("P1", 10, CancellationToken.None);

        Assert.NotNull(resultado.Cuenta);
        Assert.Equal("601112", resultado.Cuenta!.CuentaCodigo);
        Assert.Equal(EscalonSugerencia.ProveedorYMotivo, resultado.Cuenta.Escalon);
        Assert.Equal(2, resultado.CandidatasVigentes.Count);
    }

    [Fact]
    public async Task SugerirParaFacturaAsync_MotivoSeleccionadoNull_NoMotivoResolved_ReturnsNoCuenta()
    {
        // tasks.md 5.4: motivoSeleccionado = null and the provider has no history at all — the
        // motivo cascade itself resolves to null, so there is no motivo to key the cuenta cascade
        // by; the result carries neither a motivo nor a cuenta suggestion.
        var fixture = new Fixture();
        fixture.MotivoAtributo.Atributos.Add(new MotivoAtributo(10, Activo: true, OrigenLibro: "02"));

        var servicio = fixture.CrearServicio();

        var resultado = await servicio.SugerirParaFacturaAsync("PROVEEDOR_NUEVO", null, CancellationToken.None);

        Assert.Null(resultado.Motivo);
        Assert.Null(resultado.Cuenta);
        Assert.Empty(resultado.CandidatasVigentes);
    }

    [Fact]
    public async Task SugerirParaFacturaAsync_MotivoWithZeroLiveCandidates_ReturnsNoCuenta()
    {
        // tasks.md 5.6: the motivo resolves, but ResolverCandidatas yields nothing for its
        // prefixes (e.g. every declared prefix has no leaf accounts in the current plan).
        var fixture = new Fixture();
        fixture.Motivo.Motivos.Add(new Motivo(10, "Sin candidatas vigentes", "999999"));

        var servicio = fixture.CrearServicio();

        var resultado = await servicio.SugerirParaFacturaAsync("P1", 10, CancellationToken.None);

        Assert.Null(resultado.Cuenta);
        Assert.Empty(resultado.CandidatasVigentes);
    }

    [Fact]
    public async Task SugerirParaFacturaAsync_NeverCallsRegistrarUsoAsync()
    {
        // tasks.md 5.8/5.9 + design.md Data Flow: RegistrarUsoAsync is item #11's job, never
        // this module's. Explicit spy assertion, not just an absence-of-call-site inspection.
        var fixture = new Fixture();
        fixture.Motivo.Motivos.Add(new Motivo(10, "Compra de insumos", "6011"));
        fixture.CuentaContable.PlanDeCuentas.Add(new CuentaContable("601111", "Insumos A", null, null, null));
        fixture.SugerenciaCuenta.Filas.Add(new SugerenciaCuenta("P1", 10, "601111", 3, T0));

        var servicio = fixture.CrearServicio();

        await servicio.SugerirParaFacturaAsync("P1", 10, CancellationToken.None);
        await servicio.SugerirParaFacturaAsync("PROVEEDOR_NUEVO", null, CancellationToken.None);

        Assert.Equal(0, fixture.SugerenciaCuenta.RegistrarUsoAsyncCallCount);
    }

    [Fact]
    public async Task SugerirParaFacturaAsync_CombinesCuentaMotivoYFundamento_WhenMotivoNotPreSelected()
    {
        // Reactivated tasks.md 4.3: spec.md "Orchestration returns a combined result" — with
        // motivoSeleccionado = null and provider history present, the service must suggest the
        // motivo itself (via CascadaDeSugerencia.SugerirMotivo, filtered by Activo && Origen "02")
        // and then chain into SugerirCuenta for that suggested motivo, exposing Fundamento on both.
        var fixture = new Fixture();
        fixture.MotivoAtributo.Atributos.Add(new MotivoAtributo(10, Activo: true, OrigenLibro: "02"));
        fixture.MotivoAtributo.Atributos.Add(new MotivoAtributo(20, Activo: false, OrigenLibro: "02"));
        fixture.Motivo.Motivos.Add(new Motivo(10, "Compra de insumos", "6011"));
        fixture.CuentaContable.PlanDeCuentas.Add(new CuentaContable("601111", "Insumos A", null, null, null));
        // Motivo 20 has more Veces but is not offerable (Activo=false) — must be ignored.
        fixture.SugerenciaCuenta.Filas.Add(new SugerenciaCuenta("P1", 20, "701111", 99, T0));
        fixture.SugerenciaCuenta.Filas.Add(new SugerenciaCuenta("P1", 10, "601111", 5, T0));

        var servicio = fixture.CrearServicio();

        var resultado = await servicio.SugerirParaFacturaAsync("P1", null, CancellationToken.None);

        Assert.NotNull(resultado.Motivo);
        Assert.Equal(10, resultado.Motivo!.Motivo);
        Assert.False(string.IsNullOrWhiteSpace(resultado.Motivo.Fundamento));

        Assert.NotNull(resultado.Cuenta);
        Assert.Equal("601111", resultado.Cuenta!.CuentaCodigo);
        Assert.False(string.IsNullOrWhiteSpace(resultado.Cuenta.Fundamento));
    }
}
