using AiTalent.Application.Common;
using AiTalent.Application.Features.Candidates;
using AiTalent.Application.Features.Staff;
using AiTalent.Domain.Entities;
using AiTalent.Domain.Enums;
using ApplicationEntity = AiTalent.Domain.Entities.Application;

namespace AiTalent.UnitTests;

public class ApplicationStageServiceTests
{
    private readonly FakeStaffRepository _staff = new();
    private readonly FakeJobOpeningRepository _vacancies = new();
    private readonly FakeApplicationRepository _applications = new();
    private readonly FakeAuditLog _audit = new();
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly JobOpening _vacancy;

    public ApplicationStageServiceTests()
    {
        _vacancy = new JobOpening(_companyId, null, "Ingeniero Cloud", "Tecnología", null, 1);
        _vacancies.Add(_vacancy);
    }

    private ApplicationStageService CreateService() =>
        new(_vacancies, _applications, new StaffAccess(_staff), _audit, new FakeUnitOfWork());

    private Actor AddMember(bool active = true)
    {
        var userId = Guid.NewGuid();
        var member = new StaffMember(userId, _companyId, "Test User", StaffRole.Recruiter);
        if (!active)
        {
            member.SetActive(false);
        }

        _staff.Add(member);
        return new Actor(userId, MfaVerified: false);
    }

    private ApplicationEntity AddApplication(
        ApplicationSelectionStatus status = ApplicationSelectionStatus.Applied,
        Guid? jobOpeningId = null)
    {
        var application = new ApplicationEntity(jobOpeningId ?? _vacancy.Id, Guid.NewGuid(), ApplicationSource.Candidate);
        if (status is ApplicationSelectionStatus.Rejected or ApplicationSelectionStatus.Withdrawn)
        {
            application.MoveTo(status);
        }
        else
        {
            foreach (var step in new[] { ApplicationSelectionStatus.Shortlisted, ApplicationSelectionStatus.Interview, ApplicationSelectionStatus.Hired }
                         .Where(step => step <= status))
            {
                application.MoveTo(step);
            }
        }

        _applications.Applications.Add(application);
        return application;
    }

    [Fact]
    public async Task MovesApplicationToNextStageAndAudits()
    {
        var actor = AddMember();
        var application = AddApplication();

        var result = await CreateService().ChangeStatusAsync(
            actor.UserId, _vacancy.Id, application.Id, ApplicationSelectionStatus.Shortlisted, null);

        Assert.True(result.IsSuccess);
        Assert.Equal(ApplicationSelectionStatus.Shortlisted, application.SelectionStatus);
        Assert.Contains("application.stage_changed", _audit.Actions);
    }

    [Fact]
    public async Task FollowsThePipelineToHired()
    {
        var actor = AddMember();
        var application = AddApplication(ApplicationSelectionStatus.Interview);

        var result = await CreateService().ChangeStatusAsync(
            actor.UserId, _vacancy.Id, application.Id, ApplicationSelectionStatus.Hired, "Excelente entrevista");

        Assert.True(result.IsSuccess);
        Assert.Equal(ApplicationSelectionStatus.Hired, application.SelectionStatus);
    }

    [Theory]
    [InlineData(ApplicationSelectionStatus.Shortlisted, ApplicationSelectionStatus.Applied)]
    [InlineData(ApplicationSelectionStatus.Interview, ApplicationSelectionStatus.Shortlisted)]
    public async Task AllowsOneStepBackToUndoAMove(ApplicationSelectionStatus from, ApplicationSelectionStatus to)
    {
        var actor = AddMember();
        var application = AddApplication(from);

        var result = await CreateService().ChangeStatusAsync(actor.UserId, _vacancy.Id, application.Id, to, null);

        Assert.True(result.IsSuccess);
        Assert.Equal(to, application.SelectionStatus);
    }

    [Theory]
    [InlineData(ApplicationSelectionStatus.Applied, ApplicationSelectionStatus.Interview)]
    [InlineData(ApplicationSelectionStatus.Interview, ApplicationSelectionStatus.Applied)]
    [InlineData(ApplicationSelectionStatus.Applied, ApplicationSelectionStatus.Hired)]
    [InlineData(ApplicationSelectionStatus.Applied, ApplicationSelectionStatus.Applied)]
    [InlineData(ApplicationSelectionStatus.Shortlisted, ApplicationSelectionStatus.Hired)]
    [InlineData(ApplicationSelectionStatus.Rejected, ApplicationSelectionStatus.Shortlisted)]
    [InlineData(ApplicationSelectionStatus.Hired, ApplicationSelectionStatus.Rejected)]
    [InlineData(ApplicationSelectionStatus.Withdrawn, ApplicationSelectionStatus.Applied)]
    public async Task RejectsMovesOutsideThePipeline(ApplicationSelectionStatus from, ApplicationSelectionStatus to)
    {
        var actor = AddMember();
        var application = AddApplication(from);

        var result = await CreateService().ChangeStatusAsync(actor.UserId, _vacancy.Id, application.Id, to, null);

        Assert.Equal(ErrorKind.Conflict, result.Error!.Kind);
        Assert.Equal(from, application.SelectionStatus);
        Assert.Empty(_audit.Actions);
    }

    [Fact]
    public async Task ApplicationFromAnotherVacancyIsNotFound()
    {
        var actor = AddMember();
        var application = AddApplication(jobOpeningId: Guid.NewGuid());

        var result = await CreateService().ChangeStatusAsync(
            actor.UserId, _vacancy.Id, application.Id, ApplicationSelectionStatus.Shortlisted, null);

        Assert.Equal(ErrorKind.NotFound, result.Error!.Kind);
    }

    [Fact]
    public async Task VacancyFromAnotherCompanyIsNotFound()
    {
        var actor = AddMember();
        var foreignVacancy = new JobOpening(Guid.NewGuid(), null, "Otra empresa", null, null, 1);
        _vacancies.Add(foreignVacancy);
        var application = AddApplication(jobOpeningId: foreignVacancy.Id);

        var result = await CreateService().ChangeStatusAsync(
            actor.UserId, foreignVacancy.Id, application.Id, ApplicationSelectionStatus.Shortlisted, null);

        Assert.Equal(ErrorKind.NotFound, result.Error!.Kind);
        Assert.Equal(ApplicationSelectionStatus.Applied, application.SelectionStatus);
    }

    [Fact]
    public async Task NonMemberIsForbidden()
    {
        var application = AddApplication();

        var result = await CreateService().ChangeStatusAsync(
            Guid.NewGuid(), _vacancy.Id, application.Id, ApplicationSelectionStatus.Shortlisted, null);

        Assert.Equal(ErrorKind.Forbidden, result.Error!.Kind);
    }

    [Fact]
    public async Task DeactivatedMemberIsForbidden()
    {
        var actor = AddMember(active: false);
        var application = AddApplication();

        var result = await CreateService().ChangeStatusAsync(
            actor.UserId, _vacancy.Id, application.Id, ApplicationSelectionStatus.Shortlisted, null);

        Assert.Equal(ErrorKind.Forbidden, result.Error!.Kind);
    }

    [Fact]
    public async Task TooLongReasonIsInvalid()
    {
        var actor = AddMember();
        var application = AddApplication();

        var result = await CreateService().ChangeStatusAsync(
            actor.UserId,
            _vacancy.Id,
            application.Id,
            ApplicationSelectionStatus.Rejected,
            new string('x', 501));

        Assert.Equal(ErrorKind.Invalid, result.Error!.Kind);
        Assert.Equal(ApplicationSelectionStatus.Applied, application.SelectionStatus);
    }
}
