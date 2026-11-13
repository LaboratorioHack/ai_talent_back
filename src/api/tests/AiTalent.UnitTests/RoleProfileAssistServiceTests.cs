using System.Net;
using AiTalent.Application.Common;
using AiTalent.Application.Features.RoleProfiles;
using AiTalent.Application.Features.Staff;
using AiTalent.Domain.Entities;
using AiTalent.Domain.Enums;

namespace AiTalent.UnitTests;

public class RoleProfileAssistServiceTests
{
    private const string Description =
        "Buscamos un desarrollador de videojuegos con Unity y C# para un estudio de juegos móviles.";

    private readonly FakeStaffRepository _staff = new();
    private readonly FakeAiEngineClient _engine = new();
    private readonly FakeAuditLog _audit = new();
    private readonly Actor _actor;

    public RoleProfileAssistServiceTests()
    {
        var userId = Guid.NewGuid();
        _staff.Add(new StaffMember(userId, Guid.NewGuid(), "Recruiter", StaffRole.Recruiter));
        _actor = new Actor(userId, MfaVerified: false);
    }

    private RoleProfileAssistService CreateService() => new(_engine, new StaffAccess(_staff), _audit, new FixedClock(new DateTimeOffset(2026, 11, 14, 3, 0, 0, TimeSpan.Zero)));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task ProposeSendsCleanDescriptionAndExistingRequirements()
    {
        var result = await CreateService().ProposeAsync(
            _actor,
            $"  {Description}  ",
            " Game Developer ",
            null,
            [
                new ExistingRequirementInput("C#", "mandatory", 10.4m),
                new ExistingRequirementInput("   ", null, null),
            ]);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Requirements.Count);
        var request = Assert.Single(_engine.ProposeRequests);
        Assert.Equal(Description, request.Description);
        Assert.Equal("Game Developer", request.Title);
        Assert.Null(request.Area);
        var existing = Assert.Single(request.Existing);
        Assert.Equal(("C#", "MANDATORY", 10), (existing.Description, existing.Type, existing.Weight));
        Assert.Contains("role_profile.requirements_proposed", _audit.Actions);
    }

    [Fact]
    public async Task DraftSendsTheContextWithTodayInBogotaAndTheEditorOptions()
    {
        var result = await CreateService().DraftVacancyAsync(
            _actor,
            "  Necesitamos dos desarrolladores Unity, remoto, contrato indefinido.  ",
            ["Tecnología", " Diseño ", ""],
            ["Término indefinido"],
            null);

        Assert.True(result.IsSuccess);
        Assert.Equal("Game Developer", result.Value!.Draft.Title);
        var request = Assert.Single(_engine.DraftRequests);
        Assert.Equal("Necesitamos dos desarrolladores Unity, remoto, contrato indefinido.", request.Context);
        // 03:00 UTC on Nov 14 is still Nov 13 in Bogotá.
        Assert.Equal(new DateOnly(2026, 11, 13), request.Today);
        Assert.Equal(["Tecnología", "Diseño"], request.Areas);
        Assert.Empty(request.Schedules);
        Assert.Contains("role_profile.vacancy_drafted", _audit.Actions);
    }

    [Theory]
    [InlineData("corto")]
    [InlineData(null)]
    public async Task DraftRejectsTooShortContextWithoutCallingEngine(string? context)
    {
        var result = await CreateService().DraftVacancyAsync(_actor, context, null, null, null);

        Assert.Equal(ErrorKind.Invalid, result.Error!.Kind);
        Assert.Empty(_engine.DraftRequests);
    }

    [Theory]
    [InlineData("Muy corta")]
    [InlineData(null)]
    public async Task ProposeRejectsShortDescriptionWithoutCallingEngine(string? description)
    {
        var result = await CreateService().ProposeAsync(_actor, description, null, null, null);

        Assert.Equal(ErrorKind.Invalid, result.Error!.Kind);
        Assert.Empty(_engine.ProposeRequests);
    }

    [Fact]
    public async Task ProposeRejectsUnknownRequirementType()
    {
        var result = await CreateService().ProposeAsync(
            _actor,
            Description,
            null,
            null,
            [new ExistingRequirementInput("Unity", "OPTIONAL", 10m)]);

        Assert.Equal(ErrorKind.Invalid, result.Error!.Kind);
    }

    [Fact]
    public async Task ProposeRequiresCompanyMember()
    {
        var outsider = new Actor(Guid.NewGuid(), MfaVerified: false);

        var result = await CreateService().ProposeAsync(outsider, Description, null, null, null);

        Assert.Equal(ErrorKind.Forbidden, result.Error!.Kind);
        Assert.Empty(_engine.ProposeRequests);
    }

    [Fact]
    public async Task ProposeMapsEngineOutageToUnavailable()
    {
        _engine.ThrowOnAssist = new HttpRequestException("down", null, HttpStatusCode.BadGateway);

        var result = await CreateService().ProposeAsync(_actor, Description, null, null, null);

        Assert.Equal(ErrorKind.Unavailable, result.Error!.Kind);
        Assert.Empty(_audit.Actions);
    }

    [Fact]
    public async Task SuggestEquivalencesSendsRequirementContextAndExistingLabels()
    {
        var result = await CreateService().SuggestEquivalencesAsync(_actor, " C# ", "Game Developer", Description, ["Java", " "]);

        Assert.True(result.IsSuccess);
        Assert.Equal("C++", Assert.Single(result.Value!.Equivalences).Label);
        var request = Assert.Single(_engine.EquivalenceRequests);
        Assert.Equal("C#", request.Requirement);
        Assert.Equal(["Java"], request.Existing);
        Assert.Contains("role_profile.equivalences_suggested", _audit.Actions);
    }

    [Fact]
    public async Task SuggestEquivalencesRejectsEmptyRequirement()
    {
        var result = await CreateService().SuggestEquivalencesAsync(_actor, " ", null, null, null);

        Assert.Equal(ErrorKind.Invalid, result.Error!.Kind);
        Assert.Empty(_engine.EquivalenceRequests);
    }
}
