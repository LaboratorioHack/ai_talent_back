using System.Net;
using AiTalent.Application.Common;
using AiTalent.Application.Features.Evaluations;
using AiTalent.Application.Features.Staff;
using AiTalent.Domain.Entities;
using AiTalent.Domain.Enums;
using ApplicationEntity = AiTalent.Domain.Entities.Application;

namespace AiTalent.UnitTests;

public class EvaluationServiceTests
{
    private readonly FakeStaffRepository _staff = new();
    private readonly FakeJobOpeningRepository _vacancies = new();
    private readonly FakeApplicationRepository _applications = new();
    private readonly FakeAiEngineClient _engine = new();
    private readonly FakeAuditLog _audit = new();
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Actor _actor;

    public EvaluationServiceTests()
    {
        var userId = Guid.NewGuid();
        _staff.Add(new StaffMember(userId, _companyId, "Recruiter", StaffRole.Recruiter));
        _actor = new Actor(userId, MfaVerified: false);
    }

    private EvaluationService CreateService() =>
        new(_vacancies, _applications, _engine, new StaffAccess(_staff), _audit);

    private JobOpening CreateVacancy(bool withProfile = true, Guid? companyId = null)
    {
        var vacancy = new JobOpening(companyId ?? _companyId, null, "Ingeniero Cloud", "Tecnología", null, 1);
        if (withProfile)
        {
            var profile = new RoleProfile(vacancy.Id, 1, "Cloud");
            profile.ReplaceRequirements(
            [
                new Requirement(profile.Id, RequirementType.Mandatory, "SKILL", "Azure", null, 100m, null),
            ]);
            vacancy.Profiles.Add(profile);
        }

        _vacancies.Add(vacancy);
        return vacancy;
    }

    private (ApplicationEntity Application, Resume Resume) AddApplication(
        JobOpening vacancy,
        bool processed = true,
        ApplicationSelectionStatus status = ApplicationSelectionStatus.Applied,
        string name = "Ana López")
    {
        var candidate = new Candidate(vacancy.CompanyId, name, $"{Guid.NewGuid():N}@example.com");
        var resume = new Resume(candidate.Id, 1, $"cvs/{candidate.Id}.pdf", null);
        if (processed)
        {
            resume.MarkProcessed("v1", null);
        }

        var application = new ApplicationEntity(vacancy.Id, candidate.Id, ApplicationSource.Candidate);
        application.ChangeSelectionStatus(status);
        _applications.Candidates.Add(candidate);
        _applications.Resumes.Add(resume);
        _applications.Add(application);
        return (application, resume);
    }

    [Fact]
    public async Task EvaluateSendsProfileResumeAndApplicationToEngine()
    {
        var vacancy = CreateVacancy();
        var (application, resume) = AddApplication(vacancy);

        var result = await CreateService().EvaluateAsync(_actor, vacancy.Id, application.Id);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(_engine.EvaluateRequests);
        Assert.Equal(vacancy.CurrentProfile!.Id, request.RoleProfileId);
        Assert.Equal(resume.Id, request.ResumeId);
        Assert.Equal(application.Id, request.ApplicationId);
        Assert.Equal(vacancy.Id, request.JobOpeningId);
        Assert.Equal(resume.Id, result.Value!.ResumeId);
        Assert.Contains("evaluation.requested", _audit.Actions);
    }

    [Fact]
    public async Task EvaluateRejectsUnprocessedCv()
    {
        var vacancy = CreateVacancy();
        var (application, _) = AddApplication(vacancy, processed: false);

        var result = await CreateService().EvaluateAsync(_actor, vacancy.Id, application.Id);

        Assert.Equal(ErrorKind.Conflict, result.Error!.Kind);
        Assert.Empty(_engine.EvaluateRequests);
    }

    [Fact]
    public async Task EvaluateNeedsARoleProfile()
    {
        var vacancy = CreateVacancy(withProfile: false);
        var (application, _) = AddApplication(vacancy);

        var result = await CreateService().EvaluateAsync(_actor, vacancy.Id, application.Id);

        Assert.Equal(ErrorKind.Invalid, result.Error!.Kind);
    }

    [Fact]
    public async Task OtherCompanyVacancyIsNotFound()
    {
        var vacancy = CreateVacancy(companyId: Guid.NewGuid());
        var (application, _) = AddApplication(vacancy);

        var result = await CreateService().EvaluateAsync(_actor, vacancy.Id, application.Id);

        Assert.Equal(ErrorKind.NotFound, result.Error!.Kind);
    }

    [Fact]
    public async Task NonMemberIsForbidden()
    {
        var vacancy = CreateVacancy();

        var result = await CreateService().RankAsync(new Actor(Guid.NewGuid(), false), vacancy.Id, null);

        Assert.Equal(ErrorKind.Forbidden, result.Error!.Kind);
    }

