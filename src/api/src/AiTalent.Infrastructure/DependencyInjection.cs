using AiTalent.Application.Abstractions;
using AiTalent.Infrastructure.AiEngine;
using AiTalent.Infrastructure.Persistence;
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
