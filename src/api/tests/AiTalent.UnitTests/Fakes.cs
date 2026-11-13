using AiTalent.Application.Abstractions;
using AiTalent.Application.Abstractions.AiEngine;
using AiTalent.Application.Common;
using AiTalent.Domain.Entities;
using AiTalent.Domain.Enums;
using ApplicationEntity = AiTalent.Domain.Entities.Application;

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

internal sealed class FakeJobOpeningRepository : IJobOpeningRepository
{
    public List<JobOpening> Vacancies { get; } = [];

    public HashSet<Guid> UsedProfiles { get; } = [];

    public Task<IReadOnlyList<JobOpening>> ListByCompanyAsync(
        Guid companyId,
        JobOpeningStatus? status,
        string? search,
        int skip,
        int take,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<JobOpening>>(
            Filter(companyId, status, search)
                .OrderByDescending(vacancy => vacancy.CreatedAt)
                .Skip(skip)
                .Take(take)
                .ToList());

    public Task<int> CountByCompanyAsync(
        Guid companyId,
        JobOpeningStatus? status,
        string? search,
        CancellationToken ct = default) =>
        Task.FromResult(Filter(companyId, status, search).Count());

    public Task<JobOpening?> FindByIdAsync(Guid companyId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(Vacancies.FirstOrDefault(vacancy =>
            vacancy.Id == id && vacancy.CompanyId == companyId && vacancy.DeletedAt is null));

    public Task<JobOpening?> FindForApplicationAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Vacancies.FirstOrDefault(vacancy => vacancy.Id == id && vacancy.DeletedAt is null));

    public Task<JobOpening?> FindPublicAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Vacancies.FirstOrDefault(vacancy => vacancy.Id == id && vacancy.DeletedAt is null));

    public Task<bool> IsProfileInUseAsync(Guid roleProfileId, CancellationToken ct = default) =>
        Task.FromResult(UsedProfiles.Contains(roleProfileId));

    public void Add(JobOpening jobOpening) => Vacancies.Add(jobOpening);

    private IEnumerable<JobOpening> Filter(Guid companyId, JobOpeningStatus? status, string? search)
    {
        var query = Vacancies.Where(vacancy => vacancy.CompanyId == companyId && vacancy.DeletedAt is null);

        if (status is { } statusFilter)
        {
            query = query.Where(vacancy => vacancy.Status == statusFilter);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(vacancy =>
                vacancy.Title.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
                || (vacancy.Area is not null
                    && vacancy.Area.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)));
        }

        return query;
    }
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

internal sealed class FakeCandidateRepository : ICandidateRepository
{
    public Task<IReadOnlyList<Candidate>> ListByEmailAsync(string normalizedEmail, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Candidate>>(Candidates.Where(candidate => candidate.ContactEmail == normalizedEmail).ToList());

    public List<Candidate> Candidates { get; } = [];

    public Task<Candidate?> FindByCompanyAndEmailAsync(Guid companyId, string normalizedEmail, CancellationToken ct = default) =>
        Task.FromResult(Candidates.FirstOrDefault(c =>
            c.CompanyId == companyId && string.Equals(c.ContactEmail, normalizedEmail, StringComparison.Ordinal)));

    public Task<Candidate?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Candidates.FirstOrDefault(c => c.Id == id));

    public void Add(Candidate candidate) => Candidates.Add(candidate);

    public void Remove(Candidate candidate) => Candidates.Remove(candidate);
}

internal sealed class FakeResumeRepository : IResumeRepository
{
    public List<Resume> Resumes { get; } = [];

    public Task<int> GetNextVersionAsync(Guid candidateId, CancellationToken ct = default)
    {
        var max = Resumes.Where(r => r.CandidateId == candidateId).Select(r => (int?)r.Version).Max();
        return Task.FromResult((max ?? 0) + 1);
    }

    public Task<bool> ExistsWithHashAsync(Guid candidateId, string fileHash, CancellationToken ct = default) =>
        Task.FromResult(Resumes.Any(r => r.CandidateId == candidateId && r.FileHash == fileHash));

    public Task<Resume?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Resumes.FirstOrDefault(r => r.Id == id));

    public void Add(Resume resume) => Resumes.Add(resume);

    public void Remove(Resume resume) => Resumes.Remove(resume);
}

internal sealed class FakeApplicationRepository : IApplicationRepository
{
    public List<ApplicationEntity> Applications { get; } = [];
    public List<Candidate> Candidates { get; } = [];
    public List<Resume> Resumes { get; } = [];

