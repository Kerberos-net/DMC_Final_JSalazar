namespace SmartNet.Contable.Core;

/// <summary>
/// Una invariante de REGLAS.md §7 dentro del alcance de #8 (design.md Decisión 4): las 5
/// globales, PRINCIPAL, DESTINO. El tope acumulado de NC (§7) queda fuera — exige <c>SELECT</c>
/// sobre otros asientos, algo que rompería la pureza de #8 (ADR 0019); es de #10.
/// </summary>
public enum InvarianteContable
{
    /// <summary>Global 1: SUM(Debe) = SUM(Haber) sobre el asiento completo, ambos bloques.</summary>
    SumaDebeIgualHaber,

    /// <summary>Global 2: ninguna línea sin cuenta contable asignada.</summary>
    LineaSinCuenta,

    /// <summary>Global 3: FechaContable no anterior a FechaCorteContable.</summary>
    FechaAnteriorAlCorte,

    /// <summary>Global 4: el proveedor no es P00000 (Varios).</summary>
    ProveedorVarios,

    /// <summary>Global 5: Tipo=D exige Debe&gt;0,Haber=0; Tipo=H, lo contrario.</summary>
    TipoLineaInconsistente,

    /// <summary>Del bloque PRINCIPAL: cargos/401111/proveedor según la tabla de REGLAS.md §7.</summary>
    Principal,

    /// <summary>Del bloque DESTINO: cada cargo con CtaReflejaCodigo congelado tiene su par.</summary>
    Destino,
}
