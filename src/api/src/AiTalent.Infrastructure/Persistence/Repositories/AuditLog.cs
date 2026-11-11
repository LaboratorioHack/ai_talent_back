using System.Text.Json;
using AiTalent.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace AiTalent.Infrastructure.Persistence.Repositories;

internal sealed class AuditLog(AppDbContext db) : IAuditLog
{
    public async Task RecordAsync(Guid actorId, string action, string entity, string entityId, object details, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(details);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"insert into app.audit_log (actor_id, actor_type, action, entity, entity_id, details) values ({actorId}, 'user', {action}, {entity}, {entityId}, {json}::jsonb)",
            ct);
    }
}
