using AiTalent.Application.Features.Auth;
using AiTalent.Application.Features.Candidates;
using AiTalent.Application.Features.Companies;
using AiTalent.Application.Features.Dashboard;
using AiTalent.Application.Features.Evaluations;
using AiTalent.Application.Features.Governance;
using AiTalent.Application.Features.Interviews;
using AiTalent.Application.Features.Portal;
using AiTalent.Application.Features.Rankings;
using AiTalent.Application.Features.RoleProfiles;
using AiTalent.Application.Features.Staff;
using AiTalent.Application.Features.Vacancies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AiTalent.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Registrar aquí los handlers de cada Feature (Features/<Feature>/...).
        services.AddScoped<StaffAccess>();
        services.AddScoped<AuthService>();
        services.AddScoped<CompanyService>();
        services.AddScoped<StaffService>();
        services.AddScoped<VacancyService>();
        services.AddScoped<PublicVacancyService>();
        services.AddScoped<CandidateApplicationService>();
        services.AddScoped<ApplicationListService>();
        services.AddScoped<RankingService>();
        services.AddScoped<InterviewService>();
        services.AddScoped<GovernanceService>();
        services.AddScoped<ApplicationStageService>();
        services.AddScoped<EvaluationService>();
        services.AddScoped<RoleProfileAssistService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<PortalService>();
        services.AddScoped<CandidateInterviewService>();
        services.AddScoped<ApplicationActionsService>();
        services.AddScoped<ApplicantResumeService>();
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}
