using System.Text;
using AiTalent.Application.Abstractions;
using AiTalent.Infrastructure.AiEngine;
using AiTalent.Infrastructure.Auth;
using AiTalent.Infrastructure.Persistence;
using AiTalent.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IAuditLog, AuditLog>();

        // OWASP A02: the API refuses to start without a strong invite-code signing key.
        services.AddOptions<InviteCodeOptions>()
            .Bind(configuration.GetSection(InviteCodeOptions.SectionName))
            .Validate(options => Encoding.UTF8.GetByteCount(options.SigningKey) >= 32,
                "Invites:SigningKey must be at least 32 bytes.")
            .ValidateOnStart();
        services.AddSingleton<IInviteCodes, InviteCodes>();

        services.AddOptions<SupabaseAuthOptions>()
            .Configure<IConfiguration>((options, config) =>
            {
                options.Url = config["SUPABASE_URL"] ?? string.Empty;
                options.AnonKey = config["SUPABASE_ANON_KEY"] ?? string.Empty;
            })
            .Validate(options => Uri.TryCreate(options.Url, UriKind.Absolute, out _) && options.AnonKey.Length > 0,
                "SUPABASE_URL and SUPABASE_ANON_KEY must be set.")
            .ValidateOnStart();
        services.AddHttpClient<IIdentityProvider, SupabaseIdentityProvider>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<SupabaseAuthOptions>>().Value;
            client.BaseAddress = new Uri($"{options.Url.TrimEnd('/')}/auth/v1/");
            client.DefaultRequestHeaders.Add("apikey", options.AnonKey);
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.Configure<AiEngineOptions>(configuration.GetSection(AiEngineOptions.SectionName));
        services.AddHttpClient<IAiEngineClient, AiEngineClient>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<AiEngineOptions>>().Value;
                client.BaseAddress = options.BaseUrl;
                client.DefaultRequestHeaders.Add("X-Internal-Api-Key", options.ApiKey);
            })
            .AddStandardResilienceHandler();

        // Agents/Interview: agente de entrevista con Microsoft Agent Framework + Anthropic (pendiente).
        return services;
    }
}
