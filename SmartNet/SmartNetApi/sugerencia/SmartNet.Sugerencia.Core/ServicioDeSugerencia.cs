using SmartNet.Catalogos.Core;

namespace SmartNet.Sugerencia.Core;

/// <summary>
/// Single entry point for item #11/#12 (design.md Interfaces/Contracts, Data Flow). Wires the 4
/// read-only ports item #3 already shipped to the pure <see cref="CascadaDeSugerencia"/> /
/// <see cref="ResolucionDePrefijos"/> cascades. Never calls
/// <see cref="ISugerenciaCuentaRepository.RegistrarUsoAsync"/> — recording usage on confirmation is
/// item #11's job (design.md Data Flow, spec.md Non-Goals).
/// </summary>
public sealed class ServicioDeSugerencia
{
    private readonly ISugerenciaCuentaRepository _sugerenciaCuentaRepository;
    private readonly ICuentaContableRepository _cuentaContableRepository;
    private readonly IMotivoRepository _motivoRepository;
    private readonly IMotivoAtributoRepository _motivoAtributoRepository;

    public ServicioDeSugerencia(
        ISugerenciaCuentaRepository sugerenciaCuentaRepository,
        ICuentaContableRepository cuentaContableRepository,
        IMotivoRepository motivoRepository,
        IMotivoAtributoRepository motivoAtributoRepository)
    {
        _sugerenciaCuentaRepository = sugerenciaCuentaRepository;
        _cuentaContableRepository = cuentaContableRepository;
        _motivoRepository = motivoRepository;
        _motivoAtributoRepository = motivoAtributoRepository;
    }

    public async Task<SugerenciaParaFactura> SugerirParaFacturaAsync(
        string proveedorCodigo, int? motivoSeleccionado, CancellationToken ct)
    {
        SugerenciaDeMotivo? sugerenciaMotivo = null;
        var motivoEfectivo = motivoSeleccionado;

        if (motivoEfectivo is null)
        {
            var atributos = await _motivoAtributoRepository.ListarAsync(ct);
            var motivosOfrecibles = new HashSet<int>(
                atributos.Where(a => a.Activo && a.OrigenLibro == "02").Select(a => a.Motivo));

            var usoDelProveedor = await _sugerenciaCuentaRepository.ListarPorProveedorAsync(proveedorCodigo, ct);
            sugerenciaMotivo = CascadaDeSugerencia.SugerirMotivo(usoDelProveedor, motivosOfrecibles);
            motivoEfectivo = sugerenciaMotivo?.Motivo;
        }

        if (motivoEfectivo is null)
        {
            return new SugerenciaParaFactura(sugerenciaMotivo, null, Array.Empty<CuentaContable>());
        }

        var motivo = await _motivoRepository.ObtenerAsync(motivoEfectivo.Value, ct);
        var planDeCuentas = await _cuentaContableRepository.ListarPlanCompletoAsync(ct);
        var candidatasVigentes = ResolucionDePrefijos.ResolverCandidatas(motivo?.Cuenta, planDeCuentas);

        if (candidatasVigentes.Count == 0)
        {
            return new SugerenciaParaFactura(sugerenciaMotivo, null, candidatasVigentes);
        }

        var usoDelProveedorEnElMotivo = await _sugerenciaCuentaRepository.ListarPorProveedorYMotivoAsync(
            proveedorCodigo, motivoEfectivo.Value, ct);
        var usoGlobalDelMotivo = await _sugerenciaCuentaRepository.ListarPorMotivoAsync(motivoEfectivo.Value, ct);

        var sugerenciaCuenta = CascadaDeSugerencia.SugerirCuenta(
            usoDelProveedorEnElMotivo, usoGlobalDelMotivo, candidatasVigentes);

        return new SugerenciaParaFactura(sugerenciaMotivo, sugerenciaCuenta, candidatasVigentes);
    }
}
