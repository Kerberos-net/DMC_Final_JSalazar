namespace SmartNet.Auth.Core;

/// <summary>
/// Outcome of <see cref="AccessPolicy.Evaluate"/>. Scaffolding, not a schema noun — CONVENTIONS.md's
/// boundary test sends it to English ("¿existiría igual en cualquier otro proyecto?").
/// </summary>
public enum AccessDecision
{
    Allowed,
    Locked,
}
