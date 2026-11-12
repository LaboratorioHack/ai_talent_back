using AiTalent.Application.Features.Auth;
using AiTalent.Application.Features.Companies;
using AiTalent.Application.Features.Staff;
using Microsoft.Extensions.DependencyInjection;

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
        return services;
    }
}
