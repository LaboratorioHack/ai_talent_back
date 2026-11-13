using System.Text;
using AiTalent.Application.Abstractions;
using AiTalent.Infrastructure.AiEngine;
using AiTalent.Infrastructure.Auth;
using AiTalent.Infrastructure.Persistence;
using AiTalent.Infrastructure.Persistence.Repositories;
using AiTalent.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace AiTalent.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Supabase"), npgsql => npgsql.UseVector()));

        services.AddScoped<ICompanyRepository, CompanyRepository>();
        services.AddScoped<IStaffRepository, StaffRepository>();
        services.AddScoped<IJobOpeningRepository, JobOpeningRepository>();
        services.AddScoped<ICandidateRepository, CandidateRepository>();
        services.AddScoped<IResumeRepository, ResumeRepository>();
        services.AddScoped<IApplicationRepository, ApplicationRepository>();
        services.AddScoped<IEvaluationReadStore, EvaluationReadStore>();
        services.AddScoped<IInterviewReadStore, InterviewReadStore>();
        services.AddScoped<IGovernanceReadStore, GovernanceReadStore>();
        services.AddScoped<IVacancyInsights, VacancyInsights>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<ICvFileStorage, CvFileStorage>();

        // OWASP A02: the API refuses to start without a strong invite-code signing key.
        services.AddOptions<InviteCodeOptions>()
            .Bind(configuration.GetSection(InviteCodeOptions.SectionName))
            .Validate(options => Encoding.UTF8.GetByteCount(options.SigningKey) >= 32,
                "Invites:SigningKey must be at least 32 bytes.")
            .ValidateOnStart();
        services.AddSingleton<IInviteCodes, InviteCodes>();

        services.Configure<AiEngineOptions>(configuration.GetSection(AiEngineOptions.SectionName));
        services.Configure<R2StorageOptions>(configuration.GetSection(R2StorageOptions.SectionName));
        services.AddHttpClient();
        services.AddHttpClient<IAiEngineClient, AiEngineClient>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<AiEngineOptions>>().Value;
                client.BaseAddress = options.BaseUrl;
                client.DefaultRequestHeaders.Add("X-Internal-Api-Key", options.ApiKey);
                // The resilience pipeline below owns the timeouts.
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddStandardResilienceHandler(resilience =>
            {
                // Evaluations call an LLM per CV and rank runs them one after another: the 10 s / 30 s defaults cut them off.
                resilience.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
                resilience.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(10);
                resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(5);
                // Evaluate, rank and compare write evaluations and spend LLM calls: never retry a POST.
                resilience.Retry.DisableForUnsafeHttpMethods();
            });

        // Agents/Interview: agente de entrevista con Microsoft Agent Framework + Anthropic (pendiente).
        return services;
    }
}
