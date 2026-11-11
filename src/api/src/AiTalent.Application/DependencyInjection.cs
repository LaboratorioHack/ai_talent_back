using Microsoft.Extensions.DependencyInjection;

namespace AiTalent.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Registrar aquí los handlers de cada Feature (Features/<Feature>/...).
        return services;
    }
}
