namespace AiTalent.Application.Common;

/// <summary>Usuario autenticado que hace la petición. MfaVerified viene del claim `aal` del token.</summary>
public sealed record Actor(Guid UserId, bool MfaVerified);
