namespace SmartNet.Auth.Core;

/// <summary>
/// The shape <see cref="ISesionRepository.FindActiveAsync"/> returns for a live
/// <c>fact.Sesion</c> row (design.md Decision 5).
/// </summary>
public sealed record SesionActiva(
    long SesionId,
    long UsuarioId,
    string TokenHash,
    DateTimeOffset CreadaEn,
    DateTimeOffset ExpiraEn,
    DateTimeOffset UltimaActividadEn,
    string Ticket);
