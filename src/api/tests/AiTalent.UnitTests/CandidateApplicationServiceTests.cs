using System.Text;
using AiTalent.Application.Common;
using AiTalent.Application.Features.Candidates;
using AiTalent.Domain.Entities;
using AiTalent.Domain.Enums;

namespace AiTalent.UnitTests;

public class CandidateApplicationServiceTests
{
    private readonly FakeJobOpeningRepository _vacancies = new();
    private readonly FakeCandidateRepository _candidates = new();
    private readonly FakeResumeRepository _resumes = new();
    private readonly FakeApplicationRepository _applications = new();
    private readonly FakeCvFileStorage _storage = new();
    private readonly FakeAiEngineClient _engine = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly Guid _companyId = Guid.NewGuid();

    private CandidateApplicationService CreateService() =>
        new(_vacancies, _candidates, _resumes, _applications, _storage, _engine, _unitOfWork);

    private JobOpening CreateOpenVacancy()
    {
        var vacancy = new JobOpening(_companyId, null, "Ingeniero Cloud", "Tecnología", DateTimeOffset.UtcNow.AddDays(7), 1);
        vacancy.Publish();
        _vacancies.Add(vacancy);
        return vacancy;
    }

    private static ApplyToVacancyCommand MakeCommand(
        Guid vacancyId,
        string name = "Ana López",
        string email = "ana@example.com",
        bool consent = true,
        string fileName = "cv.pdf",
        string contentType = "application/pdf",
        string content = "%PDF-1.4 test")
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new ApplyToVacancyCommand(
            vacancyId,
            name,
            email,
            consent,
            fileName,
            contentType,
            bytes.Length,
            () => new MemoryStream(bytes));
    }

    [Fact]
    public async Task CandidateCanApplyToOpenVacancy()
    {
        var vacancy = CreateOpenVacancy();
        var result = await CreateService().ApplyAsync(MakeCommand(vacancy.Id));

        Assert.True(result.IsSuccess);
        var receipt = result.Value!;
        Assert.NotEqual(Guid.Empty, receipt.ApplicationId);
        Assert.NotEqual(Guid.Empty, receipt.CandidateId);
        Assert.NotEqual(Guid.Empty, receipt.ResumeId);
        Assert.Equal("PROCESSED", receipt.Status);
        Assert.Single(_applications.Applications);
        Assert.Single(_resumes.Resumes);
        Assert.Single(_candidates.Candidates);
        Assert.Single(_storage.Uploads);
        Assert.Single(_engine.IngestedResumeIds);
        var app = _applications.Applications[0];
        Assert.Equal(ApplicationSource.Candidate, app.Source);
        Assert.Equal(ApplicationSelectionStatus.Applied, app.SelectionStatus);
    }

    [Fact]
    public async Task ReapplyingToSameVacancyIsRejected()
    {
        var vacancy = CreateOpenVacancy();
        var service = CreateService();
        var first = await service.ApplyAsync(MakeCommand(vacancy.Id, email: "ana@example.com"));
        Assert.True(first.IsSuccess);

        var second = await service.ApplyAsync(MakeCommand(vacancy.Id, email: "ana@example.com", name: "Ana L."));
        Assert.False(second.IsSuccess);
        Assert.Equal(ErrorKind.Conflict, second.Error!.Kind);
        Assert.Contains("already applied", second.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(_applications.Applications);
    }

    [Fact]
    public async Task ApplyingToDraftVacancyIsRejected()
    {
        var vacancy = new JobOpening(_companyId, null, "Ingeniero Cloud", null, null, 1);
        // Status remains Draft
        _vacancies.Add(vacancy);

        var result = await CreateService().ApplyAsync(MakeCommand(vacancy.Id));
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.Conflict, result.Error!.Kind);
        Assert.Empty(_applications.Applications);
    }

    [Fact]
    public async Task ApplyingToClosedVacancyIsRejected()
    {
        var vacancy = CreateOpenVacancy();
        vacancy.Close();

        var result = await CreateService().ApplyAsync(MakeCommand(vacancy.Id));
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.Conflict, result.Error!.Kind);
    }

    [Fact]
    public async Task ApplyingToVacancyWithPastClosingDateIsRejected()
    {
        var vacancy = new JobOpening(_companyId, null, "Ingeniero Cloud", null, DateTimeOffset.UtcNow.AddMinutes(-5), 1);
        vacancy.Publish();
        _vacancies.Add(vacancy);

        var result = await CreateService().ApplyAsync(MakeCommand(vacancy.Id));
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.Conflict, result.Error!.Kind);
    }

    [Fact]
    public async Task MissingConsentIsRejected()
    {
        var vacancy = CreateOpenVacancy();
        var result = await CreateService().ApplyAsync(MakeCommand(vacancy.Id, consent: false));
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.Invalid, result.Error!.Kind);
        Assert.Empty(_applications.Applications);
    }

    [Fact]
    public async Task InvalidEmailIsRejected()
    {
        var vacancy = CreateOpenVacancy();
        var result = await CreateService().ApplyAsync(MakeCommand(vacancy.Id, email: "not-an-email"));
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.Invalid, result.Error!.Kind);
    }

    [Fact]
    public async Task UnsupportedFileTypeIsRejected()
    {
        var vacancy = CreateOpenVacancy();
        var result = await CreateService().ApplyAsync(MakeCommand(vacancy.Id, fileName: "cv.txt", contentType: "text/plain"));
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.Invalid, result.Error!.Kind);
        Assert.Empty(_applications.Applications);
    }

    [Fact]
    public async Task EmptyFileIsRejected()
    {
        var vacancy = CreateOpenVacancy();
        var result = await CreateService().ApplyAsync(MakeCommand(vacancy.Id, content: ""));
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.Invalid, result.Error!.Kind);
    }

    [Fact]
    public async Task EngineFailureMarksResumeFailed()
    {
        var vacancy = CreateOpenVacancy();
        _engine.ThrowOnIngest = new InvalidOperationException("engine down");
        var result = await CreateService().ApplyAsync(MakeCommand(vacancy.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal("FAILED", result.Value!.Status);
        var resume = Assert.Single(_resumes.Resumes);
        Assert.Equal(ResumeProcessingStatus.Failed, resume.ProcessingStatus);
        Assert.Contains("engine down", resume.ProcessingError);
    }

    [Fact]
    public async Task StorageFailureReturnsUnavailable()
    {
        var vacancy = CreateOpenVacancy();
        _storage.ThrowOnUpload = true;
        var result = await CreateService().ApplyAsync(MakeCommand(vacancy.Id));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.Unavailable, result.Error!.Kind);
        Assert.Empty(_applications.Applications);
        Assert.Empty(_resumes.Resumes);
    }

    [Fact]
    public async Task NonExistentVacancyReturnsNotFound()
    {
        var result = await CreateService().ApplyAsync(MakeCommand(Guid.NewGuid()));
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.NotFound, result.Error!.Kind);
    }

    [Fact]
    public async Task DuplicateContentIsFlagged()
    {
        var vacancy = CreateOpenVacancy();
        var service = CreateService();
        await service.ApplyAsync(MakeCommand(vacancy.Id, email: "ana@example.com"));
        // Apply to different vacancy with same file hash but different candidate scenario not needed;
        // test same candidate reapply is rejected, so duplicate-content flag is for same candidate? 
        // We check ExistsWithHashAsync for candidate before reapply check; easier to test by seeding
        var candidate = _candidates.Candidates[0];
        var vacancy2 = CreateOpenVacancy();
        // different vacancy -> different company? CreateOpenVacancy uses same companyId, different vacancy is fine
        var result = await service.ApplyAsync(MakeCommand(vacancy2.Id, email: "ana@example.com", name: "Ana López"));
        // same candidate can apply to different vacancy
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.DuplicateContent);
    }
}