    [Fact]
    public async Task EngineDownMapsToUnavailable()
    {
        var vacancy = CreateVacancy();
        var (application, _) = AddApplication(vacancy);
        _engine.ThrowOnEvaluate = new HttpRequestException("down", null, HttpStatusCode.BadGateway);

        var result = await CreateService().EvaluateAsync(_actor, vacancy.Id, application.Id);

        Assert.Equal(ErrorKind.Unavailable, result.Error!.Kind);
    }

    [Fact]
    public async Task EngineBadRequestMapsToInvalid()
    {
        var vacancy = CreateVacancy();
        var (application, _) = AddApplication(vacancy);
        _engine.ThrowOnEvaluate = new HttpRequestException("bad", null, HttpStatusCode.BadRequest);

        var result = await CreateService().EvaluateAsync(_actor, vacancy.Id, application.Id);

        Assert.Equal(ErrorKind.Invalid, result.Error!.Kind);
    }

    [Fact]
    public async Task RankUsesProcessedActiveApplicationsAndOrdersByScore()
    {
        var vacancy = CreateVacancy();
        var (low, lowResume) = AddApplication(vacancy, name: "Low");
        var (high, highResume) = AddApplication(vacancy, name: "High");
        var (failed, failedResume) = AddApplication(vacancy, name: "Failed");
        AddApplication(vacancy, processed: false, name: "Pending");
        AddApplication(vacancy, status: ApplicationSelectionStatus.Withdrawn, name: "Withdrawn");
        _engine.ScoreOf = id => id == highResume.Id ? 90f : 60f;
        _engine.UnrankableResumes.Add(failedResume.Id);

        var result = await CreateService().RankAsync(_actor, vacancy.Id, null);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(_engine.RankRequests);
        Assert.Equal(3, request.ResumeIds.Count);
        Assert.Contains(lowResume.Id, request.ResumeIds);
        var ranking = result.Value!;
        Assert.Equal([high.Id, low.Id], ranking.Ranked.Select(item => item.ApplicationId));
        Assert.Equal([1, 2], ranking.Ranked.Select(item => item.Position));
        Assert.Equal("High", ranking.Ranked[0].CandidateName);
        Assert.Equal([failed.Id], ranking.NotRanked);
    }

    [Fact]
    public async Task RankWithNothingReadySkipsTheEngine()
    {
        var vacancy = CreateVacancy();
        AddApplication(vacancy, processed: false);

        var result = await CreateService().RankAsync(_actor, vacancy.Id, null);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Ranked);
        Assert.Empty(_engine.RankRequests);
    }

    [Fact]
    public async Task CompareMapsApplicationsToResumes()
    {
        var vacancy = CreateVacancy();
        var (first, firstResume) = AddApplication(vacancy, name: "First");
        var (second, secondResume) = AddApplication(vacancy, name: "Second");

        var result = await CreateService().CompareAsync(_actor, vacancy.Id, [first.Id, second.Id]);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(_engine.CompareRequests);
        Assert.Equal([firstResume.Id, secondResume.Id], request.ResumeIds);
        Assert.Equal(["First", "Second"], result.Value!.Candidates.Select(item => item.CandidateName));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task CompareNeedsTwoOrThreeApplications(int count)
    {
        var vacancy = CreateVacancy();
        var ids = Enumerable.Range(0, count).Select(_ => AddApplication(vacancy).Application.Id).ToList();

        var result = await CreateService().CompareAsync(_actor, vacancy.Id, ids);

        Assert.Equal(ErrorKind.Invalid, result.Error!.Kind);
        Assert.Empty(_engine.CompareRequests);
    }

    [Fact]
    public async Task CompareRejectsApplicationFromAnotherVacancy()
    {
        var vacancy = CreateVacancy();
        var other = CreateVacancy();
        var (first, _) = AddApplication(vacancy);
        var (foreign, _) = AddApplication(other);

        var result = await CreateService().CompareAsync(_actor, vacancy.Id, [first.Id, foreign.Id]);

        Assert.Equal(ErrorKind.NotFound, result.Error!.Kind);
    }

    [Fact]
    public async Task EvidenceSearchesTheApplicationResume()
    {
        var vacancy = CreateVacancy();
        var (application, resume) = AddApplication(vacancy);

        var result = await CreateService().SearchEvidenceAsync(_actor, vacancy.Id, application.Id, "  Azure  ", null);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(_engine.EvidenceRequests);
        Assert.Equal(resume.Id, request.ResumeId);
        Assert.Equal("Azure", request.Query);
        Assert.Equal(8, request.Limit);
        Assert.Single(result.Value!.Search.Chunks);
    }

    [Theory]
    [InlineData("", 5)]
    [InlineData("Azure", 0)]
    [InlineData("Azure", 21)]
    public async Task EvidenceValidatesQueryAndLimit(string query, int limit)
    {
        var vacancy = CreateVacancy();
        var (application, _) = AddApplication(vacancy);

        var result = await CreateService().SearchEvidenceAsync(_actor, vacancy.Id, application.Id, query, limit);

        Assert.Equal(ErrorKind.Invalid, result.Error!.Kind);
        Assert.Empty(_engine.EvidenceRequests);
    }
}
