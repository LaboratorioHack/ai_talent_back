using System.Text.Json;
using AiTalent.Application.Features.Rankings;

namespace AiTalent.Application.Features.Portal;

/// <summary>
/// Turns the engine's structured extraction (`professional_data`) into the document the web app renders
/// as a CV, laid out like the Harvard résumé template: profile, education, experience (role, employer and
/// achievements), projects, leadership and activities, and a closing skills block (technical, languages,
/// certifications). A section only appears when the CV had something for it.
/// The extraction is anonymized, so the name and contact come from the candidate row.
/// </summary>
public static class ResumeDocBuilder
{
    private const int MaxHighlightsPerRole = 6;
    private const int MaxTechnicalSkills = 30;

    public static ResumeDocResponse Build(string name, IReadOnlyList<string> contact, string? professionalDataJson, int? pages = null)
    {
        var sections = new List<ResumeSectionResponse>();
        var headline = string.Empty;

        if (!string.IsNullOrWhiteSpace(professionalDataJson))
        {
            try
            {
                using var document = JsonDocument.Parse(professionalDataJson);
                var root = document.RootElement;
                var jobs = Items(root, "work_experience").ToList();
                headline = Text(root, "headline") ?? jobs.Select(job => Text(job, "title")).FirstOrDefault(title => title is not null) ?? string.Empty;

                var summary = Text(root, "summary");
                if (summary is not null)
                {
                    Add(sections, "Perfil", [Line("text", summary)]);
                }

                Add(sections, "Educación", Items(root, "education").SelectMany(Education).ToList());
                Add(sections, "Experiencia", jobs.SelectMany(Experience).ToList());
                Add(sections, "Proyectos", Items(root, "projects").SelectMany(Project).ToList());
                Add(sections, "Liderazgo y actividades", Strings(root, "leadership").Select(text => Line("bullet", text)).ToList());
                Add(sections, "Reconocimientos", Strings(root, "awards").Select(text => Line("bullet", text)).ToList());

                var technical = Strings(root, "skills")
                    .Concat(jobs.SelectMany(job => Strings(job, "technologies")))
                    .Concat(Items(root, "projects").SelectMany(project => Strings(project, "technologies")));
                var closing = new List<ResumeLineResponse>();
                AddLabeled(closing, "Técnicas", Distinct(technical).Take(MaxTechnicalSkills));
                AddLabeled(closing, "Idiomas", Distinct(Strings(root, "languages")));
                AddLabeled(closing, "Certificaciones", Distinct(Strings(root, "certifications")));
                Add(sections, "Habilidades", closing);
            }
            catch (JsonException)
            {
                // Fall back to the minimal document below.
            }
        }

        if (sections.Count == 0)
        {
            sections.Add(new ResumeSectionResponse("Hoja de vida", [Line("text", "Criba AI todavía está leyendo este archivo.")]));
        }

        return new ResumeDocResponse(name, headline, contact, sections, pages);
    }

    /// <summary>Role and dates, then the employer, then the achievements.</summary>
    private static IEnumerable<ResumeLineResponse> Experience(JsonElement job)
    {
        var title = Text(job, "title");
        var company = Company(job);
        if (title is null && company is null)
        {
            yield break;
        }

        yield return new ResumeLineResponse("role", title ?? company!, Range(Text(job, "start_date"), Text(job, "end_date")), null);
        if (title is not null && company is not null)
        {
            yield return Line("sub", company);
        }

        foreach (var highlight in Strings(job, "highlights").Take(MaxHighlightsPerRole))
        {
            yield return Line("bullet", highlight);
        }
    }

    /// <summary>Degree and field with its dates. The institution is never extracted (Responsible AI).</summary>
    private static IEnumerable<ResumeLineResponse> Education(JsonElement item)
    {
        var degree = string.Join(", ", new[] { Text(item, "degree"), Text(item, "field") }.Where(part => part is not null).Distinct(StringComparer.OrdinalIgnoreCase));
        if (degree.Length == 0)
        {
            yield break;
        }

        var start = Text(item, "start_date");
        var end = Text(item, "end_date") ?? Text(item, "year");
        yield return new ResumeLineResponse("role", degree, start is null ? end : Range(start, end), null);
    }

