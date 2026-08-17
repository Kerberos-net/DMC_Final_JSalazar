namespace SmartNet.Auth.Core.Tests;

/// <summary>
/// design.md Decision 5 (revised for Decision 8): THREE lockout fields, one field per persisted
/// column, nothing derived. A construction/equality test is sufficient — this is a data record,
/// not logic (task 2.6).
/// </summary>
public class UsuarioCredentialStateTests
{
    [Fact]
    public void Construction_CarriesEveryPersistedField()
    {
        var bloqueadoHasta = DateTimeOffset.Parse("2026-08-16T12:00:00Z");

        var estado = new SmartNet.Auth.Core.UsuarioCredentialState(
            UsuarioId: 1,
            NombreUsuario: "contador",
            ClaveHash: "$argon2id$v=19$m=19456,t=2,p=1$c2FsdA$aGFzaA",
            IntentosFallidos: 3,
            NivelBloqueo: 1,
            BloqueadoHasta: bloqueadoHasta,
            Activo: true);

        Assert.Equal(1, estado.UsuarioId);
        Assert.Equal("contador", estado.NombreUsuario);
        Assert.Equal("$argon2id$v=19$m=19456,t=2,p=1$c2FsdA$aGFzaA", estado.ClaveHash);
        Assert.Equal(3, estado.IntentosFallidos);
        Assert.Equal(1, estado.NivelBloqueo);
        Assert.Equal(bloqueadoHasta, estado.BloqueadoHasta);
        Assert.True(estado.Activo);
    }

    [Fact]
    public void TwoStatesWithTheSameValues_AreEqual_BecauseItIsARecord()
    {
        var a = new SmartNet.Auth.Core.UsuarioCredentialState(1, "u", "hash", 0, 0, null, true);
        var b = new SmartNet.Auth.Core.UsuarioCredentialState(1, "u", "hash", 0, 0, null, true);

        Assert.Equal(a, b);
    }
}