    public Task<ApplicationEntity?> FindByJobAndCandidateAsync(Guid jobOpeningId, Guid candidateId, CancellationToken ct = default) =>
        Task.FromResult(Applications.FirstOrDefault(a =>
            a.JobOpeningId == jobOpeningId && a.CandidateId == candidateId));

    public Task<ApplicationEntity?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Applications.FirstOrDefault(a => a.Id == id));

    public Task<bool> HasSelfSubmittedAsync(Guid userId, string? normalizedEmail, CancellationToken ct = default) =>
        Task.FromResult(Applications.Any(a =>
            a.Source == ApplicationSource.Candidate
            && (a.SubmittedBy == userId || (normalizedEmail is not null && Candidates.Any(c => c.Id == a.CandidateId && c.ContactEmail.Equals(normalizedEmail, StringComparison.OrdinalIgnoreCase))))));

    public Task<IReadOnlyList<ApplicationSubmission>> ListByJobOpeningAsync(Guid jobOpeningId, CancellationToken ct = default)
    {
        var rows = Applications
            .Where(a => a.JobOpeningId == jobOpeningId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a =>
            {
                var candidate = Candidates.First(c => c.Id == a.CandidateId);
                var resume = Resumes
                    .Where(r => r.CandidateId == candidate.Id)
                    .OrderByDescending(r => r.Version)
                    .ThenByDescending(r => r.CreatedAt)
                    .FirstOrDefault();
                return new ApplicationSubmission(
                    a.Id,
                    candidate.Id,
                    candidate.Name,
                    candidate.ContactEmail,
                    a.Source.ToString().ToUpperInvariant(),
                    a.SelectionStatus.ToString().ToUpperInvariant(),
                    a.CreatedAt,
                    a.UpdatedAt,
                    resume?.Id,
                    resume?.Version,
                    resume?.ProcessingStatus.ToString().ToUpperInvariant(),
                    resume?.ProcessingError,
                    resume?.CreatedAt);
            })
            .ToList();
        return Task.FromResult<IReadOnlyList<ApplicationSubmission>>(rows);
    }

    public void Add(ApplicationEntity application) => Applications.Add(application);

    public void Remove(ApplicationEntity application) => Applications.Remove(application);
}

internal sealed class FakeCvFileStorage : ICvFileStorage
{
    public List<(string Key, string ContentType, byte[] Bytes)> Uploads { get; } = [];
    public bool ThrowOnUpload { get; set; }

    public async Task<string> UploadAsync(string fileKey, Stream content, string contentType, CancellationToken ct = default)
    {
        if (ThrowOnUpload)
        {
            throw new InvalidOperationException("storage unavailable");
        }

        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        Uploads.Add((fileKey, contentType, ms.ToArray()));
        return fileKey;
    }

    public Task<CvFile?> DownloadAsync(string fileKey, CancellationToken ct = default)
    {
        var upload = Uploads.LastOrDefault(item => item.Key == fileKey);
        return Task.FromResult(upload.Bytes is null ? null : new CvFile(upload.Bytes, upload.ContentType));
    }
}

internal sealed class FakeAiEngineClient : IAiEngineClient
{
    public AssistantChatRequest? LastChat { get; private set; }

    public Task<AssistantChatResponse> ChatAsync(AssistantChatRequest request, CancellationToken cancellationToken = default)
    {
        LastChat = request;
        return Task.FromResult(new AssistantChatResponse("Hola, te ayudo."));
    }

