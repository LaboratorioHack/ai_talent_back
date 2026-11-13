using System.Text.Json;
using AiTalent.Application.Features.Rankings;

namespace AiTalent.Application.Features.Portal;

/// <summary>
/// Turns the engine's structured extraction (`professional_data`) into the document the web app renders
/// as a CV: profile, experience with highlights, education, certifications, languages and skills.
/// The extraction is anonymized, so the name and contact come from the candidate row.
/// </summary>
public static class ResumeDocBuilder
{
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

                var summary = Text(root, "summary");
                if (summary is not null)
                {
                    sections.Add(new ResumeSectionResponse("Perfil", [Line("text", summary)]));
                }

                var experience = new List<ResumeLineResponse>();
                foreach (var job in Items(root, "work_experience"))
                {
                    var title = Text(job, "title");
                    var company = Text(job, "company");
                    headline = headline.Length == 0 && title is not null ? title : headline;
                    var role = string.Join(" — ", new[] { title, company is null || company.Equals("Company Name", StringComparison.OrdinalIgnoreCase) ? null : company }.Where(part => part is not null));
                    if (role.Length > 0)
                    {
                        experience.Add(new ResumeLineResponse("role", role, Range(Text(job, "start_date"), Text(job, "end_date")), null));
                    }

                    experience.AddRange(Strings(job, "highlights").Take(4).Select(highlight => Line("bullet", highlight)));
                }

                if (experience.Count > 0)
                {
                    sections.Add(new ResumeSectionResponse("Experiencia", experience));
                }

                var education = Items(root, "education")
                    .Select(item => new ResumeLineResponse(
                        "role",
                        string.Join(", ", new[] { Text(item, "degree"), Text(item, "field") }.Where(part => part is not null)),
                        Text(item, "end_date") ?? Text(item, "year"),
                        null))
                    .Where(line => line.Text.Length > 0)
                    .ToList();
                if (education.Count > 0)
                {
                    sections.Add(new ResumeSectionResponse("Educación", education));
                }

                var certifications = Strings(root, "certifications").Select(text => Line("text", text)).ToList();
                if (certifications.Count > 0)
                {
                    sections.Add(new ResumeSectionResponse("Certificaciones", certifications));
                }

                var languages = Strings(root, "languages").ToList();
                if (languages.Count > 0)
                {
                    sections.Add(new ResumeSectionResponse("Idiomas", [Line("text", string.Join(", ", languages))]));
                }

                var skills = Skills(professionalDataJson);
                if (skills.Count > 0)
                {
                    sections.Add(new ResumeSectionResponse("Habilidades", [Line("text", string.Join(" · ", skills.Take(16)))]));
                }
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
