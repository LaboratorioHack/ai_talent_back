namespace AiTalent.Application.Abstractions;

/// <summary>Registro append-only en app.audit_log. Nunca incluir PII ni secretos en details.</summary>
public interface IAuditLog
{
    Task RecordAsync(Guid actorId, string action, string entity, string entityId, object details, CancellationToken ct = default);
}
