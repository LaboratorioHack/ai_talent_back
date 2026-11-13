namespace AiTalent.Application.Features.Governance;

public sealed record GovernanceAuditResponse(
    string Organization,
    GovernanceStatsResponse Stats,
    IReadOnlyList<GovernanceAttributeResponse> Attributes,
    IReadOnlyList<AuditDecisionResponse> Decisions,
    IReadOnlyList<BiasAlertResponse> Alerts);

public sealed record GovernanceStatsResponse(
    int SensitiveUsed,
    int ResumesEvaluated,
    int ScoresWithEvidence,
    int HumanDecisions,
    int AiErrorsFlagged);

public sealed record GovernanceAttributeResponse(string Label, bool Used, string Note);

public sealed record AuditDecisionResponse(
    string When,
    string Action,
    string Who,
    string Subject,
    string Status);

public sealed record BiasAlertResponse(string Id, string Vacancy, string Text, string Detail);