    public Task<EngineReadiness> GetReadinessAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new EngineReadiness(200, null));

    public bool Healthy { get; set; } = true;
    public Exception? ThrowOnIngest { get; set; }
    public List<Guid> IngestedResumeIds { get; } = [];

    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Healthy);

    public Task<CvIngestionResponse> IngestCvAsync(IngestCvRequest request, CancellationToken cancellationToken = default)
    {
        IngestedResumeIds.Add(request.ResumeId);
        if (ThrowOnIngest is not null)
        {
            throw ThrowOnIngest;
        }

        return Task.FromResult(new CvIngestionResponse(
            request.ResumeId,
            "extractor-v1",
            "text",
            null,
            1,
            null));
    }

    public Exception? ThrowOnEvaluate { get; set; }
    public List<EvaluateCandidateRequest> EvaluateRequests { get; } = [];
    public List<RankCandidatesRequest> RankRequests { get; } = [];
    public List<CompareCandidatesRequest> CompareRequests { get; } = [];
    public List<EvidenceSearchRequest> EvidenceRequests { get; } = [];

    /// <summary>Resume ids the fake engine skips when ranking (as if their evaluation failed).</summary>
    public HashSet<Guid> UnrankableResumes { get; } = [];

    public Func<Guid, float> ScoreOf { get; set; } = _ => 70f;

    public Task<CandidateEvaluationResponse> EvaluateAsync(EvaluateCandidateRequest request, CancellationToken cancellationToken = default)
    {
        EvaluateRequests.Add(request);
        if (ThrowOnEvaluate is not null)
        {
            throw ThrowOnEvaluate;
        }

        return Task.FromResult(new CandidateEvaluationResponse(
            Guid.NewGuid(),
            ScoreOf(request.ResumeId),
            "MEETS",
            0.9f,
            true,
            false,
            [],
            [],
            [],
            "fake-model",
            "v1",
            "Looks good."));
    }

    public Task<RankCandidatesResponse> RankCandidatesAsync(RankCandidatesRequest request, CancellationToken cancellationToken = default)
    {
        RankRequests.Add(request);
        if (ThrowOnEvaluate is not null)
        {
            throw ThrowOnEvaluate;
        }

        var ranked = request.ResumeIds
            .Where(id => !UnrankableResumes.Contains(id))
            .Select(id => new RankedCandidateDto(id, Guid.NewGuid(), ScoreOf(id), 0.8f, "MEETS", true))
            .OrderByDescending(row => row.TotalScore)
            .ToList();
        return Task.FromResult(new RankCandidatesResponse(request.RoleProfileId, ranked));
    }

    public Task<EvidenceSearchResponse> SearchEvidenceAsync(EvidenceSearchRequest request, CancellationToken cancellationToken = default)
    {
        EvidenceRequests.Add(request);
        return Task.FromResult(new EvidenceSearchResponse(
            request.ResumeId,
            request.Query,
            [new EvidenceChunkDto(Guid.NewGuid(), "Experience", "5 years with Azure", 1, 0.9f)]));
    }

    public Task<CompareCandidatesResponse> CompareAsync(CompareCandidatesRequest request, CancellationToken cancellationToken = default)
    {
        CompareRequests.Add(request);
        return Task.FromResult(new CompareCandidatesResponse(
            request.RoleProfileId,
            "Both are strong.",
            request.ResumeIds.Select(id => new SideBySideCandidateDto(id.ToString(), "Cloud", "English", "Platform")).ToList(),
            "Depth versus breadth.",
            []));
    }

    public List<ProposeRequirementsRequest> ProposeRequests { get; } = [];
    public List<SuggestEquivalencesRequest> EquivalenceRequests { get; } = [];
    public List<DraftVacancyRequest> DraftRequests { get; } = [];

    public Task<DraftVacancyResponse> DraftVacancyAsync(DraftVacancyRequest request, CancellationToken cancellationToken = default)
    {
        DraftRequests.Add(request);
        if (ThrowOnAssist is not null)
        {
            throw ThrowOnAssist;
        }

        return Task.FromResult(new DraftVacancyResponse(new VacancyDraftDto(
            "Game Developer", "Tecnología", "Crearás juegos móviles.", 2, null, null, "REMOTE", null, null, null,
            [new ProposedRequirementDto("Unity", "MANDATORY", 100, [])])));
    }
    public Exception? ThrowOnAssist { get; set; }

    public Task<ProposeRequirementsResponse> ProposeRequirementsAsync(ProposeRequirementsRequest request, CancellationToken cancellationToken = default)
    {
        ProposeRequests.Add(request);
        if (ThrowOnAssist is not null)
        {
            throw ThrowOnAssist;
        }

        return Task.FromResult(new ProposeRequirementsResponse(
        [
            new ProposedRequirementDto("Unity", "MANDATORY", 60, [new EquivalenceDto("Unreal Engine", 70)]),
            new ProposedRequirementDto("Shaders", "DESIRABLE", 40, []),
        ]));
    }

    public Task<SuggestEquivalencesResponse> SuggestEquivalencesAsync(SuggestEquivalencesRequest request, CancellationToken cancellationToken = default)
    {
        EquivalenceRequests.Add(request);
        if (ThrowOnAssist is not null)
        {
            throw ThrowOnAssist;
        }

        return Task.FromResult(new SuggestEquivalencesResponse([new EquivalenceDto("C++", 70)]));
    }
}

internal sealed class FakeVacancyInsights : IVacancyInsights
{
    public List<ApplicationFact> Facts { get; } = [];

    public Task<IReadOnlyList<ApplicationFact>> ListApplicationFactsAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> vacancyIds,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ApplicationFact>>(
            Facts.Where(fact => vacancyIds.Contains(fact.JobOpeningId)).ToList());
}
