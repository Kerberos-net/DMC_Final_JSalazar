using System.Reflection;

namespace SmartNet.TiposCambio.Core.Tests;

/// <summary>
/// design.md Decision 2 / Interfaces/Contracts: <c>ResultadoTipoCambio</c> is a closed abstract
/// record hierarchy — <c>Vigente</c> and <c>SinTipoCambio</c> are the only constructible cases,
/// the base ctor is <c>private protected</c> so no other assembly can add a third case
/// (tasks.md 1.6). ADR 0018 pt. 3.
/// </summary>
public class ResultadoTipoCambioTests
{
    [Fact]
    public void Vigente_WrapsATipoCambio()
    {
        var tipoCambio = new TipoCambio(
            new DateOnly(2026, 8, 14), OrigenTipoCambio.Sbs, 3.799m, 3.802m,
            new DateTime(2026, 8, 14, 9, 0, 0, DateTimeKind.Utc));

        ResultadoTipoCambio resultado = new ResultadoTipoCambio.Vigente(tipoCambio);

        var vigente = Assert.IsType<ResultadoTipoCambio.Vigente>(resultado);
        Assert.Equal(tipoCambio, vigente.Valor);
    }

    [Fact]
    public void SinTipoCambio_CarriesTheQueriedFecha()
    {
        var fecha = new DateOnly(2026, 8, 16);

        ResultadoTipoCambio resultado = new ResultadoTipoCambio.SinTipoCambio(fecha);

        var sinTipoCambio = Assert.IsType<ResultadoTipoCambio.SinTipoCambio>(resultado);
        Assert.Equal(fecha, sinTipoCambio.Fecha);
    }

    [Fact]
    public void VigenteAndSinTipoCambio_AreDistinctTypes()
    {
        var tipoCambio = new TipoCambio(
            new DateOnly(2026, 8, 14), OrigenTipoCambio.Sbs, 3.799m, 3.802m,
            new DateTime(2026, 8, 14, 9, 0, 0, DateTimeKind.Utc));

        ResultadoTipoCambio vigente = new ResultadoTipoCambio.Vigente(tipoCambio);
        ResultadoTipoCambio sinTipoCambio = new ResultadoTipoCambio.SinTipoCambio(new DateOnly(2026, 8, 16));

        Assert.IsNotType<ResultadoTipoCambio.SinTipoCambio>(vigente);
        Assert.IsNotType<ResultadoTipoCambio.Vigente>(sinTipoCambio);
    }

    [Fact]
    public void BaseHierarchy_IsClosedToOtherAssemblies()
    {
        var ctor = typeof(ResultadoTipoCambio).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, binder: null, types: [], modifiers: null);

        Assert.NotNull(ctor);
        Assert.True(ctor.IsFamilyAndAssembly,
            "ResultadoTipoCambio's constructor must be private protected — closes the hierarchy " +
            "to other assemblies (design.md Decision 2).");
    }
}
