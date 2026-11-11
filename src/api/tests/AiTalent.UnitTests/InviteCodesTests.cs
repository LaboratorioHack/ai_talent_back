using AiTalent.Domain.Enums;
using AiTalent.Infrastructure.Auth;
using Microsoft.Extensions.Options;

namespace AiTalent.UnitTests;

public class InviteCodesTests
{
    private const string Key = "test-signing-key-that-is-at-least-32-bytes-long";
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static InviteCodes Create(string key = Key, TimeSpan? lifetime = null) =>
        new(Options.Create(new InviteCodeOptions { SigningKey = key, Lifetime = lifetime ?? TimeSpan.FromDays(7) }));

    [Fact]
    public async Task ValidCodeReturnsCompanyAndRole()
    {
        var codes = Create();
        var invite = codes.Issue(CompanyId, StaffRole.Recruiter);

        var claims = await codes.ReadAsync(invite.Code);

        Assert.NotNull(claims);
        Assert.Equal(CompanyId, claims.CompanyId);
        Assert.Equal(StaffRole.Recruiter, claims.Role);
    }

    [Fact]
    public async Task ExpiredCodeIsRejected()
    {
        var codes = Create(lifetime: TimeSpan.FromMinutes(-5));
        var invite = codes.Issue(CompanyId, StaffRole.Recruiter);

        Assert.Null(await codes.ReadAsync(invite.Code));
    }

    [Fact]
    public async Task TamperedCodeIsRejected()
    {
        var codes = Create();
        var code = codes.Issue(CompanyId, StaffRole.Recruiter).Code;
        var index = code.Length - 10;
        var replacement = code[index] == 'A' ? 'B' : 'A';
        var tampered = code[..index] + replacement + code[(index + 1)..];

        Assert.Null(await codes.ReadAsync(tampered));
    }

    [Fact]
    public async Task CodeSignedWithAnotherKeyIsRejected()
    {
        var code = Create("another-signing-key-that-is-at-least-32-bytes").Issue(CompanyId, StaffRole.Recruiter).Code;

        Assert.Null(await Create().ReadAsync(code));
    }

    [Fact]
    public async Task GarbageIsRejected()
    {
        Assert.Null(await Create().ReadAsync("not-a-token"));
    }
}
