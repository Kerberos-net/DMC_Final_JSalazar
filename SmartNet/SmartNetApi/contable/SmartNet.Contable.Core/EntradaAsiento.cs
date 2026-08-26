using SmartNet.TiposCambio.Core;

namespace SmartNet.Contable.Core;

/// <summary>Moneda del asiento — REGLAS.md §4 cruza moneda con EsRelacionada.</summary>
public enum MonedaAsiento
{
    Pen,
    Usd,
}

/// <summary>
/// DTO de entrada de <see cref="ComposicionDeAsiento.Componer"/> (design.md Decisión 1). No es una
/// porción de <c>fact.Factura</c>: es lo que REGLAS.md §5/§6 necesitan, ya resuelto por #3/#4.
/// #8 no re-resuelve prefijos ni re-elige SBS/MANUAL; los consume compuestos.
/// </summary>
public sealed record EntradaAsiento
{
    public string ProveedorCodigo { get; }
    public bool EsRelacionada { get; }
    public MonedaAsiento Moneda { get; }
    public DateOnly FechaContable { get; }
    public string? MotivoDescripcion { get; }
    public TipoComprobante Comprobante { get; }
    public Afectacion Afectacion { get; }
    public decimal BaseOrig { get; }
    public decimal IgvOrig { get; }
    public decimal PercepcionOrig { get; }

    /// <summary>
    /// Solo aplica cuando <see cref="Moneda"/> es <see cref="MonedaAsiento.Usd"/> (o una NC que
    /// hereda un TC de una factura en dólares) — REGLAS.md §6. Un asiento en soles no tiene tipo
    /// de cambio: no hay conversión que aplicar.
    /// </summary>
    public TipoCambioCongelado? TipoCambio { get; }
    public IReadOnlyList<CargoSolicitado> Cargos { get; }

    /// <summary>Solo no-null cuando <see cref="Comprobante"/> es <see cref="TipoComprobante.NotaCredito"/> con referencia interna.</summary>
    public HerenciaNotaCredito? Herencia { get; }

    public EntradaAsiento(
        string ProveedorCodigo,
        bool EsRelacionada,
        MonedaAsiento Moneda,
        DateOnly FechaContable,
        string? MotivoDescripcion,
        TipoComprobante Comprobante,
        Afectacion Afectacion,
        decimal BaseOrig,
        decimal IgvOrig,
        decimal PercepcionOrig,
        TipoCambioCongelado? TipoCambio,
        IReadOnlyList<CargoSolicitado> Cargos,
        HerenciaNotaCredito? Herencia)
    {
        ArgumentNullException.ThrowIfNull(ProveedorCodigo);
        ArgumentNullException.ThrowIfNull(Cargos);

        this.ProveedorCodigo = ProveedorCodigo;
        this.EsRelacionada = EsRelacionada;
        this.Moneda = Moneda;
        this.FechaContable = FechaContable;
        this.MotivoDescripcion = MotivoDescripcion;
        this.Comprobante = Comprobante;
        this.Afectacion = Afectacion;
        this.BaseOrig = BaseOrig;
        this.IgvOrig = IgvOrig;
        this.PercepcionOrig = PercepcionOrig;
        this.TipoCambio = TipoCambio;
        this.Cargos = Cargos;
        this.Herencia = Herencia;
    }
}
