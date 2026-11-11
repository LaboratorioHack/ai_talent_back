namespace AiTalent.Api.Extensions;

/// <summary>
/// Development only: loads KEY=VALUE pairs from the nearest `.env.development` found walking up
/// from the content root (the repo root in a normal checkout). `__` maps to `:` like environment
/// variables do. Real environment variables still win because they are added afterwards.
/// </summary>
internal static class DotEnvConfigurationExtensions
{
    public static WebApplicationBuilder AddDotEnvForDevelopment(this WebApplicationBuilder builder, string fileName = ".env.development")
    {
        if (!builder.Environment.IsDevelopment())
        {
            return builder;
        }

        var path = FindUpwards(builder.Environment.ContentRootPath, fileName);
        if (path is null)
        {
            return builder;
        }

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim().Replace("__", ":", StringComparison.Ordinal);
            var value = line[(separator + 1)..].Trim().Trim('"');
            values[key] = value;
        }

        builder.Configuration.AddInMemoryCollection(values);
        builder.Configuration.AddEnvironmentVariables();
        return builder;
    }

    private static string? FindUpwards(string startDirectory, string fileName)
    {
        for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
