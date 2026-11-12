using AiTalent.Application.Abstractions;
using AiTalent.Application.Common;
using AiTalent.Domain.Entities;
using AiTalent.Domain.Enums;

namespace AiTalent.UnitTests;

internal sealed class FakeStaffRepository : IStaffRepository
{
    public List<StaffMember> Members { get; } = [];

    public Task<StaffMember?> FindByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult(Members.FirstOrDefault(m => m.UserId == userId));

    public Task<IReadOnlyList<StaffMember>> ListByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<StaffMember>>(Members.Where(m => m.CompanyId == companyId).ToList());

    public void Add(StaffMember staff) => Members.Add(staff);
}

internal sealed class FakeCompanyRepository : ICompanyRepository
{
    public List<Company> Companies { get; } = [];

    public Task<Company?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Companies.FirstOrDefault(c => c.Id == id));

    public void Add(Company company) => Companies.Add(company);
}

internal sealed class FakeUnitOfWork(bool duplicateKey = false) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken ct = default) =>
        duplicateKey ? throw new DuplicateEntityException("duplicate") : Task.CompletedTask;
}

internal sealed class FakeAuditLog : IAuditLog
{
    public List<string> Actions { get; } = [];

    public Task RecordAsync(Guid actorId, string action, string entity, string entityId, object details, CancellationToken ct = default)
    {
        Actions.Add(action);
        return Task.CompletedTask;
    }
}

internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        Task.FromResult(respond(request));
}

internal sealed class FakeInviteCodes : IInviteCodes
{
    private readonly Dictionary<string, InviteClaims> _codes = [];

    public InviteCode Issue(Guid companyId, StaffRole role)
    {
        var code = $"code-{Guid.NewGuid()}";
        _codes[code] = new InviteClaims(companyId, role);
        return new InviteCode(code, DateTimeOffset.UtcNow.AddDays(7));
    }

    public Task<InviteClaims?> ReadAsync(string code, CancellationToken ct = default) =>
        Task.FromResult(_codes.GetValueOrDefault(code));
}