    private static IEnumerable<ResumeLineResponse> Project(JsonElement project)
    {
        var name = Text(project, "name");
        var description = Text(project, "description");
        if (name is null && description is null)
        {
            yield break;
        }

        if (name is not null)
        {
            yield return new ResumeLineResponse("role", name, null, null);
        }

        if (description is not null)
        {
            yield return Line("bullet", description);
        }
    }

    /// <summary>The employer, unless the CV only had an anonymized placeholder ("Company Name").</summary>
    private static string? Company(JsonElement job)
    {
        var company = Text(job, "company");
        return company is null || company.Equals("Company Name", StringComparison.OrdinalIgnoreCase) || company.StartsWith('[') ? null : company;
    }

    private static void Add(List<ResumeSectionResponse> sections, string title, IReadOnlyList<ResumeLineResponse> lines)
    {
        if (lines.Count > 0)
        {
            sections.Add(new ResumeSectionResponse(title, lines));
        }
    }

    /// <summary>"Técnicas: C++, SQL": the web app prints the label in bold.</summary>
    private static void AddLabeled(List<ResumeLineResponse> lines, string label, IEnumerable<string> values)
    {
        var joined = string.Join(", ", values);
        if (joined.Length > 0)
        {
            lines.Add(Line("text", $"{label}: {joined}"));
        }
    }

    private static IEnumerable<string> Distinct(IEnumerable<string> values) =>
        values.Select(value => value.Trim()).Where(value => value.Length > 1).DistinctBy(value => value.ToLowerInvariant());

    /// <summary>Skills, technologies, certifications and languages found in the CV, without duplicates.</summary>
    public static IReadOnlyList<string> Skills(string? professionalDataJson)
    {
        if (string.IsNullOrWhiteSpace(professionalDataJson))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(professionalDataJson);
            var root = document.RootElement;
            var found = new List<string>();
            found.AddRange(Strings(root, "skills"));
            foreach (var job in Items(root, "work_experience"))
            {
                found.AddRange(Strings(job, "technologies"));
            }

            found.AddRange(Strings(root, "certifications"));
            found.AddRange(Strings(root, "languages"));
            if (root.TryGetProperty("total_experience_months", out var months) && months.ValueKind == JsonValueKind.Number && months.GetInt32() >= 12)
            {
                found.Insert(0, $"{months.GetInt32() / 12} años de experiencia");
            }

            return found
                .Select(skill => skill.Trim())
                .Where(skill => skill.Length > 1)
                .DistinctBy(skill => skill.ToLowerInvariant())
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static ResumeLineResponse Line(string kind, string text) => new(kind, text, null, null);

    private static string? Range(string? start, string? end) =>
        start is null && end is null ? null : $"{start ?? "?"} – {(end is null || end.Equals("Current", StringComparison.OrdinalIgnoreCase) ? "hoy" : end)}";

    private static string? Text(JsonElement node, string property) =>
        node.ValueKind == JsonValueKind.Object
        && node.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    private static IEnumerable<JsonElement> Items(JsonElement node, string property) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).ToList()
            : [];

    /// <summary>Arrays of strings, or of objects with a name/language/title (certifications and languages vary).</summary>
    private static IEnumerable<string> Strings(JsonElement node, string property)
    {
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Select(item => item.ValueKind switch
            {
                JsonValueKind.String => item.GetString(),
                JsonValueKind.Object => string.Join(" ", new[] { Text(item, "name") ?? Text(item, "language") ?? Text(item, "title"), Text(item, "level") ?? Text(item, "proficiency") }.Where(part => part is not null)),
                _ => null,
            })
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!.Trim())
            .ToList();
    }
}
